# Changelog

All notable changes to this project are documented here.
Follows [Keep a Changelog](https://keepachangelog.com/en/1.1.0/) conventions.

## [Unreleased]

### Added
- **New package `Iyu.Conventions.Testing.Snippets`: compiles the C# examples in a repository's Markdown.**
  - `DocSnippets.Read`/`Parse` read `csharp`, `cs` and `c#` fences, with or without an info string, also inside
    blockquotes. Headings are tracked outside fences only.
  - `SnippetCompiler` builds each example as a top-level program: the example's usings, then shared usings, then stand-in
    declarations for names it uses without declaring (decided on the syntax tree), then statements before types. It
    compiles with Roslyn against the references you give and fails on any error.
  - `SnippetRoster` lists deliberate fragments by heading, with the reason. It fails on a stale heading or on too few
    examples.
  - It replaces the per-repository copies of the same test. Most of those copies read only an exact ```` ```csharp ```` fence,
    so a ```` ```cs ```` example was never checked.
  - It depends on `Microsoft.CodeAnalysis.CSharp` 5.9.0. `Iyu.Conventions.Testing` itself still has no dependency.

## [0.7.0] - 2026-10-12

### Added
- **`OptionsReachability.Scan(assemblies, isOptionsType, isValidation)` with `OptionsValidation.DeclaredIn(types)`: reads in
  your own validators no longer count.** A standalone validator class that is not an `IValidateOptions<T>` (a
  `ConfigurationValidator` that range-checks settings) made an option nothing uses look honoured. Name the class and its
  reads — lambdas and local functions included — are left out, so such an option is reported unread. The two-argument
  `Scan` is unchanged.
- **`OutboundAssignment.Scan(assemblies, isOutboundType)` → `OutboundAssignmentReport.ShouldMatchRoster(knownUnassigned)`:
  the request types a library fills and sends out.** Reports the public settable properties nothing outside the type sets
  — parameters the library never sends, which an implementer reads and always gets the default for. Copies
  (`X = source.X`) and the type's own defaults do not count; `init` and `with` do.

### Changed
- **The nuget.org «Release Notes» of every package link to the GitHub release of its version**, whose notes are this CHANGELOG's section for that version.
- **The package description names all four checks** (options reachability, operational language, async cancellation, result returns); it described only the first.

## [0.6.0] - 2026-10-10

### Added
- **`OptionsTypes.WithNestedSettings(assemblies, isOptionsType)`: settings objects behind an options property are scanned
  too.** A naming rule such as `NamedWith("Options")` never saw a `Weights` or `Criteria` class held by an options type,
  so its unread members went unreported. The new rule accepts what the given rule accepts plus every public class of the
  assemblies that is the type of a public settable (or init) property of an accepted type, recursively; strings,
  collections and framework types are not followed.

## [0.5.0] - 2026-10-07

### Added
- **`OperationalLanguage.PlaceholderNamed(names)`: a rule that keeps content out of log templates.** Flags a
  `[LoggerMessage]` template with a placeholder whose whole name (case-insensitive, alignment and format ignored, escaped
  braces skipped) is one of the names — `{Query}` and `{query,20}` are flagged, `{QueryLength}` is not. Pass it to
  `OperationalLanguage.Scan` like the language rules.
- `OperationalLanguage.ContentPlaceholderNames`: a starting set of names that carry user or model text (`Content`,
  `Query`, `Prompt`, `Response`, `Input`, `Output`, `Text`, `Arguments`, …). Add your domain's names to it.

### Changed
- `OperationalLanguageReport.ShouldBeClean()` says the text «breaks the rule» rather than «the language rule», since a
  rule may now be about content.

## [0.4.0] - 2026-10-05

### Added
- **`AsyncCancellation.Scan(assemblies)`: public async methods a caller cannot cancel.** Reports each public or protected
  method of an exported type that returns `Task`, `ValueTask`, their generic forms or `IAsyncEnumerable<T>` and takes no
  `CancellationToken`, once, where the signature is declared. Overrides and implementations of outside interfaces
  (`IAsyncDisposable.DisposeAsync`) are not the library's signature and are not reported.
- **`ResultReturns.Scan(assemblies)`: public methods that return a result object** — a type with a success flag
  (`IsSuccess`/`Success`/`Succeeded`/`IsSuccessful`) and an error (`Error`/`ErrorMessage`/`Errors`/`Exception`), directly
  or through `Task<T>`/`ValueTask<T>`. Per-item outcomes inside a collection and the result type's own factories are not
  reported. `ResultReturns.IsResultType(type)` exposes the shape test.
- `PublicApiReport` for both: `Findings` (`Namespace.Type.Method(ParameterType, …)`), `MembersRead`, `ShouldMatchRoster(known)`
  (fails in both directions) and `ShouldBeClean()`.

### Changed
- **The package now carries the LICENSE text**, so a project that redistributes it can ship the MIT notice from the
  package itself.

## [0.3.0] - 2026-09-29

### Changed
- **`OptionsReachability` no longer counts validation as a read.** Reads in the options type's own `Validate…` methods
  (a hand-written `Validate()`, `IValidatableObject.Validate`, and the helpers it is split into) and in any
  `IValidateOptions<T>` validator do not count. A range check does not make an option take effect, and an option
  nothing but `Validate()` looked at used to pass the roster while having no effect. A `Validate…` method on another
  type (a guardrail's `ValidateInputAsync` reading its own options) is the feature, and still counts. A roster may
  report options as unread after this update that were only ever validated.

## [0.2.0] - 2026-09-28

### Changed
- **`OptionsReachability` no longer counts a copy written outside the options type as a read.** A getter whose value goes straight into the same property of another instance of the same options type (`new Options { X = source.X }` in a helper on some other class) carries the option; it does not honour it. Before, such a helper made every option it copied look read, and an option nothing else consumed passed the roster. A roster may report options as unread after this update that were never honoured.

### Fixed
- **An option read through a computed property the library reads is counted as read.** A computed property on the options type (`EffectiveGroup => Group ?? VaultId`) is itself a getter, and reading it from outside did not carry over to the options it reads inside. Such options were reported unread unless something else, often a copy, happened to read them.

## [0.1.0]

### Added
- `OptionsReachability.Scan(assemblies, isOptionsType)`: finds public options that nothing in the given assemblies reads, by walking method IL for calls to each option property's getter made from outside the options type. Reads inside the options type count when they happen in a member the library calls from outside (a `Validate()`, a computed property). Reads made only to copy the options into a new instance do not count (the record copy method, or any method returning its own type whose body creates an instance). The check is on the body, so a fluent method returning `this` is not mistaken for a copy.
- `OptionsReachabilityReport.ShouldMatchRoster(knownUnread)`: asserts the unread set equals the repository's declared roster, and fails both on a newly unread option and on a known one that has since been wired. It throws `RosterMismatchException`, so it works with any test framework.
- `OptionsTypes.NamedWith(suffixes)`: a naming rule for choosing options types.
- `OperationalLanguage.Scan(assemblies, isViolation)`: finds operational text that breaks a language rule. It reads `[LoggerMessage]` message templates (matched by attribute type name, with no logging dependency) and every string literal whose next object construction in the method builds an exception, which covers direct, formatted and interpolated messages. Two rules are built in, `ContainsHangul` and `NonAscii`. `ShouldBeClean()` throws `OperationalLanguageException` listing the findings, and `LogMessagesRead` and `ExceptionLiteralsRead` let a test assert the scan saw the library's text at all.
