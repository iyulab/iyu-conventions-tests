using System.Reflection;

namespace Iyu.Conventions.Testing;

/// <summary>
/// Finds public options that nothing in a library reads. An option nothing reads is a promise the library
/// does not keep: a caller sets it, and nothing changes and nothing is reported.
/// </summary>
/// <remarks>
/// <para>
/// The scan walks the IL of every method in the given assemblies and records calls to each option
/// property's getter made from outside the options type. A property with no such call is unread.
/// </para>
/// <para>
/// Reads inside an options type count only through a member the library calls from outside it: a
/// <c>Validate()</c> or computed property the library consults is how such an option is honoured. Reads
/// made to copy the options into another instance (a clone, a <c>With…</c> derivation, the compiler's
/// record copy method) are not uses and never count.
/// </para>
/// <para>
/// Two limits, both deliberate. Reading is necessary, not sufficient: an option can be read and still have
/// no effect, and that has no static signal. And a property is attributed to the type that declares it.
/// </para>
/// </remarks>
public static class OptionsReachability
{
    /// <summary>
    /// Scans <paramref name="assemblies"/> for options types (chosen by <paramref name="isOptionsType"/>) and
    /// reports which of their public instance properties nothing outside the type reads.
    /// </summary>
    /// <param name="assemblies">
    /// Every assembly whose code may read the options. The caller chooses them: a scanner that guesses which
    /// assemblies to load can silently scan none and report nothing unread.
    /// </param>
    /// <param name="isOptionsType">
    /// Which public, non-abstract classes are options types — a naming rule such as
    /// <see cref="OptionsTypes.NamedWith(string[])"/>, or any predicate the repository needs.
    /// </param>
    public static OptionsReachabilityReport Scan(IEnumerable<Assembly> assemblies, Func<Type, bool> isOptionsType)
    {
        ArgumentNullException.ThrowIfNull(assemblies);
        ArgumentNullException.ThrowIfNull(isOptionsType);

        var scanned = assemblies.Distinct().ToList();
        if (scanned.Count == 0)
        {
            throw new ArgumentException("At least one assembly is required; an empty scan reports nothing unread.", nameof(assemblies));
        }

        var optionTypes = scanned
            .SelectMany(IlInspection.SafeTypes)
            .Where(t => t is { IsClass: true, IsAbstract: false } && (t.IsPublic || t.IsNestedPublic))
            .Where(isOptionsType)
            .OrderBy(t => t.FullName, StringComparer.Ordinal)
            .ToList();
        var optionSet = optionTypes.ToHashSet();

        // (module, getter token) -> the option it reads, for properties each options type declares itself.
        var getters = new Dictionary<(Module, int), (Type Type, string Name)>();
        foreach (var type in optionTypes)
        {
            foreach (var property in type.GetProperties(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly))
            {
                if (property.GetMethod is { IsPublic: true } getter)
                {
                    getters[(getter.Module, getter.MetadataToken)] = (type, property.Name);
                }
            }
        }

        var read = new HashSet<string>(StringComparer.Ordinal);
        var crossAssembly = new HashSet<string>(StringComparer.Ordinal);
        var readsInside = new Dictionary<(Module, int), List<string>>();
        var calledFromOutside = new HashSet<(Module, int)>();
        const BindingFlags all = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance |
                                 BindingFlags.Static | BindingFlags.DeclaredOnly;

        foreach (var assembly in scanned)
        {
            foreach (var type in IlInspection.SafeTypes(assembly))
            {
                var owner = optionTypes.FirstOrDefault(o => IlInspection.IsWithin(type, o));
                IEnumerable<MethodBase> bodies = type.GetMethods(all).Cast<MethodBase>().Concat(type.GetConstructors(all));
                foreach (var method in bodies)
                {
                    foreach (var target in IlInspection.Calls(method, type.Module))
                    {
                        var targetKey = (target.Module, target.MetadataToken);
                        if (getters.TryGetValue(targetKey, out var option))
                        {
                            var key = $"{option.Type.FullName}.{option.Name}";
                            if (owner == option.Type)
                            {
                                if (method is MethodInfo && !IlInspection.IsCopy(method) && owner == method.DeclaringType)
                                {
                                    var methodKey = (method.Module, method.MetadataToken);
                                    if (!readsInside.TryGetValue(methodKey, out var list))
                                    {
                                        readsInside[methodKey] = list = [];
                                    }

                                    list.Add(key);
                                }

                                continue;
                            }

                            read.Add(key);
                            if (type.Assembly != option.Type.Assembly)
                            {
                                crossAssembly.Add(key);
                            }
                        }
                        else if (target.DeclaringType is { } declaring
                                 && optionSet.Contains(declaring)
                                 && !IlInspection.IsWithin(type, declaring))
                        {
                            calledFromOutside.Add(targetKey);
                        }
                    }
                }
            }
        }

        foreach (var (method, keys) in readsInside)
        {
            if (calledFromOutside.Contains(method))
            {
                read.UnionWith(keys);
            }
        }

        var unread = new SortedDictionary<string, IReadOnlyList<string>>(StringComparer.Ordinal);
        foreach (var type in optionTypes)
        {
            unread[type.FullName!] = type.GetProperties(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)
                .Where(p => p.GetMethod is { IsPublic: true })
                .Select(p => p.Name)
                .Where(name => !read.Contains($"{type.FullName}.{name}"))
                .Order(StringComparer.Ordinal)
                .ToArray();
        }

        return new OptionsReachabilityReport(optionTypes, unread, read, crossAssembly);
    }
}

/// <summary>Common rules for choosing which types are options types.</summary>
public static class OptionsTypes
{
    /// <summary>
    /// Types whose name ends with one of <paramref name="suffixes"/> (ordinal, case-sensitive), for example
    /// <c>NamedWith("Options", "Configuration")</c>.
    /// </summary>
    public static Func<Type, bool> NamedWith(params string[] suffixes)
    {
        ArgumentNullException.ThrowIfNull(suffixes);
        if (suffixes.Length == 0)
        {
            throw new ArgumentException("At least one suffix is required.", nameof(suffixes));
        }

        return type => suffixes.Any(s => type.Name.EndsWith(s, StringComparison.Ordinal));
    }
}
