namespace Iyu.Conventions.Testing.Fixtures.Defective;

// Public API with known violations, for AsyncCancellation and ResultReturns. Each member states what the
// scanners must conclude about it.

/// <summary>A result object with a success flag and an error: a second failure channel.</summary>
public sealed record FetchOutcome
{
    public bool IsSuccess { get; init; }

    public string? Error { get; init; }

    public string? Body { get; init; }

    /// <summary>Not reported: the type's own factory builds the value (and the record's compiler-made copy is skipped).</summary>
    public static FetchOutcome Failed(string error) => new() { Error = error };
}

/// <summary>An interface the library owns: its members are reported at the declaration.</summary>
public interface IFetcher
{
    /// <summary>Reported: no token.</summary>
    Task<string> FetchAsync(string url);
}

public class Fetcher : IFetcher
{
    /// <summary>Not reported again: it implements <see cref="IFetcher.FetchAsync"/>, reported there.</summary>
    public Task<string> FetchAsync(string url) => Task.FromResult(url);

    /// <summary>Reported (cancellation): <c>ValueTask&lt;int&gt;</c>, no token.</summary>
    public ValueTask<int> CountAsync() => ValueTask.FromResult(0);

    /// <summary>Reported (cancellation): an async stream without a token.</summary>
    public async IAsyncEnumerable<string> StreamAsync(int count)
    {
        await Task.Yield();
        for (var i = 0; i < count; i++)
        {
            yield return i.ToString(System.Globalization.CultureInfo.InvariantCulture);
        }
    }

    /// <summary>Reported (cancellation): protected is public surface for a subclass.</summary>
    protected virtual Task RefreshAsync() => Task.CompletedTask;

    /// <summary>Reported (result): the outcome object is returned directly.</summary>
    public FetchOutcome Fetch(string url) => new() { IsSuccess = true, Body = url };

    /// <summary>Reported (result) — through a task; the token is present, so not a cancellation finding.</summary>
    public Task<FetchOutcome> TryFetchAsync(string url, CancellationToken cancellationToken = default) =>
        Task.FromResult(Fetch(url));
}

public sealed class CachingFetcher : Fetcher
{
    /// <summary>Not reported: an override follows <see cref="Fetcher.RefreshAsync"/>, reported there.</summary>
    protected override Task RefreshAsync() => Task.CompletedTask;
}

internal interface IInternalLoader
{
    Task LoadAsync(string key);
}

/// <summary>A public method implementing an internal interface is still public surface.</summary>
public sealed class Loader : IInternalLoader
{
    /// <summary>Reported (cancellation): nothing else reports this signature.</summary>
    public Task LoadAsync(string key) => Task.CompletedTask;
}
