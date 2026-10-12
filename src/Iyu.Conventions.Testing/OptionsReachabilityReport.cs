namespace Iyu.Conventions.Testing;

/// <summary>The result of <see cref="OptionsReachability.Scan(IEnumerable{System.Reflection.Assembly}, Func{Type, bool})"/>.</summary>
public sealed class OptionsReachabilityReport
{
    internal OptionsReachabilityReport(
        IReadOnlyList<Type> optionTypes,
        IReadOnlyDictionary<string, IReadOnlyList<string>> unread,
        IReadOnlySet<string> read,
        IReadOnlySet<string> crossAssemblyReads)
    {
        OptionTypes = optionTypes;
        Unread = unread;
        Read = read;
        CrossAssemblyReads = crossAssemblyReads;
    }

    /// <summary>Every options type the scan found, ordered by full name.</summary>
    public IReadOnlyList<Type> OptionTypes { get; }

    /// <summary>
    /// For each options type (by full name), the public properties nothing reads. Types whose every
    /// property is read map to an empty list.
    /// </summary>
    public IReadOnlyDictionary<string, IReadOnlyList<string>> Unread { get; }

    /// <summary>Every read property, as <c>Namespace.Type.Property</c>.</summary>
    public IReadOnlySet<string> Read { get; }

    /// <summary>The read properties whose reader lives in a different assembly from the options type.</summary>
    public IReadOnlySet<string> CrossAssemblyReads { get; }

    /// <summary>
    /// Asserts that the unread properties are exactly <paramref name="knownUnread"/>: options type full
    /// name to the property names the repository accepts as unread today. Types with no unread property
    /// are left out. A newly unread option fails, and so does a known one that has since been wired; both
    /// are changes the roster has to record deliberately.
    /// </summary>
    /// <exception cref="RosterMismatchException">The found roster differs from the known one.</exception>
    public void ShouldMatchRoster(IReadOnlyDictionary<string, string[]> knownUnread)
    {
        ArgumentNullException.ThrowIfNull(knownUnread);

        Roster.ShouldMatch(
            Unread,
            knownUnread,
            "A public option nothing in the library reads is a promise it does not keep. Wire it, or change the " +
            "known roster as a deliberate decision and keep the option's documentation honest about it.");
    }
}

/// <summary>Thrown when a scan's roster differs from the roster the repository declares.</summary>
public sealed class RosterMismatchException : Exception
{
    /// <summary>Creates the exception with a message listing the difference.</summary>
    public RosterMismatchException(string message)
        : base(message)
    {
    }
}

/// <summary>The comparison every roster assertion makes: the found roster against the one the repository declares.</summary>
internal static class Roster
{
    public static void ShouldMatch(
        IReadOnlyDictionary<string, IReadOnlyList<string>> foundByType,
        IReadOnlyDictionary<string, string[]> known,
        string explanation)
    {
        ArgumentNullException.ThrowIfNull(known);

        var found = foundByType
            .Where(kv => kv.Value.Count > 0)
            .Select(kv => $"{kv.Key}: {string.Join(",", kv.Value)}")
            .Order(StringComparer.Ordinal)
            .ToList();
        var expected = known
            .Where(kv => kv.Value.Length > 0)
            .Select(kv => $"{kv.Key}: {string.Join(",", kv.Value.Order(StringComparer.Ordinal))}")
            .Order(StringComparer.Ordinal)
            .ToList();

        if (found.SequenceEqual(expected))
        {
            return;
        }

        var added = found.Except(expected).ToList();
        var removed = expected.Except(found).ToList();
        throw new RosterMismatchException(
            explanation + "\n" +
            "found but not known:\n  " + (added.Count == 0 ? "(none)" : string.Join("\n  ", added)) + "\n" +
            "known but not found:\n  " + (removed.Count == 0 ? "(none)" : string.Join("\n  ", removed)));
    }
}
