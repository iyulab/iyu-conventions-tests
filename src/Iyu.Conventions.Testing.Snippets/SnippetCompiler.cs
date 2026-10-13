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
    /// <c>["services"] = "Microsoft.Extensions.DependencyInjection.IServiceCollection services = null!;"</c>. A declaration
    /// that is a type (<c>["MyHandler"] = "sealed class MyHandler : IHandler { … }"</c>, the reader's own implementation of
    /// a library interface) is added after the example's statements, and counts as used when the example's own types use it.
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
        var statements = root.Members.OfType<GlobalStatementSyntax>().Select(s => s.ToFullString()).ToList();
        var declarations = root.Members.Where(m => m is not GlobalStatementSyntax).Select(m => m.ToFullString()).ToList();
        var body = string.Concat(statements);
        var typeKeys = StandIns.Where(s => IsTypeDeclaration(s.Value)).Select(s => s.Key).ToHashSet(StringComparer.Ordinal);
        var standIns = UndeclaredNames(body, StandIns.Keys.Where(k => !typeKeys.Contains(k))).Select(name => StandIns[name]).ToList();

        var typeStandIns = TypeStandIns(string.Concat(standIns) + body + string.Concat(declarations), typeKeys);

        var source = new StringBuilder();
        AppendUsings(source, root);
        foreach (var declaration in standIns)
        {
            source.AppendLine(declaration);
        }

        // Top-level statements must precede type declarations, whatever order the example shows them in.
        source.Append(body).AppendLine();
        foreach (var declaration in declarations.Concat(typeStandIns))
        {
            source.Append(declaration).AppendLine();
        }

        return source.ToString();
    }

    /// <summary>Compiles <paramref name="code"/> and returns its errors (empty when it compiles).</summary>
    public IReadOnlyList<Diagnostic> Compile(string code) => Compile(code, path: "");

    private List<Diagnostic> Compile(string code, string path)
    {
        var tree = CSharpSyntaxTree.ParseText(Program(code), new CSharpParseOptions(LanguageVersion.Latest), path: path);
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

    /// <summary>
    /// Compiles the examples of one document together, the way a reader assembles a guide: a type one example declares is
    /// visible to every other, so a later example may register the handler an earlier one wrote.
    /// </summary>
    /// <remarks>
    /// Each example is a source file of its own: its <c>using</c> directives and <see cref="Usings"/>, the types it declares,
    /// and its statements as the body of a method of their own — a local one example declares is not visible in the next,
    /// since the text cannot rely on it either — preceded by the value <see cref="StandIns"/> it uses. A type stand-in is
    /// added once for the document, when an example uses the name and no example declares a type of that name.
    /// <para>
    /// An example that declares a type the library already offers under the same name — a listing of an interface or an
    /// options class, as guides show them — is compiled on its own, as <see cref="Compile(string)"/> would: shared with
    /// the document, its declaration would hide the library's type from every other example.
    /// </para>
    /// </remarks>
    /// <param name="snippets">The examples of one document, in order. Their <see cref="DocSnippet.Key"/>s must differ.</param>
    public DocumentCompilation CompileDocument(IReadOnlyList<DocSnippet> snippets)
    {
        ArgumentNullException.ThrowIfNull(snippets);
        var duplicate = snippets.GroupBy(s => s.Key, StringComparer.Ordinal).FirstOrDefault(g => g.Count() > 1);
        if (duplicate is not null)
        {
            throw new ArgumentException($"Two examples share the key '{duplicate.Key}'.", nameof(snippets));
        }

        var typeKeys = StandIns.Where(s => IsTypeDeclaration(s.Value)).Select(s => s.Key).ToHashSet(StringComparer.Ordinal);
        var valueKeys = StandIns.Keys.Where(k => !typeKeys.Contains(k)).ToList();
        var library = CSharpCompilation.Create("References", references: _references);
        var standalone = snippets.Where(s => RestatesALibraryType(Parse(s.Code), library)).ToHashSet();
        var shared = snippets.Where(s => !standalone.Contains(s)).ToList();
        var declaredTypes = shared
            .SelectMany(s => Parse(s.Code).DescendantNodes().OfType<BaseTypeDeclarationSyntax>().Select(t => t.Identifier.Text))
            .ToHashSet(StringComparer.Ordinal);

        var sources = new Dictionary<string, string>(StringComparer.Ordinal);
        var named = new StringBuilder();
        for (var i = 0; i < shared.Count; i++)
        {
            var root = Parse(shared[i].Code);
            var body = string.Concat(root.Members.OfType<GlobalStatementSyntax>().Select(s => s.ToFullString()));
            var declarations = string.Concat(root.Members.Where(m => m is not GlobalStatementSyntax).Select(m => m.ToFullString()));
            var standIns = UndeclaredNames(body, valueKeys).Select(name => StandIns[name]).ToList();
            named.Append(string.Concat(standIns)).Append(body).Append(declarations);

            var source = new StringBuilder();
            AppendUsings(source, root);
            source.Append(declarations).AppendLine();
            if (body.Length > 0)
            {
                source.Append("internal static class DocumentExample").Append(i).AppendLine()
                    .AppendLine("{")
                    .AppendLine("    internal static async System.Threading.Tasks.Task RunAsync()")
                    .AppendLine("    {");
                foreach (var declaration in standIns)
                {
                    source.AppendLine(declaration);
                }

                source.Append(body).AppendLine()
                    .AppendLine("        await System.Threading.Tasks.Task.CompletedTask;")
                    .AppendLine("    }")
                    .AppendLine("}");
            }

            sources[shared[i].Key] = source.ToString();
        }

        // Type stand-ins the examples (or the stand-ins they pulled in) name and the document does not declare itself.
        var typeStandIns = TypeStandIns(named.ToString(), typeKeys.Where(k => !declaredTypes.Contains(k)).ToList());

        var parse = new CSharpParseOptions(LanguageVersion.Latest);
        var trees = sources.Select(s => CSharpSyntaxTree.ParseText(s.Value, parse, path: s.Key)).ToList();
        if (typeStandIns.Count > 0)
        {
            var standInSource = new StringBuilder();
            AppendUsings(standInSource, Parse(""));
            foreach (var declaration in typeStandIns)
            {
                standInSource.AppendLine(declaration);
            }

            trees.Add(CSharpSyntaxTree.ParseText(standInSource.ToString(), parse, path: DocumentCompilation.StandInsPath));
        }

        var compilation = CSharpCompilation.Create(
            "DocSnippets",
            trees,
            _references,
            new CSharpCompilationOptions(
                OutputKind.DynamicallyLinkedLibrary,
                nullableContextOptions: Nullable ? NullableContextOptions.Enable : NullableContextOptions.Disable));
        var errors = compilation.GetDiagnostics().Where(d => d.Severity == DiagnosticSeverity.Error).ToList();
        foreach (var snippet in standalone)
        {
            sources[snippet.Key] = Program(snippet.Code);
            errors.AddRange(Compile(snippet.Code, snippet.Key));
        }

        return new DocumentCompilation(sources, errors);
    }

    /// <summary>
    /// The type stand-ins <paramref name="named"/> uses, and the ones those use: a type stand-in is needed when the example
    /// names the type, when a value stand-in that was added declares a value of it (<c>ChatModel llm = new();</c>), or when
    /// another type stand-in that was added uses it — until nothing new is named.
    /// </summary>
    private List<string> TypeStandIns(string named, IReadOnlyCollection<string> candidates)
    {
        var typeStandIns = new List<string>();
        while (true)
        {
            var added = UndeclaredNames(named, candidates).Select(name => StandIns[name]).Where(d => !typeStandIns.Contains(d)).ToList();
            if (added.Count == 0)
            {
                return typeStandIns;
            }

            typeStandIns.AddRange(added);
            named += string.Concat(added);
        }
    }

    /// <summary>
    /// Whether the example declares a type that a namespace it can see (<see cref="Usings"/>, its own usings, or the
    /// namespace it declares into) already offers from the references, with the same name and arity.
    /// </summary>
    private bool RestatesALibraryType(CompilationUnitSyntax root, CSharpCompilation library)
    {
        var namespaces = Usings
            .Concat(root.Usings.Where(u => u.Alias is null && u.StaticKeyword.IsKind(SyntaxKind.None)).Select(u => u.NamespaceOrType.ToString()))
            .Concat(root.DescendantNodes().OfType<BaseNamespaceDeclarationSyntax>().Select(n => n.Name.ToString()))
            .Distinct(StringComparer.Ordinal)
            .ToList();
        foreach (var type in root.DescendantNodes().OfType<BaseTypeDeclarationSyntax>().Where(t => t.Parent is CompilationUnitSyntax or BaseNamespaceDeclarationSyntax))
        {
            var arity = type is TypeDeclarationSyntax { TypeParameterList: { } parameters } ? parameters.Parameters.Count : 0;
            var metadataName = arity == 0 ? type.Identifier.Text : $"{type.Identifier.Text}`{arity}";
            if (namespaces.Any(ns => library.GetTypeByMetadataName($"{ns}.{metadataName}") is not null))
            {
                return true;
            }
        }

        return false;
    }

    private void AppendUsings(StringBuilder source, CompilationUnitSyntax root)
    {
        var statedNames = root.Usings.Where(u => u.Alias is null && u.StaticKeyword.IsKind(SyntaxKind.None))
            .Select(u => u.NamespaceOrType.ToString())
            .ToHashSet(StringComparer.Ordinal);
        foreach (var directive in root.Usings)
        {
            source.AppendLine(directive.ToString().Trim());
        }

        foreach (var name in Usings.Where(n => !statedNames.Contains(n)))
        {
            source.Append("using ").Append(name).AppendLine(";");
        }
    }

    private static CompilationUnitSyntax Parse(string code) =>
        CSharpSyntaxTree.ParseText(code, new CSharpParseOptions(LanguageVersion.Latest)).GetCompilationUnitRoot();

    private static bool IsTypeDeclaration(string declaration) =>
        Parse(declaration).Members.Any(m => m is BaseTypeDeclarationSyntax or DelegateDeclarationSyntax);

    private static List<string> UndeclaredNames(string statements, IEnumerable<string> candidates)
    {
        // Parsed as a regular file of top-level statements, the way the example compiles: script mode does not read a
        // top-level `using var x = …;` / `await using var x = …;` as a declaration, so a stand-in for x was added beside it.
        var root = CSharpSyntaxTree.ParseText(statements, new CSharpParseOptions(LanguageVersion.Latest)).GetRoot();
        var declared = root.DescendantNodes().Select(n => n switch
            {
                VariableDeclaratorSyntax v => v.Identifier.Text,
                ForEachStatementSyntax f => f.Identifier.Text,
                SingleVariableDesignationSyntax s => s.Identifier.Text,
                ParameterSyntax p => p.Identifier.Text,
                LocalFunctionStatementSyntax l => l.Identifier.Text,
                BaseTypeDeclarationSyntax t => t.Identifier.Text,
                DelegateDeclarationSyntax d => d.Identifier.Text,
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
