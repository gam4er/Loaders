using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Loaders.Obfuscation.Utilities;

namespace Loaders.Obfuscation.Rewriters
{
    /// <summary>
    /// Rewrites string literals to obfuscated XOR + Base64 decoding expressions.
    ///
    /// The transformation deliberately skips critical contexts such as
    /// attributes, const fields and DllImport signatures where literal
    /// values must remain stable for the runtime.
    /// </summary>
    internal sealed class StringLiteralObfuscator : CSharpSyntaxRewriter
    {
        public override SyntaxNode VisitLiteralExpression(LiteralExpressionSyntax node)
        {
            if (!node.IsKind(SyntaxKind.StringLiteralExpression))
            {
                return base.VisitLiteralExpression(node);
            }

            if (IsExcludedContext(node))
            {
                return base.VisitLiteralExpression(node);
            }

            var decodeInvocation = StringObfuscationUtil.BuildDecodeExpression(node.Token.ValueText);
            return decodeInvocation.WithTriviaFrom(node);
        }

        private static bool IsExcludedContext(LiteralExpressionSyntax node)
        {
            if (node.Parent is AttributeArgumentSyntax attrArgument)
            {
                return true;
            }

            if (node.Parent is AttributeArgumentSyntax attributeArgument &&
                attributeArgument.Parent is AttributeArgumentListSyntax argumentList &&
                argumentList.Parent is AttributeSyntax attribute &&
                attribute.Name is QualifiedNameSyntax qualifiedName &&
                qualifiedName.Left.ToString() == "global::System")
            {
                return true;
            }

            if (node.Parent is EqualsValueClauseSyntax equalsValueClause &&
                equalsValueClause.Parent is VariableDeclaratorSyntax variableDeclarator &&
                variableDeclarator.Parent is VariableDeclarationSyntax variableDeclaration &&
                ((variableDeclaration.Parent is FieldDeclarationSyntax fieldDeclaration &&
                  fieldDeclaration.Modifiers.Any(SyntaxKind.ConstKeyword)) ||
                 (variableDeclaration.Parent is LocalDeclarationStatementSyntax localDeclaration &&
                  localDeclaration.Modifiers.Any(SyntaxKind.ConstKeyword))))
            {
                return true;
            }

            if (node.Parent is EqualsValueClauseSyntax && node.Parent.Parent is ParameterSyntax)
            {
                return true;
            }

            if (node.Parent is ParameterSyntax parameterSyntax && parameterSyntax.Default != null && parameterSyntax.Default.Value == node)
            {
                return true;
            }

            if (node.Parent is CaseSwitchLabelSyntax)
            {
                return true;
            }

            if (node.Parent is AttributeArgumentSyntax dllImportArgument &&
                dllImportArgument.Parent is AttributeArgumentListSyntax dllArgumentList &&
                dllArgumentList.Parent is AttributeSyntax dllAttribute &&
                dllAttribute.Name.ToString() == "DllImport")
            {
                return true;
            }

            return false;
        }
    }
}
