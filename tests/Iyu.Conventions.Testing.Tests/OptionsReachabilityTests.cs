using System.Reflection;
using Iyu.Conventions.Testing.Fixtures.Clean;
using Iyu.Conventions.Testing.Fixtures.Defective;
using Xunit;

namespace Iyu.Conventions.Testing.Tests;

/// <summary>
/// The scanner is checked in both directions: it must report the unread options a synthetic library is
/// built to have, and report nothing for a library whose every option is read. A scanner that sees nothing
/// passes every roster, so the rejecting direction is the one that matters.
/// </summary>
public class OptionsReachabilityTests
{
    private static readonly Assembly Defective = typeof(WidgetOptions).Assembly;
    private static readonly Assembly Clean = typeof(CleanOptions).Assembly;
    private static readonly Func<Type, bool> Options = OptionsTypes.NamedWith("Options");

    [Fact]
    public void DefectiveLibrary_ReportsExactlyTheUnreadOptions()
    {
        var report = OptionsReachability.Scan([Defective], Options);

        Assert.Equal(
            [typeof(GadgetOptions), typeof(ThingOptions), typeof(WidgetOptions)],
            report.OptionTypes);
        Assert.Equal(["Unused"], report.Unread[typeof(WidgetOptions).FullName!]);
        Assert.Equal(["Label"], report.Unread[typeof(GadgetOptions).FullName!]);
        Assert.Equal(["Extra"], report.Unread[typeof(ThingOptions).FullName!]);
    }

    [Fact]
    public void FluentValidateReturningThis_CountsItsReads()
    {
        // Same signature as a copy (returns its own type), but it creates nothing: the option it checks is
        // honoured, so it is read.
        var report = OptionsReachability.Scan([Defective], Options);

        Assert.Contains($"{typeof(ThingOptions).FullName}.{nameof(ThingOptions.Limit)}", report.Read);
    }

    [Fact]
    public void CopyHelper_ReadsDoNotCount()
    {
        // WithName reads Extra only to carry it into a new instance.
        var report = OptionsReachability.Scan([Defective], Options);

        Assert.DoesNotContain($"{typeof(ThingOptions).FullName}.{nameof(ThingOptions.Extra)}", report.Read);
    }

    [Fact]
    public void RecordWithExpressionAndToString_DoNotCountAsReads()
    {
        var report = OptionsReachability.Scan([Defective], Options);

        Assert.Contains($"{typeof(GadgetOptions).FullName}.{nameof(GadgetOptions.Size)}", report.Read);
        Assert.DoesNotContain($"{typeof(GadgetOptions).FullName}.{nameof(GadgetOptions.Label)}", report.Read);
    }

    [Fact]
    public void CleanLibrary_ReportsNothingUnread()
    {
        var report = OptionsReachability.Scan([Clean], Options);

        Assert.Equal([typeof(CleanOptions)], report.OptionTypes);
        Assert.All(report.Unread.Values, Assert.Empty);
        report.ShouldMatchRoster(new Dictionary<string, string[]>());
    }

    [Fact]
    public void ReadFromAnotherScannedAssembly_Counts_AndIsReportedAsCrossAssembly()
    {
        var alone = OptionsReachability.Scan([Defective], Options);
        var together = OptionsReachability.Scan([Defective, Clean], Options);
        var key = $"{typeof(WidgetOptions).FullName}.{nameof(WidgetOptions.Unused)}";

        Assert.DoesNotContain(key, alone.Read);
        Assert.Contains(key, together.Read);
        Assert.Contains(key, together.CrossAssemblyReads);
    }

    [Fact]
    public void ShouldMatchRoster_PassesOnTheExactRoster()
    {
        var report = OptionsReachability.Scan([Defective], Options);

        report.ShouldMatchRoster(new Dictionary<string, string[]>
        {
            [typeof(WidgetOptions).FullName!] = ["Unused"],
            [typeof(GadgetOptions).FullName!] = ["Label"],
            [typeof(ThingOptions).FullName!] = ["Extra"],
        });
    }

    [Fact]
    public void ShouldMatchRoster_FailsOnANewlyUnreadOption_AndNamesIt()
    {
        var report = OptionsReachability.Scan([Defective], Options);

        var ex = Assert.Throws<RosterMismatchException>(() => report.ShouldMatchRoster(new Dictionary<string, string[]>
        {
            [typeof(WidgetOptions).FullName!] = ["Unused"],
            [typeof(GadgetOptions).FullName!] = ["Label"],
        }));

        Assert.Contains($"{typeof(ThingOptions).FullName}: Extra", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void ShouldMatchRoster_FailsOnAKnownOptionThatIsNowRead()
    {
        // A roster entry for an option that has since been wired is stale: it must be removed deliberately.
        var report = OptionsReachability.Scan([Clean], Options);

        var ex = Assert.Throws<RosterMismatchException>(() => report.ShouldMatchRoster(new Dictionary<string, string[]>
        {
            [typeof(CleanOptions).FullName!] = ["Retries"],
        }));

        Assert.Contains("known but not found", ex.Message, StringComparison.Ordinal);
        Assert.Contains($"{typeof(CleanOptions).FullName}: Retries", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void EmptyAssemblyList_IsRejected()
    {
        Assert.Throws<ArgumentException>(() => OptionsReachability.Scan([], Options));
    }
}
