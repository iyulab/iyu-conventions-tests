using System.Reflection;
using System.Reflection.Emit;
using System.Text;

namespace Iyu.Conventions.Testing;

/// <summary>
/// Finds operational text — log message templates and exception messages — that breaks a language rule.
/// Operators read these at run time, paste them into issues and search them in log pipelines; text a
/// tokenizer or an operator cannot handle makes a failure harder to triage than it has to be.
/// </summary>
/// <remarks>
/// <para>Two sources are read from the compiled assemblies, so no source paths are assumed:</para>
/// <list type="bullet">
/// <item><description>The message of every <c>[LoggerMessage]</c> attribute (matched by type name, so this
/// package does not depend on Microsoft.Extensions.Logging).</description></item>
/// <item><description>Every string literal whose next object construction in the same method builds an
/// exception: <c>throw new X("…")</c>, <c>new X(string.Format("…", …))</c>, and interpolated messages
/// alike.</description></item>
/// </list>
/// <para>
/// Documentation comments are not operational text and are not read. Neither is a literal that never
/// reaches an exception, such as a user-facing string.
/// </para>
/// </remarks>
public static class OperationalLanguage
{
    private const string LoggerMessageAttribute = "Microsoft.Extensions.Logging.LoggerMessageAttribute";

    /// <summary>A rule that flags text containing Hangul (syllables, jamo and compatibility jamo).</summary>
    public static Func<string, bool> ContainsHangul { get; } = text => text.Any(IsHangul);

    /// <summary>A rule that flags text containing any character outside ASCII.</summary>
    public static Func<string, bool> NonAscii { get; } = text => text.Any(c => c > 0x7F);

    /// <summary>
    /// Scans <paramref name="assemblies"/> for operational text that <paramref name="isViolation"/> flags.
    /// </summary>
    /// <param name="assemblies">The assemblies to read. The caller chooses them.</param>
    /// <param name="isViolation">
    /// The rule, for example <see cref="ContainsHangul"/> or <see cref="NonAscii"/>.
    /// </param>
    public static OperationalLanguageReport Scan(IEnumerable<Assembly> assemblies, Func<string, bool> isViolation)
    {
        ArgumentNullException.ThrowIfNull(assemblies);
        ArgumentNullException.ThrowIfNull(isViolation);

        var scanned = assemblies.Distinct().ToList();
        if (scanned.Count == 0)
        {
            throw new ArgumentException("At least one assembly is required; an empty scan finds nothing.", nameof(assemblies));
        }

        var findings = new List<OperationalTextFinding>();
        var logMessages = 0;
        var exceptionLiterals = 0;
        const BindingFlags all = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance |
                                 BindingFlags.Static | BindingFlags.DeclaredOnly;

        foreach (var assembly in scanned)
        {
            foreach (var type in IlInspection.SafeTypes(assembly))
            {
                IEnumerable<MethodBase> bodies = type.GetMethods(all).Cast<MethodBase>().Concat(type.GetConstructors(all));
                foreach (var method in bodies)
                {
                    foreach (var message in LoggerMessages(method))
                    {
                        logMessages++;
                        if (isViolation(message))
                        {
                            findings.Add(new OperationalTextFinding(OperationalTextKind.LogMessage, Describe(method), message));
                        }
                    }

                    foreach (var literal in ExceptionLiterals(method))
                    {
                        exceptionLiterals++;
                        if (isViolation(literal))
                        {
                            findings.Add(new OperationalTextFinding(OperationalTextKind.ExceptionMessage, Describe(method), literal));
                        }
                    }
                }
            }
        }

        return new OperationalLanguageReport(findings, logMessages, exceptionLiterals);
    }

    private static IEnumerable<string> LoggerMessages(MethodBase method)
    {
        IList<CustomAttributeData> attributes;
        try
        {
            attributes = method.GetCustomAttributesData();
        }
        catch (Exception)
        {
            yield break;
        }

        foreach (var attribute in attributes)
        {
            if (attribute.AttributeType.FullName != LoggerMessageAttribute)
            {
                continue;
            }

            foreach (var named in attribute.NamedArguments)
            {
                if (named.MemberName == "Message" && named.TypedValue.Value is string message)
                {
                    yield return message;
                }
            }

            // The positional overloads carry the message as their only string argument.
            foreach (var argument in attribute.ConstructorArguments)
            {
                if (argument.Value is string message)
                {
                    yield return message;
                }
            }
        }
    }

    private static IEnumerable<string> ExceptionLiterals(MethodBase method)
    {
        var instructions = IlReader.Read(method);
        var pending = new List<string>();
        foreach (var instruction in instructions)
        {
            if (instruction.OpCode == OpCodes.Ldstr)
            {
                string? literal;
                try
                {
                    literal = method.Module.ResolveString((int)instruction.Operand);
                }
                catch (Exception)
                {
                    continue;
                }

                pending.Add(literal);
            }
            else if (instruction.OpCode == OpCodes.Newobj)
            {
                if (pending.Count > 0 && ConstructsException(method.Module, (int)instruction.Operand))
                {
                    foreach (var literal in pending)
                    {
                        yield return literal;
                    }
                }

                pending.Clear();
            }
        }
    }

    private static bool ConstructsException(Module module, int token)
    {
        try
        {
            return module.ResolveMethod(token) is ConstructorInfo { DeclaringType: { } type }
                && typeof(Exception).IsAssignableFrom(type);
        }
        catch (Exception)
        {
            return false;
        }
    }

    private static bool IsHangul(char c) =>
        c is (>= '가' and <= '힣') or (>= 'ᄀ' and <= 'ᇿ') or (>= '㄰' and <= '㆏');

    private static string Describe(MethodBase method) => $"{method.DeclaringType?.FullName}.{method.Name}";
}

/// <summary>Where a piece of operational text was found.</summary>
public enum OperationalTextKind
{
    /// <summary>The message template of a <c>[LoggerMessage]</c> attribute.</summary>
    LogMessage = 1,

    /// <summary>A string literal that reaches an exception constructor.</summary>
    ExceptionMessage = 2,
}

/// <summary>One piece of operational text that broke the rule.</summary>
/// <param name="Kind">Log message or exception message.</param>
/// <param name="Location">The declaring type and method, as <c>Namespace.Type.Method</c>.</param>
/// <param name="Text">The offending text.</param>
public sealed record OperationalTextFinding(OperationalTextKind Kind, string Location, string Text);

/// <summary>The result of <see cref="OperationalLanguage.Scan"/>.</summary>
public sealed class OperationalLanguageReport
{
    internal OperationalLanguageReport(IReadOnlyList<OperationalTextFinding> findings, int logMessagesRead, int exceptionLiteralsRead)
    {
        Findings = findings;
        LogMessagesRead = logMessagesRead;
        ExceptionLiteralsRead = exceptionLiteralsRead;
    }

    /// <summary>Every piece of operational text the rule flagged.</summary>
    public IReadOnlyList<OperationalTextFinding> Findings { get; }

    /// <summary>
    /// How many log message templates were read. A scan that read none cannot tell a clean library from one
    /// it failed to see; assert a lower bound when the library is known to log.
    /// </summary>
    public int LogMessagesRead { get; }

    /// <summary>How many string literals reaching an exception constructor were read.</summary>
    public int ExceptionLiteralsRead { get; }

    /// <summary>Asserts that nothing was flagged.</summary>
    /// <exception cref="OperationalLanguageException">At least one piece of text was flagged.</exception>
    public void ShouldBeClean()
    {
        if (Findings.Count == 0)
        {
            return;
        }

        var builder = new StringBuilder()
            .Append(Findings.Count)
            .AppendLine(" piece(s) of operational text break the language rule. Log and exception messages are read by operators and log pipelines:");
        foreach (var finding in Findings)
        {
            builder.Append("  ").Append(finding.Kind).Append(" in ").Append(finding.Location).Append(": ").AppendLine(finding.Text);
        }

        throw new OperationalLanguageException(builder.ToString());
    }
}

/// <summary>Thrown by <see cref="OperationalLanguageReport.ShouldBeClean"/> when text was flagged.</summary>
public sealed class OperationalLanguageException : Exception
{
    /// <summary>Creates the exception with a message listing the findings.</summary>
    public OperationalLanguageException(string message)
        : base(message)
    {
    }
}
