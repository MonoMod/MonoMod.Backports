using AsmResolver.DotNet;
using CompatUnbreaker.Utilities.AsmResolver;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Editing;
using Accessibility = Microsoft.CodeAnalysis.Accessibility;

namespace CompatUnbreaker.Tool.SkeletonGeneration.CodeGeneration;

internal static partial class SyntaxGeneratorExtensions
{
    public static SyntaxNode FieldDeclaration(this SyntaxGenerator syntaxGenerator, FieldDefinition field)
    {
        var typeContext = TypeContext.From(field, field);
        var initializer = field.Constant is { } constant
            ? syntaxGenerator.LiteralExpression(field.DeclaringType?.IsEnum == true ? field.DeclaringType.GetEnumUnderlyingType() : field.Signature.FieldType, constant.InterpretData(), typeContext)
            : null;

        return syntaxGenerator.FieldDeclaration(field, initializer);
    }

    public static SyntaxNode FieldDeclaration(this SyntaxGenerator syntaxGenerator, FieldDefinition field, SyntaxNode? initializer)
    {
        ArgumentNullException.ThrowIfNull(field.Name);
        ArgumentNullException.ThrowIfNull(field.Signature);

        var typeContext = TypeContext.From(field, field);

        return syntaxGenerator.AddAttributes(
            syntaxGenerator.FieldDeclaration(
                field.Name,
                syntaxGenerator.TypeExpression(field.Signature.FieldType, typeContext),
                (Accessibility) field.GetAccessibility(),
                field.GetModifiers(),
                initializer
            ),
            field.CustomAttributes
        );
    }
}
