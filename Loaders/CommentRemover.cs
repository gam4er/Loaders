using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis;

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Loaders
{
    public class CommentRemover : CSharpSyntaxRewriter
    {
        public override SyntaxTrivia VisitTrivia(SyntaxTrivia trivia)
        {
            // Удаляем однострочные и многострочные комментарии
            if (trivia.IsKind(SyntaxKind.SingleLineCommentTrivia) ||
                trivia.IsKind(SyntaxKind.MultiLineCommentTrivia) ||
                trivia.IsKind(SyntaxKind.DocumentationCommentExteriorTrivia) ||
                trivia.IsKind(SyntaxKind.MultiLineDocumentationCommentTrivia) ||
                trivia.IsKind(SyntaxKind.SingleLineDocumentationCommentTrivia) ||
                trivia.IsKind(SyntaxKind.EndOfDocumentationCommentToken))
            {
                return default;
            }
            return base.VisitTrivia(trivia);
        }
    }
}
