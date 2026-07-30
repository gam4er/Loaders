using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Text;

namespace Loaders.Obfuscation.Services
{
    internal static class ProjectReferenceResolver
    {
        public static Project CreateProject(AdhocWorkspace workspace, ProjectFileInfo projectInfo, string name)
        {
            if (workspace == null)
            {
                throw new ArgumentNullException(nameof(workspace));
            }

            if (projectInfo == null)
            {
                throw new ArgumentNullException(nameof(projectInfo));
            }

            var projectId = ProjectId.CreateNewId();
            var project = workspace.AddProject(ProjectInfo.Create(
                projectId,
                VersionStamp.Default,
                name,
                projectInfo.AssemblyName,
                LanguageNames.CSharp,
                filePath: projectInfo.ProjectPath,
                compilationOptions: projectInfo.CreateCompilationOptions(),
                parseOptions: projectInfo.CreateParseOptions(),
                metadataReferences: BuildMetadataReferences(projectInfo)));

            foreach (var filePath in projectInfo.CsFiles)
            {
                var code = File.ReadAllText(filePath);
                var documentId = DocumentId.CreateNewId(project.Id, Path.GetFileName(filePath));
                var loader = TextLoader.From(TextAndVersion.Create(
                    SourceText.From(code),
                    VersionStamp.Default,
                    filePath));

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

        public static IReadOnlyList<MetadataReference> BuildMetadataReferences(ProjectFileInfo projectInfo)
        {
            if (projectInfo == null)
            {
                throw new ArgumentNullException(nameof(projectInfo));
            }

            var paths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var assemblyNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var references = new List<MetadataReference>();

            void AddPath(string path)
            {
                if (string.IsNullOrWhiteSpace(path))
                {
                    return;
                }

                try
                {
                    var fullPath = Path.GetFullPath(path);
                    if (!File.Exists(fullPath) || !paths.Add(fullPath))
                    {
                        return;
                    }

                    var assemblyName = GetAssemblySimpleName(fullPath);
                    if (!string.IsNullOrWhiteSpace(assemblyName) && !assemblyNames.Add(assemblyName))
                    {
                        return;
                    }

                    references.Add(MetadataReference.CreateFromFile(fullPath));
                }
                catch
                {
                    // Reference resolution is best-effort; the final compiler diagnostics
                    // report any missing assembly that still matters.
                }
            }

            foreach (var reference in projectInfo.References.Where(reference => !string.IsNullOrWhiteSpace(reference.Include)))
            {
                if (TryResolveHintPath(projectInfo, reference.HintPath, out var hintPath))
                {
                    AddPath(hintPath);
                    continue;
                }

                if (TryResolveReferenceAssembly(reference.SimpleName, out var referenceAssemblyPath))
                {
                    AddPath(referenceAssemblyPath);
                    continue;
                }

                foreach (var gacPath in ResolveGacReferencePaths(reference))
                {
                    AddPath(gacPath);
                }
            }

            foreach (var defaultPath in GetDefaultReferencePaths())
            {
                AddPath(defaultPath);
            }

            return references;
        }

        private static IEnumerable<string> GetDefaultReferencePaths()
        {
            yield return typeof(object).Assembly.Location;
            yield return typeof(Uri).Assembly.Location;
            yield return typeof(Enumerable).Assembly.Location;
            yield return typeof(System.Xml.Linq.XDocument).Assembly.Location;
            yield return typeof(System.Data.DataTable).Assembly.Location;
            yield return typeof(System.Configuration.ConfigurationElementCollection).Assembly.Location;
            yield return typeof(System.Security.Cryptography.X509Certificates.X509Certificate2).Assembly.Location;
            yield return typeof(System.Net.Http.HttpClient).Assembly.Location;
            yield return typeof(System.Windows.Forms.Form).Assembly.Location;
            yield return typeof(Microsoft.Win32.RegistryHive).Assembly.Location;
        }

        private static bool TryResolveHintPath(ProjectFileInfo projectInfo, string hintPath, out string resolvedPath)
        {
            resolvedPath = null;
            if (string.IsNullOrWhiteSpace(hintPath))
            {
                return false;
            }

            var expandedHintPath = Environment.ExpandEnvironmentVariables(hintPath);
            foreach (var candidate in GetHintPathCandidates(projectInfo, expandedHintPath))
            {
                if (File.Exists(candidate))
                {
                    resolvedPath = candidate;
                    return true;
                }
            }

            return false;
        }

        private static IEnumerable<string> GetHintPathCandidates(ProjectFileInfo projectInfo, string hintPath)
        {
            if (Path.IsPathRooted(hintPath))
            {
                yield return Path.GetFullPath(hintPath);
                yield break;
            }

            if (!string.IsNullOrWhiteSpace(projectInfo.ProjectDirectory))
            {
                yield return Path.GetFullPath(Path.Combine(projectInfo.ProjectDirectory, hintPath));
            }

            if (!string.IsNullOrWhiteSpace(projectInfo.SourceProjectDirectory))
            {
                yield return Path.GetFullPath(Path.Combine(projectInfo.SourceProjectDirectory, hintPath));
            }

            var packageRelativePath = TryGetPackageRelativePath(hintPath);
            if (string.IsNullOrWhiteSpace(packageRelativePath))
            {
                yield break;
            }

            foreach (var packageRoot in FindNearestPackageRoots(projectInfo.ProjectDirectory)
                         .Concat(FindNearestPackageRoots(projectInfo.SourceProjectDirectory))
                         .Distinct(StringComparer.OrdinalIgnoreCase))
            {
                yield return Path.GetFullPath(Path.Combine(packageRoot, packageRelativePath));
            }
        }

        private static string TryGetPackageRelativePath(string hintPath)
        {
            var parts = hintPath
                .Split(new[] { Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar }, StringSplitOptions.RemoveEmptyEntries);
            var packagesIndex = Array.FindIndex(parts, part => string.Equals(part, "packages", StringComparison.OrdinalIgnoreCase));
            if (packagesIndex < 0 || packagesIndex >= parts.Length - 1)
            {
                return null;
            }

            return Path.Combine(parts.Skip(packagesIndex + 1).ToArray());
        }

        private static IEnumerable<string> FindNearestPackageRoots(string startDirectory)
        {
            var directory = startDirectory;
            while (!string.IsNullOrWhiteSpace(directory))
            {
                var candidate = Path.Combine(directory, "packages");
                if (Directory.Exists(candidate))
                {
                    yield return candidate;
                }

                directory = Directory.GetParent(directory)?.FullName;
            }
        }

        private static bool TryResolveReferenceAssembly(string simpleName, out string resolvedPath)
        {
            resolvedPath = null;
            if (string.IsNullOrWhiteSpace(simpleName))
            {
                return false;
            }

            var programFilesX86 = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86);
            if (!string.IsNullOrWhiteSpace(programFilesX86))
            {
                var frameworkReferencePath = Path.Combine(
                    programFilesX86,
                    "Reference Assemblies",
                    "Microsoft",
                    "Framework",
                    ".NETFramework",
                    "v4.8",
                    simpleName + ".dll");

                if (File.Exists(frameworkReferencePath))
                {
                    resolvedPath = frameworkReferencePath;
                    return true;
                }
            }

            return false;
        }

        private static IEnumerable<string> ResolveGacReferencePaths(ProjectReferenceInfo reference)
        {
            foreach (var candidate in LoadByDisplayName(reference.Include))
            {
                yield return candidate;
            }

            foreach (var candidate in LoadByDisplayName(reference.SimpleName))
            {
                yield return candidate;
            }

            foreach (var candidate in FindByNamespace(reference.SimpleName))
            {
                yield return candidate;
            }
        }

        private static IEnumerable<string> LoadByDisplayName(string displayName)
        {
            if (string.IsNullOrWhiteSpace(displayName))
            {
                yield break;
            }

            Assembly assembly = null;
            try
            {
                assembly = Assembly.ReflectionOnlyLoad(displayName);
            }
            catch
            {
                try
                {
                    assembly = Assembly.Load(displayName);
                }
                catch
                {
                    assembly = null;
                }
            }

            if (assembly != null && !string.IsNullOrWhiteSpace(assembly.Location))
            {
                yield return assembly.Location;
            }
        }

        private static IEnumerable<string> FindByNamespace(string namespaceName)
        {
            if (string.IsNullOrWhiteSpace(namespaceName))
            {
                yield break;
            }

            List<Assembly> assemblies = null;
            try
            {
                assemblies = Loaders.GAC.FindAssemblyForNamespace(namespaceName);
            }
            catch
            {
                assemblies = null;
            }

            if (assemblies == null)
            {
                yield break;
            }

            foreach (var assembly in assemblies)
            {
                if (!string.IsNullOrWhiteSpace(assembly.Location))
                {
                    yield return assembly.Location;
                }
            }
        }

        private static string GetAssemblySimpleName(string path)
        {
            try
            {
                return AssemblyName.GetAssemblyName(path).Name;
            }
            catch
            {
                return Path.GetFileNameWithoutExtension(path);
            }
        }
    }
}
