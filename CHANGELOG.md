# Changelog

All notable changes to this project are documented here.
Follows [Keep a Changelog](https://keepachangelog.com/en/1.1.0/) conventions.

## [0.3.0] - Unreleased

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
