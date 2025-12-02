using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Loaders.Obfuscation.Rewriters;

namespace Loaders.Obfuscation.Services
{
    /// <summary>
    /// High-level entry points for string literal obfuscation.
    ///
    /// This service removes comments, ensures required using directives are
    /// present and then rewrites string literals to obfuscated expressions.
    /// </summary>
    internal static class StringLiteralObfuscationService
    {
        /// <summary>
        /// Strips comments from the syntax tree and guarantees that core
        /// namespaces used by the string decoder logic are imported.
        /// </summary>
        public static SyntaxTree RemoveCommentsAndEnsureUsings(SyntaxTree syntaxTree)
        {
            var root = (CompilationUnitSyntax)syntaxTree.GetRoot();
            var commentRemover = new CommentRemover();
            root = (CompilationUnitSyntax)commentRemover.Visit(root).NormalizeWhitespace();

            root = EnsureUsingDirective(root, "System");
            root = EnsureUsingDirective(root, "System.Text");
            root = EnsureUsingDirective(root, "System.Linq");
            root = EnsureUsingDirective(root, "System.Threading.Tasks");

            return syntaxTree.WithRootAndOptions(root, syntaxTree.Options);
        }

        /// <summary>
        /// Obfuscates string literals in the syntax tree, replacing them
        /// with calls to obfuscation methods.
        /// </summary>
        public static SyntaxTree ObfuscateStrings(SyntaxTree syntaxTree)
        {
            var rewriter = new StringLiteralObfuscator();
            var newRoot = rewriter.Visit(syntaxTree.GetRoot()).NormalizeWhitespace();
            return syntaxTree.WithRootAndOptions(newRoot, syntaxTree.Options);
        }

        private static CompilationUnitSyntax EnsureUsingDirective(CompilationUnitSyntax root, string namespaceName)
        {
            if (root.Usings.Any(usingDirective => usingDirective.Name.ToString() == namespaceName))
            {
                return root;
            }

            var newUsing = SyntaxFactory.UsingDirective(SyntaxFactory.ParseName(namespaceName)).NormalizeWhitespace();
            return root.WithUsings(root.Usings.Add(newUsing)).NormalizeWhitespace();
        }
    }
}
