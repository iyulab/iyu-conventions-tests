namespace Iyu.Conventions.Testing.Snippets;

/// <summary>
/// The examples a repository deliberately does not compile — a signature sketch, pseudocode — by the heading they sit
/// under, each with the reason. Shrink it; never grow it silently.
/// </summary>
public sealed class SnippetRoster
{
    private readonly IReadOnlyDictionary<string, string> _fragments;

    /// <summary>Creates a roster from heading → reason.</summary>
    public SnippetRoster(IReadOnlyDictionary<string, string> fragments)
    {
        ArgumentNullException.ThrowIfNull(fragments);
        foreach (var (heading, reason) in fragments)
        {
            if (string.IsNullOrWhiteSpace(reason))
            {
                throw new ArgumentException($"The fragment under '{heading}' has no reason; a roster entry records a decision.", nameof(fragments));
            }
        }

        _fragments = fragments;
    }

    /// <summary>Whether <paramref name="snippet"/> sits under a heading listed as a fragment.</summary>
    public bool IsFragment(DocSnippet snippet)
    {
        ArgumentNullException.ThrowIfNull(snippet);
        return _fragments.ContainsKey(snippet.Heading);
    }

    /// <summary>
    /// Asserts that the documents still have at least <paramref name="atLeast"/> examples and that every listed heading still
    /// has one. A floor guards against a reader that silently finds nothing; a heading that no longer exists is a stale
    /// entry that would hide the next example written under that name.
    /// </summary>
    /// <exception cref="SnippetException">Too few examples, or a listed heading with none.</exception>
    public void ShouldMatch(IReadOnlyCollection<DocSnippet> snippets, int atLeast)
    {
        ArgumentNullException.ThrowIfNull(snippets);
        var problems = new List<string>();
        if (snippets.Count < atLeast)
        {
            problems.Add($"expected at least {atLeast} C# examples, found {snippets.Count}");
        }

        foreach (var heading in _fragments.Keys.Where(h => !snippets.Any(s => s.Heading == h)).Order(StringComparer.Ordinal))
        {
            problems.Add($"the fragment roster names '{heading}', but no example sits under that heading");
        }

        if (problems.Count > 0)
        {
            throw new SnippetException(string.Join("\n", problems));
        }
    }
}

/// <summary>Thrown when an example does not compile or a snippet roster no longer matches the documents.</summary>
public sealed class SnippetException : Exception
{
    /// <summary>Creates the exception with a message describing what failed.</summary>
    public SnippetException(string message)
        : base(message)
    {
    }
}
