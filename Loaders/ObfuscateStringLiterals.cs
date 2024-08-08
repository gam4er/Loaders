using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Text;

namespace Loaders
{
    internal class ObfuscateStringLiterals
    {
        
        private static Dictionary<string, string> methodNameMap = new Dictionary<string, string>();

        public static SyntaxTree Obfuscate(SyntaxTree tree)
        {
            var root = tree.GetRoot();
            var rewriter = new ObfuscationRewriter();
            var newRoot = rewriter.Visit(root).NormalizeWhitespace();
            return tree.WithRootAndOptions(newRoot, tree.Options);
        }
        public class ObfuscationRewriter : CSharpSyntaxRewriter
        {
            private byte [] GenerateRandomKey(int length)
            {
                var key = new byte [length];
                using (var rng = new RNGCryptoServiceProvider())
                {
                    rng.GetBytes(key);
                }
                return key;
            }

            private string XorEncrypt(string text, byte [] key)
            {
                var textBytes = Encoding.UTF8.GetBytes(text);
                var encryptedBytes = new byte [textBytes.Length];

                for (int i = 0; i < textBytes.Length; i++)
                {
                    encryptedBytes [i] = (byte)(textBytes [i] ^ key [i % key.Length]);
                }

                return Convert.ToBase64String(encryptedBytes);
            }

            private string Base64Encode(string plainText)
            {
                var plainTextBytes = Encoding.UTF8.GetBytes(plainText);
                return Convert.ToBase64String(plainTextBytes);
            }

            public override SyntaxNode VisitLiteralExpression(LiteralExpressionSyntax node)
            {
                if (node.Parent is EqualsValueClauseSyntax equalsValueClause &&
                    equalsValueClause.Parent is VariableDeclaratorSyntax variableDeclarator &&
                    variableDeclarator.Parent is VariableDeclarationSyntax variableDeclaration &&
                    (
                        variableDeclaration.Parent is FieldDeclarationSyntax fieldDeclaration &&
                        fieldDeclaration.Modifiers.Any(SyntaxKind.ConstKeyword) ||
                        variableDeclaration.Parent is LocalDeclarationStatementSyntax localDeclaration &&
                        localDeclaration.Modifiers.Any(SyntaxKind.ConstKeyword)
                    ))
                {
                    return base.VisitLiteralExpression(node);
                }

                if (node.Parent is EqualsValueClauseSyntax parentEqualsValueClause &&
                    parentEqualsValueClause.Parent is ParameterSyntax)
                {
                    return base.VisitLiteralExpression(node);
                }

                if (node.Parent is ParameterSyntax parameterSyntax &&
                    parameterSyntax.Default != null &&
                    parameterSyntax.Default.Value == node)
                {
                    return base.VisitLiteralExpression(node);
                }

                if (node.Parent is CaseSwitchLabelSyntax)
                {
                    return base.VisitLiteralExpression(node);
                }

                if (node.Parent is AttributeArgumentSyntax attributeArgument &&
                                        attributeArgument.Parent is AttributeArgumentListSyntax argumentList &&
                                        argumentList.Parent is AttributeSyntax attribute &&
                                        attribute.Name.ToString() == "DllImport")
                {
                    return base.VisitLiteralExpression(node);
                }

                if (node.IsKind(SyntaxKind.StringLiteralExpression))
                {
                    string originalText = node.Token.ValueText;
                    byte [] key = GenerateRandomKey(8); // 8-байтовый ключ
                    string xorEncryptedText = XorEncrypt(originalText, key);
                    string base64Key = Convert.ToBase64String(key);

                    var decodeInvocation = SyntaxFactory.ParseExpression($"Encoding.UTF8.GetString(Convert.FromBase64String(\"{xorEncryptedText}\").Select((b, i) => (byte)(b ^ Convert.FromBase64String(\"{base64Key}\")[i % {key.Length}])).ToArray())");
                    return decodeInvocation;
                }

                return base.VisitLiteralExpression(node);
            }

            /*
            // Avoid obfuscating reserved namespaces
            public override SyntaxNode VisitNamespaceDeclaration(NamespaceDeclarationSyntax node)
            {
                if (reservedNamespaces.Contains(node.Name.ToString().Split('.') [0]))
                {
                    return base.VisitNamespaceDeclaration(node);
                }
                return base.VisitNamespaceDeclaration(node);
            }
            
            public override SyntaxNode VisitUsingDirective(UsingDirectiveSyntax node)
            {
                if (reservedNamespaces.Any(ns => node.Name.ToString().StartsWith(ns)))
                {
                    return node;
                }
                return base.VisitUsingDirective(node);
            }
            
            public override SyntaxNode VisitMethodDeclaration(MethodDeclarationSyntax node)
            {
                if (node.AttributeLists.Any(al => al.Attributes.Any(a => a.Name.ToString() == "DllImport")))
                {
                    return node; // Пропускаем методы с атрибутом [DllImport("")]
                }

                var obfuscatedName = GetObfuscatedName(node.Identifier.Text);
                methodNameMap [node.Identifier.Text] = obfuscatedName;
                return base.VisitMethodDeclaration(node.WithIdentifier(SyntaxFactory.Identifier(obfuscatedName))).NormalizeWhitespace();
            }
            */
        }

    }

}
