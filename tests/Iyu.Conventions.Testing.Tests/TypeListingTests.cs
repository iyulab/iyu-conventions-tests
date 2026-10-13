using Iyu.Conventions.Testing.Fixtures.Clean;
using Iyu.Conventions.Testing.Snippets;
using Xunit;

namespace Iyu.Conventions.Testing.Tests;

/// <summary>
/// <see cref="SnippetCompiler.CheckListings"/>: an example that restates a library type is checked member by member against
/// the real type — a correct listing passes, a missing member and a different signature fail, a sample with bodies is
/// compiled instead, and with the option off a listing is only compiled (which checks its syntax and nothing else).
/// </summary>
public class TypeListingTests
{
    private static readonly SnippetCompiler Checking = new(SnippetCompiler.ReferencesFor(typeof(CleanService).Assembly))
    {
        Usings = ["System", "System.Collections.Generic", "System.Threading", "System.Threading.Tasks", "Iyu.Conventions.Testing.Fixtures.Clean"],
        CheckListings = true,
    };

    private static readonly SnippetCompiler NotChecking = new(SnippetCompiler.ReferencesFor(typeof(CleanService).Assembly))
    {
        Usings = ["System", "System.Collections.Generic", "System.Threading", "System.Threading.Tasks", "Iyu.Conventions.Testing.Fixtures.Clean"],
    };

    [Fact]
    public void ACorrectListing_HasNoErrors()
    {
        Assert.Empty(Checking.Compile("""
            public class CleanOptions
            {
                public int Retries { get; set; }
                public TimeSpan Timeout { get; set; }
                public CleanOptions Validate();
            }
            """));
        Assert.Empty(Checking.Compile("""
            public sealed class CleanClient
            {
                public Task<string> GetAsync(string url, CancellationToken cancellationToken = default);
                public Task<IReadOnlyList<ItemOutcome>> RunBatchAsync(IEnumerable<string> items, CancellationToken cancellationToken = default);
            }
            """));
        Assert.Empty(Checking.Compile("""
            public interface IDisposable
            {
                void Dispose();
            }
            """));
    }

    [Fact]
    public void AMemberTheTypeDoesNotDeclare_IsListing001()
    {
        var errors = Checking.Compile("""
            public class CleanOptions
            {
                public int Retries { get; set; }
                public bool NoSuchSwitch { get; set; }
            }
            """);

        var error = Assert.Single(errors);
        Assert.Equal("LISTING001", error.Id);
        Assert.Contains("NoSuchSwitch", error.GetMessage(System.Globalization.CultureInfo.InvariantCulture), StringComparison.Ordinal);
    }

    [Fact]
    public void AnEnumValueTheTypeDoesNotDeclare_IsListing001()
    {
        var errors = Checking.Compile("""
            public enum DayOfWeek { Sunday, Monday, Funday }
            """);

        Assert.Equal("LISTING001", Assert.Single(errors).Id);
    }

    [Theory]
    [InlineData("public string Retries { get; set; }")]
    [InlineData("public CleanOptions Validate(int strictness);")]
    [InlineData("public void Validate();")]
    public void AMemberWithAnotherSignature_IsListing002(string member)
    {
        var errors = Checking.Compile($$"""
            public class CleanOptions
            {
                {{member}}
            }
            """);

        Assert.Equal("LISTING002", Assert.Single(errors).Id);
    }

    [Theory]
    // CleanOptions.Retries is { get; set; }: a listing that shows init (or get only) tells the reader something else.
    [InlineData("public class CleanOptions { public int Retries { get; init; } }")]
    [InlineData("public class CleanOptions { public int Retries { get; } }")]
    // CleanRequest.Model is not required.
    [InlineData("public record CleanRequest { public required string Model { get; init; } }")]
    public void AnAccessorOrRequiredTheTypeDoesNotHave_IsListing002(string listing)
    {
        Assert.Equal("LISTING002", Assert.Single(Checking.Compile(listing)).Id);
    }

    [Fact]
    public void ARecordWithInitAccessors_ListedAsItIs_HasNoErrors()
    {
        Assert.Empty(Checking.Compile("""
            public record CleanRequest
            {
                public string Model { get; init; }
                public int MaxTokens { get; init; }
            }
            """));
    }

    private static readonly SnippetCompiler Strict = new(SnippetCompiler.ReferencesFor(typeof(CleanService).Assembly))
    {
        Usings = ["System", "System.Collections.Generic", "System.Threading", "System.Threading.Tasks", "Iyu.Conventions.Testing.Fixtures.Clean"],
        CheckListings = true,
        StrictListings = true,
    };

    [Theory]
    // A type the library does not export.
    [InlineData("public record CleanResponse { public string Url { get; init; } }", "no namespace")]
    // CleanRequest is a record, CleanOptions a class: listed as the other kind.
    [InlineData("public class CleanRequest { public string Model { get; init; } }", "is a record")]
    [InlineData("public record CleanOptions { public int Retries { get; set; } }", "is a class")]
    public void WhenStrictListings_ATypeOnlyBlockThatDoesNot_IsListing004(string listing, string says)
    {
        var error = Assert.Single(Strict.Compile(listing));

        Assert.Equal("LISTING004", error.Id);
        Assert.Contains(says, error.GetMessage(System.Globalization.CultureInfo.InvariantCulture), StringComparison.Ordinal);
        // Without the option the same block is the reader's own type: compiled as a sample, and it compiles.
        Assert.Empty(Checking.Compile(listing));
    }

    [Fact]
    public void WhenStrictListings_AParameterUnderAnotherName_IsListing002_AndNotByDefault()
    {
        // The parameter is url, not address: a reader passing it by name gets a compile error. A guide may shorten names
        // (ct for cancellationToken), so only a strict listing compares them.
        const string listing = "public sealed class CleanClient { public Task<string> GetAsync(string address, CancellationToken ct = default); }";

        Assert.Equal("LISTING002", Assert.Single(Strict.Compile(listing)).Id);
        Assert.Empty(Checking.Compile(listing));
    }

    [Fact]
    public void WhenStrictListings_ACorrectListing_StillHasNoErrors()
    {
        Assert.Empty(Strict.Compile("public class CleanOptions { public int Retries { get; set; } }"));
    }

    [Fact]
    public void AListingThatMatchesTwoVisibleTypes_IsListing003()
    {
        // System.Threading.Timer and System.Timers.Timer are both classes.
        var errors = Checking.Compile("""
            using System.Timers;

            public class Timer
            {
                public void Dispose();
            }
            """);

        Assert.Equal("LISTING003", Assert.Single(errors).Id);
    }

    [Fact]
    public void ASampleWithBodies_IsCompiled_NotCheckedAsAListing()
    {
        // A class under a library name whose members have bodies is the reader's own code: compiled, and it compiles.
        Assert.Empty(Checking.Compile("""
            public class CleanOptions
            {
                public int Retries { get; set; }
                public bool Extra() => true;
            }
            """));
    }

    [Fact]
    public void WithTheOptionOff_AListingIsOnlyCompiled()
    {
        const string listing = """
            public class CleanOptions
            {
                public bool NoSuchSwitch { get; set; }
            }
            """;

        // Compiled, the listing declares a second CleanOptions and checks nothing about the real one.
        Assert.Empty(NotChecking.Compile(listing));
        Assert.Equal("LISTING001", Assert.Single(Checking.Compile(listing)).Id);
    }

    [Fact]
    public void CompileDocument_ChecksListingsAndCountsTheTypes()
    {
        var snippets = DocSnippets.Parse("""
            ```csharp
            public class CleanOptions
            {
                public int Retries { get; set; }
                public int Attempts { get; set; }
            }
            ```

            ```csharp
            public interface IDisposable { void Dispose(); }
            public interface IAsyncDisposable { ValueTask DisposeAsync(); }
            ```

            ```csharp
            var service = new CleanService(new CleanOptions { Retries = 2 }.Validate());
            ```
            """, "GUIDE.md");

        var compiled = Checking.CompileDocument(snippets);

        Assert.Equal(3, compiled.ListedTypes);
        Assert.Equal("LISTING001", Assert.Single(compiled.ErrorsOf(snippets[0])).Id);
        Assert.Empty(compiled.ErrorsOf(snippets[1]));
        Assert.Empty(compiled.ErrorsOf(snippets[2]));
        Assert.Contains("public int Attempts", compiled.SourceOf(snippets[0]), StringComparison.Ordinal);
        Assert.Equal(0, NotChecking.CompileDocument(snippets).ListedTypes);
    }
}
