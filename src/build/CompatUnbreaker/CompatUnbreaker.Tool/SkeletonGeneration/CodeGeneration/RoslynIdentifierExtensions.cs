using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Simplification;

namespace CompatUnbreaker.Tool.SkeletonGeneration.CodeGeneration;

internal static class RoslynIdentifierExtensions
{
    public static string EscapeIdentifier(
        this string identifier,
        bool isQueryContext = false
    )
    {
        var nullIndex = identifier.IndexOf('\0', StringComparison.Ordinal);
        if (nullIndex >= 0)
        {
            identifier = identifier[..nullIndex];
        }

        var needsEscaping = SyntaxFacts.GetKeywordKind(identifier) != SyntaxKind.None;

        // Check if we need to escape this contextual keyword
        needsEscaping = needsEscaping || (isQueryContext && SyntaxFacts.IsQueryContextualKeyword(SyntaxFacts.GetContextualKeywordKind(identifier)));

        return needsEscaping ? "@" + identifier : identifier;
    }

    public static SyntaxToken ToIdentifierToken(this string identifier, bool isQueryContext = false)
    {
        var escaped = identifier.EscapeIdentifier(isQueryContext);

        if (escaped.Length == 0 || escaped[0] != '@')
        {
            return SyntaxFactory.Identifier(escaped);
        }

        var unescaped = identifier.StartsWith('@')
            ? identifier[1..]
            : identifier;

        var token = SyntaxFactory.Identifier(
            default, SyntaxKind.None, "@" + unescaped, unescaped, default);

        if (!identifier.StartsWith('@'))
        {
            token = token.WithAdditionalAnnotations(Simplifier.Annotation);
        }

        return token;
    }

    public static IdentifierNameSyntax ToIdentifierName(this string identifier)
    {
        return SyntaxFactory.IdentifierName(identifier.ToIdentifierToken());
    }
}
