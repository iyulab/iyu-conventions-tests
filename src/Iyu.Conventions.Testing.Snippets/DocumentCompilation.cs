using Microsoft.CodeAnalysis;

namespace Iyu.Conventions.Testing.Snippets;

/// <summary>
/// The result of <see cref="SnippetCompiler.CompileDocument"/>: the errors of each example, and the errors that belong to
/// no example (a type stand-in that no longer matches the library).
/// </summary>
public sealed class DocumentCompilation
{
    /// <summary>The file name the document's type stand-ins compile under; errors in it are <see cref="SharedErrors"/>.</summary>
    public const string StandInsPath = "(stand-ins)";

    private readonly IReadOnlyDictionary<string, string> _sources;
    private readonly IReadOnlyList<Diagnostic> _errors;

    internal DocumentCompilation(IReadOnlyDictionary<string, string> sources, IReadOnlyList<Diagnostic> errors)
    {
        _sources = sources;
        _errors = errors;
    }

    /// <summary>Errors outside every example — in the document's type stand-ins, or with no source location.</summary>
    public IReadOnlyList<Diagnostic> SharedErrors =>
        [.. _errors.Where(e => e.Location.SourceTree?.FilePath is not { } path || !_sources.ContainsKey(path))];

    /// <summary>The errors located in <paramref name="snippet"/>'s source (empty when it compiles).</summary>
    public IReadOnlyList<Diagnostic> ErrorsOf(DocSnippet snippet)
    {
        ArgumentNullException.ThrowIfNull(snippet);
        return [.. _errors.Where(e => e.Location.SourceTree?.FilePath == snippet.Key)];
    }

    /// <summary>The source <paramref name="snippet"/> compiled as — put it in a failure message.</summary>
    public string SourceOf(DocSnippet snippet)
    {
        ArgumentNullException.ThrowIfNull(snippet);
        return _sources.TryGetValue(snippet.Key, out var source)
            ? source
            : throw new ArgumentException($"'{snippet.Key}' is not an example of this document.", nameof(snippet));
    }

    /// <summary>Throws when <paramref name="snippet"/> has errors.</summary>
    /// <exception cref="SnippetException">The example has compile errors; the message lists them and the compiled source.</exception>
    public void ShouldCompile(DocSnippet snippet)
    {
        var errors = ErrorsOf(snippet);
        if (errors.Count == 0)
        {
            return;
        }

        throw new SnippetException(
            $"{snippet.Key} does not compile against the current library (with the rest of its document):\n" +
            string.Join("\n", errors.Select(e => e.ToString())) + "\n--- compiled as ---\n" + SourceOf(snippet));
    }

    /// <summary>Throws when there are errors outside every example.</summary>
    /// <exception cref="SnippetException">The document's stand-ins (or something outside every example) do not compile.</exception>
    public void ShouldHaveNoSharedErrors()
    {
        var shared = SharedErrors;
        if (shared.Count > 0)
        {
            throw new SnippetException("The document compiles with errors outside its examples:\n" + string.Join("\n", shared.Select(e => e.ToString())));
        }
    }
}
