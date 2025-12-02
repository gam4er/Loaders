using System.Collections.Generic;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Loaders.Obfuscation.Utilities;

namespace Loaders.Obfuscation.Rewriters
{
    /// <summary>
    /// Collects class declarations and builds a mapping between original
    /// class names and their obfuscated counterparts.
    ///
    /// The mapping is later used both by semantic (Roslyn-based) and
    /// syntactic renamers to ensure consistent type names across the
    /// obfuscated project.
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
