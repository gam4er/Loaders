using System;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Loaders.Obfuscation.Utilities;

namespace Loaders.Obfuscation.Rewriters
{
    /// <summary>
    /// Rewrites interpolated strings ($"...") by reconstructing them via string.Concat
    /// using obfuscated literal segments and original interpolations.
    /// </summary>
    internal sealed class InterpolatedStringObfuscator : CSharpSyntaxRewriter
    {
        public override SyntaxNode VisitInterpolatedStringExpression(InterpolatedStringExpressionSyntax node)
        {
            // If no interpolation parts, let base literal obfuscator handle it.
            if (!node.Contents.Any(c => c is InterpolationSyntax))
            {
                return base.VisitInterpolatedStringExpression(node);
            }

            // Build a list of expressions: obfuscated literal segments and visited interpolations
            var parts = node.Contents.Select(content =>
            {
                if (content is InterpolatedStringTextSyntax text)
                {
                    var literalText = text.TextToken.ValueText;
                    var decodeExpr = StringObfuscationUtil.BuildDecodeExpression(literalText);
                    return (ExpressionSyntax)decodeExpr;
                }

                if (content is InterpolationSyntax interpolation)
                {
                    // Visit the interpolation expression to allow other rewriters to run
                    var visitedExpr = (ExpressionSyntax)Visit(interpolation.Expression);
                    return visitedExpr;
                }

                return SyntaxFactory.LiteralExpression(SyntaxKind.StringLiteralExpression, SyntaxFactory.Literal(""));
            }).ToArray();

            // string.Concat(part1, part2, ...)
            var argList = SyntaxFactory.SeparatedList(parts.Select(SyntaxFactory.Argument));
            var concatCall = SyntaxFactory.InvocationExpression(
                SyntaxFactory.MemberAccessExpression(
                    SyntaxKind.SimpleMemberAccessExpression,
                    SyntaxFactory.IdentifierName("string"),
                    SyntaxFactory.IdentifierName("Concat")))
                .WithArgumentList(SyntaxFactory.ArgumentList(argList));

            return concatCall.WithTriviaFrom(node);
        }
    }
}
