using AsmResolver.DotNet;
using CompatUnbreaker.Tool.Utilities.AsmResolver;
using CompatUnbreaker.Utilities.AsmResolver;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Editing;
using Accessibility = Microsoft.CodeAnalysis.Accessibility;

namespace CompatUnbreaker.Tool.SkeletonGeneration.CodeGeneration;

internal static partial class SyntaxGeneratorExtensions
{
    public static SyntaxNode EventDeclaration(this SyntaxGenerator syntaxGenerator, EventDefinition @event)
    {
        ArgumentNullException.ThrowIfNull(@event.Name);
        ArgumentNullException.ThrowIfNull(@event.EventType);

        var typeContext = TypeContext.From(@event, @event);

        var isAuto = @event.AddMethod?.IsCompilerGenerated() == true;

        SyntaxNode declaration;

        if (isAuto)
        {
            declaration = syntaxGenerator.EventDeclaration(
                @event.Name,
                syntaxGenerator.TypeExpression(@event.EventType, typeContext),
                (Accessibility) @event.GetAccessibility(),
                @event.GetModifiers()
            );
        }
        else
        {
            declaration = syntaxGenerator.CustomEventDeclaration(
                @event.Name,
                syntaxGenerator.TypeExpression(@event.EventType, typeContext),
                (Accessibility) @event.GetAccessibility(),
                @event.GetModifiers()
            );
        }

        // TODO
        // if (symbol.ExplicitInterfaceImplementations.Length > 0)
        // {
        //     ev = this.WithExplicitInterfaceImplementations(ev, ImmutableArray<ISymbol>.CastUp(symbol.ExplicitInterfaceImplementations));
        // }
        return syntaxGenerator.AddAttributes(declaration, @event.CustomAttributes);
    }
}
