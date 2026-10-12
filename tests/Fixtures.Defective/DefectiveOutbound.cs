namespace Iyu.Conventions.Testing.Fixtures.Defective;

/// <summary>
/// A request the library fills and hands to a caller's implementation. <see cref="RequestFactory"/> sets <c>Model</c> and
/// <c>Temperature</c>; <c>TopP</c> is only carried by a copy and <c>Seed</c> only gets its default from the type's own
/// initializer, so both are parameters the library never sends.
/// </summary>
public class CompletionRequest
{
    public string Model { get; set; } = "";

    public double Temperature { get; set; }

    public double? TopP { get; set; }

    public int Seed { get; set; } = 7;

    public CompletionRequest() => Seed = 7;
}

public static class RequestFactory
{
    public static CompletionRequest Create(string model, double temperature) => new()
    {
        Model = model,
        Temperature = temperature,
    };

    // A copy: TopP's value comes straight from the same property of the source.
    public static CompletionRequest Copy(CompletionRequest source) => new()
    {
        Model = source.Model + "-copy",
        TopP = source.TopP,
    };
}
