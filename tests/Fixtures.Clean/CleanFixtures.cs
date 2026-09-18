using Iyu.Conventions.Testing.Fixtures.Defective;

namespace Iyu.Conventions.Testing.Fixtures.Clean;

// A synthetic library whose every option is read, for the scanner's own tests.

public class CleanOptions
{
    public int Retries { get; set; }

    public TimeSpan Timeout { get; set; }

    public CleanOptions Validate()
    {
        if (Retries < 0)
        {
            throw new InvalidOperationException("Retries must not be negative.");
        }

        return this;
    }
}

public sealed class CleanService
{
    public CleanService(CleanOptions options)
    {
        var valid = options.Validate();
        Budget = valid.Timeout * 2;
    }

    public TimeSpan Budget { get; }
}

/// <summary>
/// Reads an option declared in the defective fixture. Scanned together with it, that option is read — from
/// another assembly; scanned alone, the defective fixture still reports it unread.
/// </summary>
public static class CrossReader
{
    public static int Read(WidgetOptions options) => options.Unused;
}
