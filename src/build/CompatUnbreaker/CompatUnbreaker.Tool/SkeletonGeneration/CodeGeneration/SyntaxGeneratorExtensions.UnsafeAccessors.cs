using System.Runtime.CompilerServices;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Editing;

namespace CompatUnbreaker.Tool.SkeletonGeneration.CodeGeneration;

internal static partial class SyntaxGeneratorExtensions
{
    [UnsafeAccessor(UnsafeAccessorKind.Method)]
    private static extern SyntaxNode ClassDeclaration(
        this SyntaxGenerator @this,
        bool isRecord,
        string name,
        IEnumerable<SyntaxNode>? typeParameters,
        Accessibility accessibility,
        DeclarationModifiers modifiers,
        SyntaxNode? baseType,
        IEnumerable<SyntaxNode>? interfaceTypes,
        IEnumerable<SyntaxNode>? members
    );

    [UnsafeAccessor(UnsafeAccessorKind.Method)]
    private static extern SyntaxNode StructDeclaration(
        this SyntaxGenerator @this,
        bool isRecord,
        string name,
        IEnumerable<SyntaxNode>? typeParameters,
        Accessibility accessibility,
        DeclarationModifiers modifiers,
        IEnumerable<SyntaxNode>? interfaceTypes,
        IEnumerable<SyntaxNode>? members
    );

    [UnsafeAccessor(UnsafeAccessorKind.Method)]
    private static extern SyntaxNode EnumDeclaration(
        this SyntaxGenerator @this,
        string name,
        SyntaxNode? underlyingType,
        Accessibility accessibility = Accessibility.NotApplicable,
        DeclarationModifiers modifiers = default,
        IEnumerable<SyntaxNode>? members = null
    );

    [UnsafeAccessor(UnsafeAccessorKind.Method)]
    private static extern SyntaxNode MethodDeclaration(
        this SyntaxGenerator @this,
        string name,
        IEnumerable<SyntaxNode>? parameters,
        IEnumerable<SyntaxNode>? typeParameters,
        SyntaxNode? returnType,
        Accessibility accessibility,
        DeclarationModifiers modifiers,
        IEnumerable<SyntaxNode>? statements
    );

    [UnsafeAccessor(UnsafeAccessorKind.Method)]
    private static extern SyntaxNode ParameterDeclaration(
        this SyntaxGenerator @this,
        string name,
        SyntaxNode? type,
        SyntaxNode? initializer,
        RefKind refKind,
        bool isExtension,
        bool isParams,
        bool isScoped
    );

    [UnsafeAccessor(UnsafeAccessorKind.Method)]
    private static extern SyntaxNode OperatorDeclaration(
        this SyntaxGenerator @this,
        string operatorName,
        bool isImplicitConversion,
        IEnumerable<SyntaxNode>? parameters = null,
        SyntaxNode? returnType = null,
        Accessibility accessibility = Accessibility.NotApplicable,
        DeclarationModifiers modifiers = default,
        IEnumerable<SyntaxNode>? statements = null
    );

    [UnsafeAccessor(UnsafeAccessorKind.Method)]
    private static extern SyntaxNode PropertyDeclaration(
        this SyntaxGenerator @this,
        string name,
        SyntaxNode type,
        SyntaxNode? getAccessor,
        SyntaxNode? setAccessor,
        Accessibility accessibility,
        DeclarationModifiers modifiers
    );

    [UnsafeAccessor(UnsafeAccessorKind.Method)]
    private static extern SyntaxNode SetAccessorDeclaration(
        this SyntaxGenerator @this,
        Accessibility accessibility,
        bool isInitOnly,
        IEnumerable<SyntaxNode>? statements
    );

    [UnsafeAccessor(UnsafeAccessorKind.Method)]
    private static extern SyntaxNode WithTypeParameters(
        this SyntaxGenerator @this,
        SyntaxNode declaration,
        IEnumerable<SyntaxNode> typeParameters
    );

    [UnsafeAccessor(UnsafeAccessorKind.Method)]
    private static extern SyntaxNode WithTypeConstraint(
        this SyntaxGenerator @this,
        SyntaxNode declaration,
        string typeParameterName,
        SpecialTypeConstraintKind kinds,
        bool isUnamangedType,
        IEnumerable<SyntaxNode>? types
    );
}
