using System;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Loaders.Obfuscation.Rewriters
{
    /// <summary>
    /// Rewrites string literals to obfuscated XOR + Base64 decoding expressions.
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

            string originalText = node.Token.ValueText;
            byte[] key = GenerateRandomKey(8);
            string xorEncryptedText = XorEncrypt(originalText, key);
            string base64Key = Convert.ToBase64String(key);

            var decodeInvocation = SyntaxFactory.ParseExpression(
                $"Encoding.UTF8.GetString(Convert.FromBase64String(\"{xorEncryptedText}\").Select((value, index) => (byte)(value ^ Convert.FromBase64String(\"{base64Key}\")[index % {key.Length}])).ToArray())");

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

        private static byte[] GenerateRandomKey(int length)
        {
            var key = new byte[length];
            using var rng = new RNGCryptoServiceProvider();
            rng.GetBytes(key);
            return key;
        }

        private static string XorEncrypt(string text, byte[] key)
        {
            byte[] textBytes = Encoding.UTF8.GetBytes(text);
            byte[] encryptedBytes = new byte[textBytes.Length];

            for (int i = 0; i < textBytes.Length; i++)
            {
                encryptedBytes[i] = (byte)(textBytes[i] ^ key[i % key.Length]);
            }

            return Convert.ToBase64String(encryptedBytes);
        }
    }
}
