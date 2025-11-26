using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace Loaders.Obfuscation.Rewriters
{
    public class CommentRemover : CSharpSyntaxRewriter
    {
        public override SyntaxTrivia VisitTrivia(SyntaxTrivia trivia)
        {
            if (trivia.IsKind(SyntaxKind.SingleLineCommentTrivia) ||
                trivia.IsKind(SyntaxKind.MultiLineCommentTrivia) ||
                trivia.IsKind(SyntaxKind.DocumentationCommentExteriorTrivia) ||
                trivia.IsKind(SyntaxKind.MultiLineDocumentationCommentTrivia) ||
                trivia.IsKind(SyntaxKind.SingleLineDocumentationCommentTrivia) ||
                trivia.IsKind(SyntaxKind.EndOfDocumentationCommentToken) ||
                trivia.IsKind(SyntaxKind.RegionDirectiveTrivia) ||
                trivia.IsKind(SyntaxKind.EndRegionDirectiveTrivia) ||
                trivia.IsKind(SyntaxKind.RegionKeyword) ||
                trivia.IsKind(SyntaxKind.EndRegionKeyword))
            {
                return default;
            }

            return base.VisitTrivia(trivia);
        }
    }
}
