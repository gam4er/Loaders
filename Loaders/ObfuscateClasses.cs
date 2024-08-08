using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis;

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Security.Cryptography;

namespace Loaders
{
    public class ObfuscateClasses : CSharpSyntaxRewriter
    {

        private static Dictionary<string, string> classNameMap = new Dictionary<string, string>();

        private static readonly HashSet<string> reservedNamespaces = new HashSet<string>
        {
            "System",
            "Microsoft",
            //"Seatbelt" // Add other reserved namespaces as needed
        };

        private readonly Dictionary<string, string> _nameMap = new Dictionary<string, string>();

        private string GetObfuscatedName(string originalName)
        {
            if (!_nameMap.TryGetValue(originalName, out var obfuscatedName))
            {
                using (SHA256 sha256 = SHA256.Create())
                {
                    byte [] hashBytes = sha256.ComputeHash(Encoding.UTF8.GetBytes(originalName));
                    obfuscatedName = "O_" + BitConverter.ToString(hashBytes).Replace("-", "").Substring(0, 8);
                    _nameMap [originalName] = obfuscatedName;
                }
            }
            return obfuscatedName;
        }

        public override SyntaxNode VisitClassDeclaration(ClassDeclarationSyntax node)
        {
            var obfuscatedName = GetObfuscatedName(node.Identifier.Text);
            classNameMap [node.Identifier.Text] = obfuscatedName;
            var newNode = node.WithIdentifier(SyntaxFactory.Identifier(obfuscatedName)).NormalizeWhitespace();

            // Replace constructors with obfuscated names
            var constructorRewriter = new ConstructorRewriter(node.Identifier.Text, obfuscatedName);
            newNode = (ClassDeclarationSyntax)constructorRewriter.Visit(newNode);

            return base.VisitClassDeclaration(newNode);
        }

        private class ConstructorRewriter : CSharpSyntaxRewriter
        {
            private readonly string _originalName;
            private readonly string _obfuscatedName;

            public ConstructorRewriter(string originalName, string obfuscatedName)
            {
                _originalName = originalName;
                _obfuscatedName = obfuscatedName;
            }

            public override SyntaxNode VisitConstructorDeclaration(ConstructorDeclarationSyntax node)
            {
                if (node.Identifier.Text == _originalName)
                {
                    return node.WithIdentifier(SyntaxFactory.Identifier(_obfuscatedName)).NormalizeWhitespace();
                }
                return base.VisitConstructorDeclaration(node);
            }
        }

        private class MethodParameterNormalizer : CSharpSyntaxRewriter
        {
            public override SyntaxNode VisitParameter(ParameterSyntax node)
            {
                var identifierToken = node.Identifier;
                var typeSyntax = node.Type;

                if (typeSyntax != null)
                {
                    var trailingTrivia = typeSyntax.GetTrailingTrivia();

                    // Add a space if it is not already present
                    if (!trailingTrivia.Any(trivia => trivia.IsKind(SyntaxKind.WhitespaceTrivia)))
                    {
                        trailingTrivia = trailingTrivia.Add(SyntaxFactory.Whitespace(" "));
                        typeSyntax = typeSyntax.WithTrailingTrivia(trailingTrivia);
                    }

                    return node.WithType(typeSyntax).NormalizeWhitespace();
                }

                return base.VisitParameter(node).NormalizeWhitespace();
            }
        }

        public static SyntaxTree ReplaceClassNames(SyntaxTree tree, Dictionary<string, string> classNameMap, Dictionary<string, string> methodNameMap)
        {
            var root = tree.GetRoot();
            var rewriter = new ClassNameReplacer(classNameMap, methodNameMap);
            var newRoot = rewriter.Visit(root);
            /*может и не надо уже нормализить*/
            newRoot = new MethodParameterNormalizer().Visit(newRoot).NormalizeWhitespace(); // Normalize method parameters
            return tree.WithRootAndOptions(newRoot, tree.Options);
        }
        private class ClassNameReplacer : CSharpSyntaxRewriter
        {
            private readonly Dictionary<string, string> _classNameMap;
            private readonly Dictionary<string, string> _methodNameMap;

            public ClassNameReplacer(Dictionary<string, string> classNameMap, Dictionary<string, string> methodNameMap)
            {
                _classNameMap = classNameMap;
                _methodNameMap = methodNameMap;
            }

            public override SyntaxNode VisitIdentifierName(IdentifierNameSyntax node)
            {
                if (_classNameMap.TryGetValue(node.Identifier.Text, out var newName))
                {
                    // Check if the identifier is part of a reserved namespace and skip replacement
                    if (IsInReservedNamespace(node))
                    {
                        return node;
                    }
                    return node.WithIdentifier(SyntaxFactory.Identifier(newName)).NormalizeWhitespace();
                }

                if (_methodNameMap.TryGetValue(node.Identifier.Text, out var newMethodName))
                {
                    return node.WithIdentifier(SyntaxFactory.Identifier(newMethodName)).NormalizeWhitespace();
                }

                return base.VisitIdentifierName(node);
            }

            private bool IsInReservedNamespace(SyntaxNode node)
            {
                var parent = node.Parent;
                while (parent != null)
                {
                    if (parent is NamespaceDeclarationSyntax namespaceDeclaration)
                    {
                        return reservedNamespaces.Any(ns => namespaceDeclaration.Name.ToString().StartsWith(ns));
                    }
                    parent = parent.Parent;
                }
                return false;
            }

            public override SyntaxNode VisitQualifiedName(QualifiedNameSyntax node)
            {
                if (IsInReservedNamespace(node))
                {
                    return node;
                }
                return base.VisitQualifiedName(node);
            }

            private bool IsInReservedNamespace(QualifiedNameSyntax node)
            {
                var name = node.ToString();
                return reservedNamespaces.Any(ns => name.StartsWith(ns));
            }

        }


    }

}
