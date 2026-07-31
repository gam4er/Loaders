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
    /// <summary>
    /// Performs semantic class renaming using Roslyn symbol APIs.
    ///
    /// For each entry in the class map the corresponding type symbol is
    /// renamed so that all references (base types, fields, parameters,
    /// object constructions, etc.) are updated consistently across the
    /// working copy of the Seatbelt project.
    /// </summary>
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
                        .Where(c => c.Identifier.Text == originalName || c.Identifier.Text == obfuscatedName)
                        .ToList();

                    foreach (var classDeclaration in classDeclarations)
                    {
                        var symbol = semanticModel.GetDeclaredSymbol(classDeclaration);
                        if (symbol != null && symbol.Name != obfuscatedName)
                        {
                            solution = Renamer.RenameSymbolAsync(solution, symbol, obfuscatedName, workspace.Options)
                                .GetAwaiter()
                                .GetResult();
                        }
                    }
                }

                project = solution.GetProject(project.Id);
            }

            // Persist updated documents back to the physical files in the obfuscated project tree.
            foreach (var documentId in project.DocumentIds)
            {
                var updatedDocument = solution.GetDocument(documentId);
                if (updatedDocument == null)
                {
                    continue;
                }

                var newText = updatedDocument.GetTextAsync().GetAwaiter().GetResult();
                var path = updatedDocument.FilePath;

                if (!string.IsNullOrEmpty(path))
                {
                    File.WriteAllText(path, newText.ToString());
                }
            }
        }

        private static Project CreateProject(AdhocWorkspace workspace, IEnumerable<string> filePaths)
        {
            // Add a richer set of framework references so Roslyn can resolve symbols across documents
            var references = new List<MetadataReference>
            {
                MetadataReference.CreateFromFile(typeof(object).Assembly.Location),
                MetadataReference.CreateFromFile(typeof(Uri).Assembly.Location), // System
                MetadataReference.CreateFromFile(typeof(Enumerable).Assembly.Location), // System.Core
                MetadataReference.CreateFromFile(typeof(System.Xml.Linq.XDocument).Assembly.Location), // System.Xml.Linq
                MetadataReference.CreateFromFile(typeof(System.Configuration.ConfigurationElementCollection).Assembly.Location), // System.Configuration
                MetadataReference.CreateFromFile(typeof(Microsoft.Win32.RegistryHive).Assembly.Location), // Microsoft.Win32.Registry
                MetadataReference.CreateFromFile(typeof(System.IO.Compression.GZipStream).Assembly.Location), // System.IO.Compression
                MetadataReference.CreateFromFile(typeof(System.Numerics.BigInteger).Assembly.Location), // System.Numerics
            };

            var projectInfo = ProjectInfo.Create(
                ProjectId.CreateNewId(),
                VersionStamp.Default,
                name: "LoadersObfuscation",
                assemblyName: "LoadersObfuscation",
                language: LanguageNames.CSharp,
                metadataReferences: references);

            var project = workspace.AddProject(projectInfo);

            foreach (var filePath in filePaths)
            {
                var code = File.ReadAllText(filePath);

                // Associate document with its physical path using DocumentInfo + TextLoader
                var documentId = DocumentId.CreateNewId(project.Id, Path.GetFileName(filePath));
                var loader = TextLoader.From(TextAndVersion.Create(SourceText.From(code), VersionStamp.Default, filePath));

                var documentInfo = DocumentInfo.Create(
                    documentId,
                    Path.GetFileName(filePath),
                    filePath: filePath,
                    loader: loader);

                var solution = project.Solution.AddDocument(documentInfo);
                project = solution.GetProject(project.Id);
            }

            return project;
        }
    }
}
