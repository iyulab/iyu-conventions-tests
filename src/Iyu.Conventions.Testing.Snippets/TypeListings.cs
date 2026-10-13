using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Iyu.Conventions.Testing.Snippets;

/// <summary>
/// Checks a type listing — an example that restates a library type to show its shape — against the real type, member by
/// member: a listed member the type does not declare, or declares with another signature, is reported. Compiling a listing
/// checks only its syntax: it declares a second type of that name.
/// </summary>
internal static class TypeListings
{
#pragma warning disable RS2008 // these label documentation findings for a test; no analyzer ships them
    internal static readonly DiagnosticDescriptor MissingMember = new(
        "LISTING001", "Listed member does not exist",
        "The listing of {0} names '{1}', which the type does not declare", "Docs", DiagnosticSeverity.Error, true);

    internal static readonly DiagnosticDescriptor DifferentSignature = new(
        "LISTING002", "Listed member has another signature",
        "The listing of {0} shows '{1}', but the type declares {2}", "Docs", DiagnosticSeverity.Error, true);

    internal static readonly DiagnosticDescriptor AmbiguousListing = new(
        "LISTING003", "Listed type is ambiguous",
        "The listing of {0} matches {1}; state the namespace in the example", "Docs", DiagnosticSeverity.Error, true);

    internal static readonly DiagnosticDescriptor UnknownType = new(
        "LISTING004", "Listed type is not a library type",
        "The listing declares {0}, {1}", "Docs", DiagnosticSeverity.Error, true);
#pragma warning restore RS2008

    /// <summary>
    /// A listing: the declarations that restate a library type, each with its candidates, and — only when the compiler
    /// requires every listing to restate one — the declarations that do not, with a same-named type of another kind if the
    /// library has one.
    /// </summary>
    internal sealed record Listing(
        List<(BaseTypeDeclarationSyntax Declaration, List<INamedTypeSymbol> Candidates)> Types,
        List<(BaseTypeDeclarationSyntax Declaration, INamedTypeSymbol? OtherKind)> Unmatched,
        bool Strict);

    /// <summary>
    /// The library types <paramref name="root"/> restates, when the example declares nothing but types, none of them (an
    /// interface aside) with a body. A declaration that names no library type of the same kind in a namespace the example
    /// can see makes the example a sample (null) — unless <paramref name="requireLibraryTypes"/>, when it is kept as
    /// unmatched and reported. Null when the example is not a listing — a sample class that implements something.
    /// </summary>
    internal static Listing? Listed(
        CompilationUnitSyntax root, IReadOnlyList<string> usings, Compilation library, bool requireLibraryTypes = false)
    {
        // Enumerated, not rebuilt: a new SyntaxList (what `[m]` would be target-typed to) holds detached copies, whose
        // locations point into a tree without the example's path.
        var members = root.Members
            .SelectMany(m => m is BaseNamespaceDeclarationSyntax ns ? ns.Members.AsEnumerable() : Enumerable.Repeat(m, 1))
            .ToList();
        if (members.Count == 0 || !members.All(m => m is BaseTypeDeclarationSyntax))
        {
            return null;
        }

        var declared = members.Cast<BaseTypeDeclarationSyntax>().ToList();
        if (declared.Any(d => d is not InterfaceDeclarationSyntax && d.DescendantNodes().Any(n => n is BlockSyntax or ArrowExpressionClauseSyntax)))
        {
            return null;
        }

        var visible = usings
            .Concat(root.Usings.Where(u => u.Alias is null && u.StaticKeyword.IsKind(SyntaxKind.None)).Select(u => u.NamespaceOrType.ToString()))
            .Distinct(StringComparer.Ordinal)
            .ToList();
        var listed = declared.Select(d => (d, Candidates(d, visible, library))).ToList();
        var matched = listed.Where(l => l.Item2.Count > 0).ToList();
        if (matched.Count == listed.Count)
        {
            return new Listing(matched, [], requireLibraryTypes);
        }

        return requireLibraryTypes
            ? new Listing(matched, [.. listed.Where(l => l.Item2.Count == 0).Select(l => (l.d, OtherKind(l.d, visible, library)))], Strict: true)
            : null;
    }

    /// <summary>The members of each listed type that the library type does not declare, or declares differently.</summary>
    internal static IEnumerable<Diagnostic> Check(Listing listing)
    {
        foreach (var (declaration, other) in listing.Unmatched)
        {
            var name = declaration.Identifier.Text;
            yield return Diagnostic.Create(UnknownType, declaration.Identifier.GetLocation(), name, other is null
                ? "which no namespace the example can see exports as a public type"
                : $"as a {KindOf(declaration)}, but {other.ToDisplayString()} is a {KindOf(other)}");
        }

        foreach (var (declaration, candidates) in listing.Types)
        {
            if (candidates.Count > 1)
            {
                yield return Diagnostic.Create(AmbiguousListing, declaration.Identifier.GetLocation(), declaration.Identifier.Text,
                    string.Join(", ", candidates.Select(c => c.ToDisplayString())));
                continue;
            }

            foreach (var member in declaration.ChildNodes().OfType<MemberDeclarationSyntax>())
            {
                if (CheckMember(candidates[0], member, listing.Strict) is { } diagnostic)
                {
                    yield return diagnostic;
                }
            }
        }
    }

    private static List<INamedTypeSymbol> Candidates(BaseTypeDeclarationSyntax declaration, IReadOnlyList<string> visible, Compilation library)
    {
        var declaredIn = declaration.Ancestors().OfType<BaseNamespaceDeclarationSyntax>().FirstOrDefault()?.Name.ToString();
        var namespaces = declaredIn is null ? visible : [declaredIn];
        var arity = declaration is TypeDeclarationSyntax { TypeParameterList: { } parameters } ? parameters.Parameters.Count : 0;
        var metadataName = arity == 0 ? declaration.Identifier.Text : $"{declaration.Identifier.Text}`{arity}";
        return namespaces
            .Select(ns => library.GetTypeByMetadataName($"{ns}.{metadataName}"))
            .OfType<INamedTypeSymbol>()
            .Where(t => t.DeclaredAccessibility == Accessibility.Public && SameKind(declaration, t))
            .Distinct<INamedTypeSymbol>(SymbolEqualityComparer.Default)
            .ToList();
    }

    // A record and a class are different kinds here: a listing that calls a record a class (or the reverse) misstates how
    // the type is built and compared.
    private static bool SameKind(BaseTypeDeclarationSyntax declaration, INamedTypeSymbol type) =>
        string.Equals(KindOf(declaration), KindOf(type), StringComparison.Ordinal);

    private static string KindOf(BaseTypeDeclarationSyntax declaration) => declaration switch
    {
        InterfaceDeclarationSyntax => "interface",
        EnumDeclarationSyntax => "enum",
        StructDeclarationSyntax => "struct",
        RecordDeclarationSyntax r when r.ClassOrStructKeyword.IsKind(SyntaxKind.StructKeyword) => "record struct",
        RecordDeclarationSyntax => "record",
        _ => "class",
    };

    private static string KindOf(INamedTypeSymbol type) => type.TypeKind switch
    {
        TypeKind.Interface => "interface",
        TypeKind.Enum => "enum",
        TypeKind.Struct => type.IsRecord ? "record struct" : "struct",
        TypeKind.Delegate => "delegate",
        _ => type.IsRecord ? "record" : "class",
    };

    /// <summary>A public type with the declaration's name in a visible namespace but of another kind, if there is one.</summary>
    private static INamedTypeSymbol? OtherKind(BaseTypeDeclarationSyntax declaration, IReadOnlyList<string> visible, Compilation library)
    {
        var declaredIn = declaration.Ancestors().OfType<BaseNamespaceDeclarationSyntax>().FirstOrDefault()?.Name.ToString();
        var namespaces = declaredIn is null ? visible : [declaredIn];
        var arity = declaration is TypeDeclarationSyntax { TypeParameterList: { } parameters } ? parameters.Parameters.Count : 0;
        var metadataName = arity == 0 ? declaration.Identifier.Text : $"{declaration.Identifier.Text}`{arity}";
        return namespaces
            .Select(ns => library.GetTypeByMetadataName($"{ns}.{metadataName}"))
            .OfType<INamedTypeSymbol>()
            .FirstOrDefault(t => t.DeclaredAccessibility == Accessibility.Public);
    }

    private static Diagnostic? CheckMember(INamedTypeSymbol listed, MemberDeclarationSyntax member, bool strict)
    {
        switch (member)
        {
            case MethodDeclarationSyntax method:
                {
                    var name = method.Identifier.Text;
                    var real = Lookup(listed, name).OfType<IMethodSymbol>().Where(m => m.MethodKind == MethodKind.Ordinary).ToList();
                    if (real.Count == 0)
                    {
                        return Diagnostic.Create(MissingMember, method.Identifier.GetLocation(), listed.Name, name);
                    }

                    // Strict listings also state parameter names (a reader passes arguments by them); by default a guide may
                    // shorten them (`ct` for `cancellationToken`), so only the types are compared.
                    var shown = Signature(Format(method.ReturnType), name,
                        method.ParameterList.Parameters.Select(p => (p.Type is null ? "?" : Format(p.Type)) + (strict ? " " + p.Identifier.Text : "")));
                    var declared = real.Select(m => Signature(Format(m.ReturnType), name, m.Parameters.Select(p => Format(p.Type) + (strict ? " " + p.Name : ""))))
                        .Distinct(StringComparer.Ordinal).ToList();
                    return declared.Contains(shown, StringComparer.Ordinal)
                        ? null
                        : Diagnostic.Create(DifferentSignature, method.Identifier.GetLocation(), listed.Name, shown, string.Join(" | ", declared));
                }

            case PropertyDeclarationSyntax property:
                {
                    var name = property.Identifier.Text;
                    var real = Lookup(listed, name).OfType<IPropertySymbol>().FirstOrDefault();
                    if (real is null)
                    {
                        return Diagnostic.Create(MissingMember, property.Identifier.GetLocation(), listed.Name, name);
                    }

                    // The accessors a reader can use (get, set or init) and `required` are part of the property's contract:
                    // a listing that shows `set` for an init-only property tells the reader they can change it later.
                    var shown = PropertyText(
                        property.Modifiers.Any(m => m.IsKind(SyntaxKind.RequiredKeyword)), Format(property.Type), name, ShownAccessors(property));
                    var declared = PropertyText(real.IsRequired, Format(real.Type), name, PublicAccessors(real));
                    return string.Equals(shown, declared, StringComparison.Ordinal)
                        ? null
                        : Diagnostic.Create(DifferentSignature, property.Identifier.GetLocation(), listed.Name, shown, declared);
                }

            case EnumMemberDeclarationSyntax enumMember:
                return listed.GetMembers(enumMember.Identifier.Text).OfType<IFieldSymbol>().Any()
                    ? null
                    : Diagnostic.Create(MissingMember, enumMember.Identifier.GetLocation(), listed.Name, enumMember.Identifier.Text);

            case FieldDeclarationSyntax field:
                {
                    var variable = field.Declaration.Variables[0];
                    return Lookup(listed, variable.Identifier.Text).Any(s => s is IFieldSymbol or IPropertySymbol)
                        ? null
                        : Diagnostic.Create(MissingMember, variable.Identifier.GetLocation(), listed.Name, variable.Identifier.Text);
                }

            default:
                return null;
        }
    }

    /// <summary>The public members named <paramref name="name"/> on the type, its base classes, and (for an interface) the interfaces it extends.</summary>
    private static IEnumerable<ISymbol> Lookup(INamedTypeSymbol type, string name)
    {
        var types = new List<INamedTypeSymbol> { type };
        if (type.TypeKind == TypeKind.Interface)
        {
            types.AddRange(type.AllInterfaces);
        }
        else
        {
            for (var b = type.BaseType; b is not null; b = b.BaseType)
            {
                types.Add(b);
            }
        }

        return types.SelectMany(t => t.GetMembers(name)).Where(m => m.DeclaredAccessibility == Accessibility.Public);
    }

    private static string PropertyText(bool required, string type, string name, string accessors) =>
        $"{(required ? "required " : "")}{type} {name} {{ {accessors} }}";

    /// <summary>The accessors a listed property shows without an access modifier (a `private set` is not the reader's).</summary>
    private static string ShownAccessors(PropertyDeclarationSyntax property) =>
        property.AccessorList is null
            ? "get;"
            : string.Join(" ", property.AccessorList.Accessors
                .Where(a => a.Modifiers.Count == 0)
                .Select(a => a.Keyword.Text + ";"));

    private static string PublicAccessors(IPropertySymbol property)
    {
        var accessors = new List<string>();
        if (property.GetMethod is { DeclaredAccessibility: Accessibility.Public })
        {
            accessors.Add("get;");
        }

        if (property.SetMethod is { DeclaredAccessibility: Accessibility.Public } set)
        {
            accessors.Add(set.IsInitOnly ? "init;" : "set;");
        }

        return string.Join(" ", accessors);
    }

    private static string Signature(string returnType, string name, IEnumerable<string> parameters) =>
        $"{returnType} {name}({string.Join(", ", parameters)})";

    /// <summary>A type as a listing writes it: simple names, C# keywords, nullability not distinguished.</summary>
    private static string Format(TypeSyntax type) => type switch
    {
        PredefinedTypeSyntax p => p.Keyword.Text,
        NullableTypeSyntax n => Format(n.ElementType),
        ArrayTypeSyntax a => Format(a.ElementType) + string.Concat(a.RankSpecifiers.Select(_ => "[]")),
        QualifiedNameSyntax q => Format(q.Right),
        AliasQualifiedNameSyntax a => Format(a.Name),
        GenericNameSyntax g => $"{g.Identifier.Text}<{string.Join(", ", g.TypeArgumentList.Arguments.Select(Format))}>",
        IdentifierNameSyntax i => i.Identifier.Text,
        TupleTypeSyntax t => $"({string.Join(", ", t.Elements.Select(e => Format(e.Type)))})",
        RefTypeSyntax r => Format(r.Type),
        _ => type.ToString(),
    };

    private static string Format(ITypeSymbol type) => type switch
    {
        IArrayTypeSymbol a => Format(a.ElementType) + "[]",
        INamedTypeSymbol { OriginalDefinition.SpecialType: SpecialType.System_Nullable_T } n => Format(n.TypeArguments[0]),
        INamedTypeSymbol { IsTupleType: true } n => $"({string.Join(", ", n.TupleElements.Select(e => Format(e.Type)))})",
        _ when type.SpecialType != SpecialType.None => type.ToDisplayString(SymbolDisplayFormat.MinimallyQualifiedFormat).TrimEnd('?'),
        INamedTypeSymbol { IsGenericType: true } n => $"{n.Name}<{string.Join(", ", n.TypeArguments.Select(Format))}>",
        _ => type.Name,
    };
}
