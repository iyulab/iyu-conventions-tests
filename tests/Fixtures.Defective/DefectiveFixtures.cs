namespace Iyu.Conventions.Testing.Fixtures.Defective;

// A synthetic library with known reachability, for the scanner's own tests. Each type states what the
// scanner must conclude about it.

/// <summary><c>Used</c> is read by <see cref="Widget"/>; <c>Unused</c> is read by nothing.</summary>
public class WidgetOptions
{
    public int Used { get; set; }

    public int Unused { get; set; }
}

public sealed class Widget
{
    public Widget(WidgetOptions options)
    {
        Size = options.Used;
    }

    public int Size { get; }
}

/// <summary>
/// A record: the library derives a copy with a <c>with</c> expression and formats it with
/// <c>ToString()</c>, and reads only <c>Size</c> itself. Copying and printing are not uses, so
/// <c>Label</c> stays unread.
/// </summary>
public record GadgetOptions
{
    public int Size { get; init; }

    public string? Label { get; init; }
}

public sealed class Gadget
{
    public Gadget(GadgetOptions options)
    {
        var grown = options with { Size = 1 };
        Size = grown.Size;
        Text = options.ToString();
    }

    public int Size { get; }

    public string Text { get; }
}

/// <summary>
/// <c>Limit</c> is read only inside <see cref="Validate"/>, a fluent method returning <c>this</c> that the
/// library calls — so it is read. <c>Extra</c> is read only by <see cref="WithName"/>, which exists to copy
/// the options into a new instance — so it is not. <c>Name</c> is read by the library directly.
/// </summary>
public class ThingOptions
{
    public int Limit { get; set; }

    public string Name { get; set; } = string.Empty;

    public int Extra { get; set; }

    public ThingOptions Validate()
    {
        if (Limit < 0)
        {
            throw new InvalidOperationException("Limit must not be negative.");
        }

        return this;
    }

    public ThingOptions WithName(string name) => new() { Limit = Limit, Name = name, Extra = Extra };
}

public sealed class Thing
{
    public Thing(ThingOptions options)
    {
        var valid = options.Validate();
        Title = valid.WithName("renamed").Name;
    }

    public string Title { get; }
}
