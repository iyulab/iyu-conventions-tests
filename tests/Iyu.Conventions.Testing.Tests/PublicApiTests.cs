using System.Reflection;
using Iyu.Conventions.Testing.Fixtures.Clean;
using Iyu.Conventions.Testing.Fixtures.Defective;
using Xunit;

namespace Iyu.Conventions.Testing.Tests;

/// <summary>
/// Both directions, like the other scans: every known violation in the defective fixture is found and
/// nothing else is, and the clean fixture passes while the scan demonstrably read its methods.
/// </summary>
public class PublicApiTests
{
    private static readonly Assembly Defective = typeof(Fetcher).Assembly;
    private static readonly Assembly Clean = typeof(CleanClient).Assembly;

    private const string Ns = "Iyu.Conventions.Testing.Fixtures.Defective";

    [Fact]
    public void Cancellation_FindsEachUncancellableSignature_OnceWhereItIsDeclared()
    {
        var report = AsyncCancellation.Scan([Defective]);

        Assert.Equal(
            [
                $"{Ns}.Fetcher.CountAsync()",
                $"{Ns}.Fetcher.RefreshAsync()",
                $"{Ns}.Fetcher.StreamAsync(Int32)",
                $"{Ns}.IFetcher.FetchAsync(String)",
                $"{Ns}.Loader.LoadAsync(String)",
            ],
            report.Findings);
    }

    [Fact]
    public void ResultReturns_FindsDirectAndTaskWrappedReturns()
    {
        var report = ResultReturns.Scan([Defective]);

        Assert.Equal(
            [
                $"{Ns}.Fetcher.Fetch(String)",
                $"{Ns}.Fetcher.TryFetchAsync(String, CancellationToken)",
            ],
            report.Findings);
    }

    [Fact]
    public void CleanFixture_PassesBothRules_AfterReadingItsMethods()
    {
        var cancellation = AsyncCancellation.Scan([Clean]);
        var results = ResultReturns.Scan([Clean]);

        cancellation.ShouldBeClean();
        results.ShouldBeClean();
        Assert.True(cancellation.MembersRead >= 3, $"the scan must see the fixture's async methods (read {cancellation.MembersRead})");
        Assert.True(results.MembersRead >= 3, $"the scan must see the fixture's methods (read {results.MembersRead})");
    }

    [Fact]
    public void Roster_FailsInBothDirections()
    {
        var report = AsyncCancellation.Scan([Defective]);
        var known = report.Findings.ToList();

        report.ShouldMatchRoster(known);

        var missing = Assert.Throws<RosterMismatchException>(() => report.ShouldMatchRoster(known.Skip(1)));
        Assert.Contains(known[0], missing.Message, StringComparison.Ordinal);

        var stale = Assert.Throws<RosterMismatchException>(() => report.ShouldMatchRoster(known.Append($"{Ns}.Gone.OldAsync()")));
        Assert.Contains("Gone.OldAsync", stale.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void ResultType_NeedsBothTheFlagAndTheError()
    {
        Assert.True(ResultReturns.IsResultType(typeof(FetchOutcome)));
        Assert.True(ResultReturns.IsResultType(typeof(ItemOutcome)));
        Assert.False(ResultReturns.IsResultType(typeof(string)));
    }

    [Fact]
    public void EmptyAssemblyList_IsRejected()
    {
        Assert.Throws<ArgumentException>(() => AsyncCancellation.Scan([]));
        Assert.Throws<ArgumentException>(() => ResultReturns.Scan([]));
    }
}
