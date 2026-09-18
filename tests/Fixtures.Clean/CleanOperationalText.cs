using Microsoft.Extensions.Logging;

namespace Iyu.Conventions.Testing.Fixtures.Clean;

// Operational text that follows the rules, next to a user-facing string that is out of scope.

public static partial class CleanLog
{
    [LoggerMessage(EventId = 1, Level = LogLevel.Information, Message = "Job started: {Name}")]
    public static partial void Started(ILogger logger, string name);
}

public static class CleanThrows
{
    public static void Direct() => throw new InvalidOperationException("The configuration is invalid.");

    public static string Greeting() => "안녕하세요";
}
