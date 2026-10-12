namespace Iyu.Conventions.Testing.Fixtures.Defective;

// A synthetic library with known reachability, for the scanner's own tests. Each type states what the
// scanner must conclude about it.

/// <summary><c>Used</c> is read by <see cref="Widget"/>; <c>Unused</c> is read by nothing.</summary>
public class WidgetOptions
{
    public int Used { get; set; }

    public int Unused { get; set; }
}

/// <summary>
/// An <c>IValidateOptions&lt;T&gt;</c> validator reads <c>Unused</c>: validating an option is not using it, so it stays unread.
/// </summary>
public sealed class WidgetOptionsValidator : Microsoft.Extensions.Options.IValidateOptions<WidgetOptions>
{
    public Microsoft.Extensions.Options.ValidateOptionsResult Validate(string? name, WidgetOptions options)
        => options.Unused < 0
            ? Microsoft.Extensions.Options.ValidateOptionsResult.Fail("Unused must not be negative.")
            : Microsoft.Extensions.Options.ValidateOptionsResult.Success;
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
/// <c>Limit</c> is read only inside <see cref="Validate"/>: checking a value is not using it, so it is unread.
/// <c>Budget</c> is read only inside <see cref="Clamped"/>, a fluent method returning <c>this</c> that the library
/// calls — not a copy, so it is read. <c>Extra</c> is read only by <see cref="WithName"/>, which exists to copy the
/// options into a new instance — so it is not. <c>Name</c> is read by the library directly.
/// </summary>
public class ThingOptions
{
    public int Limit { get; set; }

    public int Budget { get; set; }

    public int Effective { get; private set; }

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

    public ThingOptions Clamped()
    {
        Effective = Math.Max(0, Budget);
        return this;
    }

    public ThingOptions WithName(string name) => new() { Limit = Limit, Name = name, Extra = Extra };
}

/// <summary>
/// <c>Kept</c> is read by <see cref="Holder"/>. <c>Ratio</c> is read only by <see cref="HolderCopies.Clamped"/>, a helper
/// outside the options type that copies them into a new instance, so it is not read. <c>Mode</c> goes into a property of
/// another type, which is using it.
/// </summary>
public class HolderOptions
{
    public float Ratio { get; set; }

    /// <summary>Read only through <see cref="EffectiveGroup"/>, which the library reads, so it is read.</summary>
    public string? Group { get; set; }

    public string EffectiveGroup => string.IsNullOrEmpty(Group) ? "default" : Group;

    public int Kept { get; set; }

    public string Mode { get; set; } = string.Empty;
}

public static class HolderCopies
{
    public static HolderOptions Clamped(HolderOptions source, int limit)
        => new() { Ratio = source.Ratio, Kept = Math.Min(source.Kept, limit), Mode = source.Mode };
}

public sealed class Holder
{
    public Holder(HolderOptions options)
    {
        var clamped = HolderCopies.Clamped(options, 10);
        Size = clamped.Kept;
        Mode = new ModeSetting { Mode = options.Mode };
        GroupName = options.EffectiveGroup;
    }

    public string GroupName { get; }

    public int Size { get; }

    public ModeSetting Mode { get; }
}

public sealed class ModeSetting
{
    public string Mode { get; set; } = string.Empty;
}

public sealed class Thing
{
    public Thing(ThingOptions options)
    {
        var valid = options.Validate().Clamped();
        Title = valid.WithName("renamed").Name;
        Effective = valid.Effective;
    }

    public string Title { get; }

    public int Effective { get; }
}

/// <summary>
/// An options type whose settings live in an object of their own: <c>Weights</c> is read, and of the weights only
/// <c>Semantic</c> is. A scan by name sees <see cref="ScoringOptions"/> alone; following its settable properties also
/// finds <see cref="ScoringWeights"/> and its unread <c>Keyword</c>.
/// </summary>
public class ScoringOptions
{
    public ScoringWeights Weights { get; set; } = new();
}

public class ScoringWeights
{
    public double Semantic { get; set; }

    public double Keyword { get; set; }
}

public sealed class Scorer
{
    public Scorer(ScoringOptions options)
    {
        Score = options.Weights.Semantic;
    }

    public double Score { get; }
}

/// <summary>
/// <c>Used</c> is read by <see cref="Gizmo"/>. <c>CheckedOnly</c> is read only by <see cref="GizmoConfigurationChecker"/>,
/// a standalone validator that is not an <c>IValidateOptions&lt;T&gt;</c>. The scan counts that read unless the caller
/// names the checker as a validator, and then <c>CheckedOnly</c> is unread.
/// </summary>
public class GizmoOptions
{
    public int Used { get; set; }

    public int CheckedOnly { get; set; }
}

public sealed class GizmoConfigurationChecker
{
    public IReadOnlyList<string> Check(GizmoOptions options)
    {
        var errors = new List<string>();
        if (options.CheckedOnly < 0)
        {
            errors.Add("CheckedOnly must not be negative.");
        }

        // A read inside a lambda lands in a compiler-generated type nested in the checker.
        Func<bool> tooLarge = () => options.CheckedOnly > 1000;
        if (tooLarge())
        {
            errors.Add("CheckedOnly is too large.");
        }

        return errors;
    }
}

public sealed class Gizmo
{
    public Gizmo(GizmoOptions options)
    {
        Size = options.Used;
    }

    public int Size { get; }
}
