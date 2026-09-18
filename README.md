# Iyu.Conventions.Testing

Convention checks for .NET libraries, run from a repository's own test project.

The first check finds **public options that nothing in the library reads**. An option nothing reads is a promise the library does not keep: a caller sets it, and nothing changes and nothing is reported. No build, test or review notices it, because nothing fails.

The package has no dependency beyond the runtime. Assertions throw `RosterMismatchException`, so it works with xUnit, NUnit, MSTest or anything else that treats an exception as a failure.

## Usage

```csharp
using System.Reflection;
using Iyu.Conventions.Testing;

public class ConventionsTests
{
    // The assemblies whose code may read the options. You choose them: a scanner that guesses which
    // assemblies to load can silently scan none and report nothing unread.
    private static readonly Assembly[] Libraries = [typeof(MyLibrary.Client).Assembly, typeof(MyLibrary.Storage.Store).Assembly];

    // Options the repository accepts as unread today, as a deliberate and visible decision.
    // Shrink this list; never grow it silently.
    private static readonly Dictionary<string, string[]> KnownUnread = new()
    {
        ["MyLibrary.ClientOptions"] = ["LegacyMode"],
    };

    [Fact]
    public void EveryPublicOption_IsRead() =>
        OptionsReachability.Scan(Libraries, OptionsTypes.NamedWith("Options"))
            .ShouldMatchRoster(KnownUnread);
}
```

The roster fails in both directions. A newly added option that nothing reads fails, and so does a known entry for an option that has since been wired: both are changes the roster has to record on purpose.

## What counts as a read

- A call to the option property's getter from any type outside the options type.
- A read inside the options type, in a member the library calls from outside it (a `Validate()`, a computed property). A fluent method that returns `this` counts.
- **Not** a read made only to copy the options into a new instance: the compiler's record copy method, or any method returning the options type whose body creates one (a constructor call, a record copy, `MemberwiseClone`).

Reading is necessary, not sufficient. An option can be read and still have no effect, and that has no static signal. A property is attributed to the type that declares it.

## Report

`OptionsReachability.Scan` returns an `OptionsReachabilityReport`:

| Member | Meaning |
|---|---|
| `OptionTypes` | Every options type found |
| `Unread` | Per options type (full name), the public properties nothing reads |
| `Read` | Every read property, as `Namespace.Type.Property` |
| `CrossAssemblyReads` | Reads whose reader lives in another assembly than the options type |

## License

MIT
