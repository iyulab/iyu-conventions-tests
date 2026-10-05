using System.Reflection;
using System.Text;

namespace Iyu.Conventions.Testing;

/// <summary>
/// Finds public asynchronous methods a caller cannot cancel: methods returning <see cref="Task"/>,
/// <see cref="ValueTask"/>, their generic forms or <see cref="IAsyncEnumerable{T}"/> that take no
/// <see cref="CancellationToken"/>.
/// </summary>
/// <remarks>
/// <para>
/// The public surface is every public or protected method of an exported type. Each signature is reported
/// once, where it is declared: an override or an interface implementation is governed by the declaration
/// it follows. A signature this library does not own is not reported at all — an override of a base class
/// or an implementation of an interface from another assembly (<c>IAsyncDisposable.DisposeAsync</c>,
/// <c>TextWriter.FlushAsync()</c>) cannot add a parameter.
/// </para>
/// <para>Property accessors, operators and delegate types are not methods a caller cancels, and are skipped.</para>
/// </remarks>
public static class AsyncCancellation
{
    /// <summary>Scans the public surface of <paramref name="assemblies"/>.</summary>
    /// <param name="assemblies">The assemblies to read. The caller chooses them.</param>
    public static PublicApiReport Scan(IEnumerable<Assembly> assemblies)
    {
        var scanned = PublicApi.Require(assemblies);
        var findings = new List<string>();
        var read = 0;

        foreach (var method in PublicApi.DeclaredSurface(scanned))
        {
            if (!PublicApi.IsAsyncReturn(method.ReturnType))
            {
                continue;
            }

            read++;
            if (!method.GetParameters().Any(p => p.ParameterType == typeof(CancellationToken)))
            {
                findings.Add(PublicApi.Describe(method));
            }
        }

        return new PublicApiReport(
            findings,
            read,
            "A public async method without a CancellationToken cannot be cancelled by its caller. Add " +
            "`CancellationToken cancellationToken = default`, or record the method in the known roster as a deliberate decision.");
    }
}

/// <summary>
/// Finds public methods that report failure through a result object instead of an exception: methods
/// returning (directly, or through <see cref="Task{TResult}"/> / <see cref="ValueTask{TResult}"/>) a type
/// that carries both a success flag and an error.
/// </summary>
/// <remarks>
/// <para>
/// A result type is one with a public <see cref="bool"/> property named <c>IsSuccess</c>, <c>Success</c>,
/// <c>Succeeded</c> or <c>IsSuccessful</c> and a public property named <c>Error</c>, <c>ErrorMessage</c>,
/// <c>Errors</c> or <c>Exception</c>. Its caller has to catch exceptions and check the flag; with
/// exceptions as the one failure channel, the flag is a second one.
/// </para>
/// <para>
/// Only a direct return counts. Per-item outcomes inside a collection (a batch where some items fail and
/// the rest succeed) are data, not a failure channel, and are not reported. Neither are the result type's own
/// factories (<c>Success(…)</c>, <c>Failed(…)</c>): they build the value. The surface rules are those of
/// <see cref="AsyncCancellation"/>, and compiler-generated methods (a record's <c>&lt;Clone&gt;$</c>) are skipped
/// by both scans.
/// </para>
/// </remarks>
public static class ResultReturns
{
    private static readonly string[] SuccessNames = ["IsSuccess", "Success", "Succeeded", "IsSuccessful"];
    private static readonly string[] ErrorNames = ["Error", "ErrorMessage", "Errors", "Exception"];

    /// <summary>Scans the public surface of <paramref name="assemblies"/>.</summary>
    /// <param name="assemblies">The assemblies to read. The caller chooses them.</param>
    public static PublicApiReport Scan(IEnumerable<Assembly> assemblies)
    {
        var scanned = PublicApi.Require(assemblies);
        var findings = new List<string>();
        var read = 0;

        foreach (var method in PublicApi.DeclaredSurface(scanned))
        {
            read++;
            var returned = Unwrap(method.ReturnType);
            // The result type's own factories and copies (Success(...), Failed(...), With...) build the value;
            // the failure channel is the API that hands it to a caller.
            if (returned is not null && returned != method.DeclaringType && IsResultType(returned))
            {
                findings.Add(PublicApi.Describe(method));
            }
        }

        return new PublicApiReport(
            findings,
            read,
            "A public method returning a success flag and an error gives its caller two failure channels. Return " +
            "the value and throw on failure, or record the method in the known roster as a deliberate decision.");
    }

    /// <summary>Whether <paramref name="type"/> has both a success flag and an error property.</summary>
    public static bool IsResultType(Type type)
    {
        ArgumentNullException.ThrowIfNull(type);
        var properties = type.GetProperties(BindingFlags.Public | BindingFlags.Instance);
        return properties.Any(p => p.PropertyType == typeof(bool) && SuccessNames.Contains(p.Name, StringComparer.Ordinal))
            && properties.Any(p => ErrorNames.Contains(p.Name, StringComparer.Ordinal));
    }

    private static Type? Unwrap(Type type)
    {
        if (type == typeof(void) || type == typeof(Task) || type == typeof(ValueTask))
        {
            return null;
        }

        if (type.IsGenericType)
        {
            var definition = type.GetGenericTypeDefinition();
            if (definition == typeof(Task<>) || definition == typeof(ValueTask<>))
            {
                return type.GetGenericArguments()[0];
            }
        }

        return type;
    }
}

/// <summary>The result of <see cref="AsyncCancellation.Scan"/> or <see cref="ResultReturns.Scan"/>.</summary>
public sealed class PublicApiReport
{
    private readonly string _rule;

    internal PublicApiReport(IReadOnlyList<string> findings, int membersRead, string rule)
    {
        Findings = findings.Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToList();
        MembersRead = membersRead;
        _rule = rule;
    }

    /// <summary>
    /// Every method that breaks the rule, as <c>Namespace.Type.Method(ParameterType, …)</c>, ordered.
    /// </summary>
    public IReadOnlyList<string> Findings { get; }

    /// <summary>
    /// How many public methods the rule was applied to. A scan that read none cannot tell a clean library
    /// from one it failed to see; assert a lower bound.
    /// </summary>
    public int MembersRead { get; }

    /// <summary>
    /// Asserts that the findings are exactly <paramref name="known"/>: the methods the repository accepts
    /// today. A new finding fails, and so does a known one that has since been fixed or removed; both are
    /// changes the roster has to record deliberately.
    /// </summary>
    /// <exception cref="RosterMismatchException">The findings differ from the known roster.</exception>
    public void ShouldMatchRoster(IEnumerable<string> known)
    {
        ArgumentNullException.ThrowIfNull(known);

        var expected = known.Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToList();
        if (Findings.SequenceEqual(expected))
        {
            return;
        }

        var added = Findings.Except(expected, StringComparer.Ordinal).ToList();
        var removed = expected.Except(Findings, StringComparer.Ordinal).ToList();
        throw new RosterMismatchException(
            _rule + "\n" +
            "found but not known:\n  " + (added.Count == 0 ? "(none)" : string.Join("\n  ", added)) + "\n" +
            "known but not found:\n  " + (removed.Count == 0 ? "(none)" : string.Join("\n  ", removed)));
    }

    /// <summary>Asserts that nothing was found.</summary>
    /// <exception cref="RosterMismatchException">At least one method breaks the rule.</exception>
    public void ShouldBeClean() => ShouldMatchRoster([]);
}

internal static class PublicApi
{
    private const BindingFlags Declared = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance |
                                          BindingFlags.Static | BindingFlags.DeclaredOnly;

    public static List<Assembly> Require(IEnumerable<Assembly> assemblies)
    {
        ArgumentNullException.ThrowIfNull(assemblies);
        var scanned = assemblies.Distinct().ToList();
        if (scanned.Count == 0)
        {
            throw new ArgumentException("At least one assembly is required; an empty scan finds nothing.", nameof(assemblies));
        }

        return scanned;
    }

    /// <summary>
    /// Public and protected methods of exported types, each at the declaration it follows: overrides and
    /// interface implementations are left out (their declaration is reported, or is not this library's).
    /// </summary>
    public static IEnumerable<MethodInfo> DeclaredSurface(IReadOnlyCollection<Assembly> scanned)
    {
        foreach (var type in scanned.SelectMany(IlInspection.SafeTypes).Where(t => t.IsVisible))
        {
            if (typeof(Delegate).IsAssignableFrom(type))
            {
                continue;
            }

            var implementations = InterfaceImplementations(type);
            foreach (var method in type.GetMethods(Declared))
            {
                var exposed = method.IsPublic || method.IsFamily || method.IsFamilyOrAssembly;
                if (!exposed || method.IsSpecialName || implementations.Contains(method) || IsCompilerGenerated(method))
                {
                    continue;
                }

                if (method.GetBaseDefinition() != method)
                {
                    continue;
                }

                yield return method;
            }
        }
    }

    // A record's <Clone>$ and similar are emitted by the compiler; no one writes or calls them by name.
    private static bool IsCompilerGenerated(MethodInfo method) =>
        method.Name.StartsWith('<') ||
        method.IsDefined(typeof(System.Runtime.CompilerServices.CompilerGeneratedAttribute), inherit: false);

    public static bool IsAsyncReturn(Type type)
    {
        if (type == typeof(Task) || type == typeof(ValueTask))
        {
            return true;
        }

        if (!type.IsGenericType)
        {
            return false;
        }

        var definition = type.GetGenericTypeDefinition();
        return definition == typeof(Task<>) || definition == typeof(ValueTask<>) || definition == typeof(IAsyncEnumerable<>);
    }

    public static string Describe(MethodInfo method)
    {
        var builder = new StringBuilder()
            .Append(method.DeclaringType?.FullName?.Replace('+', '.'))
            .Append('.')
            .Append(method.Name)
            .Append('(');
        var parameters = method.GetParameters();
        for (var i = 0; i < parameters.Length; i++)
        {
            if (i > 0)
            {
                builder.Append(", ");
            }

            AppendTypeName(builder, parameters[i].ParameterType);
        }

        return builder.Append(')').ToString();
    }

    private static HashSet<MethodInfo> InterfaceImplementations(Type type)
    {
        var set = new HashSet<MethodInfo>();
        if (type.IsInterface)
        {
            return set;
        }

        foreach (var contract in type.GetInterfaces())
        {
            // An internal interface of the same assembly is not reported at its declaration, so a public
            // method implementing it stays on the surface.
            if (!contract.IsVisible && contract.Assembly == type.Assembly)
            {
                continue;
            }

            InterfaceMapping map;
            try
            {
                map = type.GetInterfaceMap(contract);
            }
            catch (Exception)
            {
                continue;
            }

            foreach (var target in map.TargetMethods)
            {
                if (target.DeclaringType == type)
                {
                    set.Add(target);
                }
            }
        }

        return set;
    }

    private static void AppendTypeName(StringBuilder builder, Type type)
    {
        if (type.IsByRef)
        {
            AppendTypeName(builder, type.GetElementType()!);
            builder.Append('&');
            return;
        }

        if (type.IsArray)
        {
            AppendTypeName(builder, type.GetElementType()!);
            builder.Append("[]");
            return;
        }

        if (!type.IsGenericType)
        {
            builder.Append(type.Name);
            return;
        }

        var name = type.Name;
        var tick = name.IndexOf('`', StringComparison.Ordinal);
        builder.Append(tick < 0 ? name : name[..tick]).Append('<');
        var arguments = type.GetGenericArguments();
        for (var i = 0; i < arguments.Length; i++)
        {
            if (i > 0)
            {
                builder.Append(", ");
            }

            AppendTypeName(builder, arguments[i]);
        }

        builder.Append('>');
    }
}
