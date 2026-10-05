# Iyu.Conventions.Testing

Convention checks for .NET libraries, run from a repository's own test project.

Four checks today:

- **Options reachability** finds public options that nothing in the library reads.
- **Operational language** finds log and exception messages that break a language rule.
- **Async cancellation** finds public async methods that take no `CancellationToken`.
- **Result returns** finds public methods that report failure through a success flag and an error instead of an exception.

The first finds **public options that nothing in the library reads**. An option nothing reads is a promise the library does not keep: a caller sets it, and nothing changes and nothing is reported. No build, test or review notices it, because nothing fails.

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
- A read inside the options type, in a member the library calls from outside it (a computed property, a fluent method that returns `this`).
- **Not** validation (0.3.0+): a read in the options type's own `Validate…` methods, or in an `IValidateOptions<T>` validator. A range check does not make an option take effect. A `Validate…` method on another type (a guardrail's `ValidateInputAsync` reading its own options) is a use.
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

## Operational language

Log message templates and exception messages are read by operators at run time, pasted into issues and indexed by log pipelines. `OperationalLanguage` reads them from the compiled assemblies:

```csharp
[Fact]
public void OperationalTextIsAscii()
{
    var report = OperationalLanguage.Scan(Libraries, OperationalLanguage.NonAscii);
    Assert.True(report.ExceptionLiteralsRead > 0, "the scan must see the library's exception messages");
    report.ShouldBeClean();
}
```

- `[LoggerMessage]` templates, matched by attribute type name. The package does not depend on Microsoft.Extensions.Logging.
- String literals whose next object construction in the method builds an exception. This covers `throw new X("…")`, `string.Format("…", …)` passed to one, and interpolated messages.
- It does not read documentation comments, or string literals that never reach an exception (user-facing text).

The rules are plain predicates. `ContainsHangul` and `NonAscii` are built in; pass your own for anything else.

## Public API: cancellation and result returns

Two rules on the public surface (public and protected methods of exported types), read by reflection:

```csharp
// Methods the repository accepts today. Shrink these lists; never grow them silently.
private static readonly string[] KnownUncancellable = ["MyLibrary.Store.FlushAsync()"];
private static readonly string[] KnownResultReturns = [];

[Fact]
public void PublicAsyncMethods_TakeACancellationToken() =>
    AsyncCancellation.Scan(Libraries).ShouldMatchRoster(KnownUncancellable);

[Fact]
public void PublicMethods_DoNotReturnResultObjects() =>
    ResultReturns.Scan(Libraries).ShouldMatchRoster(KnownResultReturns);
```

- **`AsyncCancellation`** reports a method returning `Task`, `ValueTask`, their generic forms or `IAsyncEnumerable<T>` that has no `CancellationToken` parameter.
- **`ResultReturns`** reports a method returning, directly or through `Task<T>`/`ValueTask<T>`, a type with a public `bool` named `IsSuccess`, `Success`, `Succeeded` or `IsSuccessful` and a public property named `Error`, `ErrorMessage`, `Errors` or `Exception`. Per-item outcomes inside a collection (a batch) are not reported, nor are the result type's own factories.
- Each signature is reported once, where it is declared: an override or an interface implementation follows its declaration. A signature the library does not own (an override of a framework base, an implementation of an outside interface such as `IAsyncDisposable.DisposeAsync`) is not reported. Compiler-generated methods, property accessors and delegate types are skipped.
- A finding reads `Namespace.Type.Method(ParameterType, …)`. `ShouldMatchRoster` fails in both directions, like the options roster; `ShouldBeClean()` is the empty roster. `MembersRead` lets a test assert the scan saw the library at all.

## License

MIT
