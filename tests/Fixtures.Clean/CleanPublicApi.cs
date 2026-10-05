namespace Iyu.Conventions.Testing.Fixtures.Clean;

// Public API that follows both rules, plus the shapes the scanners must leave alone.

/// <summary>A per-item outcome: inside a batch, a failed item is data, not a failure channel.</summary>
public sealed class ItemOutcome
{
    public bool Success { get; init; }

    public string? ErrorMessage { get; init; }
}

public sealed class CleanClient : IAsyncDisposable
{
    public Task<string> GetAsync(string url, CancellationToken cancellationToken = default) => Task.FromResult(url);

    public async IAsyncEnumerable<int> ReadAllAsync(
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        await Task.Yield();
        yield return 1;
    }

    /// <summary>Per-item outcomes in a list are not reported.</summary>
    public Task<IReadOnlyList<ItemOutcome>> RunBatchAsync(IEnumerable<string> items, CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<ItemOutcome>>(items.Select(_ => new ItemOutcome { Success = true }).ToList());

    /// <summary>Not this library's signature: <see cref="IAsyncDisposable"/> fixes it.</summary>
    public ValueTask DisposeAsync() => ValueTask.CompletedTask;

    /// <summary>Not public: the scanners only read the public surface.</summary>
    internal Task WarmUpAsync() => Task.CompletedTask;
}

/// <summary>Overriding a framework method whose signature has no token is not this library's choice.</summary>
public sealed class CleanWriter : StringWriter
{
    public override Task FlushAsync() => Task.CompletedTask;
}
