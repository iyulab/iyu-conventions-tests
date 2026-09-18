# Changelog

All notable changes to this project are documented here.
Follows [Keep a Changelog](https://keepachangelog.com/en/1.1.0/) conventions.

## [0.1.0]

### Added
- `OptionsReachability.Scan(assemblies, isOptionsType)`: finds public options that nothing in the given assemblies reads, by walking method IL for calls to each option property's getter made from outside the options type. Reads inside the options type count when they happen in a member the library calls from outside (a `Validate()`, a computed property). Reads made only to copy the options into a new instance do not count (the record copy method, or any method returning its own type whose body creates an instance). The check is on the body, so a fluent method returning `this` is not mistaken for a copy.
- `OptionsReachabilityReport.ShouldMatchRoster(knownUnread)`: asserts the unread set equals the repository's declared roster, and fails both on a newly unread option and on a known one that has since been wired. It throws `RosterMismatchException`, so it works with any test framework.
- `OptionsTypes.NamedWith(suffixes)`: a naming rule for choosing options types.
- `OperationalLanguage.Scan(assemblies, isViolation)`: finds operational text that breaks a language rule. It reads `[LoggerMessage]` message templates (matched by attribute type name, with no logging dependency) and every string literal whose next object construction in the method builds an exception, which covers direct, formatted and interpolated messages. Two rules are built in, `ContainsHangul` and `NonAscii`. `ShouldBeClean()` throws `OperationalLanguageException` listing the findings, and `LogMessagesRead` and `ExceptionLiteralsRead` let a test assert the scan saw the library's text at all.
