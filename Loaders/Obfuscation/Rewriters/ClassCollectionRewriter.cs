using System.Collections.Generic;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Loaders.Obfuscation.Utilities;

namespace Loaders.Obfuscation.Rewriters
{
    /// <summary>
    /// Collects class declarations and builds a mapping between original and obfuscated names.
    /// </summary>
    public sealed class ClassCollectionRewriter : CSharpSyntaxRewriter
    {
        private readonly Dictionary<string, string> _classNameMap = new();

        public override SyntaxNode VisitClassDeclaration(ClassDeclarationSyntax node)
        {
            string originalName = node.Identifier.Text;
            string obfuscatedName = ObfuscatedNameGenerator.Generate(originalName);
            _classNameMap[originalName] = obfuscatedName;
            return base.VisitClassDeclaration(node);
        }

        public IReadOnlyDictionary<string, string> GetClassMap() => _classNameMap;
    }
}
