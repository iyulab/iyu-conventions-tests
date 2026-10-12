using System.Reflection;
using Iyu.Conventions.Testing.Fixtures.Clean;
using Iyu.Conventions.Testing.Fixtures.Defective;
using Xunit;

namespace Iyu.Conventions.Testing.Tests;

/// <summary>
/// Both directions: the defective library's request has two properties nothing fills (one only copied, one only
/// defaulted by the type itself), and the clean library's request is fully filled.
/// </summary>
public class OutboundAssignmentTests
{
    private static readonly Assembly Defective = typeof(CompletionRequest).Assembly;
    private static readonly Assembly Clean = typeof(CleanRequest).Assembly;

    [Fact]
    public void DefectiveLibrary_ReportsTheParametersItNeverSends()
    {
        var report = OutboundAssignment.Scan([Defective], t => t == typeof(CompletionRequest));

        Assert.Equal([typeof(CompletionRequest)], report.OutboundTypes);
        Assert.Equal(["Seed", "TopP"], report.Unassigned[typeof(CompletionRequest).FullName!]);
        Assert.Contains($"{typeof(CompletionRequest).FullName}.{nameof(CompletionRequest.Model)}", report.Assigned);
        Assert.Contains($"{typeof(CompletionRequest).FullName}.{nameof(CompletionRequest.Temperature)}", report.Assigned);
    }

    [Fact]
    public void CleanLibrary_ReportsNothing_InitAndWithCount()
    {
        var report = OutboundAssignment.Scan([Clean], t => t == typeof(CleanRequest));

        Assert.Equal([typeof(CleanRequest)], report.OutboundTypes);
        Assert.Empty(report.Unassigned[typeof(CleanRequest).FullName!]);
        report.ShouldMatchRoster(new Dictionary<string, string[]>());
    }

    [Fact]
    public void Roster_FailsOnANewUnassignedProperty_AndOnAKnownOneThatIsNowFilled()
    {
        var report = OutboundAssignment.Scan([Defective], t => t == typeof(CompletionRequest));
        var name = typeof(CompletionRequest).FullName!;

        report.ShouldMatchRoster(new Dictionary<string, string[]> { [name] = ["TopP", "Seed"] });
        Assert.Throws<RosterMismatchException>(() => report.ShouldMatchRoster(new Dictionary<string, string[]> { [name] = ["TopP"] }));
        Assert.Throws<RosterMismatchException>(() =>
            report.ShouldMatchRoster(new Dictionary<string, string[]> { [name] = ["TopP", "Seed", "Model"] }));
    }

    [Fact]
    public void Scan_RequiresAnAssembly()
    {
        Assert.Throws<ArgumentException>(() => OutboundAssignment.Scan([], _ => true));
    }
}
