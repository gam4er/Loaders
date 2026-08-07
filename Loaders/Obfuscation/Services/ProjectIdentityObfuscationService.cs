using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using Loaders.Obfuscation.Utilities;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Loaders.Obfuscation.Services
{
    internal sealed class ProjectIdentityObfuscationResult
    {
        public ProjectIdentityObfuscationResult(
            string sourceProjectFileName,
            string generatedProjectFileName,
            string oldRootNamespace,
            string newRootNamespace,
            string oldAssemblyName,
            string newAssemblyName,
            string oldProjectGuid,
            string newProjectGuid)
        {
            SourceProjectFileName = sourceProjectFileName;
            GeneratedProjectFileName = generatedProjectFileName;
            OldRootNamespace = oldRootNamespace ?? string.Empty;
            NewRootNamespace = newRootNamespace ?? string.Empty;
            OldAssemblyName = oldAssemblyName ?? string.Empty;
            NewAssemblyName = newAssemblyName ?? string.Empty;
            OldProjectGuid = oldProjectGuid ?? string.Empty;
            NewProjectGuid = newProjectGuid ?? string.Empty;
        }

        public string SourceProjectFileName { get; }
        public string GeneratedProjectFileName { get; }
        public string OldRootNamespace { get; }
        public string NewRootNamespace { get; }
        public string OldAssemblyName { get; }
        public string NewAssemblyName { get; }
        public string OldProjectGuid { get; }
        public string NewProjectGuid { get; }
    }

    internal static class ProjectIdentityObfuscationService
    {
        public static ProjectIdentityObfuscationResult Obfuscate(
            string projectDirectory,
            string sourceProjectFileName,
            IObfuscatedNameProvider nameProvider)
        {
            if (string.IsNullOrWhiteSpace(projectDirectory))
            {
                throw new ArgumentException("Project directory is required.", nameof(projectDirectory));
            }

            if (string.IsNullOrWhiteSpace(sourceProjectFileName))
            {
                throw new ArgumentException("Source project file name is required.", nameof(sourceProjectFileName));
            }

            if (nameProvider == null)
            {
                throw new ArgumentNullException(nameof(nameProvider));
            }

            var projectPath = Path.GetFullPath(Path.Combine(projectDirectory, sourceProjectFileName));
            var project = XDocument.Load(projectPath);
            var root = project.Root ?? throw new InvalidOperationException("Invalid project file.");
            var ns = root.Name.Namespace;

            var oldAssemblyName = GetProjectProperty(root, ns, "AssemblyName");
            if (string.IsNullOrWhiteSpace(oldAssemblyName))
            {
                oldAssemblyName = Path.GetFileNameWithoutExtension(sourceProjectFileName);
            }

            var oldRootNamespace = GetProjectProperty(root, ns, "RootNamespace");
            if (string.IsNullOrWhiteSpace(oldRootNamespace))
            {
                oldRootNamespace = oldAssemblyName;
            }

            var oldProjectGuid = GetProjectProperty(root, ns, "ProjectGuid");
            var newAssemblyName = nameProvider.Generate("project-assembly:" + oldAssemblyName);
            var newRootNamespace = nameProvider.Generate("project-root-namespace:" + oldRootNamespace);
            var newProjectGuid = "{" + Guid.NewGuid().ToString("D", CultureInfo.InvariantCulture).ToUpperInvariant() + "}";

            SetProjectProperty(root, ns, "ProjectGuid", newProjectGuid);
            SetProjectProperty(root, ns, "RootNamespace", newRootNamespace);
            SetProjectProperty(root, ns, "AssemblyName", newAssemblyName);
            ReplaceProjectPropertyPrefix(root, ns, "StartupObject", oldRootNamespace, newRootNamespace);

            var newProjectFileName = newAssemblyName + ".csproj";
            var newProjectPath = Path.GetFullPath(Path.Combine(projectDirectory, newProjectFileName));
            if (!string.Equals(projectPath, newProjectPath, StringComparison.OrdinalIgnoreCase) &&
                File.Exists(newProjectPath))
            {
                throw new IOException("Generated project file already exists: " + newProjectPath);
            }

            project.Save(projectPath);

            RewriteRootNamespace(projectDirectory, root, ns, oldRootNamespace, newRootNamespace);
            RewriteAssemblyInfo(projectDirectory, root, ns, newAssemblyName);

            if (!string.Equals(projectPath, newProjectPath, StringComparison.OrdinalIgnoreCase))
            {
                File.Move(projectPath, newProjectPath);
            }

            return new ProjectIdentityObfuscationResult(
                sourceProjectFileName,
                newProjectFileName,
                oldRootNamespace,
                newRootNamespace,
                oldAssemblyName,
                newAssemblyName,
                oldProjectGuid,
                newProjectGuid);
        }

        private static string GetProjectProperty(XElement root, XNamespace ns, string propertyName)
        {
            return root.Descendants(ns + propertyName)
                .Select(element => element.Value)
                .FirstOrDefault(value => !string.IsNullOrWhiteSpace(value)) ?? string.Empty;
        }

        private static void SetProjectProperty(XElement root, XNamespace ns, string propertyName, string value)
        {
            var element = root.Descendants(ns + propertyName).FirstOrDefault();
            if (element == null)
            {
                var propertyGroup = root.Elements(ns + "PropertyGroup").FirstOrDefault();
                if (propertyGroup == null)
                {
                    propertyGroup = new XElement(ns + "PropertyGroup");
                    root.AddFirst(propertyGroup);
                }

                element = new XElement(ns + propertyName);
                propertyGroup.Add(element);
            }

            element.Value = value;
        }

        private static void ReplaceProjectPropertyPrefix(
            XElement root,
            XNamespace ns,
            string propertyName,
            string oldPrefix,
            string newPrefix)
        {
            if (string.IsNullOrWhiteSpace(oldPrefix) || string.IsNullOrWhiteSpace(newPrefix))
            {
                return;
            }

            foreach (var element in root.Descendants(ns + propertyName))
            {
                if (TryReplaceRootPrefix(element.Value, oldPrefix, newPrefix, out var replacement))
                {
                    element.Value = replacement;
                }
            }
        }

        private static void RewriteRootNamespace(
            string projectDirectory,
            XElement root,
            XNamespace ns,
            string oldRootNamespace,
            string newRootNamespace)
        {
            if (string.IsNullOrWhiteSpace(oldRootNamespace) ||
                string.IsNullOrWhiteSpace(newRootNamespace) ||
                string.Equals(oldRootNamespace, newRootNamespace, StringComparison.Ordinal))
            {
                return;
            }

            var rewriter = new RootNamespaceRewriter(oldRootNamespace, newRootNamespace);
            foreach (var file in GetCompileFiles(projectDirectory, root, ns))
            {
                if (!File.Exists(file))
                {
                    continue;
                }

                var source = File.ReadAllText(file);
                var tree = CSharpSyntaxTree.ParseText(source, path: file);
                var oldRoot = tree.GetRoot();
                var newRoot = rewriter.Visit(oldRoot);
                newRoot = RewriteDisabledRootNamespaceTrivia(newRoot ?? oldRoot, oldRootNamespace, newRootNamespace);
                var rewritten = newRoot.ToFullString();
                if (!string.Equals(source, rewritten, StringComparison.Ordinal))
                {
                    File.WriteAllText(file, rewritten);
                }
            }
        }

        private static SyntaxNode RewriteDisabledRootNamespaceTrivia(
            SyntaxNode root,
            string oldRootNamespace,
            string newRootNamespace)
        {
            var disabledTextTrivia = root
                .DescendantTrivia(descendIntoTrivia: true)
                .Where(trivia => trivia.IsKind(SyntaxKind.DisabledTextTrivia))
                .ToList();
            if (disabledTextTrivia.Count == 0)
            {
                return root;
            }

            return root.ReplaceTrivia(
                disabledTextTrivia,
                (originalTrivia, rewrittenTrivia) =>
                {
                    var originalText = originalTrivia.ToFullString();
                    var rewrittenText = RewriteInactiveRootNamespaceText(
                        originalText,
                        oldRootNamespace,
                        newRootNamespace);
                    return string.Equals(originalText, rewrittenText, StringComparison.Ordinal)
                        ? originalTrivia
                        : SyntaxFactory.DisabledText(rewrittenText);
                });
        }

        private static string RewriteInactiveRootNamespaceText(
            string source,
            string oldRootNamespace,
            string newRootNamespace)
        {
            var escapedOldRoot = Regex.Escape(oldRootNamespace);
            source = Regex.Replace(
                source,
                @"\bnamespace\s+" + escapedOldRoot + @"(?=\.|\s|\{|$)",
                match => match.Value.Substring(0, match.Value.Length - oldRootNamespace.Length) + newRootNamespace,
                RegexOptions.CultureInvariant);

            source = Regex.Replace(
                source,
                @"\busing\s+" + escapedOldRoot + @"(?=\.|;|\s)",
                match => match.Value.Substring(0, match.Value.Length - oldRootNamespace.Length) + newRootNamespace,
                RegexOptions.CultureInvariant);

            return source;
        }

        private static IEnumerable<string> GetCompileFiles(string projectDirectory, XElement root, XNamespace ns)
        {
            return root.Descendants(ns + "Compile")
                .Attributes("Include")
                .Select(attribute => Path.GetFullPath(Path.Combine(projectDirectory, attribute.Value)));
        }

        private static void RewriteAssemblyInfo(
            string projectDirectory,
            XElement root,
            XNamespace ns,
            string newAssemblyName)
        {
            foreach (var file in GetCompileFiles(projectDirectory, root, ns))
            {
                if (!GeneratedFileClassifier.IsAssemblyInfoFile(file) || !File.Exists(file))
                {
                    continue;
                }

                var source = File.ReadAllText(file);
                var tree = CSharpSyntaxTree.ParseText(source, path: file);
                var rewriter = new AssemblyInfoRewriter(newAssemblyName, Guid.NewGuid().ToString("D", CultureInfo.InvariantCulture));
                var oldRoot = tree.GetRoot();
                var newRoot = rewriter.Visit(oldRoot);
                if (newRoot != null)
                {
                    File.WriteAllText(file, newRoot.ToFullString());
                }
            }
        }

        private static bool TryReplaceRootPrefix(
            string value,
            string oldRootNamespace,
            string newRootNamespace,
            out string replacement)
        {
            replacement = value;
            if (string.IsNullOrWhiteSpace(value))
            {
                return false;
            }

            if (string.Equals(value, oldRootNamespace, StringComparison.Ordinal))
            {
                replacement = newRootNamespace;
                return true;
            }

            if (value.StartsWith(oldRootNamespace + ".", StringComparison.Ordinal))
            {
                replacement = newRootNamespace + value.Substring(oldRootNamespace.Length);
                return true;
            }

            if (value.StartsWith("global::" + oldRootNamespace, StringComparison.Ordinal))
            {
                var suffix = value.Substring(("global::" + oldRootNamespace).Length);
                if (suffix.Length == 0 || suffix.StartsWith(".", StringComparison.Ordinal))
                {
                    replacement = "global::" + newRootNamespace + suffix;
                    return true;
                }
            }

            return false;
        }

        private sealed class RootNamespaceRewriter : CSharpSyntaxRewriter
        {
            private readonly string _oldRootNamespace;
            private readonly string _newRootNamespace;

            public RootNamespaceRewriter(string oldRootNamespace, string newRootNamespace)
            {
                _oldRootNamespace = oldRootNamespace;
                _newRootNamespace = newRootNamespace;
            }

            public override SyntaxNode VisitNamespaceDeclaration(NamespaceDeclarationSyntax node)
            {
                var updated = (NamespaceDeclarationSyntax)base.VisitNamespaceDeclaration(node);
                return TryReplaceName(updated.Name, out var name)
                    ? updated.WithName(name.WithTriviaFrom(updated.Name))
                    : updated;
            }

            public override SyntaxNode VisitFileScopedNamespaceDeclaration(FileScopedNamespaceDeclarationSyntax node)
            {
                var updated = (FileScopedNamespaceDeclarationSyntax)base.VisitFileScopedNamespaceDeclaration(node);
                return TryReplaceName(updated.Name, out var name)
                    ? updated.WithName(name.WithTriviaFrom(updated.Name))
                    : updated;
            }

            public override SyntaxNode VisitUsingDirective(UsingDirectiveSyntax node)
            {
                var updated = (UsingDirectiveSyntax)base.VisitUsingDirective(node);
                if (updated.Name == null)
                {
                    return updated;
                }

                return TryReplaceName(updated.Name, out var name)
                    ? updated.WithName(name.WithTriviaFrom(updated.Name))
                    : updated;
            }

            public override SyntaxNode VisitQualifiedName(QualifiedNameSyntax node)
            {
                var updated = (QualifiedNameSyntax)base.VisitQualifiedName(node);
                return TryReplaceName(updated, out var name)
                    ? name.WithTriviaFrom(updated)
                    : updated;
            }

            private bool TryReplaceName(NameSyntax name, out NameSyntax replacement)
            {
                replacement = name;
                if (!TryReplaceRootPrefix(name.ToString(), _oldRootNamespace, _newRootNamespace, out var text))
                {
                    return false;
                }

                replacement = SyntaxFactory.ParseName(text);
                return true;
            }
        }

        private sealed class AssemblyInfoRewriter : CSharpSyntaxRewriter
        {
            private readonly IReadOnlyDictionary<string, string> _replacementValues;

            public AssemblyInfoRewriter(string newAssemblyName, string newComGuid)
            {
                _replacementValues = new Dictionary<string, string>(StringComparer.Ordinal)
                {
                    ["AssemblyTitle"] = newAssemblyName,
                    ["AssemblyDescription"] = string.Empty,
                    ["AssemblyCompany"] = string.Empty,
                    ["AssemblyProduct"] = newAssemblyName,
                    ["AssemblyCopyright"] = string.Empty,
                    ["AssemblyTrademark"] = string.Empty,
                    ["Guid"] = newComGuid
                };
            }

            public override SyntaxNode VisitAttribute(AttributeSyntax node)
            {
                var updated = (AttributeSyntax)base.VisitAttribute(node);
                var name = GetAttributeName(updated.Name);
                if (!_replacementValues.TryGetValue(name, out var value))
                {
                    return updated;
                }

                return updated.WithArgumentList(SyntaxFactory.AttributeArgumentList(
                    SyntaxFactory.SingletonSeparatedList(
                        SyntaxFactory.AttributeArgument(
                            SyntaxFactory.LiteralExpression(
                                SyntaxKind.StringLiteralExpression,
                                SyntaxFactory.Literal(value))))));
            }

            private static string GetAttributeName(NameSyntax name)
            {
                string value;
                if (name is QualifiedNameSyntax qualifiedName)
                {
                    value = qualifiedName.Right.Identifier.Text;
                }
                else if (name is AliasQualifiedNameSyntax aliasQualifiedName)
                {
                    value = aliasQualifiedName.Name.Identifier.Text;
                }
                else if (name is IdentifierNameSyntax identifierName)
                {
                    value = identifierName.Identifier.Text;
                }
                else
                {
                    value = name.ToString();
                }

                return value.EndsWith("Attribute", StringComparison.Ordinal)
                    ? value.Substring(0, value.Length - "Attribute".Length)
                    : value;
            }
        }
    }
}
