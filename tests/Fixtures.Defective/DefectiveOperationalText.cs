using System.Globalization;
using Microsoft.Extensions.Logging;

namespace Iyu.Conventions.Testing.Fixtures.Defective;

// Operational text with known language problems, for the scanner's own tests.

public static partial class DefectiveLog
{
    /// <summary>Hangul in a named-argument message: flagged.</summary>
    [LoggerMessage(EventId = 1, Level = LogLevel.Information, Message = "작업 시작: {Name}")]
    public static partial void Started(ILogger logger, string name);

    /// <summary>ASCII in a positional message: not flagged.</summary>
    [LoggerMessage(2, LogLevel.Warning, "Retrying {Name}")]
    public static partial void Retrying(ILogger logger, string name);
}

public static class DefectiveThrows
{
    /// <summary>Hangul thrown directly: flagged.</summary>
    public static void Direct() => throw new InvalidOperationException("설정이 잘못되었습니다");

    /// <summary>Hangul in a format string that feeds the exception: flagged.</summary>
    public static void Formatted(int value) =>
        throw new ArgumentException(string.Format(CultureInfo.InvariantCulture, "값 {0} 은 허용되지 않습니다", value));

    /// <summary>Hangul in an interpolated message: flagged.</summary>
    public static void Interpolated(int attempts) => throw new InvalidOperationException($"재시도 {attempts}회 실패");

    /// <summary>Non-ASCII punctuation only: flagged by the ASCII rule, not by the Hangul rule.</summary>
    public static void Punctuated() => throw new InvalidOperationException("Timeout — retry later");

    /// <summary>A user-facing string that never reaches an exception: never flagged.</summary>
    public static string Greeting() => "안녕하세요";
}
