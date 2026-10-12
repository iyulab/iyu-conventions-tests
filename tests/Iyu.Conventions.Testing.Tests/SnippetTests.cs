using Iyu.Conventions.Testing.Fixtures.Clean;
using Iyu.Conventions.Testing.Snippets;
using Xunit;

namespace Iyu.Conventions.Testing.Tests;

/// <summary>
/// The snippet reader and compiler, in both directions: what must be read is read and what must not be is not; a valid
/// example compiles and a broken one fails.
/// </summary>
public class SnippetTests
{
    private static readonly SnippetCompiler Compiler = new(SnippetCompiler.ReferencesFor(typeof(CleanService).Assembly))
    {
        Usings = ["System", "System.Collections.Generic", "System.Linq", "System.Threading.Tasks", "Iyu.Conventions.Testing.Fixtures.Clean"],
        StandIns = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["options"] = "CleanOptions options = new();",
        },
    };

    private const string Document = """
        # Library

        Intro.

        ```csharp
        var service = new CleanService(new CleanOptions());
        ```

        ## Other spellings

        ```cs
        var a = 1;
        ```

        ```C# title="sample"
        var b = 2;
        ```

        ## Shell

        ```bash
        # not a heading - a shell comment inside a fence
        dotnet test
        ```

        ```csharp
        var c = 3;
        ```

        > ```csharp
        > var quoted = 4;
        > ```

        ```json
        { "csharp": true }
        ```

        ````csharp
        var four = "```";
        ````
        """;

    [Fact]
    public void Parse_ReadsCSharpCsAndCSharpSymbolFences_WithInfoStrings_AndInsideBlockquotes()
    {
        var snippets = DocSnippets.Parse(Document, "README.md");

        Assert.Equal(6, snippets.Count);
        Assert.Contains(snippets, s => s.Code.Contains("var a = 1;", StringComparison.Ordinal));
        Assert.Contains(snippets, s => s.Code.Contains("var b = 2;", StringComparison.Ordinal));
        Assert.Contains(snippets, s => s.Code == "var quoted = 4;\n");
        Assert.Contains(snippets, s => s.Code.Contains("var four = \"```\";", StringComparison.Ordinal));
        Assert.DoesNotContain(snippets, s => s.Code.Contains("csharp\": true", StringComparison.Ordinal));
    }

    [Fact]
    public void Parse_TracksHeadingsOutsideFencesOnly_AndReportsTheFirstCodeLine()
    {
        var snippets = DocSnippets.Parse(Document, "README.md");

        var first = snippets[0];
        Assert.Equal("Library", first.Heading);
        Assert.Equal(6, first.Line);
        Assert.Equal("README.md line 6: Library", first.Key);

        // The shell comment inside the bash fence does not rename the section.
        Assert.Equal("Shell", snippets.Single(s => s.Code.Contains("var c = 3;", StringComparison.Ordinal)).Heading);
    }

    [Fact]
    public void Key_UsesARelativeName_AndShortensARootedPath()
    {
        Assert.Equal("src/Pkg/README.md line 6: Library", DocSnippets.Parse(Document, "src/Pkg/README.md")[0].Key);

        var path = Path.Combine(Path.GetTempPath(), $"snippets-{Guid.NewGuid():N}.md");
        File.WriteAllText(path, Document);
        try
        {
            Assert.Equal($"{Path.GetFileName(path)} line 6: Library", DocSnippets.Read(path)[0].Key);
            Assert.Equal("docs/guide.md line 6: Library", DocSnippets.Read(path, "docs/guide.md")[0].Key);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void Read_ReadsAFile()
    {
        var path = Path.Combine(Path.GetTempPath(), $"snippets-{Guid.NewGuid():N}.md");
        File.WriteAllText(path, Document);
        try
        {
            Assert.Equal(6, DocSnippets.Read(path).Count);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void Compile_AValidExample_HasNoErrors()
    {
        Assert.Empty(Compiler.Compile("var service = new CleanService(new CleanOptions());"));
    }

    [Fact]
    public void Compile_RejectsAMemberTheLibraryDoesNotHave()
    {
        // Positive control: without it, a compiler that saw no references would pass every example.
        Assert.NotEmpty(Compiler.Compile("var service = new CleanService(new CleanOptions()); service.DoesNotExist();"));
    }

    [Fact]
    public void StandIn_IsAddedWhenUsedButNotDeclared_AndNotOtherwise()
    {
        var used = Compiler.Program("var service = new CleanService(options);");
        Assert.Contains("CleanOptions options = new();", used, StringComparison.Ordinal);
        Assert.Empty(Compiler.Compile("var service = new CleanService(options);"));

        // Declared by the example itself: the stand-in would be a duplicate declaration.
        var declared = Compiler.Program("var options = new CleanOptions(); var service = new CleanService(options);");
        Assert.DoesNotContain("CleanOptions options = new();", declared, StringComparison.Ordinal);

        // A member named like the stand-in is not a use of it.
        var member = Compiler.Program("var x = new { options = 1 }; var y = x.options;");
        Assert.DoesNotContain("CleanOptions options = new();", member, StringComparison.Ordinal);
    }

    // Every way a top-level example can declare a local is a declaration: a stand-in added next to it is a duplicate (CS0128).
    [Theory]
    [InlineData("await using var options = new System.IO.MemoryStream(); options.WriteByte(1);")]
    [InlineData("using var options = new System.IO.MemoryStream(); options.WriteByte(1);")]
    [InlineData("var (options, other) = (new CleanOptions(), 2); _ = options;")]
    [InlineData("int.TryParse(\"1\", out var options); _ = options;")]
    [InlineData("object o = new CleanOptions(); if (o is CleanOptions options) { _ = options; }")]
    [InlineData("foreach (var options in new[] { new CleanOptions() }) { _ = options; }")]
    public void StandIn_IsNotAdded_WhenTheExampleDeclaresTheName(string example)
    {
        Assert.DoesNotContain("CleanOptions options = new();", Compiler.Program(example), StringComparison.Ordinal);
        Assert.Empty(Compiler.Compile(example));
    }

    [Fact]
    public void Program_KeepsTheExamplesOwnUsings_AndDoesNotRepeatThem()
    {
        var program = Compiler.Program("using System.Linq;\nvar n = new[] { 1 }.Count();");

        Assert.Equal(1, CountOf(program, "using System.Linq;"));
        Assert.Empty(Compiler.Compile("using System.Linq;\nvar n = new[] { 1 }.Count();"));
    }

    [Fact]
    public void AnExampleThatOnlyDeclaresTypes_CompilesAsALibrary()
    {
        Assert.Empty(Compiler.Compile("public sealed class Sample { public int Value { get; set; } }"));
    }

    [Fact]
    public void StatementsAfterTypes_AreMovedAhead()
    {
        Assert.Empty(Compiler.Compile("public sealed class Sample { public int Value { get; set; } }\nvar s = new Sample { Value = 1 };"));
    }

    [Fact]
    public void ShouldCompile_ThrowsWithTheErrorsAndTheCompiledSource()
    {
        var snippet = new DocSnippet("README.md", 3, "Usage", "var x = new CleanService(); // no such constructor");

        var ex = Assert.Throws<SnippetException>(() => Compiler.ShouldCompile(snippet));
        Assert.Contains("README.md line 3: Usage", ex.Message, StringComparison.Ordinal);
        Assert.Contains("--- compiled as ---", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Roster_FailsOnAHeadingWithNoExample_AndOnTooFewExamples()
    {
        var snippets = DocSnippets.Parse(Document, "README.md");
        var roster = new SnippetRoster(new Dictionary<string, string> { ["Shell"] = "sketch" });

        Assert.True(roster.IsFragment(snippets.Single(s => s.Code.Contains("var c = 3;", StringComparison.Ordinal))));
        roster.ShouldMatch(snippets, atLeast: 6);
        Assert.Throws<SnippetException>(() => roster.ShouldMatch(snippets, atLeast: 7));
        Assert.Throws<SnippetException>(() =>
            new SnippetRoster(new Dictionary<string, string> { ["Gone"] = "renamed section" }).ShouldMatch(snippets, atLeast: 1));
    }

    [Fact]
    public void Roster_RequiresAReason_AndCompilerRequiresReferences()
    {
        Assert.Throws<ArgumentException>(() => new SnippetRoster(new Dictionary<string, string> { ["Usage"] = " " }));
        Assert.Throws<ArgumentException>(() => new SnippetCompiler([]));
    }

    private static int CountOf(string text, string value)
    {
        var count = 0;
        for (var i = text.IndexOf(value, StringComparison.Ordinal); i >= 0; i = text.IndexOf(value, i + 1, StringComparison.Ordinal))
        {
            count++;
        }

        return count;
    }
}
