using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Rename;
using Microsoft.CodeAnalysis.Text;

namespace Loaders.Obfuscation.Services
{
    internal static class ClassRenamer
    {
        public static void RenameClasses(IReadOnlyDictionary<string, string> classMap, IReadOnlyCollection<string> filePaths)
        {
            if (classMap == null)
            {
                throw new ArgumentNullException(nameof(classMap));
            }

            if (filePaths == null)
            {
                throw new ArgumentNullException(nameof(filePaths));
            }

            using var workspace = new AdhocWorkspace();
            var project = CreateProject(workspace, filePaths);
            var solution = project.Solution;

            foreach (var kvp in classMap)
            {
                var originalName = kvp.Key;
                var obfuscatedName = kvp.Value;
                foreach (var documentId in project.DocumentIds)
                {
                    var document = solution.GetDocument(documentId);
                    if (document == null)
                    {
                        continue;
                    }

                    var root = document.GetSyntaxRootAsync().GetAwaiter().GetResult();
                    var semanticModel = document.GetSemanticModelAsync().GetAwaiter().GetResult();
                    if (root == null || semanticModel == null)
                    {
                        continue;
                    }

                    var classDeclarations = root.DescendantNodesAndSelf()
                        .OfType<Microsoft.CodeAnalysis.CSharp.Syntax.ClassDeclarationSyntax>()
                        .Where(c => c.Identifier.Text == originalName)
                        .ToList();

                    foreach (var classDeclaration in classDeclarations)
                    {
                        var symbol = semanticModel.GetDeclaredSymbol(classDeclaration);
                        if (symbol != null)
                        {
                            solution = Renamer.RenameSymbolAsync(solution, symbol, obfuscatedName, workspace.Options)
                                .GetAwaiter()
                                .GetResult();
                        }
                    }
                }

                project = solution.GetProject(project.Id);
            }

            foreach (var documentId in project.DocumentIds)
            {
                var updatedDocument = solution.GetDocument(documentId);
                if (updatedDocument == null)
                {
                    continue;
                }

                var newText = updatedDocument.GetTextAsync().GetAwaiter().GetResult();
                if (updatedDocument.FilePath != null)
                {
                    File.WriteAllText(updatedDocument.FilePath, newText.ToString());
                }
            }
        }

        private static Project CreateProject(AdhocWorkspace workspace, IEnumerable<string> filePaths)
        {
            var projectInfo = ProjectInfo.Create(
                ProjectId.CreateNewId(),
                VersionStamp.Default,
                name: "LoadersObfuscation",
                assemblyName: "LoadersObfuscation",
                language: LanguageNames.CSharp,
                metadataReferences: new[] { MetadataReference.CreateFromFile(typeof(object).Assembly.Location) });

            var project = workspace.AddProject(projectInfo);

            foreach (var filePath in filePaths)
            {
                var code = File.ReadAllText(filePath);
                project = workspace
                    .AddDocument(project.Id, Path.GetFileName(filePath), SourceText.From(code)/*, filePath: filePath*/)
                    .Project;
            }

            return project;
        }
    }
}
