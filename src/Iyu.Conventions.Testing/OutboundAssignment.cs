using System.Reflection;

namespace Iyu.Conventions.Testing;

/// <summary>
/// The other direction of <see cref="OptionsReachability"/>: for a request type the library fills and sends out — read by
/// a caller's implementation, not by the library — finds the public properties the library never sets. Such a property is
/// a parameter the library never sends: an implementer reads it and always gets the default.
/// </summary>
/// <remarks>
/// <para>
/// A reachability scan cannot judge these types. Nothing in the library reads them (the reader lives outside, in whatever
/// implements the interface they are passed to), so every property is «unread» and the roster says nothing. What the
/// library owes such a type is the other half: that it fills what it declares.
/// </para>
/// <para>
/// The scan records calls to each property's setter (<c>init</c> accessors included) made from outside the type. A setter
/// whose value comes straight from the same property of another instance (<c>new Request { X = source.X }</c>) carries the
/// value, it does not fill it, and does not count. A value set only by the type's own constructor or initializer is a
/// default, not something the library sends on purpose, and does not count either.
/// </para>
/// </remarks>
public static class OutboundAssignment
{
    /// <summary>
    /// Scans <paramref name="assemblies"/> for the request types <paramref name="isOutboundType"/> accepts and reports which
    /// of their public settable properties nothing outside the type sets.
    /// </summary>
    /// <param name="assemblies">Every assembly whose code may fill the requests.</param>
    /// <param name="isOutboundType">Which public, non-abstract classes are requests the library sends out.</param>
    public static OutboundAssignmentReport Scan(IEnumerable<Assembly> assemblies, Func<Type, bool> isOutboundType)
    {
        ArgumentNullException.ThrowIfNull(assemblies);
        ArgumentNullException.ThrowIfNull(isOutboundType);

        var scanned = assemblies.Distinct().ToList();
        if (scanned.Count == 0)
        {
            throw new ArgumentException("At least one assembly is required; an empty scan reports nothing unassigned.", nameof(assemblies));
        }

        var outboundTypes = scanned
            .SelectMany(IlInspection.SafeTypes)
            .Where(t => t is { IsClass: true, IsAbstract: false } && (t.IsPublic || t.IsNestedPublic))
            .Where(isOutboundType)
            .OrderBy(t => t.FullName, StringComparer.Ordinal)
            .ToList();

        // (module, setter token) -> the property it sets, for properties each request type declares itself.
        var setters = new Dictionary<(Module, int), (Type Type, string Name)>();
        foreach (var type in outboundTypes)
        {
            foreach (var property in type.GetProperties(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly))
            {
                if (property.SetMethod is { IsPublic: true } setter)
                {
                    setters[(setter.Module, setter.MetadataToken)] = (type, property.Name);
                }
            }
        }

        var assigned = new HashSet<string>(StringComparer.Ordinal);
        const BindingFlags all = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance |
                                 BindingFlags.Static | BindingFlags.DeclaredOnly;
        foreach (var assembly in scanned)
        {
            foreach (var type in IlInspection.SafeTypes(assembly))
            {
                IEnumerable<MethodBase> bodies = type.GetMethods(all).Cast<MethodBase>().Concat(type.GetConstructors(all));
                foreach (var method in bodies)
                {
                    foreach (var (setter, copied) in IlInspection.SetterCalls(method, type.Module))
                    {
                        if (copied || !setters.TryGetValue((setter.Module, setter.MetadataToken), out var property))
                        {
                            continue;
                        }

                        if (!IlInspection.IsWithin(type, property.Type))
                        {
                            assigned.Add($"{property.Type.FullName}.{property.Name}");
                        }
                    }
                }
            }
        }

        var unassigned = new SortedDictionary<string, IReadOnlyList<string>>(StringComparer.Ordinal);
        foreach (var type in outboundTypes)
        {
            unassigned[type.FullName!] = type.GetProperties(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)
                .Where(p => p.SetMethod is { IsPublic: true })
                .Select(p => p.Name)
                .Where(name => !assigned.Contains($"{type.FullName}.{name}"))
                .Order(StringComparer.Ordinal)
                .ToArray();
        }

        return new OutboundAssignmentReport(outboundTypes, unassigned, assigned);
    }
}

/// <summary>The result of <see cref="OutboundAssignment.Scan"/>.</summary>
public sealed class OutboundAssignmentReport
{
    internal OutboundAssignmentReport(
        IReadOnlyList<Type> outboundTypes,
        IReadOnlyDictionary<string, IReadOnlyList<string>> unassigned,
        IReadOnlySet<string> assigned)
    {
        OutboundTypes = outboundTypes;
        Unassigned = unassigned;
        Assigned = assigned;
    }

    /// <summary>Every request type the scan found, ordered by full name.</summary>
    public IReadOnlyList<Type> OutboundTypes { get; }

    /// <summary>
    /// For each request type (by full name), the public settable properties nothing outside it sets. Types whose every
    /// property is set map to an empty list.
    /// </summary>
    public IReadOnlyDictionary<string, IReadOnlyList<string>> Unassigned { get; }

    /// <summary>Every property the library sets, as <c>Namespace.Type.Property</c>.</summary>
    public IReadOnlySet<string> Assigned { get; }

    /// <summary>
    /// Asserts that the unassigned properties are exactly <paramref name="knownUnassigned"/>: request type full name to the
    /// property names the repository accepts as never sent today. A newly unassigned property fails, and so does a known
    /// one the library has since started filling.
    /// </summary>
    /// <exception cref="RosterMismatchException">The found roster differs from the known one.</exception>
    public void ShouldMatchRoster(IReadOnlyDictionary<string, string[]> knownUnassigned)
    {
        Roster.ShouldMatch(
            Unassigned,
            knownUnassigned,
            "A public property of a request the library sends out but never sets is a parameter it never sends: an " +
            "implementer reads it and always gets the default. Fill it, remove it, or change the known roster as a " +
            "deliberate decision.");
    }
}
