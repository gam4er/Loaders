using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Rename;
using Microsoft.CodeAnalysis.Text;

namespace Loaders.Obfuscation.Services
{
    internal static class ClassRenamer
    {
        public static async Task RenameClassesAsync(IReadOnlyDictionary<string, string> classMap, IReadOnlyCollection<string> filePaths)
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

            foreach (var (originalName, obfuscatedName) in classMap)
            {
                foreach (var documentId in project.DocumentIds)
                {
                    var document = solution.GetDocument(documentId);
                    if (document == null)
                    {
                        continue;
                    }

                    var root = await document.GetSyntaxRootAsync().ConfigureAwait(false);
                    var semanticModel = await document.GetSemanticModelAsync().ConfigureAwait(false);
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
                            solution = await Renamer.RenameSymbolAsync(solution, symbol, obfuscatedName, workspace.Options)
                                .ConfigureAwait(false);
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

                var newText = await updatedDocument.GetTextAsync().ConfigureAwait(false);
                if (updatedDocument.FilePath != null)
                {
                    await File.WriteAllTextAsync(updatedDocument.FilePath, newText.ToString()).ConfigureAwait(false);
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
                project = workspace.AddDocument(project.Id, Path.GetFileName(filePath), SourceText.From(code), filePath: filePath)
                    .Project;
            }

            return project;
        }
    }
}
