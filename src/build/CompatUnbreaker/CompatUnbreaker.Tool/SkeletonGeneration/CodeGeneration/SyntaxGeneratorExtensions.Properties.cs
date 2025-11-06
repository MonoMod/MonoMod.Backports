using AsmResolver.DotNet;
using AsmResolver.DotNet.Signatures;
using CompatUnbreaker.Tool.Utilities.AsmResolver;
using CompatUnbreaker.Utilities.AsmResolver;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Editing;
using Accessibility = Microsoft.CodeAnalysis.Accessibility;

namespace CompatUnbreaker.Tool.SkeletonGeneration.CodeGeneration;

internal static partial class SyntaxGeneratorExtensions
{
    public static SyntaxNode PropertyDeclaration(
        this SyntaxGenerator syntaxGenerator,
        PropertyDefinition property,
        IEnumerable<SyntaxNode>? getAccessorStatements = null,
        IEnumerable<SyntaxNode>? setAccessorStatements = null
    )
    {
        /*property.IsIndexer ? syntaxGenerator.IndexerDeclaration(property) :*/

        var typeContext = TypeContext.From(property, property);

        var propertyAccessibility = (Accessibility) property.GetAccessibility();
        var getMethodSymbol = property.GetMethod;
        var setMethodSymbol = property.SetMethod;

        SyntaxNode? getAccessor = null;
        SyntaxNode? setAccessor = null;

        if (getMethodSymbol is not null)
        {
            var getMethodAccessibility = (Accessibility) getMethodSymbol.GetAccessibility();
            getAccessor = syntaxGenerator.GetAccessorDeclaration(getMethodAccessibility < propertyAccessibility ? getMethodAccessibility : Accessibility.NotApplicable, getAccessorStatements);
        }

        if (setMethodSymbol is not null)
        {
            var setMethodAccessibility = (Accessibility) setMethodSymbol.GetAccessibility();
            setAccessor = SetAccessorDeclaration(
                syntaxGenerator,
                setMethodAccessibility < propertyAccessibility ? setMethodAccessibility : Accessibility.NotApplicable,
                isInitOnly: setMethodSymbol.IsInitOnly(),
                setAccessorStatements);
        }

        var propDecl = PropertyDeclaration(
            syntaxGenerator,
            property.Name,
            TypeExpression(syntaxGenerator, property.Signature.ReturnType, property.GetRefKind(), typeContext),
            getAccessor,
            setAccessor,
            propertyAccessibility,
            property.GetModifiers());

        // TODO
        // if (property.ExplicitInterfaceImplementations.Length > 0)
        // {
        //     propDecl = this.WithExplicitInterfaceImplementations(propDecl,
        //         ImmutableArray<ISymbol>.CastUp(property.ExplicitInterfaceImplementations));
        // }

        return syntaxGenerator.AddAttributes(propDecl, property.CustomAttributes);
    }

    private static bool IsInitOnly(this MethodDefinition method)
    {
        return !method.IsStatic /*&& method.PropertySet */ &&
               method.Signature.ReturnType?.HasCustomModifier(m => m.IsRequired && m.IsTypeOf("System.Runtime.CompilerServices", "IsExternalInit")) == true;
    }

    private static RefKind GetRefKind(this PropertyDefinition property)
    {
        if (property.Signature.ReturnType is ByReferenceTypeSignature)
        {
            if (property.HasIsReadOnlyAttribute())
            {
                return RefKind.RefReadOnly;
            }

            return RefKind.Ref;
        }

        return RefKind.None;
    }
}
