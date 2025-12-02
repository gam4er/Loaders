using System.Collections.Generic;
using System.IO;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Loaders.Obfuscation.Services
{
    /// <summary>
    /// Syntactic fallback renamer: replaces constructor type names ("new ClassName(...)")
    /// based solely on a provided class name map. Use this only if semantic renaming is
    /// insufficient in some scenarios.
    /// </summary>
    internal static class SimpleConstructorRenameService
    {
        public static void RenameConstructors(IReadOnlyDictionary<string, string> classMap, IEnumerable<string> filePaths)
        {
            foreach (var filePath in filePaths)
            {
                var source = File.ReadAllText(filePath);
                var syntaxTree = CSharpSyntaxTree.ParseText(source);
                var rewriter = new ConstructorTypeRewriter(classMap);
                var newRoot = rewriter.Visit(syntaxTree.GetRoot());
                File.WriteAllText(filePath, newRoot.ToFullString());
            }
        }

        private sealed class ConstructorTypeRewriter : CSharpSyntaxRewriter
        {
            private readonly IReadOnlyDictionary<string, string> _classMap;

            public ConstructorTypeRewriter(IReadOnlyDictionary<string, string> classMap)
            {
                _classMap = classMap;
            }

            public override SyntaxNode VisitObjectCreationExpression(ObjectCreationExpressionSyntax node)
            {
                // Only touch the type name; arguments and initializer stay as-is.
                var type = node.Type as IdentifierNameSyntax;
                if (type != null && _classMap.TryGetValue(type.Identifier.Text, out var newName))
                {
                    var updatedType = SyntaxFactory.IdentifierName(newName).WithTriviaFrom(type);
                    return node.WithType(updatedType);
                }

                return base.VisitObjectCreationExpression(node);
            }
        }
    }
}
