using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Loaders.Obfuscation.Rewriters;

namespace Loaders.Obfuscation.Services
{
    internal static class StringLiteralObfuscationService
    {
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
