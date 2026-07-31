using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Loaders.Obfuscation.Utilities;

namespace Loaders.Obfuscation.Rewriters
{
    /// <summary>
    /// Rewrites string literals to obfuscated decoding expressions.
    ///
    /// The transformation deliberately skips critical contexts such as
    /// attributes, const fields and DllImport signatures where literal
    /// values must remain stable for the runtime.
    /// </summary>
    internal sealed class StringLiteralObfuscator : CSharpSyntaxRewriter
    {
        private readonly StringObfuscationStrategy? _strategy;

        public StringLiteralObfuscator(StringObfuscationStrategy? strategy)
        {
            _strategy = strategy;
        }

        public override SyntaxNode VisitLiteralExpression(LiteralExpressionSyntax node)
        {
            if (!node.IsKind(SyntaxKind.StringLiteralExpression))
            {
                return base.VisitLiteralExpression(node);
            }

            if (StringObfuscationContext.IsExcludedContext(node))
            {
                return base.VisitLiteralExpression(node);
            }

            var decodeInvocation = _strategy.HasValue
                ? StringObfuscationUtil.BuildDecodeExpression(node.Token.ValueText, _strategy.Value)
                : StringObfuscationUtil.BuildDecodeExpression(node.Token.ValueText);
            return decodeInvocation.WithTriviaFrom(node);
        }
    }

    internal static class StringObfuscationContext
    {
        public static bool IsExcludedContext(ExpressionSyntax node)
        {
            if (node.Ancestors().Any(ancestor =>
                    ancestor is AttributeArgumentSyntax ||
                    ancestor is CaseSwitchLabelSyntax ||
                    ancestor is ConstantPatternSyntax ||
                    ancestor is GotoStatementSyntax))
            {
                return true;
            }

            if (node.Ancestors().OfType<ParameterSyntax>().Any(parameter => parameter.Default?.Value == node))
            {
                return true;
            }

            foreach (var declaration in node.Ancestors().OfType<VariableDeclarationSyntax>())
            {
                if (declaration.Parent is FieldDeclarationSyntax fieldDeclaration &&
                    fieldDeclaration.Modifiers.Any(SyntaxKind.ConstKeyword))
                {
                    return true;
                }

                if (declaration.Parent is LocalDeclarationStatementSyntax localDeclaration &&
                    localDeclaration.Modifiers.Any(SyntaxKind.ConstKeyword))
                {
                    return true;
                }
            }

            if (IsInsideObviousExpressionTree(node))
            {
                return true;
            }

            if (IsFormattableStringTarget(node))
            {
                return true;
            }

            return false;
        }

        private static bool IsInsideObviousExpressionTree(ExpressionSyntax node)
        {
            foreach (var lambda in node.Ancestors().OfType<LambdaExpressionSyntax>())
            {
                if (lambda.Ancestors().OfType<CastExpressionSyntax>().Any(cast => IsExpressionTreeType(cast.Type)))
                {
                    return true;
                }

                if (lambda.Ancestors().OfType<VariableDeclarationSyntax>().Any(declaration => IsExpressionTreeType(declaration.Type)))
                {
                    return true;
                }
            }

            return false;
        }

        private static bool IsFormattableStringTarget(ExpressionSyntax node)
        {
            var variableDeclaration = node.Ancestors().OfType<VariableDeclarationSyntax>().FirstOrDefault();
            if (variableDeclaration != null && IsFormattableStringType(variableDeclaration.Type))
            {
                return true;
            }

            var castExpression = node.Ancestors().OfType<CastExpressionSyntax>().FirstOrDefault();
            return castExpression != null && IsFormattableStringType(castExpression.Type);
        }

        private static bool IsExpressionTreeType(TypeSyntax type)
        {
            var text = RemoveWhitespace(type.ToString());
            return text.Contains("Expression<") ||
                   text.Contains("Expressions.Expression<");
        }

        private static bool IsFormattableStringType(TypeSyntax type)
        {
            var text = RemoveWhitespace(type.ToString());
            return text == "FormattableString" ||
                   text == "System.FormattableString" ||
                   text == "IFormattable" ||
                   text == "System.IFormattable";
        }

        private static string RemoveWhitespace(string value)
        {
            return new string(value.Where(c => !char.IsWhiteSpace(c)).ToArray());
        }
    }
}
