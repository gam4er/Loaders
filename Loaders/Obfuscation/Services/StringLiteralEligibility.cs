using System;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Loaders.Obfuscation.Services
{
    internal static class StringLiteralEligibility
    {
        public static string GetSkipReason(ExpressionSyntax node, SemanticModel semanticModel)
        {
            if (node == null)
            {
                return "null-node";
            }

            if (GeneratedFileClassifier.IsGeneratedFile(node.SyntaxTree?.FilePath))
            {
                return "generated-file";
            }

            if (node.Ancestors().Any(ancestor => ancestor is AttributeArgumentSyntax))
            {
                return "attribute-argument";
            }

            if (node.Ancestors().Any(ancestor => ancestor is CaseSwitchLabelSyntax))
            {
                return "case-label";
            }

            if (node.Ancestors().Any(ancestor => ancestor is GotoStatementSyntax))
            {
                return "goto-case";
            }

            if (node.Ancestors().Any(ancestor => ancestor is ConstantPatternSyntax))
            {
                return "constant-pattern";
            }

            if (node.Ancestors().OfType<ParameterSyntax>().Any(parameter => parameter.Default?.Value == node))
            {
                return "default-parameter";
            }

            if (node.Ancestors().OfType<LockStatementSyntax>().Any(lockStatement => lockStatement.Expression == node))
            {
                return "lock-expression";
            }

            if (node.Ancestors().OfType<FixedStatementSyntax>().Any())
            {
                return "fixed-statement";
            }

            foreach (var declaration in node.Ancestors().OfType<VariableDeclarationSyntax>())
            {
                if (declaration.Parent is FieldDeclarationSyntax fieldDeclaration &&
                    fieldDeclaration.Modifiers.Any(SyntaxKind.ConstKeyword))
                {
                    return "const-field";
                }

                if (declaration.Parent is LocalDeclarationStatementSyntax localDeclaration &&
                    localDeclaration.Modifiers.Any(SyntaxKind.ConstKeyword))
                {
                    return "const-local";
                }
            }

            if (IsInsideObviousExpressionTree(node))
            {
                return "expression-tree";
            }

            if (semanticModel == null)
            {
                return null;
            }

            var typeInfo = semanticModel.GetTypeInfo(node);
            if (IsFormattableStringType(typeInfo.ConvertedType) || IsFormattableStringType(typeInfo.Type))
            {
                return "formattable-string-target";
            }

            if (node is InterpolatedStringExpressionSyntax && !IsStringLikeInterpolatedTarget(typeInfo))
            {
                return "non-string-interpolated-target";
            }

            return null;
        }

        private static bool IsStringLikeInterpolatedTarget(TypeInfo typeInfo)
        {
            if (typeInfo.ConvertedType == null)
            {
                return true;
            }

            return typeInfo.ConvertedType.SpecialType == SpecialType.System_String ||
                   typeInfo.ConvertedType.SpecialType == SpecialType.System_Object;
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

        private static bool IsExpressionTreeType(TypeSyntax type)
        {
            var text = RemoveWhitespace(type.ToString());
            return text.Contains("Expression<") ||
                   text.Contains("Expressions.Expression<");
        }

        private static bool IsFormattableStringType(ITypeSymbol type)
        {
            if (type == null)
            {
                return false;
            }

            var displayName = type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
            return string.Equals(displayName, "global::System.FormattableString", StringComparison.Ordinal) ||
                   string.Equals(displayName, "global::System.IFormattable", StringComparison.Ordinal);
        }

        private static string RemoveWhitespace(string value)
        {
            return new string(value.Where(c => !char.IsWhiteSpace(c)).ToArray());
        }
    }
}
