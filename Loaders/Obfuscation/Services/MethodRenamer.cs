using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Xml.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Rename;
using Microsoft.CodeAnalysis.Text;

namespace Loaders.Obfuscation.Services
{
    internal static class MethodRenamer
    {
        public static IReadOnlyDictionary<string, string> CollectMethodMap(IReadOnlyCollection<string> filePaths)
        {
            if (filePaths == null) throw new ArgumentNullException(nameof(filePaths));

            var collector = new Loaders.Obfuscation.Rewriters.MethodCollectionRewriter();
            foreach (var file in filePaths)
            {
                var code = File.ReadAllText(file);
                var tree = Microsoft.CodeAnalysis.CSharp.CSharpSyntaxTree.ParseText(code);
                collector.Visit(tree.GetRoot());
            }

            return collector.GetMethodMap();
        }

        public static IReadOnlyList<Loaders.Obfuscation.Rewriters.MethodRenameEntry> CollectMethodEntries(IReadOnlyCollection<string> filePaths)
        {
            if (filePaths == null) throw new ArgumentNullException(nameof(filePaths));

            var collector = new Loaders.Obfuscation.Rewriters.MethodCollectionRewriter();
            foreach (var file in filePaths)
            {
                var code = File.ReadAllText(file);
                var tree = Microsoft.CodeAnalysis.CSharp.CSharpSyntaxTree.ParseText(code);
                collector.Visit(tree.GetRoot());
            }

            return collector.GetEntries();
        }

        // New renaming pipeline modeled after ClassRenamer: iterate collected entries and rename symbols via Roslyn across the solution.
        public static void RenameMethods(IReadOnlyDictionary<string, string> methodMap, IReadOnlyCollection<string> filePaths)
        {
            if (methodMap == null) throw new ArgumentNullException(nameof(methodMap));
            if (filePaths == null) throw new ArgumentNullException(nameof(filePaths));

            using var workspace = new AdhocWorkspace();
            var project = CreateProject(workspace, filePaths);
            var solution = project.Solution;

            // Build a snapshot of entries to drive precise renames (class, name, parameter count, new name)
            var entries = CollectMethodEntries(filePaths);

            foreach (var entry in entries)
            {
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

                    // Find candidate class declarations matching either original or already obfuscated name
                    var classDeclarations = root.DescendantNodesAndSelf()
                        .OfType<Microsoft.CodeAnalysis.CSharp.Syntax.ClassDeclarationSyntax>()
                        .Where(c => c.Identifier.Text == entry.ClassName)
                        .ToList();

                    foreach (var classDeclaration in classDeclarations)
                    {
                        var classSymbol = semanticModel.GetDeclaredSymbol(classDeclaration);
                        if (classSymbol == null)
                        {
                            continue;
                        }

                        // If the class derives from a base type other than object, skip to avoid breaking inheritance contracts
                        if (classSymbol.BaseType != null && classSymbol.BaseType.SpecialType != SpecialType.System_Object)
                        {
                            continue;
                        }

                        // Scan methods in the class to find the exact symbol by name and parameter count
                        var methods = classDeclaration.Members
                            .OfType<Microsoft.CodeAnalysis.CSharp.Syntax.MethodDeclarationSyntax>()
                            .Where(m => m.Identifier.Text == entry.MethodName)
                            .ToList();

                        foreach (var methodDecl in methods)
                        {
                            var symbol = semanticModel.GetDeclaredSymbol(methodDecl);
                            if (symbol == null)
                            {
                                continue;
                            }

                            // Validate parameter count match for overload disambiguation
                            if (symbol.Parameters.Length != entry.ParameterCount)
                            {
                                continue;
                            }

                            // Skip unsafe renames consistent with previous filters
                            if (symbol.IsOverride ||
                                symbol.ExplicitInterfaceImplementations.Length > 0 ||
                                symbol.MethodKind != MethodKind.Ordinary ||
                                symbol.IsExtern ||
                                symbol.IsImplicitlyDeclared ||
                                symbol.GetAttributes().Any(a => a.AttributeClass?.ToDisplayString() == "System.Runtime.InteropServices.DllImportAttribute") ||
                                (symbol.Name == nameof(object.ToString)) ||
                                (symbol.Name == nameof(object.GetHashCode)) ||
                                (symbol.Name == nameof(object.Equals) && symbol.Parameters.Length == 1) ||
                                (symbol.Name == nameof(IDisposable.Dispose) && symbol.Parameters.Length == 0 && classSymbol.AllInterfaces.Any(i => i.ToDisplayString() == typeof(IDisposable).FullName)) ||
                                HasSerializationCallbackAttribute(symbol) ||
                                string.Equals(symbol.Name, "Main", StringComparison.Ordinal))
                            {
                                continue;
                            }

                            // Acquire target new name either from entry or methodMap fallback
                            var key = $"{entry.ClassName}.{entry.MethodName}";
                            var newName = entry.NewName;
                            if (string.IsNullOrEmpty(newName) && methodMap.TryGetValue(key, out var mapName))
                            {
                                newName = mapName;
                            }
                            if (string.IsNullOrEmpty(newName) || symbol.Name == newName)
                            {
                                continue;
                            }

                            solution = Renamer.RenameSymbolAsync(solution, symbol, newName, workspace.Options)
                                .GetAwaiter()
                                .GetResult();
                        }
                    }
                }

                project = solution.GetProject(project.Id);
            }

            // Persist updated documents back to disk
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

        private static bool HasSerializationCallbackAttribute(ISymbol symbol)
        {
            var serializationCallbackAttributes = new[]
            {
                "System.Runtime.Serialization.OnSerializingAttribute",
                "System.Runtime.Serialization.OnSerializedAttribute",
                "System.Runtime.Serialization.OnDeserializingAttribute",
                "System.Runtime.Serialization.OnDeserializedAttribute"
            };
            return symbol.GetAttributes().Any(a => serializationCallbackAttributes.Contains(a.AttributeClass?.ToDisplayString()));
        }

        private static Project CreateProject(AdhocWorkspace workspace, IEnumerable<string> filePaths)
        {
            var references = BuildMetadataReferencesFromNearestProject(filePaths);

            var projectInfo = ProjectInfo.Create(
                ProjectId.CreateNewId(),
                VersionStamp.Default,
                name: "LoadersMethodObfuscation",
                assemblyName: "LoadersMethodObfuscation",
                language: LanguageNames.CSharp,
                metadataReferences: references);

            var project = workspace.AddProject(projectInfo);

            foreach (var filePath in filePaths)
            {
                var code = File.ReadAllText(filePath);
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

        private static List<MetadataReference> BuildMetadataReferencesFromNearestProject(IEnumerable<string> filePaths)
        {
            var refs = new List<MetadataReference>
            {
                MetadataReference.CreateFromFile(typeof(object).Assembly.Location),
                MetadataReference.CreateFromFile(typeof(Uri).Assembly.Location),
                MetadataReference.CreateFromFile(typeof(Enumerable).Assembly.Location),
                MetadataReference.CreateFromFile(typeof(System.Xml.Linq.XDocument).Assembly.Location),
                MetadataReference.CreateFromFile(typeof(System.Configuration.ConfigurationElementCollection).Assembly.Location),
                MetadataReference.CreateFromFile(typeof(Microsoft.Win32.RegistryHive).Assembly.Location),
            };

            // Try to locate a .csproj near the provided file paths and read its <Reference Include="..."> entries
            try
            {
                var first = filePaths.FirstOrDefault();
                if (!string.IsNullOrEmpty(first))
                {
                    var dir = Path.GetDirectoryName(first);
                    while (!string.IsNullOrEmpty(dir))
                    {
                        var csproj = Directory.GetFiles(dir, "*.csproj", SearchOption.TopDirectoryOnly).FirstOrDefault();
                        if (csproj != null)
                        {
                            var doc = XDocument.Load(csproj);
                            XNamespace ns = doc.Root?.Name.Namespace ?? XNamespace.None;
                            var includes = doc
                                .Descendants(ns + "Reference")
                                .Attributes("Include")
                                .Select(a => a.Value)
                                .Distinct()
                                .ToList();

                            foreach (var include in includes)
                            {
                                var assemblies = Loaders.GAC.FindAssemblyForNamespace(include);
                                if (assemblies != null)
                                {
                                    foreach (var asm in assemblies)
                                    {
                                        try
                                        {
                                            refs.Add(MetadataReference.CreateFromFile(asm.Location));
                                        }
                                        catch
                                        {
                                            // Ignore bad references
                                        }
                                    }
                                }
                            }
                            break;
                        }
                        dir = Path.GetDirectoryName(dir);
                    }
                }
            }
            catch
            {
                // Best-effort: keep default references
            }

            return refs;
        }
    }
}
