namespace Iyu.Conventions.Testing.Fixtures.Clean;

/// <summary>A request whose every property the library sets — through an initializer and through a <c>with</c>.</summary>
public record CleanRequest
{
    public string Model { get; init; } = "";

    public int MaxTokens { get; init; }
}

public static class CleanRequestFactory
{
    public static CleanRequest Create(string model) => new CleanRequest { Model = model } with { MaxTokens = 256 };
}
