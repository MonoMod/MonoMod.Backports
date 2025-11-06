using System.Diagnostics.CodeAnalysis;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace CompatUnbreaker.Tool.SkeletonGeneration;

internal sealed class MethodBodyRewriter : CSharpSyntaxRewriter
{
    public static MethodBodyRewriter Instance { get; } = new();

    public static BlockSyntax Body { get; } = SyntaxFactory.Block(SyntaxFactory.ParseStatement("throw new NotImplementedException();"));
    public static ArrowExpressionClauseSyntax ExpressionBody { get; } = SyntaxFactory.ArrowExpressionClause(SyntaxFactory.ParseExpression("throw new NotImplementedException()"));

    [return: NotNullIfNotNull(nameof(node))]
    public override SyntaxNode? Visit(SyntaxNode? node)
    {
        if (node is BaseMethodDeclarationSyntax methodNode)
        {
            node = VisitBaseMethodDeclarationSyntax(methodNode);
        }

        return base.Visit(node);
    }

    private SyntaxNode VisitBaseMethodDeclarationSyntax(BaseMethodDeclarationSyntax node)
    {
        if (node.Body != null)
        {
            return node.WithBody(Body);
        }

        return node;
    }

    public override SyntaxNode VisitAccessorDeclaration(AccessorDeclarationSyntax node)
    {
        // if (node.Parent?.Parent is BasePropertyDeclarationSyntax property
        //     && (property.Modifiers.Any(SyntaxKind.AbstractKeyword) || property.Modifiers.Any(SyntaxKind.ExternKeyword)))
        // {
        //     return node;
        // }
        if (node.Body == null && node.ExpressionBody == null)
        {
            return node;
        }

        return node.WithBody(null).WithExpressionBody(ExpressionBody).WithSemicolonToken(SyntaxFactory.Token(SyntaxKind.SemicolonToken));
    }
}
