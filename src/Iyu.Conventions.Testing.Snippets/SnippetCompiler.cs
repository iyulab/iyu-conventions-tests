using System.Reflection;
using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Iyu.Conventions.Testing.Snippets;

/// <summary>
/// Compiles one documentation example as a program against the library, the way a reader would paste it into a project.
/// </summary>
/// <remarks>
/// <para>
/// The example becomes a top-level program: its own <c>using</c> directives first, then <see cref="Usings"/> (the ones it
/// does not state itself), then the <see cref="StandIns"/> it needs, then its statements, then any types it declares.
/// A stand-in is a declaration of a value a reader already has from the surrounding text — the service collection, a client
/// built in the previous example. It is added only when the example uses the name and does not declare it, judged on the
/// syntax tree (a local, a parameter, a <c>foreach</c> variable, a pattern designation or a local function declares a name;
/// a member access <c>x.Name</c>, a named argument, an anonymous type's member name or an initializer's property does not
/// use one).
/// </para>
/// <para>
/// An example with no statements (it only declares types) compiles as a library. Every error-severity diagnostic is a
/// failure; warnings are not, since an example is not held to the library's own analyzer settings.
/// </para>
/// </remarks>
public sealed class SnippetCompiler
{
    private readonly IReadOnlyList<MetadataReference> _references;

    /// <summary>Creates a compiler that references <paramref name="references"/>.</summary>
    /// <param name="references">
    /// What an example may use. <see cref="ReferencesFor(Assembly[])"/> gives the platform plus the assemblies you name; you
    /// choose them, because a compiler that guesses can silently compile against nothing.
    /// </param>
    public SnippetCompiler(IEnumerable<MetadataReference> references)
    {
        ArgumentNullException.ThrowIfNull(references);
        _references = references.ToList();
        if (_references.Count == 0)
        {
            throw new ArgumentException("At least one reference is required; an empty set compiles nothing.", nameof(references));
        }
    }

    /// <summary>
    /// Namespaces every example may use without stating them — the implicit usings of an SDK project plus the ones the
    /// document states once (in its Quick Start) and assumes afterwards. Default: the console project's implicit usings.
    /// </summary>
    public IReadOnlyList<string> Usings { get; init; } =
        ["System", "System.Collections.Generic", "System.IO", "System.Linq", "System.Net.Http", "System.Threading", "System.Threading.Tasks"];

    /// <summary>
    /// Declarations by the name they declare, added to an example that uses the name without declaring it — for example
    /// <c>["services"] = "Microsoft.Extensions.DependencyInjection.IServiceCollection services = null!;"</c>.
    /// </summary>
    public IReadOnlyDictionary<string, string> StandIns { get; init; } = new Dictionary<string, string>(StringComparer.Ordinal);

    /// <summary>Whether the program compiles with nullable reference types enabled. Default: true.</summary>
    public bool Nullable { get; init; } = true;

    /// <summary>The platform assemblies plus <paramref name="assemblies"/> and everything they have loaded so far.</summary>
    public static IReadOnlyList<MetadataReference> ReferencesFor(params Assembly[] assemblies)
    {
        ArgumentNullException.ThrowIfNull(assemblies);
        var paths = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES") is string trusted)
        {
            foreach (var path in trusted.Split(Path.PathSeparator).Where(p => p.Length > 0))
            {
                paths.TryAdd(Path.GetFileName(path), path);
            }
        }

        foreach (var assembly in assemblies.Concat(AppDomain.CurrentDomain.GetAssemblies()))
        {
            if (!assembly.IsDynamic && assembly.Location.Length > 0)
            {
                paths.TryAdd(Path.GetFileName(assembly.Location), assembly.Location);
            }
        }

        return paths.Values.Select(p => (MetadataReference)MetadataReference.CreateFromFile(p)).ToList();
    }

    /// <summary>The source an example compiles as — put it in a failure message so the reader sees what was compiled.</summary>
    public string Program(string code)
    {
        ArgumentNullException.ThrowIfNull(code);
        var root = Parse(code);
        var stated = root.Usings.Select(u => u.ToString().Trim()).ToList();
        var statedNames = root.Usings.Where(u => u.Alias is null && u.StaticKeyword.IsKind(SyntaxKind.None))
            .Select(u => u.NamespaceOrType.ToString())
            .ToHashSet(StringComparer.Ordinal);

        var statements = root.Members.OfType<GlobalStatementSyntax>().Select(s => s.ToFullString()).ToList();
        var declarations = root.Members.Where(m => m is not GlobalStatementSyntax).Select(m => m.ToFullString()).ToList();
        var body = string.Concat(statements);
        var standIns = UndeclaredNames(body, StandIns.Keys).Select(name => StandIns[name]);

        var source = new StringBuilder();
        foreach (var directive in stated)
        {
            source.AppendLine(directive);
        }

        foreach (var name in Usings.Where(n => !statedNames.Contains(n)))
        {
            source.Append("using ").Append(name).AppendLine(";");
        }

        foreach (var declaration in standIns)
        {
            source.AppendLine(declaration);
        }

        // Top-level statements must precede type declarations, whatever order the example shows them in.
        source.Append(body).AppendLine();
        foreach (var declaration in declarations)
        {
            source.Append(declaration).AppendLine();
        }

        return source.ToString();
    }

    /// <summary>Compiles <paramref name="code"/> and returns its errors (empty when it compiles).</summary>
    public IReadOnlyList<Diagnostic> Compile(string code)
    {
        var tree = CSharpSyntaxTree.ParseText(Program(code), new CSharpParseOptions(LanguageVersion.Latest));
        var isLibrary = !tree.GetCompilationUnitRoot().Members.OfType<GlobalStatementSyntax>().Any();
        var compilation = CSharpCompilation.Create(
            "DocSnippet",
            [tree],
            _references,
            new CSharpCompilationOptions(
                isLibrary ? OutputKind.DynamicallyLinkedLibrary : OutputKind.ConsoleApplication,
                nullableContextOptions: Nullable ? NullableContextOptions.Enable : NullableContextOptions.Disable));

        return compilation.GetDiagnostics().Where(d => d.Severity == DiagnosticSeverity.Error).ToList();
    }

    /// <summary>Compiles <paramref name="snippet"/> and throws when it does not compile.</summary>
    /// <exception cref="SnippetException">The example has compile errors; the message lists them and the compiled source.</exception>
    public void ShouldCompile(DocSnippet snippet)
    {
        ArgumentNullException.ThrowIfNull(snippet);
        var errors = Compile(snippet.Code);
        if (errors.Count == 0)
        {
            return;
        }

        throw new SnippetException(
            $"{snippet.Key} does not compile against the current library:\n" +
            string.Join("\n", errors.Select(e => e.ToString())) + "\n--- compiled as ---\n" + Program(snippet.Code));
    }

    private static CompilationUnitSyntax Parse(string code) =>
        CSharpSyntaxTree.ParseText(code, new CSharpParseOptions(LanguageVersion.Latest)).GetCompilationUnitRoot();

    private static List<string> UndeclaredNames(string statements, IEnumerable<string> candidates)
    {
        var root = CSharpSyntaxTree.ParseText(statements, new CSharpParseOptions(LanguageVersion.Latest, kind: SourceCodeKind.Script)).GetRoot();
        var declared = root.DescendantNodes().Select(n => n switch
            {
                VariableDeclaratorSyntax v => v.Identifier.Text,
                ForEachStatementSyntax f => f.Identifier.Text,
                SingleVariableDesignationSyntax s => s.Identifier.Text,
                ParameterSyntax p => p.Identifier.Text,
                LocalFunctionStatementSyntax l => l.Identifier.Text,
                _ => null,
            })
            .OfType<string>()
            .ToHashSet(StringComparer.Ordinal);
        var used = root.DescendantNodes().OfType<IdentifierNameSyntax>()
            .Where(i => i.Parent is not (NameColonSyntax or NameEqualsSyntax)
                        && !(i.Parent is MemberAccessExpressionSyntax access && access.Name == i)
                        && !(i.Parent is AssignmentExpressionSyntax { Parent: InitializerExpressionSyntax } assignment && assignment.Left == i))
            .Select(i => i.Identifier.Text)
            .ToHashSet(StringComparer.Ordinal);

        return candidates.Where(c => used.Contains(c) && !declared.Contains(c)).Order(StringComparer.Ordinal).ToList();
    }
}
