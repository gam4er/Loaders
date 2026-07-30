using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Loaders.Obfuscation.Rewriters;
using Loaders.Obfuscation.Utilities;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Rename;

namespace Loaders.Obfuscation.Services
{
    internal static class ExtendedSymbolRenameService
    {
        private static readonly SymbolDisplayFormat FullyQualifiedDisplay =
            SymbolDisplayFormat.FullyQualifiedFormat;

        private static readonly string[] SensitiveAttributeNameParts =
        {
            "JsonProperty",
            "JsonPropertyName",
            "DataMember",
            "DataContract",
            "EnumMember",
            "XmlElement",
            "XmlAttribute",
            "XmlArray",
            "XmlArrayItem",
            "XmlRoot",
            "MessagePack",
            "Key",
            "ProtoMember",
            "YamlMember",
            "CommandOption",
            "Option",
            "Argument"
        };

        private static readonly string[] SerializationCallbackAttributeNames =
        {
            "OnSerializing",
            "OnSerialized",
            "OnDeserializing",
            "OnDeserialized",
            "OnSerializingAttribute",
            "OnSerializedAttribute",
            "OnDeserializingAttribute",
            "OnDeserializedAttribute"
        };

        public static IReadOnlyDictionary<string, string> RenameNamespacesAndTypes(
            ProjectFileInfo projectInfo,
            IObfuscatedNameProvider nameProvider,
            IReadOnlyCollection<string> excludedTypeNames)
        {
            if (projectInfo == null)
            {
                throw new ArgumentNullException(nameof(projectInfo));
            }

            if (nameProvider == null)
            {
                throw new ArgumentNullException(nameof(nameProvider));
            }

            RunStage(
                projectInfo,
                "LoadersExtendedNamespaceRename",
                nameProvider,
                CollectNamespaceCandidates);

            var typeCandidates = RunStage(
                projectInfo,
                "LoadersExtendedTypeRename",
                nameProvider,
                (project, generatedNames) => CollectTypeCandidates(project, excludedTypeNames, generatedNames));

            return BuildClassMap(typeCandidates);
        }

        public static void RenameMembersAndLocals(
            ProjectFileInfo projectInfo,
            IObfuscatedNameProvider nameProvider,
            bool skipOutAssignmentHelpers)
        {
            if (projectInfo == null)
            {
                throw new ArgumentNullException(nameof(projectInfo));
            }

            if (nameProvider == null)
            {
                throw new ArgumentNullException(nameof(nameProvider));
            }

            RunStage(
                projectInfo,
                "LoadersExtendedMemberRename",
                nameProvider,
                (project, generatedNames) => CollectMemberCandidates(project, skipOutAssignmentHelpers, generatedNames));

            RunStage(
                projectInfo,
                "LoadersExtendedParameterRename",
                nameProvider,
                CollectParameterCandidates);

            RunStage(
                projectInfo,
                "LoadersExtendedLocalRename",
                nameProvider,
                CollectLocalCandidates);
        }

        private static IReadOnlyList<RenameCandidate> RunStage(
            ProjectFileInfo projectInfo,
            string projectName,
            IObfuscatedNameProvider nameProvider,
            Func<Project, IReadOnlyCollection<string>, IReadOnlyList<SymbolCandidate>> collectCandidates)
        {
            using var workspace = new AdhocWorkspace();
            var project = ProjectReferenceResolver.CreateProject(workspace, projectInfo, projectName);
            var solution = project.Solution;
            var generatedNames = new HashSet<string>(StringComparer.Ordinal);
            var plannedRenames = collectCandidates(project, generatedNames)
                .Select(candidate => new RenameCandidate(
                    candidate.Symbol,
                    candidate.OriginalName,
                    nameProvider.Generate(candidate.Kind + ":" + GetSymbolIdentity(candidate.Symbol)),
                    candidate.Kind,
                    candidate.DeclarationKind,
                    candidate.FilePath))
                .ToList();

            var appliedRenames = new List<RenameCandidate>();

            foreach (var plannedRename in plannedRenames)
            {
                generatedNames.Add(plannedRename.NewName);
                var currentSymbol = ResolveCurrentSymbol(project, plannedRename);
                if (currentSymbol == null ||
                    generatedNames.Contains(currentSymbol.Name) ||
                    string.Equals(currentSymbol.Name, plannedRename.NewName, StringComparison.Ordinal))
                {
                    continue;
                }

                try
                {
                    solution = Renamer.RenameSymbolAsync(solution, currentSymbol, plannedRename.NewName, workspace.Options)
                        .GetAwaiter()
                        .GetResult();
                }
                catch (Exception exception) when (IsSkippableRenameFailure(exception))
                {
                    generatedNames.Add(currentSymbol.Name);
                    continue;
                }

                appliedRenames.Add(plannedRename);
                project = solution.GetProject(project.Id);
                if (project == null)
                {
                    throw new InvalidOperationException("Roslyn project was not available after extended rename.");
                }
            }

            PersistDocuments(project, solution);
            return appliedRenames;
        }

        private static bool IsSkippableRenameFailure(Exception exception)
        {
            return exception is InvalidOperationException ||
                   exception is ArgumentException ||
                   exception is NotSupportedException;
        }

        private static ISymbol ResolveCurrentSymbol(Project project, RenameCandidate candidate)
        {
            var document = project.Documents.FirstOrDefault(item =>
                string.Equals(item.FilePath, candidate.FilePath, StringComparison.OrdinalIgnoreCase));
            if (document == null)
            {
                return null;
            }

            var root = document.GetSyntaxRootAsync().GetAwaiter().GetResult();
            var semanticModel = document.GetSemanticModelAsync().GetAwaiter().GetResult();
            if (root == null || semanticModel == null)
            {
                return null;
            }

            switch (candidate.DeclarationKind)
            {
                case ExtendedDeclarationKind.Namespace:
                    return root.DescendantNodesAndSelf()
                        .OfType<NamespaceDeclarationSyntax>()
                        .Where(node => GetNamespaceLastName(node.Name.ToString()) == candidate.OriginalName)
                        .Select(node => semanticModel.GetDeclaredSymbol(node) as ISymbol)
                        .FirstOrDefault(symbol => symbol?.Name == candidate.OriginalName);
                case ExtendedDeclarationKind.FileScopedNamespace:
                    return root.DescendantNodesAndSelf()
                        .OfType<FileScopedNamespaceDeclarationSyntax>()
                        .Where(node => GetNamespaceLastName(node.Name.ToString()) == candidate.OriginalName)
                        .Select(node => semanticModel.GetDeclaredSymbol(node) as ISymbol)
                        .FirstOrDefault(symbol => symbol?.Name == candidate.OriginalName);
                case ExtendedDeclarationKind.Type:
                    return root.DescendantNodesAndSelf()
                        .OfType<BaseTypeDeclarationSyntax>()
                        .Where(node => node.Identifier.Text == candidate.OriginalName)
                        .Select(node => semanticModel.GetDeclaredSymbol(node) as ISymbol)
                        .Concat(root.DescendantNodesAndSelf()
                            .OfType<DelegateDeclarationSyntax>()
                            .Where(node => node.Identifier.Text == candidate.OriginalName)
                            .Select(node => semanticModel.GetDeclaredSymbol(node) as ISymbol))
                        .FirstOrDefault(symbol => symbol?.Name == candidate.OriginalName);
                case ExtendedDeclarationKind.Method:
                    return root.DescendantNodesAndSelf()
                        .OfType<MethodDeclarationSyntax>()
                        .Where(node => node.Identifier.Text == candidate.OriginalName)
                        .Select(node => semanticModel.GetDeclaredSymbol(node) as ISymbol)
                        .FirstOrDefault(symbol => symbol?.Name == candidate.OriginalName);
                case ExtendedDeclarationKind.Property:
                    return root.DescendantNodesAndSelf()
                        .OfType<PropertyDeclarationSyntax>()
                        .Where(node => node.Identifier.Text == candidate.OriginalName)
                        .Select(node => semanticModel.GetDeclaredSymbol(node) as ISymbol)
                        .FirstOrDefault(symbol => symbol?.Name == candidate.OriginalName);
                case ExtendedDeclarationKind.Field:
                    return root.DescendantNodesAndSelf()
                        .OfType<FieldDeclarationSyntax>()
                        .SelectMany(node => node.Declaration.Variables)
                        .Where(node => node.Identifier.Text == candidate.OriginalName)
                        .Select(node => semanticModel.GetDeclaredSymbol(node) as ISymbol)
                        .FirstOrDefault(symbol => symbol?.Name == candidate.OriginalName);
                case ExtendedDeclarationKind.EnumMember:
                    return root.DescendantNodesAndSelf()
                        .OfType<EnumMemberDeclarationSyntax>()
                        .Where(node => node.Identifier.Text == candidate.OriginalName)
                        .Select(node => semanticModel.GetDeclaredSymbol(node) as ISymbol)
                        .FirstOrDefault(symbol => symbol?.Name == candidate.OriginalName);
                case ExtendedDeclarationKind.Event:
                    return root.DescendantNodesAndSelf()
                        .OfType<EventDeclarationSyntax>()
                        .Where(node => node.Identifier.Text == candidate.OriginalName)
                        .Select(node => semanticModel.GetDeclaredSymbol(node) as ISymbol)
                        .Concat(root.DescendantNodesAndSelf()
                            .OfType<EventFieldDeclarationSyntax>()
                            .SelectMany(node => node.Declaration.Variables)
                            .Where(node => node.Identifier.Text == candidate.OriginalName)
                            .Select(node => semanticModel.GetDeclaredSymbol(node) as ISymbol))
                        .FirstOrDefault(symbol => symbol?.Name == candidate.OriginalName);
                case ExtendedDeclarationKind.Parameter:
                    return root.DescendantNodesAndSelf()
                        .OfType<ParameterSyntax>()
                        .Where(node => node.Identifier.Text == candidate.OriginalName)
                        .Select(node => semanticModel.GetDeclaredSymbol(node) as ISymbol)
                        .FirstOrDefault(symbol => symbol?.Name == candidate.OriginalName);
                case ExtendedDeclarationKind.Local:
                    return root.DescendantNodesAndSelf()
                        .OfType<VariableDeclaratorSyntax>()
                        .Where(node => node.Identifier.Text == candidate.OriginalName)
                        .Select(node => semanticModel.GetDeclaredSymbol(node) as ISymbol)
                        .Where(symbol => symbol is ILocalSymbol)
                        .Concat(root.DescendantNodesAndSelf()
                            .OfType<ForEachStatementSyntax>()
                            .Where(node => node.Identifier.Text == candidate.OriginalName)
                            .Select(node => semanticModel.GetDeclaredSymbol(node) as ISymbol))
                        .Concat(root.DescendantNodesAndSelf()
                            .OfType<CatchDeclarationSyntax>()
                            .Where(node => node.Identifier.Text == candidate.OriginalName)
                            .Select(node => semanticModel.GetDeclaredSymbol(node) as ISymbol))
                        .FirstOrDefault(symbol => symbol?.Name == candidate.OriginalName);
                default:
                    return null;
            }
        }

        private static string GetNamespaceLastName(string namespaceName)
        {
            if (string.IsNullOrWhiteSpace(namespaceName))
            {
                return string.Empty;
            }

            var parts = namespaceName.Split('.');
            return parts.Length == 0 ? namespaceName : parts[parts.Length - 1];
        }

        private static IReadOnlyList<SymbolCandidate> CollectNamespaceCandidates(
            Project project,
            IReadOnlyCollection<string> generatedNames)
        {
            var candidates = new List<SymbolCandidate>();
            var seen = new HashSet<string>(StringComparer.Ordinal);

            foreach (var document in GetCandidateDocuments(project))
            {
                var root = document.GetSyntaxRootAsync().GetAwaiter().GetResult();
                var semanticModel = document.GetSemanticModelAsync().GetAwaiter().GetResult();
                if (root == null || semanticModel == null)
                {
                    continue;
                }

                foreach (var namespaceDeclaration in root.DescendantNodesAndSelf().OfType<NamespaceDeclarationSyntax>())
                {
                    var symbol = semanticModel.GetDeclaredSymbol(namespaceDeclaration) as INamespaceSymbol;
                    AddNamespaceCandidate(
                        symbol,
                        candidates,
                        seen,
                        generatedNames,
                        ExtendedDeclarationKind.Namespace,
                        document.FilePath);
                }

                foreach (var namespaceDeclaration in root.DescendantNodesAndSelf().OfType<FileScopedNamespaceDeclarationSyntax>())
                {
                    var symbol = semanticModel.GetDeclaredSymbol(namespaceDeclaration) as INamespaceSymbol;
                    AddNamespaceCandidate(
                        symbol,
                        candidates,
                        seen,
                        generatedNames,
                        ExtendedDeclarationKind.FileScopedNamespace,
                        document.FilePath);
                }
            }

            return candidates
                .OrderByDescending(candidate => candidate.Symbol.ToDisplayString(FullyQualifiedDisplay).Count(ch => ch == '.'))
                .ThenBy(candidate => candidate.Symbol.ToDisplayString(FullyQualifiedDisplay), StringComparer.Ordinal)
                .ToList();
        }

        private static IReadOnlyList<SymbolCandidate> CollectTypeCandidates(
            Project project,
            IReadOnlyCollection<string> excludedTypeNames,
            IReadOnlyCollection<string> generatedNames)
        {
            var candidates = new List<SymbolCandidate>();
            var seen = new HashSet<string>(StringComparer.Ordinal);
            var excluded = new HashSet<string>(
                excludedTypeNames ?? Array.Empty<string>(),
                StringComparer.Ordinal);

            foreach (var document in GetCandidateDocuments(project))
            {
                var root = document.GetSyntaxRootAsync().GetAwaiter().GetResult();
                var semanticModel = document.GetSemanticModelAsync().GetAwaiter().GetResult();
                if (root == null || semanticModel == null)
                {
                    continue;
                }

                foreach (var typeDeclaration in root.DescendantNodesAndSelf().OfType<BaseTypeDeclarationSyntax>())
                {
                    var symbol = semanticModel.GetDeclaredSymbol(typeDeclaration) as INamedTypeSymbol;
                    if (IsRenameableType(symbol, excluded))
                    {
                        AddCandidate(symbol, ExtendedRenameKind.Type, candidates, seen, generatedNames);
                    }
                }

                foreach (var delegateDeclaration in root.DescendantNodesAndSelf().OfType<DelegateDeclarationSyntax>())
                {
                    var symbol = semanticModel.GetDeclaredSymbol(delegateDeclaration) as INamedTypeSymbol;
                    if (IsRenameableType(symbol, excluded))
                    {
                        AddCandidate(symbol, ExtendedRenameKind.Type, candidates, seen, generatedNames);
                    }
                }
            }

            return candidates
                .OrderByDescending(candidate => candidate.Symbol.ToDisplayString(FullyQualifiedDisplay).Count(ch => ch == '+'))
                .ThenBy(candidate => candidate.Symbol.ToDisplayString(FullyQualifiedDisplay), StringComparer.Ordinal)
                .ToList();
        }

        private static IReadOnlyList<SymbolCandidate> CollectMemberCandidates(
            Project project,
            bool skipOutAssignmentHelpers,
            IReadOnlyCollection<string> generatedNames)
        {
            var candidates = new List<SymbolCandidate>();
            var seen = new HashSet<string>(StringComparer.Ordinal);

            foreach (var document in GetCandidateDocuments(project))
            {
                var root = document.GetSyntaxRootAsync().GetAwaiter().GetResult();
                var semanticModel = document.GetSemanticModelAsync().GetAwaiter().GetResult();
                if (root == null || semanticModel == null)
                {
                    continue;
                }

                foreach (var enumMember in root.DescendantNodesAndSelf().OfType<EnumMemberDeclarationSyntax>())
                {
                    var symbol = semanticModel.GetDeclaredSymbol(enumMember) as IFieldSymbol;
                    if (IsRenameableEnumMember(symbol))
                    {
                        AddCandidate(symbol, ExtendedRenameKind.Member, candidates, seen, generatedNames);
                    }
                }

                foreach (var method in root.DescendantNodesAndSelf().OfType<MethodDeclarationSyntax>())
                {
                    var symbol = semanticModel.GetDeclaredSymbol(method) as IMethodSymbol;
                    if (IsRenameableMethod(symbol, skipOutAssignmentHelpers))
                    {
                        AddCandidate(symbol, ExtendedRenameKind.Member, candidates, seen, generatedNames);
                    }
                }

                foreach (var property in root.DescendantNodesAndSelf().OfType<PropertyDeclarationSyntax>())
                {
                    var symbol = semanticModel.GetDeclaredSymbol(property) as IPropertySymbol;
                    if (IsRenameableProperty(symbol))
                    {
                        AddCandidate(symbol, ExtendedRenameKind.Member, candidates, seen, generatedNames);
                    }
                }

                foreach (var field in root.DescendantNodesAndSelf().OfType<FieldDeclarationSyntax>())
                {
                    foreach (var variable in field.Declaration.Variables)
                    {
                        var symbol = semanticModel.GetDeclaredSymbol(variable) as IFieldSymbol;
                        if (IsRenameableField(symbol))
                        {
                            AddCandidate(symbol, ExtendedRenameKind.Member, candidates, seen, generatedNames);
                        }
                    }
                }

                foreach (var eventDeclaration in root.DescendantNodesAndSelf().OfType<EventDeclarationSyntax>())
                {
                    var symbol = semanticModel.GetDeclaredSymbol(eventDeclaration) as IEventSymbol;
                    if (IsRenameableEvent(symbol))
                    {
                        AddCandidate(symbol, ExtendedRenameKind.Member, candidates, seen, generatedNames);
                    }
                }

                foreach (var eventField in root.DescendantNodesAndSelf().OfType<EventFieldDeclarationSyntax>())
                {
                    foreach (var variable in eventField.Declaration.Variables)
                    {
                        var symbol = semanticModel.GetDeclaredSymbol(variable) as IEventSymbol;
                        if (IsRenameableEvent(symbol))
                        {
                            AddCandidate(symbol, ExtendedRenameKind.Member, candidates, seen, generatedNames);
                        }
                    }
                }
            }

            return candidates;
        }

        private static IReadOnlyList<SymbolCandidate> CollectParameterCandidates(
            Project project,
            IReadOnlyCollection<string> generatedNames)
        {
            var candidates = new List<SymbolCandidate>();
            var seen = new HashSet<string>(StringComparer.Ordinal);

            foreach (var document in GetCandidateDocuments(project))
            {
                var root = document.GetSyntaxRootAsync().GetAwaiter().GetResult();
                var semanticModel = document.GetSemanticModelAsync().GetAwaiter().GetResult();
                if (root == null || semanticModel == null)
                {
                    continue;
                }

                foreach (var parameter in root.DescendantNodesAndSelf().OfType<ParameterSyntax>())
                {
                    var symbol = semanticModel.GetDeclaredSymbol(parameter) as IParameterSymbol;
                    if (IsRenameableParameter(symbol))
                    {
                        AddCandidate(symbol, ExtendedRenameKind.Parameter, candidates, seen, generatedNames);
                    }
                }
            }

            return candidates;
        }

        private static IReadOnlyList<SymbolCandidate> CollectLocalCandidates(
            Project project,
            IReadOnlyCollection<string> generatedNames)
        {
            var candidates = new List<SymbolCandidate>();
            var seen = new HashSet<string>(StringComparer.Ordinal);

            foreach (var document in GetCandidateDocuments(project))
            {
                var root = document.GetSyntaxRootAsync().GetAwaiter().GetResult();
                var semanticModel = document.GetSemanticModelAsync().GetAwaiter().GetResult();
                if (root == null || semanticModel == null)
                {
                    continue;
                }

                foreach (var variable in root.DescendantNodesAndSelf().OfType<VariableDeclaratorSyntax>())
                {
                    var symbol = semanticModel.GetDeclaredSymbol(variable) as ILocalSymbol;
                    if (IsRenameableLocal(symbol))
                    {
                        AddCandidate(symbol, ExtendedRenameKind.Local, candidates, seen, generatedNames);
                    }
                }

                foreach (var foreachStatement in root.DescendantNodesAndSelf().OfType<ForEachStatementSyntax>())
                {
                    var symbol = semanticModel.GetDeclaredSymbol(foreachStatement) as ILocalSymbol;
                    if (IsRenameableLocal(symbol))
                    {
                        AddCandidate(symbol, ExtendedRenameKind.Local, candidates, seen, generatedNames);
                    }
                }

                foreach (var catchDeclaration in root.DescendantNodesAndSelf().OfType<CatchDeclarationSyntax>())
                {
                    var symbol = semanticModel.GetDeclaredSymbol(catchDeclaration) as ILocalSymbol;
                    if (IsRenameableLocal(symbol))
                    {
                        AddCandidate(symbol, ExtendedRenameKind.Local, candidates, seen, generatedNames);
                    }
                }
            }

            return candidates;
        }

        private static IEnumerable<Document> GetCandidateDocuments(Project project)
        {
            return project.Documents.Where(document => !IsGeneratedFile(document.FilePath));
        }

        private static void AddNamespaceCandidate(
            INamespaceSymbol symbol,
            ICollection<SymbolCandidate> candidates,
            ISet<string> seen,
            IReadOnlyCollection<string> generatedNames,
            ExtendedDeclarationKind declarationKind,
            string filePath)
        {
            if (symbol == null ||
                symbol.IsGlobalNamespace ||
                string.IsNullOrWhiteSpace(symbol.Name))
            {
                return;
            }

            AddCandidate(symbol, ExtendedRenameKind.Namespace, candidates, seen, generatedNames, declarationKind, filePath);
        }

        private static void AddCandidate(
            ISymbol symbol,
            ExtendedRenameKind kind,
            ICollection<SymbolCandidate> candidates,
            ISet<string> seen,
            IReadOnlyCollection<string> generatedNames,
            ExtendedDeclarationKind? declarationKind = null,
            string filePath = null)
        {
            if (symbol == null ||
                generatedNames.Contains(symbol.Name))
            {
                return;
            }

            var identity = GetSymbolIdentity(symbol);
            if (!seen.Add(identity))
            {
                return;
            }

            var resolvedFilePath = filePath ?? symbol.Locations
                .FirstOrDefault(location => location.IsInSource)
                ?.SourceTree
                ?.FilePath;
            if (string.IsNullOrWhiteSpace(resolvedFilePath))
            {
                return;
            }

            candidates.Add(new SymbolCandidate(
                symbol,
                symbol.Name,
                kind,
                declarationKind ?? GetDeclarationKind(symbol, kind),
                resolvedFilePath));
        }

        private static ExtendedDeclarationKind GetDeclarationKind(ISymbol symbol, ExtendedRenameKind kind)
        {
            if (kind == ExtendedRenameKind.Namespace)
            {
                return ExtendedDeclarationKind.Namespace;
            }

            if (kind == ExtendedRenameKind.Type)
            {
                return ExtendedDeclarationKind.Type;
            }

            if (kind == ExtendedRenameKind.Parameter)
            {
                return ExtendedDeclarationKind.Parameter;
            }

            if (kind == ExtendedRenameKind.Local)
            {
                return ExtendedDeclarationKind.Local;
            }

            if (symbol is IMethodSymbol)
            {
                return ExtendedDeclarationKind.Method;
            }

            if (symbol is IPropertySymbol)
            {
                return ExtendedDeclarationKind.Property;
            }

            if (symbol is IEventSymbol)
            {
                return ExtendedDeclarationKind.Event;
            }

            if (symbol is IFieldSymbol field && field.ContainingType?.TypeKind == TypeKind.Enum)
            {
                return ExtendedDeclarationKind.EnumMember;
            }

            return ExtendedDeclarationKind.Field;
        }

        private static bool IsRenameableType(INamedTypeSymbol symbol, ISet<string> excludedTypeNames)
        {
            if (symbol == null ||
                symbol.IsImplicitlyDeclared ||
                string.IsNullOrWhiteSpace(symbol.Name) ||
                excludedTypeNames.Contains(symbol.Name) ||
                HasAnyGeneratedDeclaration(symbol) ||
                HasSensitiveAttribute(symbol))
            {
                return false;
            }

            return symbol.TypeKind == TypeKind.Class ||
                   symbol.TypeKind == TypeKind.Struct ||
                   symbol.TypeKind == TypeKind.Interface ||
                   symbol.TypeKind == TypeKind.Enum ||
                   symbol.TypeKind == TypeKind.Delegate;
        }

        private static bool IsRenameableEnumMember(IFieldSymbol symbol)
        {
            return symbol != null &&
                   !symbol.IsImplicitlyDeclared &&
                   symbol.ContainingType?.TypeKind == TypeKind.Enum &&
                   !HasAnyGeneratedDeclaration(symbol) &&
                   !HasSensitiveAttribute(symbol);
        }

        private static bool IsRenameableMethod(IMethodSymbol symbol, bool skipOutAssignmentHelpers)
        {
            if (symbol == null ||
                symbol.IsImplicitlyDeclared ||
                symbol.MethodKind != MethodKind.Ordinary ||
                symbol.IsOverride ||
                symbol.IsExtern ||
                symbol.ContainingType?.TypeKind == TypeKind.Interface ||
                HasAnyGeneratedDeclaration(symbol) ||
                HasSensitiveAttribute(symbol) ||
                HasDllImportAttribute(symbol) ||
                HasSerializationCallbackAttribute(symbol) ||
                IsCommonContractName(symbol.Name) ||
                IsInterfaceContractMember(symbol))
            {
                return false;
            }

            return !skipOutAssignmentHelpers || !IsOutAssignmentHelper(symbol);
        }

        private static bool IsRenameableProperty(IPropertySymbol symbol)
        {
            return symbol != null &&
                   !symbol.IsImplicitlyDeclared &&
                   !symbol.IsIndexer &&
                   !symbol.IsOverride &&
                   symbol.ContainingType?.TypeKind != TypeKind.Interface &&
                   !HasAnyGeneratedDeclaration(symbol) &&
                   !HasSensitiveAttribute(symbol) &&
                   !IsCommonContractName(symbol.Name) &&
                   !IsInterfaceContractMember(symbol);
        }

        private static bool IsRenameableField(IFieldSymbol symbol)
        {
            return symbol != null &&
                   !symbol.IsImplicitlyDeclared &&
                   symbol.AssociatedSymbol == null &&
                   symbol.ContainingType?.TypeKind != TypeKind.Enum &&
                   !HasAnyGeneratedDeclaration(symbol) &&
                   !HasSensitiveAttribute(symbol);
        }

        private static bool IsRenameableEvent(IEventSymbol symbol)
        {
            return symbol != null &&
                   !symbol.IsImplicitlyDeclared &&
                   !symbol.IsOverride &&
                   symbol.ContainingType?.TypeKind != TypeKind.Interface &&
                   !HasAnyGeneratedDeclaration(symbol) &&
                   !HasSensitiveAttribute(symbol) &&
                   !IsInterfaceContractMember(symbol);
        }

        private static bool IsRenameableParameter(IParameterSymbol symbol)
        {
            if (symbol == null ||
                symbol.IsImplicitlyDeclared ||
                string.IsNullOrWhiteSpace(symbol.Name) ||
                symbol.Name == "_" ||
                HasAnyGeneratedDeclaration(symbol) ||
                HasSensitiveAttribute(symbol))
            {
                return false;
            }

            var containingSymbol = symbol.ContainingSymbol;
            switch (containingSymbol)
            {
                case IMethodSymbol method:
                    return IsRenameableParameterContainer(method);
                case IPropertySymbol property:
                    return IsRenameableProperty(property);
                default:
                    return false;
            }
        }

        private static bool IsRenameableParameterContainer(IMethodSymbol method)
        {
            return method != null &&
                   !method.IsImplicitlyDeclared &&
                   method.MethodKind != MethodKind.AnonymousFunction &&
                   method.MethodKind != MethodKind.LocalFunction &&
                   method.MethodKind != MethodKind.PropertyGet &&
                   method.MethodKind != MethodKind.PropertySet &&
                   method.MethodKind != MethodKind.EventAdd &&
                   method.MethodKind != MethodKind.EventRemove &&
                   !method.IsOverride &&
                   !method.IsExtern &&
                   method.ContainingType?.TypeKind != TypeKind.Interface &&
                   !HasAnyGeneratedDeclaration(method) &&
                   !HasSensitiveAttribute(method) &&
                   !HasDllImportAttribute(method) &&
                   !HasSerializationCallbackAttribute(method) &&
                   !IsCommonContractName(method.Name) &&
                   !IsInterfaceContractMember(method);
        }

        private static bool IsRenameableLocal(ILocalSymbol symbol)
        {
            return symbol != null &&
                   !symbol.IsImplicitlyDeclared &&
                   !symbol.IsRef &&
                   string.IsNullOrWhiteSpace(symbol.Name) == false &&
                   symbol.Name != "_" &&
                   !HasAnyGeneratedDeclaration(symbol);
        }

        private static bool IsCommonContractName(string name)
        {
            return string.Equals(name, "Main", StringComparison.Ordinal) ||
                   string.Equals(name, "Dispose", StringComparison.Ordinal) ||
                   string.Equals(name, nameof(object.ToString), StringComparison.Ordinal) ||
                   string.Equals(name, nameof(object.GetHashCode), StringComparison.Ordinal) ||
                   string.Equals(name, nameof(object.Equals), StringComparison.Ordinal);
        }

        private static bool HasDllImportAttribute(ISymbol symbol)
        {
            return symbol.GetAttributes().Any(attribute =>
                string.Equals(attribute.AttributeClass?.Name, "DllImportAttribute", StringComparison.Ordinal) ||
                string.Equals(attribute.AttributeClass?.Name, "DllImport", StringComparison.Ordinal));
        }

        private static bool HasSerializationCallbackAttribute(ISymbol symbol)
        {
            return symbol.GetAttributes().Any(attribute =>
                SerializationCallbackAttributeNames.Contains(
                    attribute.AttributeClass?.Name ?? string.Empty,
                    StringComparer.Ordinal));
        }

        private static bool HasSensitiveAttribute(ISymbol symbol)
        {
            return symbol.GetAttributes().Any(IsSensitiveAttribute);
        }

        private static bool IsSensitiveAttribute(AttributeData attribute)
        {
            var name = attribute.AttributeClass?.Name ?? string.Empty;
            var fullName = attribute.AttributeClass?.ToDisplayString(FullyQualifiedDisplay) ?? string.Empty;

            return SensitiveAttributeNameParts.Any(part =>
                name.IndexOf(part, StringComparison.OrdinalIgnoreCase) >= 0 ||
                fullName.IndexOf(part, StringComparison.OrdinalIgnoreCase) >= 0);
        }

        private static bool IsInterfaceContractMember(ISymbol symbol)
        {
            var containingType = symbol.ContainingType;
            if (containingType == null)
            {
                return false;
            }

            foreach (var interfaceType in containingType.AllInterfaces)
            {
                foreach (var interfaceMember in interfaceType.GetMembers())
                {
                    var implementation = containingType.FindImplementationForInterfaceMember(interfaceMember);
                    if (SymbolEqualityComparer.Default.Equals(implementation, symbol))
                    {
                        return true;
                    }
                }
            }

            return false;
        }

        private static bool IsOutAssignmentHelper(IMethodSymbol symbol)
        {
            if (symbol.DeclaredAccessibility != Accessibility.Private ||
                !symbol.IsStatic ||
                !symbol.ReturnsVoid ||
                symbol.Parameters.Length == 0 ||
                symbol.Parameters.Last().RefKind != RefKind.Out)
            {
                return false;
            }

            var methodSyntax = symbol.DeclaringSyntaxReferences
                .Select(reference => reference.GetSyntax())
                .OfType<MethodDeclarationSyntax>()
                .FirstOrDefault();
            if (methodSyntax == null ||
                methodSyntax.Body?.Statements.Count != 1)
            {
                return false;
            }

            var lastParameterName = symbol.Parameters.Last().Name;
            if (!(methodSyntax.Body.Statements[0] is ExpressionStatementSyntax expressionStatement) ||
                !(expressionStatement.Expression is AssignmentExpressionSyntax assignment) ||
                !(assignment.Left is IdentifierNameSyntax left))
            {
                return false;
            }

            return string.Equals(left.Identifier.Text, lastParameterName, StringComparison.Ordinal);
        }

        private static bool HasAnyGeneratedDeclaration(ISymbol symbol)
        {
            return symbol.DeclaringSyntaxReferences.Any(reference =>
                IsGeneratedFile(reference.SyntaxTree?.FilePath));
        }

        private static string GetSymbolIdentity(ISymbol symbol)
        {
            var sourceLocation = symbol.Locations.FirstOrDefault(location => location.IsInSource);
            var locationKey = sourceLocation == null
                ? string.Empty
                : (sourceLocation.SourceTree?.FilePath ?? string.Empty) + ":" + sourceLocation.SourceSpan.Start;

            return symbol.Kind + ":" +
                   symbol.ToDisplayString(FullyQualifiedDisplay) + ":" +
                   locationKey;
        }

        private static IReadOnlyDictionary<string, string> BuildClassMap(IEnumerable<RenameCandidate> typeCandidates)
        {
            var classCandidates = typeCandidates
                .Where(candidate => candidate.Symbol is INamedTypeSymbol typeSymbol && typeSymbol.TypeKind == TypeKind.Class)
                .ToList();

            var duplicateNames = new HashSet<string>(
                classCandidates
                    .GroupBy(candidate => candidate.OriginalName, StringComparer.Ordinal)
                    .Where(group => group.Count() > 1)
                    .Select(group => group.Key),
                StringComparer.Ordinal);

            return classCandidates
                .Where(candidate => !duplicateNames.Contains(candidate.OriginalName))
                .ToDictionary(
                    candidate => candidate.OriginalName,
                    candidate => candidate.NewName,
                    StringComparer.Ordinal);
        }

        private static void PersistDocuments(Project project, Solution solution)
        {
            var updatedProject = solution.GetProject(project.Id);
            if (updatedProject == null)
            {
                return;
            }

            foreach (var documentId in updatedProject.DocumentIds)
            {
                var updatedDocument = solution.GetDocument(documentId);
                if (updatedDocument == null || string.IsNullOrWhiteSpace(updatedDocument.FilePath))
                {
                    continue;
                }

                var newText = updatedDocument.GetTextAsync().GetAwaiter().GetResult();
                File.WriteAllText(updatedDocument.FilePath, newText.ToString());
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

        private sealed class RenameCandidate
        {
            public RenameCandidate(
                ISymbol symbol,
                string originalName,
                string newName,
                ExtendedRenameKind kind,
                ExtendedDeclarationKind declarationKind,
                string filePath)
            {
                Symbol = symbol;
                OriginalName = originalName;
                NewName = newName;
                Kind = kind;
                DeclarationKind = declarationKind;
                FilePath = filePath;
            }

            public ISymbol Symbol { get; }
            public string OriginalName { get; }
            public string NewName { get; }
            public ExtendedRenameKind Kind { get; }
            public ExtendedDeclarationKind DeclarationKind { get; }
            public string FilePath { get; }
        }

        private sealed class SymbolCandidate
        {
            public SymbolCandidate(
                ISymbol symbol,
                string originalName,
                ExtendedRenameKind kind,
                ExtendedDeclarationKind declarationKind,
                string filePath)
            {
                Symbol = symbol;
                OriginalName = originalName;
                Kind = kind;
                DeclarationKind = declarationKind;
                FilePath = filePath;
            }

            public ISymbol Symbol { get; }
            public string OriginalName { get; }
            public ExtendedRenameKind Kind { get; }
            public ExtendedDeclarationKind DeclarationKind { get; }
            public string FilePath { get; }
        }

        private enum ExtendedRenameKind
        {
            Namespace,
            Type,
            Member,
            Parameter,
            Local
        }

        private enum ExtendedDeclarationKind
        {
            Namespace,
            FileScopedNamespace,
            Type,
            Method,
            Property,
            Field,
            EnumMember,
            Event,
            Parameter,
            Local
        }
    }
}
