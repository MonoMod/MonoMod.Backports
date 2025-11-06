using AsmResolver.DotNet;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Editing;

namespace CompatUnbreaker.Tool.SkeletonGeneration.CodeGeneration;

internal static partial class SyntaxGeneratorExtensions
{
    public static TypeSyntax TypeExpression(this SyntaxGenerator syntaxGenerator, ITypeDescriptor typeDescriptor, TypeContext context)
    {
        return AsmResolverTypeSyntaxGenerator.TypeExpression(typeDescriptor, context);
    }

    private static TypeSyntax TypeExpression(this SyntaxGenerator syntaxGenerator, ITypeDescriptor typeSymbol, RefKind refKind, TypeContext context)
    {
        var type = syntaxGenerator.TypeExpression(typeSymbol, context);
        if (type is RefTypeSyntax refType)
        {
            type = refType.Type;
        }

        return refKind switch
        {
            RefKind.Ref => SyntaxFactory.RefType(type),
            RefKind.RefReadOnly => SyntaxFactory.RefType(SyntaxFactory.Token(SyntaxKind.RefKeyword), SyntaxFactory.Token(SyntaxKind.ReadOnlyKeyword), type),
            _ => type,
        };
    }

    public static SyntaxNode Declaration(this SyntaxGenerator syntaxGenerator, IMemberDefinition member, Func<IMemberDefinition, bool>? memberFilter = null)
    {
        return member switch
        {
            TypeDefinition type => syntaxGenerator.TypeDeclaration(type, memberFilter),
            MethodDefinition method => syntaxGenerator.MethodDeclaration(method),
            FieldDefinition field => syntaxGenerator.FieldDeclaration(field),
            PropertyDefinition property => syntaxGenerator.PropertyDeclaration(property),
            EventDefinition @event => syntaxGenerator.EventDeclaration(@event),
            _ => throw new ArgumentOutOfRangeException(nameof(member)),
        };
    }
}
