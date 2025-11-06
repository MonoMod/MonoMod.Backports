using AsmResolver.DotNet;
using AsmResolver.PE.DotNet.Metadata.Tables;
using CompatUnbreaker.Tool.Utilities.AsmResolver;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Editing;
using static Microsoft.CodeAnalysis.CSharp.SyntaxFactory;

namespace CompatUnbreaker.Tool.SkeletonGeneration.CodeGeneration;

internal static partial class SyntaxGeneratorExtensions
{
    public static SyntaxNode WithTypeParametersAndConstraints(this SyntaxGenerator syntaxGenerator, SyntaxNode declaration, IList<GenericParameter> typeParameters, TypeContext typeContext)
    {
        if (typeParameters.Count <= 0)
            return declaration;

        declaration = WithTypeParameters(syntaxGenerator, declaration, typeParameters.Select(syntaxGenerator.TypeParameter));

        foreach (var typeParameter in typeParameters)
        {
            ArgumentNullException.ThrowIfNull(typeParameter.Name);

            var parameterContext = typeContext.WithTransformsAttributeProvider(typeParameter);
            var hasNotNullConstraint = parameterContext.Transform.TryConsumeNullableTransform() == NullableAnnotation.NotAnnotated;

            if (HasSomeConstraint(typeParameter) || hasNotNullConstraint)
            {
                var constraints = typeParameter.Constraints
                    .Where(c =>
                        c.Constraint != null &&
                        (!typeParameter.HasNotNullableValueTypeConstraint || !c.Constraint.ToTypeSignature().StripModifiers().IsTypeOf("System", "ValueType"))
                    );

                var kinds = SpecialTypeConstraintKind.None;
                if (typeParameter.HasDefaultConstructorConstraint && !typeParameter.HasNotNullableValueTypeConstraint)
                    kinds |= SpecialTypeConstraintKind.Constructor;
                if (typeParameter.HasReferenceTypeConstraint && hasNotNullConstraint)
                    kinds |= SpecialTypeConstraintKind.ReferenceType;
                if (typeParameter.HasNotNullableValueTypeConstraint)
                    kinds |= SpecialTypeConstraintKind.ValueType;

                declaration = syntaxGenerator.WithTypeConstraint(
                    declaration,
                    typeParameter.Name,
                    kinds,
                    isUnamangedType: typeParameter.HasUnmanagedTypeConstraint(),
                    types: constraints.Select(c => syntaxGenerator.TypeExpression(c.Constraint!, typeContext))
                );

                if (hasNotNullConstraint)
                {
                    if (!typeParameter.HasReferenceTypeConstraint)
                    {
                        declaration = AddTypeConstraints(
                            declaration,
                            typeParameter.Name,
                            [TypeConstraint(IdentifierName("notnull"))],
                            true
                        );
                    }
                }
                else if (typeParameter.HasReferenceTypeConstraint)
                {
                    declaration = AddTypeConstraints(
                        declaration,
                        typeParameter.Name,
                        [ClassOrStructConstraint(SyntaxKind.ClassConstraint).WithQuestionToken(Token(SyntaxKind.QuestionToken))],
                        true
                    );
                }

                if (typeParameter.HasAllowByRefLike)
                {
                    declaration = AddTypeConstraints(
                        declaration,
                        typeParameter.Name,
                        [AllowsConstraintClause([RefStructConstraint()])]
                    );
                }
            }
        }

        return declaration;
    }

    private static bool HasSomeConstraint(GenericParameter typeParameter)
    {
        return typeParameter.HasDefaultConstructorConstraint ||
               typeParameter.HasReferenceTypeConstraint ||
               typeParameter.HasNotNullableValueTypeConstraint ||
               typeParameter.Constraints.Count > 0;
    }

    private static SyntaxNode TypeParameter(this SyntaxGenerator syntaxGenerator, GenericParameter typeParameter)
    {
        return SyntaxFactory.TypeParameter(
            attributeLists: [.. syntaxGenerator.Attributes(typeParameter.CustomAttributes).Cast<AttributeListSyntax>()],
            varianceKeyword: typeParameter.Variance switch
            {
                GenericParameterAttributes.Contravariant => Token(SyntaxKind.InKeyword),
                GenericParameterAttributes.Covariant => Token(SyntaxKind.OutKeyword),
                _ => default,
            },
            Identifier(typeParameter.Name!)
        );
    }

    private static SyntaxNode AddTypeConstraints(SyntaxNode declaration, string typeParameterName, SeparatedSyntaxList<TypeParameterConstraintSyntax> constraints, bool insert = false)
    {
        return declaration switch
        {
            MethodDeclarationSyntax method => method.WithConstraintClauses(AddTypeConstraints(method.ConstraintClauses, typeParameterName, constraints, insert)),
            TypeDeclarationSyntax type => type.WithConstraintClauses(AddTypeConstraints(type.ConstraintClauses, typeParameterName, constraints, insert)),
            DelegateDeclarationSyntax @delegate => @delegate.WithConstraintClauses(AddTypeConstraints(@delegate.ConstraintClauses, typeParameterName, constraints, insert)),
            _ => declaration,
        };
    }

    private static SyntaxList<TypeParameterConstraintClauseSyntax> AddTypeConstraints(SyntaxList<TypeParameterConstraintClauseSyntax> clauses, string typeParameterName, SeparatedSyntaxList<TypeParameterConstraintSyntax> constraints, bool insert)
    {
        var clause = clauses.FirstOrDefault(c => c.Name.Identifier.ToString() == typeParameterName);

        if (clause == null)
        {
            return clauses.Add(TypeParameterConstraintClause(typeParameterName.ToIdentifierName(), constraints));
        }

        return clauses.Replace(clause, clause.WithConstraints(insert ? clause.Constraints.InsertRange(0, constraints) : clause.Constraints.AddRange(constraints)));
    }
}
