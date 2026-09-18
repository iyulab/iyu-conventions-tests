using System.Reflection;
using Iyu.Conventions.Testing.Fixtures.Clean;
using Iyu.Conventions.Testing.Fixtures.Defective;
using Xunit;

namespace Iyu.Conventions.Testing.Tests;

/// <summary>
/// Checked in both directions, like the options scan: the defective fixture's known problems must all be
/// found and nothing else, and the clean fixture must pass while the scan demonstrably read its text.
/// </summary>
public class OperationalLanguageTests
{
    private static readonly Assembly Defective = typeof(DefectiveThrows).Assembly;
    private static readonly Assembly Clean = typeof(CleanThrows).Assembly;

    [Fact]
    public void HangulRule_FindsTheLogMessageAndEveryExceptionPath()
    {
        var report = OperationalLanguage.Scan([Defective], OperationalLanguage.ContainsHangul);

        var found = report.Findings.Select(f => $"{f.Kind}:{f.Location.Split('.').Last()}").Order(StringComparer.Ordinal).ToList();
        Assert.Contains("LogMessage:Started", found);
        Assert.Contains("ExceptionMessage:Direct", found);
        Assert.Contains("ExceptionMessage:Formatted", found);
        Assert.Contains("ExceptionMessage:Interpolated", found);
        Assert.DoesNotContain(found, f => f.EndsWith(":Retrying", StringComparison.Ordinal));
        Assert.DoesNotContain(found, f => f.EndsWith(":Greeting", StringComparison.Ordinal));
        Assert.DoesNotContain(found, f => f.EndsWith(":Punctuated", StringComparison.Ordinal));
    }

    [Fact]
    public void AsciiRule_AlsoFindsNonAsciiPunctuation()
    {
        var report = OperationalLanguage.Scan([Defective], OperationalLanguage.NonAscii);

        Assert.Contains(report.Findings, f => f is { Kind: OperationalTextKind.ExceptionMessage } && f.Location.EndsWith(".Punctuated", StringComparison.Ordinal));
        Assert.DoesNotContain(report.Findings, f => f.Location.EndsWith(".Greeting", StringComparison.Ordinal));
    }

    [Fact]
    public void ShouldBeClean_FailsOnTheDefectiveFixture_AndNamesTheText()
    {
        var report = OperationalLanguage.Scan([Defective], OperationalLanguage.ContainsHangul);

        var ex = Assert.Throws<OperationalLanguageException>(report.ShouldBeClean);
        Assert.Contains("설정이 잘못되었습니다", ex.Message, StringComparison.Ordinal);
        Assert.Contains("작업 시작", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void CleanFixture_PassesUnderTheStrictRule_AfterReadingItsText()
    {
        var report = OperationalLanguage.Scan([Clean], OperationalLanguage.NonAscii);

        report.ShouldBeClean();
        Assert.True(report.LogMessagesRead >= 1, $"the scan must see the fixture's log message (read {report.LogMessagesRead})");
        Assert.True(report.ExceptionLiteralsRead >= 1, $"the scan must see the fixture's exception message (read {report.ExceptionLiteralsRead})");
    }

    [Fact]
    public void EmptyAssemblyList_IsRejected()
    {
        Assert.Throws<ArgumentException>(() => OperationalLanguage.Scan([], OperationalLanguage.ContainsHangul));
    }
}
