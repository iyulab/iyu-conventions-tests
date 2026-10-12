using System.Text;
using System.Text.RegularExpressions;

namespace Iyu.Conventions.Testing.Snippets;

/// <summary>One C# example found in a Markdown document.</summary>
/// <param name="File">The document it came from, as given to <see cref="DocSnippets.Read"/>.</param>
/// <param name="Line">The 1-based line of the example's first code line.</param>
/// <param name="Heading">The text of the last heading above the example, or <c>(top)</c>.</param>
/// <param name="Code">The example's code, with any blockquote markers removed.</param>
public sealed record DocSnippet(string File, int Line, string Heading, string Code)
{
    /// <summary>A stable name for test output and theory data: <c>README.md line 42: Quick Start</c>.</summary>
    public string Key => $"{Path.GetFileName(File)} line {Line}: {Heading}";
}

/// <summary>Reads the C# examples out of Markdown.</summary>
/// <remarks>
/// A fence counts when its info string starts with <c>csharp</c>, <c>cs</c> or <c>c#</c> (case-insensitive; anything after
/// a space is ignored), also inside a blockquote. Only an exact <c>```csharp</c> fence used to count in the copies this
/// replaces, so an example fenced as <c>```cs</c> was never checked. A heading is a line outside any fence that starts with
/// <c>#</c>, so a shell comment inside a <c>```bash</c> fence does not rename the section the next example belongs to.
/// </remarks>
public static partial class DocSnippets
{
    [GeneratedRegex(@"^(`{3,}|~{3,})\s*(csharp|cs|c#)(\s.*)?$", RegexOptions.IgnoreCase)]
    private static partial Regex CSharpFence();

    [GeneratedRegex(@"^(`{3,}|~{3,})")]
    private static partial Regex AnyFence();

    /// <summary>Reads every C# example in the Markdown file at <paramref name="path"/>.</summary>
    public static IReadOnlyList<DocSnippet> Read(string path)
    {
        ArgumentException.ThrowIfNullOrEmpty(path);
        return Parse(File.ReadAllText(path), path);
    }

    /// <summary>Reads every C# example in <paramref name="markdown"/>; <paramref name="file"/> names it in the results.</summary>
    public static IReadOnlyList<DocSnippet> Parse(string markdown, string file = "(markdown)")
    {
        ArgumentNullException.ThrowIfNull(markdown);
        var lines = markdown.Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n');
        var snippets = new List<DocSnippet>();
        var heading = "(top)";

        for (var i = 0; i < lines.Length; i++)
        {
            var quoted = lines[i].TrimStart().StartsWith('>');
            var line = Unquote(lines[i], quoted).Trim();
            var fence = AnyFence().Match(line);
            if (!fence.Success)
            {
                if (!quoted && lines[i].StartsWith('#'))
                {
                    heading = lines[i].TrimStart('#').Trim();
                }

                continue;
            }

            // Any fence is skipped to its closing marker, so nothing inside it is read as a heading.
            var marker = fence.Groups[1].Value;
            var isCSharp = CSharpFence().IsMatch(line);
            var start = i + 1;
            var code = new StringBuilder();
            for (i++; i < lines.Length; i++)
            {
                var inner = Unquote(lines[i], quoted);
                var trimmed = inner.Trim();
                if (trimmed.StartsWith(marker, StringComparison.Ordinal) && trimmed.TrimStart(marker[0]).Length == 0)
                {
                    break;
                }

                code.Append(inner).Append('\n');
            }

            if (isCSharp)
            {
                snippets.Add(new DocSnippet(file, start + 1, heading, code.ToString()));
            }
        }

        return snippets;
    }

    private static string Unquote(string line, bool quoted)
    {
        if (!quoted)
        {
            return line;
        }

        var t = line.TrimStart();
        if (!t.StartsWith('>'))
        {
            return line;
        }

        t = t[1..];
        return t.StartsWith(' ') ? t[1..] : t;
    }
}
