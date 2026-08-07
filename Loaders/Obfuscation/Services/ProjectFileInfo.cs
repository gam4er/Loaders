using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using CSharpLanguageVersion = Microsoft.CodeAnalysis.CSharp.LanguageVersion;

namespace Loaders.Obfuscation.Services
{
    internal sealed class ProjectFileInfo
    {
        public ProjectFileInfo(
            string projectPath,
            string sourceProjectPath,
            IReadOnlyList<string> csFiles,
            IReadOnlyList<ProjectReferenceInfo> references,
            IReadOnlyList<ProjectEmbeddedResourceInfo> embeddedResources,
            string outputType,
            string rootNamespace,
            string assemblyName,
            string startupObject,
            string languageVersion,
            bool allowUnsafeBlocks)
        {
            ProjectPath = projectPath;
            SourceProjectPath = sourceProjectPath;
            ProjectDirectory = System.IO.Path.GetDirectoryName(projectPath) ?? string.Empty;
            SourceProjectDirectory = System.IO.Path.GetDirectoryName(sourceProjectPath) ?? string.Empty;
            CsFiles = csFiles;
            TransformableCsFiles = csFiles
                .Where(GeneratedFileClassifier.IsTransformableSourceFile)
                .ToArray();
            GeneratedCsFiles = csFiles
                .Where(GeneratedFileClassifier.IsGeneratedFile)
                .ToArray();
            References = references;
            EmbeddedResources = embeddedResources ?? Array.Empty<ProjectEmbeddedResourceInfo>();
            OutputType = string.IsNullOrWhiteSpace(outputType) ? "Exe" : outputType.Trim();
            RootNamespace = rootNamespace?.Trim() ?? string.Empty;
            AssemblyName = string.IsNullOrWhiteSpace(assemblyName)
                ? System.IO.Path.GetFileNameWithoutExtension(projectPath)
                : assemblyName.Trim();
            StartupObject = startupObject?.Trim() ?? string.Empty;
            LanguageVersion = languageVersion?.Trim() ?? string.Empty;
            AllowUnsafeBlocks = allowUnsafeBlocks;
        }

        public string ProjectPath { get; }
        public string SourceProjectPath { get; }
        public string ProjectDirectory { get; }
        public string SourceProjectDirectory { get; }
        public IReadOnlyList<string> CsFiles { get; }
        public IReadOnlyList<string> TransformableCsFiles { get; }
        public IReadOnlyList<string> GeneratedCsFiles { get; }
        public IReadOnlyList<ProjectReferenceInfo> References { get; }
        public IReadOnlyList<ProjectEmbeddedResourceInfo> EmbeddedResources { get; }
        public string OutputType { get; }
        public string RootNamespace { get; }
        public string AssemblyName { get; }
        public string StartupObject { get; }
        public string LanguageVersion { get; }
        public bool AllowUnsafeBlocks { get; }

        public string OutputFileName => OutputKind == OutputKind.DynamicallyLinkedLibrary
            ? AssemblyName + ".dll"
            : AssemblyName + ".exe";

        public bool ShouldInvokeEntryPoint => OutputKind == OutputKind.ConsoleApplication;

        public OutputKind OutputKind
        {
            get
            {
                if (string.Equals(OutputType, "Library", StringComparison.OrdinalIgnoreCase))
                {
                    return OutputKind.DynamicallyLinkedLibrary;
                }

                if (string.Equals(OutputType, "WinExe", StringComparison.OrdinalIgnoreCase))
                {
                    return OutputKind.WindowsApplication;
                }

                return OutputKind.ConsoleApplication;
            }
        }

        public CSharpParseOptions CreateParseOptions()
        {
            var options = CSharpParseOptions.Default;
            if (TryParseLanguageVersion(LanguageVersion, out var languageVersion))
            {
                options = options.WithLanguageVersion(languageVersion);
            }

            return options;
        }

        public CSharpCompilationOptions CreateCompilationOptions()
        {
            var options = new CSharpCompilationOptions(
                OutputKind,
                optimizationLevel: OptimizationLevel.Release,
                allowUnsafe: AllowUnsafeBlocks);

            return options;
        }

        private static bool TryParseLanguageVersion(string value, out CSharpLanguageVersion languageVersion)
        {
            languageVersion = CSharpLanguageVersion.Default;
            if (string.IsNullOrWhiteSpace(value))
            {
                return false;
            }

            switch (value.Trim().ToLowerInvariant())
            {
                case "default":
                    languageVersion = CSharpLanguageVersion.Default;
                    return true;
                case "latest":
                    languageVersion = CSharpLanguageVersion.Latest;
                    return true;
                case "latestmajor":
                    languageVersion = CSharpLanguageVersion.LatestMajor;
                    return true;
                case "preview":
                    languageVersion = CSharpLanguageVersion.Preview;
                    return true;
                case "7":
                case "7.0":
                    languageVersion = CSharpLanguageVersion.CSharp7;
                    return true;
                case "7.1":
                    languageVersion = CSharpLanguageVersion.CSharp7_1;
                    return true;
                case "7.2":
                    languageVersion = CSharpLanguageVersion.CSharp7_2;
                    return true;
                case "7.3":
                    languageVersion = CSharpLanguageVersion.CSharp7_3;
                    return true;
                case "8":
                case "8.0":
                    languageVersion = CSharpLanguageVersion.CSharp8;
                    return true;
                case "9":
                case "9.0":
                    languageVersion = CSharpLanguageVersion.CSharp9;
                    return true;
                case "10":
                case "10.0":
                    languageVersion = CSharpLanguageVersion.CSharp10;
                    return true;
                case "11":
                case "11.0":
                    languageVersion = CSharpLanguageVersion.CSharp11;
                    return true;
                case "12":
                case "12.0":
                    languageVersion = CSharpLanguageVersion.CSharp12;
                    return true;
                default:
                    return Enum.TryParse(value, ignoreCase: true, out languageVersion);
            }
        }
    }

    internal sealed class ProjectReferenceInfo
    {
        public ProjectReferenceInfo(string include, string hintPath)
        {
            Include = include?.Trim() ?? string.Empty;
            HintPath = hintPath?.Trim() ?? string.Empty;
        }

        public string Include { get; }
        public string HintPath { get; }

        public string SimpleName
        {
            get
            {
                if (string.IsNullOrWhiteSpace(Include))
                {
                    return string.Empty;
                }

                try
                {
                    return new System.Reflection.AssemblyName(Include).Name;
                }
                catch
                {
                    return Include.Split(',').FirstOrDefault()?.Trim() ?? Include;
                }
            }
        }
    }

    internal sealed class ProjectEmbeddedResourceInfo
    {
        public ProjectEmbeddedResourceInfo(string include, string logicalName)
        {
            Include = include?.Trim() ?? string.Empty;
            LogicalName = logicalName?.Trim() ?? string.Empty;
        }

        public string Include { get; }
        public string LogicalName { get; }
    }
}
