using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Loaders.Obfuscation.Rewriters;
using Loaders.Obfuscation.Utilities;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Formatting;

namespace Loaders.Obfuscation.Services
{
    internal static class OutAssignmentMethodService
    {
        public static void Rewrite(
            ProjectFileInfo projectInfo,
            Action<string> reportProgress,
            IObfuscatedNameProvider nameProvider)
        {
            if (projectInfo == null)
            {
                throw new ArgumentNullException(nameof(projectInfo));
            }

            if (nameProvider == null)
            {
                throw new ArgumentNullException(nameof(nameProvider));
            }

            using var workspace = new AdhocWorkspace();
            var project = ProjectReferenceResolver.CreateProject(workspace, projectInfo, "LoadersOutAssignments");
            var compilation = project.GetCompilationAsync().GetAwaiter().GetResult();
            if (compilation == null)
            {
                return;
            }

            var reservedMemberNames = CollectReservedMemberNames(compilation);

            foreach (var documentId in project.DocumentIds)
            {
                var document = project.GetDocument(documentId);
                if (document == null)
                {
                    reportProgress?.Invoke(string.Empty);
                    continue;
                }

                if (IsGeneratedFile(document.FilePath))
                {
                    reportProgress?.Invoke(document.FilePath ?? string.Empty);
                    continue;
                }

                var syntaxTree = document.GetSyntaxTreeAsync().GetAwaiter().GetResult();
                var root = syntaxTree?.GetRoot();
                if (syntaxTree == null || root == null)
                {
                    reportProgress?.Invoke(document.FilePath ?? string.Empty);
                    continue;
                }

                var semanticModel = compilation.GetSemanticModel(syntaxTree);
                var rewriter = new OutAssignmentMethodRewriter(semanticModel, reservedMemberNames, nameProvider);
                var newRoot = rewriter.Visit(root);

                if (rewriter.Changed && !string.IsNullOrWhiteSpace(document.FilePath))
                {
                    var formattedRoot = Formatter.Format(newRoot, workspace);
                    File.WriteAllText(document.FilePath, formattedRoot.ToFullString());
                }

                reportProgress?.Invoke(document.FilePath ?? string.Empty);
            }
        }

        private static IDictionary<string, HashSet<string>> CollectReservedMemberNames(Compilation compilation)
        {
            var reservedNames = new Dictionary<string, HashSet<string>>(StringComparer.Ordinal);

            foreach (var syntaxTree in compilation.SyntaxTrees)
            {
                var semanticModel = compilation.GetSemanticModel(syntaxTree);
                var root = syntaxTree.GetRoot();

                foreach (var classDeclaration in root.DescendantNodesAndSelf().OfType<ClassDeclarationSyntax>())
                {
                    var classSymbol = semanticModel.GetDeclaredSymbol(classDeclaration) as INamedTypeSymbol;
                    if (classSymbol == null)
                    {
                        continue;
                    }

                    var typeKey = OutAssignmentMethodRewriter.GetTypeKey(classSymbol);
                    if (!reservedNames.TryGetValue(typeKey, out var names))
                    {
                        names = new HashSet<string>(StringComparer.Ordinal);
                        reservedNames[typeKey] = names;
                    }

                    foreach (var memberName in GetDeclaredMemberNames(classDeclaration))
                    {
                        names.Add(memberName);
                    }

                    foreach (var usedMethodName in GetUsedMethodNames(classDeclaration, semanticModel))
                    {
                        names.Add(usedMethodName);
                    }
                }
            }

            return reservedNames;
        }

        private static IEnumerable<string> GetDeclaredMemberNames(ClassDeclarationSyntax classDeclaration)
        {
            foreach (var member in classDeclaration.Members)
            {
                switch (member)
                {
                    case MethodDeclarationSyntax method:
                        yield return method.Identifier.Text;
                        break;
                    case PropertyDeclarationSyntax property:
                        yield return property.Identifier.Text;
                        break;
                    case EventDeclarationSyntax eventDeclaration:
                        yield return eventDeclaration.Identifier.Text;
                        break;
                    case FieldDeclarationSyntax field:
                        foreach (var variable in field.Declaration.Variables)
                        {
                            yield return variable.Identifier.Text;
                        }
                        break;
                    case EventFieldDeclarationSyntax eventField:
                        foreach (var variable in eventField.Declaration.Variables)
                        {
                            yield return variable.Identifier.Text;
                        }
                        break;
                    case ClassDeclarationSyntax nestedClass:
                        yield return nestedClass.Identifier.Text;
                        break;
                    case StructDeclarationSyntax nestedStruct:
                        yield return nestedStruct.Identifier.Text;
                        break;
                    case InterfaceDeclarationSyntax nestedInterface:
                        yield return nestedInterface.Identifier.Text;
                        break;
                    case EnumDeclarationSyntax nestedEnum:
                        yield return nestedEnum.Identifier.Text;
                        break;
                }
            }
        }

        private static IEnumerable<string> GetUsedMethodNames(
            ClassDeclarationSyntax classDeclaration,
            SemanticModel semanticModel)
        {
            foreach (var identifier in classDeclaration.DescendantNodes().OfType<IdentifierNameSyntax>())
            {
                var symbol = semanticModel.GetSymbolInfo(identifier).Symbol;
                if (symbol is IMethodSymbol)
                {
                    yield return identifier.Identifier.Text;
                }
            }
        }

        private static bool IsGeneratedFile(string filePath)
        {
            if (string.IsNullOrWhiteSpace(filePath))
            {
                return true;
            }

            var fileName = Path.GetFileName(filePath);
            return fileName.Equals("AssemblyInfo.cs", StringComparison.OrdinalIgnoreCase) ||
                   fileName.EndsWith(".Designer.cs", StringComparison.OrdinalIgnoreCase) ||
                   fileName.EndsWith(".g.cs", StringComparison.OrdinalIgnoreCase) ||
                   fileName.EndsWith(".g.i.cs", StringComparison.OrdinalIgnoreCase) ||
                   fileName.EndsWith(".Generated.cs", StringComparison.OrdinalIgnoreCase);
        }
    }
}
