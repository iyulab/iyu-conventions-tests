# Changelog

All notable changes to this project are documented here.
Follows [Keep a Changelog](https://keepachangelog.com/en/1.1.0/) conventions.

## [0.13.0] - 2026-10-13

### Changed
- **Breaking: a listed property states what a reader can do with it.** `LISTING002` now also compares a property's public
  accessors (`get` / `set` / `init`) and `required`, and a record and a class are different kinds. A listing that showed
  `{ get; init; }` for a settable property (or left out `required`) passed before and now fails. Migration: write the
  property as the type declares it; a listing that calls a record `class` (or the reverse) is no longer recognised as a
  listing — use the type's kind.

### Added
- **`SnippetCompiler.StrictListings`** (opt-in, with `CheckListings`) for documents whose type-only examples are all API
  references: an example that only declares types without bodies must restate a library type — one that names a type no
  visible namespace exports, or lists it as another kind, is `LISTING004` (without it, such an example compiles as the
  reader's own type and checks nothing) — and a listed method's parameter names must be the type's (`LISTING002`).

## [0.12.0] - 2026-10-13

### Added
- **A listing is checked against the real type.** With `SnippetCompiler.CheckListings = true`, an example that only restates
  library types (an interface, an options class or an enum with its members and no bodies) is no longer compiled, which
  checked only its syntax. Each listed member is looked up on the real type: a member the type does not declare is
  `LISTING001`, one with another signature is `LISTING002`, and a listing that matches a type in two visible namespaces is
  `LISTING003`. This works in `Compile`/`ShouldCompile` and in `CompileDocument`. `DocumentCompilation.ListedTypes` counts
  the types checked this way. The option is off by default.

## [0.11.0] - 2026-10-13

### Added
- **A document's examples can be compiled together.** `SnippetCompiler.CompileDocument(snippets)` returns a
  `DocumentCompilation`. A type one example declares is visible to the others, so a guide that writes a handler in one
  example and registers it in the next compiles. Each example's statements run in a method of their own, so a local
  does not leak from one example to the next. Errors are reported per example (`ErrorsOf`, `ShouldCompile`); errors that
  belong to no example are reported by `SharedErrors` and `ShouldHaveNoSharedErrors`. An example that restates a library
  type (a listing of an interface or an options class) is compiled on its own: shared, it would hide the library's type
  from the rest of the document.

## [0.10.1] - 2026-10-13

### Fixed
- **A type stand-in named only by another stand-in is declared.** When an example uses `llm` and the value stand-in is
  `ChatModel llm = new();`, the type stand-in `ChatModel` is now added too; so is a type stand-in that another added type
  stand-in uses. Before, only a type the example itself named was added, and such an example failed with CS0246.

## [0.10.0] - 2026-10-13

### Added
- **`SnippetCompiler.StandIns` can declare a type.** A stand-in whose declaration is a class, record, struct, interface,
  enum or delegate (the reader's own implementation of a library interface, such as `sealed class MyHandler : IHandler`)
  is added after the example's statements, where a top-level program allows a type. It counts as used when the example's
  statements or its own types use it, and it is not added when the example declares a type of that name. Before, it was
  put in front of the statements and failed to compile.

## [0.9.0] - 2026-10-12

### Added
- **`DocSnippets.Read(path, file)` names the document in the results.** Pass a path relative to the repository
  (`src/Package/README.md`) when a test reads several documents with the same file name. `DocSnippet.Key` then shows that
  name, so two READMEs' examples no longer share a key. A rooted path is still shortened to its file name.

### Fixed
- **`SnippetCompiler` no longer adds a stand-in next to an example's own `using var` / `await using var` declaration.**
  The stand-in check parsed the example in script mode, which does not read a top-level `using var x = …;` as a
  declaration, so the example failed with CS0128 («a local named x is already defined»).

## [0.8.0] - 2026-10-12

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
