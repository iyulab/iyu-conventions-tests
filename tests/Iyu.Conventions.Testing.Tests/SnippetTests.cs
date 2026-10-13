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
    private const string Guide = """
        # Guide

        ## A handler

        ```csharp
        public sealed class RetryingHandler
        {
            public CleanService Create(CleanOptions options) => new(options.Validate());
        }
        ```

        ## Using it

        ```csharp
        var handler = new RetryingHandler();
        var service = handler.Create(options);
        Console.WriteLine(service.Attempts);
        ```
        """;

    [Fact]
    public void CompileDocument_SeesATypeAnotherExampleDeclares()
    {
        var snippets = DocSnippets.Parse(Guide, "GUIDE.md");

        // Alone, the second example does not compile: the handler is declared in the first.
        Assert.NotEmpty(Compiler.Compile(snippets[1].Code));

        var compiled = Compiler.CompileDocument(snippets);
        Assert.All(snippets, s => Assert.Empty(compiled.ErrorsOf(s)));
        Assert.Empty(compiled.SharedErrors);
        compiled.ShouldCompile(snippets[1]);
        Assert.Contains("CleanOptions options = new();", compiled.SourceOf(snippets[1]), StringComparison.Ordinal);
    }

    [Fact]
    public void CompileDocument_ReportsAnErrorOnTheExampleThatHasIt()
    {
        var snippets = DocSnippets.Parse(Guide.Replace("handler.Create(options)", "handler.Build(options)", StringComparison.Ordinal), "GUIDE.md");

        var compiled = Compiler.CompileDocument(snippets);

        Assert.Empty(compiled.ErrorsOf(snippets[0]));
        Assert.Contains(compiled.ErrorsOf(snippets[1]), e => e.Id == "CS1061");
        Assert.Throws<SnippetException>(() => compiled.ShouldCompile(snippets[1]));
    }

    [Fact]
    public void CompileDocument_DoesNotCarryALocalFromOneExampleToTheNext()
    {
        var snippets = DocSnippets.Parse("""
            ```csharp
            var count = 3;
            ```

            ```csharp
            Console.WriteLine(count);
            ```
            """, "GUIDE.md");

        var compiled = Compiler.CompileDocument(snippets);

        Assert.Empty(compiled.ErrorsOf(snippets[0]));
        Assert.Contains(compiled.ErrorsOf(snippets[1]), e => e.Id == "CS0103");
    }

    [Fact]
    public void CompileDocument_AddsATypeStandInOnce_AndNotWhenTheDocumentDeclaresTheType()
    {
        var compiler = new SnippetCompiler(SnippetCompiler.ReferencesFor(typeof(CleanService).Assembly))
        {
            Usings = ["System", "Iyu.Conventions.Testing.Fixtures.Clean"],
            StandIns = new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["MyModel"] = "sealed class MyModel { public CleanOptions Options { get; } = new(); }",
            },
        };
        var twoUses = DocSnippets.Parse("""
            ```csharp
            var a = new CleanService(new MyModel().Options);
            ```

            ```csharp
            var b = new CleanService(new MyModel().Options);
            ```
            """, "GUIDE.md");

        var compiled = compiler.CompileDocument(twoUses);
        Assert.All(twoUses, s => Assert.Empty(compiled.ErrorsOf(s)));
        Assert.Empty(compiled.SharedErrors);

        // The document declares MyModel itself: the stand-in would collide with it, so it is not added.
        var declared = DocSnippets.Parse("""
            ```csharp
            public sealed class MyModel { public CleanOptions Options { get; } = new(); }
            ```

            ```csharp
            var a = new CleanService(new MyModel().Options);
            ```
            """, "GUIDE.md");
        var own = compiler.CompileDocument(declared);
        Assert.All(declared, s => Assert.Empty(own.ErrorsOf(s)));
        Assert.Empty(own.SharedErrors);
    }

    [Fact]
    public void CompileDocument_ReportsTwoExamplesThatDeclareTheSameType()
    {
        var snippets = DocSnippets.Parse("""
            ```csharp
            public sealed class Tools { }
            ```

            ```csharp
            public sealed class Tools { public int Count => 1; }
            ```
            """, "GUIDE.md");

        var compiled = Compiler.CompileDocument(snippets);

        Assert.Contains(snippets, s => compiled.ErrorsOf(s).Any(e => e.Id == "CS0101"));
    }

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

    // A stand-in can be a type the reader writes themselves (their own implementation of a library interface): it goes
    // after the example's statements, where a type declaration may stand in a top-level program.
    [Fact]
    public void TypeStandIn_IsDeclaredAfterTheStatements_WhenUsed_AndNotWhenTheExampleDeclaresIt()
    {
        var compiler = new SnippetCompiler(SnippetCompiler.ReferencesFor(typeof(CleanService).Assembly))
        {
            Usings = ["System", "Iyu.Conventions.Testing.Fixtures.Clean"],
            StandIns = new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["MyOptions"] = "sealed class MyOptions { public CleanOptions Build() => new(); }",
            },
        };

        const string uses = "var service = new CleanService(new MyOptions().Build());";
        Assert.Contains("sealed class MyOptions", compiler.Program(uses), StringComparison.Ordinal);
        Assert.Empty(compiler.Compile(uses));

        // Used only inside the example's own type.
        const string usedInType = "var h = new Holder();\nsealed class Holder { public MyOptions Options { get; } = new(); }";
        Assert.Empty(compiler.Compile(usedInType));

        // Declared by the example itself.
        const string declares = "var o = new MyOptions();\nsealed class MyOptions { }";
        Assert.DoesNotContain("public CleanOptions Build()", compiler.Program(declares), StringComparison.Ordinal);
        Assert.Empty(compiler.Compile(declares));
    }

    // The type may be named only by another stand-in: a value stand-in that declares a value of it (`MyModel model = new();`
    // for an example that just uses `model`), or a type stand-in that uses it. The copies this kit replaced handled the first
    // (an example used `llm`, whose stand-in is `ChatModel llm = new();`, and needed `class ChatModel`).
    [Fact]
    public void TypeStandIn_IsDeclared_WhenOnlyAnotherStandInNamesIt()
    {
        var compiler = new SnippetCompiler(SnippetCompiler.ReferencesFor(typeof(CleanService).Assembly))
        {
            Usings = ["System", "Iyu.Conventions.Testing.Fixtures.Clean"],
            StandIns = new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["model"] = "MyModel model = new();",
                ["MyModel"] = "sealed class MyModel { public MyOptions Options { get; } = new(); }",
                ["MyOptions"] = "sealed class MyOptions { public CleanOptions Build() => new(); }",
            },
        };

        const string example = "var service = new CleanService(model.Options.Build());";
        var program = compiler.Program(example);
        Assert.Contains("sealed class MyModel", program, StringComparison.Ordinal);
        Assert.Contains("sealed class MyOptions", program, StringComparison.Ordinal);
        Assert.Empty(compiler.Compile(example));

        // Nothing names them: neither is added.
        Assert.DoesNotContain("class MyModel", compiler.Program("var n = 1;"), StringComparison.Ordinal);
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
