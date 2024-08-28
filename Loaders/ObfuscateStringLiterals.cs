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
        public static SyntaxTree AddUsingsNoComments(SyntaxTree syntaxTree)
        {
            var root = syntaxTree.GetRoot() as CompilationUnitSyntax;

            // Удаление комментариев
            var commentRemover = new CommentRemover();
            root = (CompilationUnitSyntax)commentRemover.Visit(root).NormalizeWhitespace();
            syntaxTree = syntaxTree.WithRootAndOptions(root, syntaxTree.Options);

            // Проверяем наличие директивы using System;
            var hasUsingSystem = root.Usings
                                         .Any(u => u.Name.ToString() == "System");

            // Добавляем директиву using System; если её нет
            if (!hasUsingSystem)
            {
                var newUsing = SyntaxFactory.UsingDirective(SyntaxFactory.ParseName("System")).NormalizeWhitespace();
                var newUsings = root.Usings.Add(newUsing);
                root = root.WithUsings(newUsings).NormalizeWhitespace();

                syntaxTree = syntaxTree.WithRootAndOptions(root, syntaxTree.Options);
            }

            // Проверяем наличие директивы using System.Text;
            var hasUsingSystemText = root.Usings
                                         .Any(u => u.Name.ToString() == "System.Text");

            // Добавляем директиву using System.Text; если её нет
            if (!hasUsingSystemText)
            {
                var newUsing = SyntaxFactory.UsingDirective(SyntaxFactory.ParseName("System.Text")).NormalizeWhitespace();
                var newUsings = root.Usings.Add(newUsing);
                root = root.WithUsings(newUsings).NormalizeWhitespace();

                syntaxTree = syntaxTree.WithRootAndOptions(root, syntaxTree.Options);
            }

            // Проверяем наличие директивы using System.Linq;
            var hasUsingLinq = root.Usings
                                         .Any(u => u.Name.ToString() == "System.Linq");

            // Добавляем директиву using System.Linq; если её нет
            if (!hasUsingLinq)
            {
                var newUsing = SyntaxFactory.UsingDirective(SyntaxFactory.ParseName("System.Linq")).NormalizeWhitespace();
                var newUsings = root.Usings.Add(newUsing);
                root = root.WithUsings(newUsings).NormalizeWhitespace();

                syntaxTree = syntaxTree.WithRootAndOptions(root, syntaxTree.Options);
            }

            return syntaxTree;
        }
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
                // Исключение обфускации строк внутри атрибутов, если атрибут находится в пространстве имен System
                if (node.Parent is AttributeArgumentSyntax attrArgument)
                {
                    return base.VisitLiteralExpression(node);
                }

                // Исключение обфускации строк внутри атрибутов, если атрибут находится в пространстве имен System
                if (node.Parent is AttributeArgumentSyntax attArgument &&
                    attArgument.Parent is AttributeArgumentListSyntax argList &&
                    argList.Parent is AttributeSyntax attr &&
                    attr.Name is QualifiedNameSyntax qualifiedName &&
                    qualifiedName.Left.ToString() == "global::System")
                {
                    return base.VisitLiteralExpression(node);
                }


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

                    var decodeInvocation = SyntaxFactory.ParseExpression($"Encoding.UTF8.GetString(Convert.FromBase64String(\"{xorEncryptedText}\").Select((bbb, iii) => (byte)(bbb ^ Convert.FromBase64String(\"{base64Key}\")[iii % {key.Length}])).ToArray())");
                    return decodeInvocation;
                }

                return base.VisitLiteralExpression(node);
            }

        }

    }

}
