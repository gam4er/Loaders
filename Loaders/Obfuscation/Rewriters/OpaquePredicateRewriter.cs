using System;
using System.Collections.Generic;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Loaders.Obfuscation.Rewriters
{
    /// <summary>
    /// Injects opaque predicates and arithmetic noise into method bodies to complicate control-flow analysis.
    /// </summary>
    public sealed class OpaquePredicateRewriter : CSharpSyntaxRewriter
    {
        private readonly Random _random = new();

        public override SyntaxNode VisitMethodDeclaration(MethodDeclarationSyntax node)
        {
            if (node.Body is null || node.Modifiers.Any(SyntaxKind.AbstractKeyword) || node.Modifiers.Any(SyntaxKind.ExternKeyword))
            {
                return base.VisitMethodDeclaration(node);
            }

            var injectedStatements = GenerateNoiseStatements();
            var updatedBody = node.Body.WithStatements(node.Body.Statements.InsertRange(0, injectedStatements));
            var updatedNode = node.WithBody(updatedBody);

            return base.VisitMethodDeclaration(updatedNode);
        }

        private IEnumerable<StatementSyntax> GenerateNoiseStatements()
        {
            string seedIdentifier = GenerateIdentifier();
            string loopIdentifier = GenerateIdentifier();
            int saltOne = _random.Next(50, 5000);
            int saltTwo = _random.Next(25, 2500);
            int saltThree = _random.Next(5, 1000);

            var seedInitialization = SyntaxFactory.ParseStatement(
                $"var {seedIdentifier} = unchecked((int)((System.DateTime.UtcNow.Ticks & 0xFFFF) ^ {saltOne}));");

            var opaqueIf = SyntaxFactory.ParseStatement(
                $"if ((({seedIdentifier} >> 3) ^ {saltTwo}) % 7 == 0) {{ _ = System.Math.Abs({seedIdentifier} ^ {saltThree}); }} else {{ System.GC.KeepAlive({seedIdentifier}); }}");

            var loop = SyntaxFactory.ParseStatement(
                $"for (int {loopIdentifier} = 0; {loopIdentifier} < 3; {loopIdentifier}++) {{ {seedIdentifier} = unchecked(({seedIdentifier} << 1) ^ ({saltThree} + {loopIdentifier})); }}");

            return new[]
            {
                seedInitialization.WithTrailingTrivia(SyntaxFactory.ElasticCarriageReturnLineFeed),
                opaqueIf.WithTrailingTrivia(SyntaxFactory.ElasticCarriageReturnLineFeed),
                loop.WithTrailingTrivia(SyntaxFactory.ElasticCarriageReturnLineFeed),
            };
        }

        private string GenerateIdentifier()
        {
            const string prefix = "__noise";
            string suffix = Guid.NewGuid().ToString("N").Substring(0, 6);
            return prefix + suffix;
        }
    }
}
