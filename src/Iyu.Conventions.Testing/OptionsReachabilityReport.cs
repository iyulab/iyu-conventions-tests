namespace Iyu.Conventions.Testing;

/// <summary>The result of <see cref="OptionsReachability.Scan"/>.</summary>
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

        var found = Unread
            .Where(kv => kv.Value.Count > 0)
            .Select(kv => $"{kv.Key}: {string.Join(",", kv.Value)}")
            .Order(StringComparer.Ordinal)
            .ToList();
        var expected = knownUnread
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
            "A public option nothing in the library reads is a promise it does not keep. Wire it, or change the " +
            "known roster as a deliberate decision and keep the option's documentation honest about it.\n" +
            "found but not known:\n  " + (added.Count == 0 ? "(none)" : string.Join("\n  ", added)) + "\n" +
            "known but not found:\n  " + (removed.Count == 0 ? "(none)" : string.Join("\n  ", removed)));
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
