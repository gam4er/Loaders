using System.Collections.Generic;
using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Loaders.Obfuscation.Utilities;

namespace Loaders.Obfuscation.Rewriters
{
    /// <summary>
    /// Rewrites interpolated strings ($"...") through obfuscated composite
    /// format strings while preserving interpolation alignment and format clauses.
    /// </summary>
    internal sealed class InterpolatedStringObfuscator : CSharpSyntaxRewriter
    {
        private readonly StringObfuscationStrategy? _strategy;

        public InterpolatedStringObfuscator(StringObfuscationStrategy? strategy)
        {
            _strategy = strategy;
        }

        public override SyntaxNode VisitInterpolatedStringExpression(InterpolatedStringExpressionSyntax node)
        {
            if (StringObfuscationContext.IsExcludedContext(node))
            {
                return base.VisitInterpolatedStringExpression(node);
            }

            var formatBuilder = new StringBuilder();
            var interpolationArguments = new List<ArgumentSyntax>();

            foreach (var content in node.Contents)
            {
                if (content is InterpolatedStringTextSyntax text)
                {
                    formatBuilder.Append(text.TextToken.ValueText);
                    continue;
                }

                if (content is InterpolationSyntax interpolation)
                {
                    var argumentIndex = interpolationArguments.Count;
                    formatBuilder.Append(BuildFormatItem(argumentIndex, interpolation));
                    interpolationArguments.Add(SyntaxFactory.Argument((ExpressionSyntax)Visit(interpolation.Expression)));
                }
            }

            var formatExpression = BuildDecodeExpression(formatBuilder.ToString());
            if (interpolationArguments.Count == 0)
            {
                return formatExpression.WithTriviaFrom(node);
            }

            var arguments = new List<ArgumentSyntax>
            {
                SyntaxFactory.Argument(SyntaxFactory.ParseExpression("System.Globalization.CultureInfo.CurrentCulture")),
                SyntaxFactory.Argument(formatExpression)
            };
            arguments.AddRange(interpolationArguments);

            var formatCall = SyntaxFactory.InvocationExpression(
                SyntaxFactory.MemberAccessExpression(
                    SyntaxKind.SimpleMemberAccessExpression,
                    SyntaxFactory.ParseName("System.String"),
                    SyntaxFactory.IdentifierName("Format")))
                .WithArgumentList(SyntaxFactory.ArgumentList(SyntaxFactory.SeparatedList(arguments)));

            return formatCall.WithTriviaFrom(node);
        }

        private ExpressionSyntax BuildDecodeExpression(string plainText)
        {
            return _strategy.HasValue
                ? StringObfuscationUtil.BuildDecodeExpression(plainText, _strategy.Value)
                : StringObfuscationUtil.BuildDecodeExpression(plainText);
        }

        private static string BuildFormatItem(int argumentIndex, InterpolationSyntax interpolation)
        {
            var builder = new StringBuilder();
            builder.Append('{');
            builder.Append(argumentIndex);
            if (interpolation.AlignmentClause != null)
            {
                builder.Append(interpolation.AlignmentClause.ToString());
            }

            if (interpolation.FormatClause != null)
            {
                builder.Append(interpolation.FormatClause.ToString());
            }

            builder.Append('}');
            return builder.ToString();
        }

    }
}
