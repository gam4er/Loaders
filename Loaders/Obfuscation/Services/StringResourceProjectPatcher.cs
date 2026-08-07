using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Xml.Linq;
using Loaders.Obfuscation.Utilities;

namespace Loaders.Obfuscation.Services
{
    internal static class StringResourceProjectPatcher
    {
        public static void PatchProject(
            string projectPath,
            string loaderSourcePath,
            string resourcePath,
            string manifestResourceName,
            IReadOnlyCollection<StringRuntimeDependency> dependencies)
        {
            if (string.IsNullOrWhiteSpace(projectPath))
            {
                throw new ArgumentException("Project path is required.", nameof(projectPath));
            }

            var project = XDocument.Load(projectPath);
            var root = project.Root ?? throw new InvalidOperationException("Invalid project XML.");
            var ns = root.Name.Namespace;
            var projectDirectory = Path.GetDirectoryName(projectPath) ?? string.Empty;
            var sdkStyle = root.Attribute("Sdk") != null;

            var changed = false;
            var loaderInclude = ToProjectIncludePath(GetRelativePath(projectDirectory, loaderSourcePath));
            var resourceInclude = ToProjectIncludePath(GetRelativePath(projectDirectory, resourcePath));

            if (!sdkStyle)
            {
                changed |= EnsureCompileItem(root, ns, loaderInclude);
            }

            changed |= EnsureEmbeddedResourceItem(root, ns, resourceInclude, manifestResourceName);
            changed |= EnsureAssemblyReferences(root, ns, dependencies);

            if (changed)
            {
                project.Save(projectPath);
            }

            EnsurePackagesConfig(projectDirectory, dependencies);
        }

        private static bool EnsureCompileItem(XElement root, XNamespace ns, string include)
        {
            if (root.Descendants(ns + "Compile").Any(element =>
                    string.Equals(NormalizeInclude(element.Attribute("Include")?.Value), NormalizeInclude(include), StringComparison.OrdinalIgnoreCase)))
            {
                return false;
            }

            var itemGroup = root.Elements(ns + "ItemGroup")
                .FirstOrDefault(group => group.Elements(ns + "Compile").Any());
            if (itemGroup == null)
            {
                itemGroup = new XElement(ns + "ItemGroup");
                root.Add(itemGroup);
            }

            itemGroup.Add(new XElement(ns + "Compile", new XAttribute("Include", include)));
            return true;
        }

        private static bool EnsureEmbeddedResourceItem(
            XElement root,
            XNamespace ns,
            string include,
            string logicalName)
        {
            var existing = root.Descendants(ns + "EmbeddedResource")
                .FirstOrDefault(element =>
                    string.Equals(element.Element(ns + "LogicalName")?.Value, logicalName, StringComparison.Ordinal) ||
                    string.Equals(NormalizeInclude(element.Attribute("Include")?.Value), NormalizeInclude(include), StringComparison.OrdinalIgnoreCase));

            if (existing != null)
            {
                var changed = false;
                if (!string.Equals(existing.Attribute("Include")?.Value, include, StringComparison.Ordinal))
                {
                    existing.SetAttributeValue("Include", include);
                    changed = true;
                }

                changed |= SetElementValue(existing, ns + "LogicalName", logicalName);
                changed |= SetElementValue(existing, ns + "WithCulture", "false");
                return changed;
            }

            var itemGroup = root.Elements(ns + "ItemGroup")
                .FirstOrDefault(group => group.Elements(ns + "EmbeddedResource").Any());
            if (itemGroup == null)
            {
                itemGroup = new XElement(ns + "ItemGroup");
                root.Add(itemGroup);
            }

            itemGroup.Add(new XElement(ns + "EmbeddedResource",
                new XAttribute("Include", include),
                new XElement(ns + "LogicalName", logicalName),
                new XElement(ns + "WithCulture", "false")));
            return true;
        }

        private static bool EnsureAssemblyReferences(
            XElement root,
            XNamespace ns,
            IReadOnlyCollection<StringRuntimeDependency> dependencies)
        {
            if (dependencies == null || dependencies.Count == 0)
            {
                return false;
            }

            var changed = false;
            var existingReferences = new HashSet<string>(
                root.Descendants(ns + "Reference")
                    .Select(reference => GetReferenceSimpleName(reference.Attribute("Include")?.Value)),
                StringComparer.OrdinalIgnoreCase);

            var itemGroup = root.Elements(ns + "ItemGroup")
                .FirstOrDefault(group => group.Elements(ns + "Reference").Any());
            if (itemGroup == null)
            {
                itemGroup = new XElement(ns + "ItemGroup");
                root.Add(itemGroup);
            }

            foreach (var assemblyReference in dependencies
                         .Select(dependency => dependency.AssemblyReference)
                         .Where(value => !string.IsNullOrWhiteSpace(value))
                         .Distinct(StringComparer.OrdinalIgnoreCase))
            {
                var simpleName = GetReferenceSimpleName(assemblyReference);
                if (existingReferences.Contains(simpleName))
                {
                    continue;
                }

                itemGroup.Add(new XElement(ns + "Reference", new XAttribute("Include", assemblyReference)));
                existingReferences.Add(simpleName);
                changed = true;
            }

            return changed;
        }

        private static void EnsurePackagesConfig(
            string projectDirectory,
            IReadOnlyCollection<StringRuntimeDependency> dependencies)
        {
            if (dependencies == null)
            {
                return;
            }

            var packages = dependencies
                .Where(dependency => !string.IsNullOrWhiteSpace(dependency.PackageId) &&
                                     !string.IsNullOrWhiteSpace(dependency.PackageVersion))
                .GroupBy(dependency => dependency.PackageId, StringComparer.OrdinalIgnoreCase)
                .Select(group => group.First())
                .ToArray();
            if (packages.Length == 0)
            {
                return;
            }

            var packagesPath = Path.Combine(projectDirectory, "packages.config");
            XDocument document;
            XElement root;
            if (File.Exists(packagesPath))
            {
                document = XDocument.Load(packagesPath);
                root = document.Root ?? new XElement("packages");
            }
            else
            {
                root = new XElement("packages");
                document = new XDocument(root);
            }

            var changed = false;
            foreach (var package in packages)
            {
                var existing = root.Elements("package")
                    .FirstOrDefault(element =>
                        string.Equals(element.Attribute("id")?.Value, package.PackageId, StringComparison.OrdinalIgnoreCase));
                if (existing != null)
                {
                    continue;
                }

                root.Add(new XElement("package",
                    new XAttribute("id", package.PackageId),
                    new XAttribute("version", package.PackageVersion),
                    new XAttribute("targetFramework", "net48")));
                changed = true;
            }

            if (changed)
            {
                document.Save(packagesPath);
            }
        }

        private static bool SetElementValue(XElement parent, XName name, string value)
        {
            var element = parent.Element(name);
            if (element == null)
            {
                parent.Add(new XElement(name, value));
                return true;
            }

            if (string.Equals(element.Value, value, StringComparison.Ordinal))
            {
                return false;
            }

            element.Value = value;
            return true;
        }

        private static string GetReferenceSimpleName(string include)
        {
            if (string.IsNullOrWhiteSpace(include))
            {
                return string.Empty;
            }

            var commaIndex = include.IndexOf(',');
            return commaIndex >= 0
                ? include.Substring(0, commaIndex).Trim()
                : include.Trim();
        }

        private static string NormalizeInclude(string include)
        {
            return (include ?? string.Empty).Replace('/', '\\');
        }

        private static string ToProjectIncludePath(string relativePath)
        {
            return relativePath.Replace(Path.AltDirectorySeparatorChar, Path.DirectorySeparatorChar);
        }

        private static string GetRelativePath(string basePath, string fullPath)
        {
            var baseUri = new Uri(AppendDirectorySeparatorChar(Path.GetFullPath(basePath)));
            var fullUri = new Uri(Path.GetFullPath(fullPath));
            var relativeUri = baseUri.MakeRelativeUri(fullUri);
            var relativePath = Uri.UnescapeDataString(relativeUri.ToString());
            return relativePath.Replace('/', Path.DirectorySeparatorChar);
        }

        private static string AppendDirectorySeparatorChar(string path)
        {
            if (!path.EndsWith(Path.DirectorySeparatorChar.ToString()) &&
                !path.EndsWith(Path.AltDirectorySeparatorChar.ToString()))
            {
                return path + Path.DirectorySeparatorChar;
            }

            return path;
        }
    }
}
