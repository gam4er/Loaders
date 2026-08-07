using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading;
using System.Xml.Linq;
using Loaders.Obfuscation.Rewriters;
using Loaders.Obfuscation.Services;
using Loaders.Obfuscation.Utilities;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Emit;
using Microsoft.CodeAnalysis.Formatting;
using Spectre.Console;
using Spectre.Console.Cli;

/// <summary>
/// Entry point for the Seatbelt obfuscation pipeline.
///
/// The compiler copies the original Seatbelt project into an isolated
/// working directory, runs all obfuscation steps over the copy and then
/// compiles and executes the obfuscated binary in-memory.
/// </summary>
internal static class InMemCompiler
{
    // Types that must not be renamed to avoid breaking interop/framework behavior.
    private static readonly IReadOnlyCollection<string> ExcludedClasses = new HashSet<string>
    {
        //"Runtime",
        //"TextFormatterBase",
        //"CommandOutputTypeAttribute",
        //"CommandOutputType",
        //"Advapi32",
        //"WindowsFirewallProfileSettings",
        //"Principal",
        //"WindowsDefenderSettings",
        //"AsrRule",
        //"AsrSettings",
        //"AuditEntry",
        //"Iphlpapi",
        //"MTPuTTYConfig",
        //"Kernel32",
        //"Ntdll",
        //"RegistryUtil",
        //"ExtensionMethods",
        //"MiscUtil",
        //"Shell32",
        //"SecurityUtil",
        //"NetAadJoinInfo",
        //"Secur32",
        //"FileUtil",
    };

    private static int Main(string[] args)
    {
        var app = new CommandApp<ObfuscationCommand>();
        app.Configure(config =>
        {
            config.SetApplicationName("Loaders");
            config.CaseSensitivity(CaseSensitivity.None);
            config.SetExceptionHandler((exception, resolver) =>
            {
                AnsiConsole.WriteException(exception, ExceptionFormats.ShortenPaths);
                return 1;
            });
        });

        return app.Run(args);
    }

    internal static int Run(
        string sourceFolder,
        string outputFolder,
        bool outAssignmentMethods,
        bool skipSymbolRenaming,
        bool renameExtendedSymbols,
        bool beLeo,
        StringObfuscationStrategy? stringObfuscationStrategy)
    {
        var normalizedSourceFolder = NormalizeDirectoryPath(sourceFolder);
        var requestedOutputFolder = NormalizeDirectoryPath(outputFolder);
        ValidatePaths(normalizedSourceFolder, requestedOutputFolder);
        var normalizedOutputFolder = ResolveAvailableOutputFolder(requestedOutputFolder);
        if (!string.Equals(requestedOutputFolder, normalizedOutputFolder, StringComparison.OrdinalIgnoreCase))
        {
            AnsiConsole.WriteLine($"Requested output already exists; using: {normalizedOutputFolder}");
        }

        var sourceProjectFileName = FindProjectFileName(normalizedSourceFolder);
        var nameProvider = ObfuscatedNameGenerator.CreateProvider(beLeo);

        PrepareOutputProject(normalizedSourceFolder, normalizedOutputFolder);

        var projectIdentity = ProjectIdentityObfuscationService.Obfuscate(
            normalizedOutputFolder,
            sourceProjectFileName,
            nameProvider);
        AnsiConsole.WriteLine(
            $"Project identity: {projectIdentity.OldAssemblyName} -> {projectIdentity.NewAssemblyName}");

        var projectFileName = projectIdentity.GeneratedProjectFileName;
        var projectPaths = LoadProject(
            normalizedOutputFolder,
            normalizedSourceFolder,
            projectFileName,
            sourceProjectFileName);

        var stringObfuscationResult = ProjectStringObfuscator.ObfuscateProject(
            new ProjectStringObfuscationRequest(projectPaths, nameProvider, stringObfuscationStrategy));
        WriteStringObfuscationMetrics(normalizedOutputFolder, stringObfuscationResult);

        projectPaths = LoadProject(
            normalizedOutputFolder,
            normalizedSourceFolder,
            projectFileName,
            sourceProjectFileName);

        if (outAssignmentMethods)
        {
            RunFileProgressWithCallback(
                "Rewriting assignments as out-methods",
                projectPaths.CsFiles,
                reportProgress => OutAssignmentMethodService.Rewrite(projectPaths, reportProgress, nameProvider));
        }

        if (!skipSymbolRenaming)
        {
            AddMethodOverloads(projectPaths.TransformableCsFiles);

            IReadOnlyDictionary<string, string> classMap;
            if (renameExtendedSymbols)
            {
                classMap = new Dictionary<string, string>();
                RunStatus("Renaming namespaces and types", context =>
                {
                    context.Status("[yellow]Resolving namespace and type symbols[/]");
                    classMap = ExtendedSymbolRenameService.RenameNamespacesAndTypes(
                        projectPaths,
                        nameProvider,
                        GetExcludedClassNames(projectPaths),
                        message => context.Status(message));
                    WriteClassMap(classMap, normalizedOutputFolder);
                });
            }
            else
            {
                classMap = CollectClassNameMap(
                    projectPaths.TransformableCsFiles,
                    nameProvider,
                    normalizedOutputFolder,
                    GetExcludedClassNames(projectPaths));

                RunStatus($"Renaming {classMap.Count} classes", context =>
                {
                    context.Status("[yellow]Resolving class symbols[/]");
                    ClassRenamer.RenameClasses(classMap, projectPaths.TransformableCsFiles);
                });
            }

            RunFileProgressWithCallback(
                "Fixing constructors",
                projectPaths.TransformableCsFiles,
                reportProgress => SimpleConstructorRenameService.RenameConstructors(
                    classMap,
                    projectPaths.TransformableCsFiles,
                    reportProgress));

            if (renameExtendedSymbols)
            {
                RunStatus("Renaming members, parameters and locals", context =>
                {
                    context.Status("[yellow]Resolving extended symbols[/]");
                    ExtendedSymbolRenameService.RenameMembersAndLocals(
                        projectPaths,
                        nameProvider,
                        outAssignmentMethods,
                        message => context.Status(message));
                });
            }
            else
            {
                IReadOnlyList<MethodRenameEntry> methodEntries = null;
                RunStatus("Preparing method renaming", context =>
                {
                    context.Status("[yellow]Collecting method symbols[/]");
                    methodEntries = MethodRenamer.CollectMethodEntries(
                        projectPaths.TransformableCsFiles,
                        skipOutAssignmentHelpers: outAssignmentMethods,
                        nameProvider: nameProvider);
                    context.Status($"[yellow]Renaming {methodEntries.Count} methods[/]");
                    MethodRenamer.RenameMethods(
                        methodEntries,
                        projectPaths.TransformableCsFiles);
                });
            }
        }

        var outputPath = string.Empty;
        var compilationSucceeded = false;
        RunStatus("Compiling obfuscated project", context =>
        {
            context.Status("[yellow]Emitting executable assembly[/]");
            compilationSucceeded = Compile(projectPaths, stringObfuscationResult, normalizedOutputFolder, out outputPath);
        });

        if (!compilationSucceeded)
        {
            return 1;
        }

        AnsiConsole.MarkupLine("[green]Obfuscation completed.[/]");
        AnsiConsole.WriteLine($"Output: {outputPath}");
        return 0;
    }

    private static string NormalizeDirectoryPath(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            throw new ArgumentException("A directory path is required.", nameof(path));
        }

        var fullPath = Path.GetFullPath(path);
        var root = Path.GetPathRoot(fullPath);
        if (!string.Equals(fullPath, root, StringComparison.OrdinalIgnoreCase))
        {
            fullPath = fullPath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        }

        return fullPath;
    }

    private static void ValidatePaths(string sourceFolder, string outputFolder)
    {
        if (!Directory.Exists(sourceFolder))
        {
            throw new DirectoryNotFoundException($"Source directory was not found: {sourceFolder}");
        }

        if (IsSameOrDescendant(sourceFolder, outputFolder) || IsSameOrDescendant(outputFolder, sourceFolder))
        {
            throw new InvalidOperationException("Source and output directories must be separate and cannot be nested.");
        }
    }

    private static string ResolveAvailableOutputFolder(string requestedOutputFolder)
    {
        if (!Directory.Exists(requestedOutputFolder) && !File.Exists(requestedOutputFolder))
        {
            return requestedOutputFolder;
        }

        for (var index = 1; index < 1000; index++)
        {
            var candidate = requestedOutputFolder + "_" + index.ToString("00", CultureInfo.InvariantCulture);
            if (!Directory.Exists(candidate) && !File.Exists(candidate))
            {
                return candidate;
            }
        }

        throw new InvalidOperationException(
            $"Could not find an available output directory near: {requestedOutputFolder}");
    }

    private static bool IsSameOrDescendant(string parent, string candidate)
    {
        if (string.Equals(parent, candidate, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        var parentPrefix = parent.EndsWith(Path.DirectorySeparatorChar.ToString(), StringComparison.Ordinal) ||
                           parent.EndsWith(Path.AltDirectorySeparatorChar.ToString(), StringComparison.Ordinal)
            ? parent
            : parent + Path.DirectorySeparatorChar;

        return candidate.StartsWith(parentPrefix, StringComparison.OrdinalIgnoreCase);
    }

    private static string FindProjectFileName(string sourceFolder)
    {
        var projectFiles = Directory.GetFiles(sourceFolder, "*.csproj", SearchOption.TopDirectoryOnly);
        if (projectFiles.Length != 1)
        {
            throw new InvalidOperationException(
                $"Expected exactly one .csproj in the source directory, found {projectFiles.Length}.");
        }

        return Path.GetFileName(projectFiles[0]);
    }

    private static void PrepareOutputProject(string sourceFolder, string outputFolder)
    {
        if (Directory.Exists(outputFolder))
        {
            throw new InvalidOperationException($"Output directory already exists: {outputFolder}");
        }

        Directory.CreateDirectory(outputFolder);

        var files = Directory.GetFiles(sourceFolder, "*", SearchOption.AllDirectories)
            .Where(file => !IsCopyExcluded(sourceFolder, file))
            .ToList();
        RunFileProgress("Copying project", files, file =>
        {
            var relative = GetRelativePath(sourceFolder, file);
            var destination = Path.Combine(outputFolder, relative);
            var destinationDir = Path.GetDirectoryName(destination);
            if (!string.IsNullOrEmpty(destinationDir))
            {
                Directory.CreateDirectory(destinationDir);
            }

            File.Copy(file, destination, overwrite: true);
        });
    }

    private static bool IsCopyExcluded(string sourceFolder, string path)
    {
        var relative = GetRelativePath(sourceFolder, path);
        var segments = relative.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        for (var index = 0; index < segments.Length - 1; index++)
        {
            if (IsExcludedCopyDirectoryName(segments[index]))
            {
                return true;
            }
        }

        var fileName = Path.GetFileName(path);
        return fileName.EndsWith(".user", StringComparison.OrdinalIgnoreCase) ||
               fileName.EndsWith(".suo", StringComparison.OrdinalIgnoreCase) ||
               fileName.EndsWith(".cache", StringComparison.OrdinalIgnoreCase) ||
               fileName.EndsWith(".dtbcache.json", StringComparison.OrdinalIgnoreCase) ||
               fileName.EndsWith(".csproj.user", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsExcludedCopyDirectoryName(string directoryName)
    {
        return string.Equals(directoryName, ".vs", StringComparison.OrdinalIgnoreCase) ||
               string.Equals(directoryName, "bin", StringComparison.OrdinalIgnoreCase) ||
               string.Equals(directoryName, "obj", StringComparison.OrdinalIgnoreCase) ||
               string.Equals(directoryName, ".git", StringComparison.OrdinalIgnoreCase) ||
               string.Equals(directoryName, ".idea", StringComparison.OrdinalIgnoreCase) ||
               string.Equals(directoryName, ".vscode", StringComparison.OrdinalIgnoreCase) ||
               string.Equals(directoryName, "TestResults", StringComparison.OrdinalIgnoreCase);
    }

    private static void WriteStringObfuscationMetrics(
        string outputFolder,
        ProjectStringObfuscationResult result)
    {
        if (result == null)
        {
            return;
        }

        var metricsDirectory = Path.Combine(outputFolder, "metrics");
        var logsDirectory = Path.Combine(outputFolder, "logs");
        Directory.CreateDirectory(metricsDirectory);
        Directory.CreateDirectory(logsDirectory);

        var metricsPath = Path.Combine(metricsDirectory, "string-obfuscation-metrics.txt");
        using (var writer = new StreamWriter(metricsPath, append: false, encoding: Encoding.UTF8))
        {
            writer.WriteLine("String obfuscation metrics");
            writer.WriteLine("LiteralOccurrences={0}", result.Statistics.LiteralOccurrences);
            writer.WriteLine("RewrittenOccurrences={0}", result.Statistics.RewrittenOccurrences);
            writer.WriteLine("SkippedOccurrences={0}", result.Statistics.SkippedOccurrences);
            writer.WriteLine("UniqueStrings={0}", result.Statistics.UniqueStrings);
            writer.WriteLine("DeduplicatedOccurrences={0}", result.Statistics.DeduplicatedOccurrences);
            writer.WriteLine("GeneratedSourceBytes={0}", result.Statistics.GeneratedSourceBytes);
            writer.WriteLine("ResourceBytes={0}", result.Statistics.ResourceBytes);
            writer.WriteLine("ObfuscationMilliseconds={0}", result.Statistics.ObfuscationMilliseconds);
            writer.WriteLine("LoaderSourcePath={0}", result.LoaderSourcePath);
            writer.WriteLine("ResourcePath={0}", result.ResourcePath);
            writer.WriteLine("ManifestResourceName={0}", result.ManifestResourceName);
            writer.WriteLine();
            writer.WriteLine("SkippedByReason");
            foreach (var item in result.Statistics.SkippedByReason.OrderBy(item => item.Key, StringComparer.Ordinal))
            {
                writer.WriteLine("{0}={1}", item.Key, item.Value);
            }

            writer.WriteLine();
            writer.WriteLine("CodecCounts");
            foreach (var item in result.Statistics.CodecCounts.OrderBy(item => item.Key, StringComparer.Ordinal))
            {
                writer.WriteLine("{0}={1}", item.Key, item.Value);
            }
        }

        var diagnosticsPath = Path.Combine(logsDirectory, "string-obfuscation-diagnostics.log");
        using (var writer = new StreamWriter(diagnosticsPath, append: false, encoding: Encoding.UTF8))
        {
            foreach (var diagnostic in result.Diagnostics)
            {
                writer.WriteLine("{0}\t{1}\t{2}", diagnostic.Path, diagnostic.Reason, diagnostic.Message);
            }
        }

        AnsiConsole.WriteLine(
            $"String resource: {result.Statistics.RewrittenOccurrences} replacements, {result.Statistics.UniqueStrings} unique, {result.Statistics.ResourceBytes} bytes.");
    }

    private static void EnsureStringDecoderReferences(string csprojPath, StringObfuscationStrategy? strategy)
    {
        var requiredReferences = GetRequiredStringDecoderReferences(strategy).ToArray();
        if (requiredReferences.Length == 0)
        {
            return;
        }

        var csproj = XDocument.Load(csprojPath);
        var root = csproj.Root ?? throw new InvalidOperationException("Invalid csproj content");
        var ns = root.Name.Namespace;
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

        var changed = false;
        foreach (var referenceName in requiredReferences)
        {
            if (existingReferences.Contains(referenceName))
            {
                continue;
            }

            itemGroup.Add(new XElement(ns + "Reference", new XAttribute("Include", referenceName)));
            changed = true;
        }

        if (changed)
        {
            csproj.Save(csprojPath);
        }
    }

    private static IEnumerable<string> GetRequiredStringDecoderReferences(StringObfuscationStrategy? strategy)
    {
        if (!strategy.HasValue ||
            strategy.Value == StringObfuscationStrategy.GZipBase64 ||
            strategy.Value == StringObfuscationStrategy.GZipLcgBase64)
        {
            yield return "System.IO.Compression";
        }

        if (strategy == StringObfuscationStrategy.BigIntegerPacking)
        {
            yield return "System.Numerics";
        }
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

    private static void RunStatus(string description, Action<StatusContext> action)
    {
        AnsiConsole.Status()
            .AutoRefresh(true)
            .Start(description, action);
    }

    private static void RunFileProgress(string description, IEnumerable<string> filePaths, Action<string> processFile)
    {
        var files = filePaths.ToList();
        if (files.Count == 0)
        {
            return;
        }

        AnsiConsole.Progress()
            .AutoClear(false)
            .Columns(new ProgressColumn[]
            {
                new TaskDescriptionColumn(),
                new ProgressBarColumn(),
                new PercentageColumn(),
                new RemainingTimeColumn(),
                new SpinnerColumn(),
            })
            .Start(context =>
            {
                var task = context.AddTask(description, autoStart: true, maxValue: files.Count);
                foreach (var file in files)
                {
                    processFile(file);
                    task.Increment(1);
                }
            });
    }

    private static void RunFileProgressWithCallback(
        string description,
        IEnumerable<string> filePaths,
        Action<Action<string>> processFiles)
    {
        var files = filePaths.ToList();
        if (files.Count == 0)
        {
            return;
        }

        AnsiConsole.Progress()
            .AutoClear(false)
            .Columns(new ProgressColumn[]
            {
                new TaskDescriptionColumn(),
                new ProgressBarColumn(),
                new PercentageColumn(),
                new RemainingTimeColumn(),
                new SpinnerColumn(),
            })
            .Start(context =>
            {
                var task = context.AddTask(description, autoStart: true, maxValue: files.Count);
                processFiles(_ => task.Increment(1));
            });
    }

    // Simple relative path helper compatible with .NET Framework 4.8.
    private static string GetRelativePath(string basePath, string fullPath)
    {
        var baseUri = new Uri(AppendDirectorySeparatorChar(basePath));
        var fullUri = new Uri(fullPath);
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

    private static ProjectFileInfo LoadProject(
        string projectRoot,
        string sourceProjectRoot,
        string generatedCsprojName,
        string sourceCsprojName)
    {
        var csprojPath = Path.Combine(projectRoot, generatedCsprojName);
        var sourceCsprojPath = Path.Combine(sourceProjectRoot, sourceCsprojName);
        XDocument csproj = XDocument.Load(csprojPath);
        XNamespace ns = csproj.Root?.Name.Namespace ?? throw new InvalidOperationException("Invalid csproj content");

        var projectDirectory = Path.GetDirectoryName(csprojPath) ?? string.Empty;

        var csFiles = csproj
            .Descendants(ns + "Compile")
            .Attributes("Include")
            .Select(a => Path.Combine(projectDirectory, a.Value))
            .ToList();

        var references = csproj
            .Descendants(ns + "Reference")
            .Select(reference => new ProjectReferenceInfo(
                reference.Attribute("Include")?.Value,
                reference.Element(ns + "HintPath")?.Value))
            .ToList();

        var embeddedResources = csproj
            .Descendants(ns + "EmbeddedResource")
            .Select(resource => new ProjectEmbeddedResourceInfo(
                resource.Attribute("Include")?.Value,
                resource.Element(ns + "LogicalName")?.Value))
            .ToList();

        if (!csFiles.Any())
        {
            throw new InvalidOperationException("No C# files found in the specified project.");
        }

        return new ProjectFileInfo(
            csprojPath,
            sourceCsprojPath,
            csFiles,
            references,
            embeddedResources,
            GetProjectProperty(csproj, ns, "OutputType"),
            GetProjectProperty(csproj, ns, "RootNamespace"),
            GetProjectProperty(csproj, ns, "AssemblyName"),
            GetProjectProperty(csproj, ns, "StartupObject"),
            GetProjectProperty(csproj, ns, "LangVersion"),
            string.Equals(GetProjectProperty(csproj, ns, "AllowUnsafeBlocks"), "true", StringComparison.OrdinalIgnoreCase));
    }

    private static string GetProjectProperty(XDocument csproj, XNamespace ns, string name)
    {
        return csproj
            .Descendants(ns + name)
            .Select(element => element.Value)
            .FirstOrDefault(value => !string.IsNullOrWhiteSpace(value)) ?? string.Empty;
    }

    private static void ObfuscateStringLiterals(IEnumerable<string> csFiles, StringObfuscationStrategy? strategy)
    {
        RunFileProgress("Obfuscating strings", csFiles, csFile =>
        {
            if (csFile.IndexOf("AssemblyInfo.cs", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                return;
            }

            var sourceCode = File.ReadAllText(csFile);
            var syntaxTree = CSharpSyntaxTree.ParseText(sourceCode);

            syntaxTree = StringLiteralObfuscationService.RemoveCommentsAndEnsureUsings(syntaxTree);
            syntaxTree = StringLiteralObfuscationService.ObfuscateStrings(syntaxTree, strategy);

            File.WriteAllText(csFile, syntaxTree.GetRoot().ToFullString());
        });
    }

    private static void AddMethodOverloads(IEnumerable<string> csFiles)
    {
        RunFileProgress("Adding method overloads", csFiles, csFile =>
        {
            var sourceCode = File.ReadAllText(csFile);
            var syntaxTree = CSharpSyntaxTree.ParseText(sourceCode);
            var rewriter = new MethodOverloadRewriter();
            var newRoot = rewriter.Visit(syntaxTree.GetRoot());
            var formattedRoot = Formatter.Format(newRoot, new AdhocWorkspace());
            File.WriteAllText(csFile, formattedRoot.ToFullString());
        });
    }

    private static Dictionary<string, string> CollectClassNameMap(
        IEnumerable<string> csFiles,
        IObfuscatedNameProvider nameProvider,
        string outputFolder,
        IReadOnlyCollection<string> excludedClassNames)
    {
        var classCollector = new ClassCollectionRewriter(nameProvider);

        RunFileProgress("Collecting classes", csFiles, csFile =>
        {
            var sourceCode = File.ReadAllText(csFile);
            var syntaxTree = CSharpSyntaxTree.ParseText(sourceCode);
            classCollector.Visit(syntaxTree.GetRoot());
        });

        var map = classCollector.GetClassMap()
            .Where(pair => excludedClassNames == null || !excludedClassNames.Contains(pair.Key))
            .OrderBy(pair => pair.Key)
            .ToDictionary(pair => pair.Key, pair => pair.Value);

        WriteClassMap(map, outputFolder);
        return map;
    }

    private static IReadOnlyCollection<string> GetExcludedClassNames(ProjectFileInfo projectInfo)
    {
        var excluded = new HashSet<string>(ExcludedClasses, StringComparer.Ordinal);
        var startupTypeName = GetStartupTypeName(projectInfo?.StartupObject);
        if (!string.IsNullOrWhiteSpace(startupTypeName))
        {
            excluded.Add(startupTypeName);
        }

        return excluded;
    }

    private static string GetStartupTypeName(string startupObject)
    {
        if (string.IsNullOrWhiteSpace(startupObject))
        {
            return string.Empty;
        }

        var lastDot = startupObject.LastIndexOf('.');
        return lastDot >= 0 && lastDot < startupObject.Length - 1
            ? startupObject.Substring(lastDot + 1)
            : startupObject.Trim();
    }

    private static void WriteClassMap(IReadOnlyDictionary<string, string> map, string outputFolder)
    {
        try
        {
            var csvPath = Path.GetFullPath(Path.Combine(outputFolder, "class-map.csv"));
            var csvDir = Path.GetDirectoryName(csvPath);
            if (!string.IsNullOrEmpty(csvDir))
            {
                Directory.CreateDirectory(csvDir);
            }

            using var writer = new StreamWriter(csvPath, false);
            writer.WriteLine("Original,Obfuscated");
            foreach (var kvp in map)
            {
                writer.WriteLine($"{kvp.Key},{kvp.Value}");
            }
        }
        catch
        {
            // CSV generation is best-effort only; ignore any IO failures.
        }
    }

    /// <summary>
    /// Optional legacy syntactic class obfuscation pass.
    ///
    /// Kept for experimentation and debugging; the main pipeline relies on
    /// semantic renaming via <see cref="ClassRenamer"/> and a focused
    /// syntactic constructor pass.
    /// </summary>
    private static void ApplyClassObfuscation(IEnumerable<string> csFiles, IReadOnlyDictionary<string, string> classMap)
    {
        var rewriter = new ClassObfuscationRewriter(classMap);

        foreach (var csFile in csFiles)
        {
            var sourceCode = File.ReadAllText(csFile);
            var syntaxTree = CSharpSyntaxTree.ParseText(sourceCode);
            var newRoot = rewriter.Visit(syntaxTree.GetRoot());
            File.WriteAllText(csFile, newRoot.ToFullString());
        }
    }

    private static bool Compile(
        ProjectFileInfo projectInfo,
        ProjectStringObfuscationResult stringObfuscationResult,
        string outputFolder,
        out string outputPath)
    {
        var metadataReferences = ProjectReferenceResolver.BuildMetadataReferences(projectInfo);
        var parseOptions = projectInfo.CreateParseOptions();
        var compilation = CSharpCompilation.Create(projectInfo.AssemblyName, options: projectInfo.CreateCompilationOptions());
        var syntaxTrees = projectInfo.CsFiles.ToDictionary(
            file => file,
            file => CSharpSyntaxTree.ParseText(File.ReadAllText(file), parseOptions, file));
        compilation = compilation.AddSyntaxTrees(syntaxTrees.Values);
        compilation = compilation.AddReferences(metadataReferences);

        outputPath = Path.Combine(outputFolder, projectInfo.OutputFileName);

        Directory.CreateDirectory(outputFolder);

        using var ms = new MemoryStream();
        EmitResult result;
        var manifestResources = BuildManifestResources(projectInfo, stringObfuscationResult);
        if (manifestResources.Count != 0)
        {
            result = compilation.Emit(ms, manifestResources: manifestResources);
        }
        else
        {
            result = compilation.Emit(ms);
        }

        if (!result.Success)
        {
            IEnumerable<Diagnostic> failures = result.Diagnostics.Where(diagnostic => diagnostic.IsWarningAsError || diagnostic.Severity == DiagnosticSeverity.Error);
            AnsiConsole.WriteLine($"Compilation failed with {failures.Count()} errors.");
            var compilationErrorsLogPath = Path.GetFullPath(Path.Combine(outputFolder, "compilation-errors.log"));
            AnsiConsole.WriteLine($"Compilation errors log: {compilationErrorsLogPath}");

            var compilationErrorsDirectory = Path.GetDirectoryName(compilationErrorsLogPath);
            if (!string.IsNullOrEmpty(compilationErrorsDirectory))
            {
                Directory.CreateDirectory(compilationErrorsDirectory);
            }

            using var logWriter = new StreamWriter(compilationErrorsLogPath, append: false);
            foreach (Diagnostic diagnostic in failures)
            {
                AnsiConsole.WriteLine("{0}: {1}, {2}", diagnostic.Id, diagnostic.GetMessage(), diagnostic.Location);
                logWriter.WriteLine("{0}: {1}, {2}", diagnostic.Id, diagnostic.GetMessage(), diagnostic.Location);
            }
            return false;
        }

        File.WriteAllBytes(outputPath, ms.ToArray());
        if (projectInfo.ShouldInvokeEntryPoint)
        {
            Assembly.Load(ms.ToArray()).EntryPoint?.Invoke(null, new object[] { new[] { "--help" } });
        }

        return true;
    }

    private static IReadOnlyList<ResourceDescription> BuildManifestResources(
        ProjectFileInfo projectInfo,
        ProjectStringObfuscationResult stringObfuscationResult)
    {
        var resources = new List<ResourceDescription>();
        var names = new HashSet<string>(StringComparer.Ordinal);

        ResourceDescription stringResource = null;
        if (stringObfuscationResult != null && stringObfuscationResult.HasResource)
        {
            stringResource = stringObfuscationResult.CreateRoslynResource();
        }

        foreach (var resource in projectInfo.EmbeddedResources)
        {
            if (string.IsNullOrWhiteSpace(resource.LogicalName) ||
                string.IsNullOrWhiteSpace(resource.Include))
            {
                continue;
            }

            if (Path.GetExtension(resource.Include).Equals(".resx", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            if (!names.Add(resource.LogicalName))
            {
                continue;
            }

            if (stringResource != null &&
                string.Equals(resource.LogicalName, stringObfuscationResult.ManifestResourceName, StringComparison.Ordinal))
            {
                resources.Add(stringResource);
                continue;
            }

            var resourcePath = Path.IsPathRooted(resource.Include)
                ? Path.GetFullPath(resource.Include)
                : Path.GetFullPath(Path.Combine(projectInfo.ProjectDirectory, resource.Include));
            if (!File.Exists(resourcePath))
            {
                continue;
            }

            var logicalName = resource.LogicalName;
            resources.Add(new ResourceDescription(
                logicalName,
                () => new FileStream(resourcePath, FileMode.Open, FileAccess.Read, FileShare.Read),
                isPublic: false));
        }

        if (stringResource != null && names.Add(stringObfuscationResult.ManifestResourceName))
        {
            resources.Add(stringResource);
        }

        return resources;
    }
}

public sealed class ObfuscationSettings : CommandSettings
{
    [CommandOption("--source <PATH>", isRequired: true)]
    [Description("Source directory containing the project to obfuscate.")]
    public string Source { get; set; }

    [CommandOption("--output <PATH>", isRequired: true)]
    [Description("Directory where the copied and obfuscated project is written.")]
    public string Output { get; set; }

    [CommandOption("--out-assignment-methods")]
    [Description("Rewrite safe local assignments through generated helper methods with out parameters.")]
    public bool OutAssignmentMethods { get; set; }

    [CommandOption("--rename-extended-symbols")]
    [Description("Rename namespaces, types, members, parameters and locals using conservative Roslyn semantic rules.")]
    public bool RenameExtendedSymbols { get; set; }

    [CommandOption("--skip-symbol-renaming")]
    [Description("Run project preparation and string obfuscation without namespace/type/member renaming.")]
    public bool SkipSymbolRenaming { get; set; }

    [CommandOption("--BeLeo|--be-leo")]
    [Description("Generate obfuscated identifiers from the embedded War and Peace text.")]
    public bool BeLeo { get; set; }

    [CommandOption("--string-obfuscation-strategy <STRATEGY>")]
    [Description("String literal obfuscation strategy. Omit to choose automatically per literal.")]
    public string StringObfuscationStrategy { get; set; }
}

public sealed class ObfuscationCommand : Command<ObfuscationSettings>
{
    protected override int Execute(CommandContext context, ObfuscationSettings settings, CancellationToken cancellationToken)
    {
        if (!TryParseStringObfuscationStrategy(settings.StringObfuscationStrategy, out var stringObfuscationStrategy))
        {
            var validValues = string.Join(", ", Enum.GetNames(typeof(StringObfuscationStrategy)));
            AnsiConsole.MarkupLine("[red]Invalid --string-obfuscation-strategy value.[/]");
            AnsiConsole.WriteLine($"Value: {settings.StringObfuscationStrategy}");
            AnsiConsole.WriteLine($"Valid values: {validValues}");
            return 1;
        }

        return InMemCompiler.Run(
            settings.Source,
            settings.Output,
            settings.OutAssignmentMethods,
            settings.SkipSymbolRenaming,
            settings.RenameExtendedSymbols,
            settings.BeLeo,
            stringObfuscationStrategy);
    }

    private static bool TryParseStringObfuscationStrategy(
        string value,
        out StringObfuscationStrategy? strategy)
    {
        strategy = null;
        if (string.IsNullOrWhiteSpace(value))
        {
            return true;
        }

        if (Enum.TryParse(value.Trim(), true, out StringObfuscationStrategy parsed) &&
            Enum.IsDefined(typeof(StringObfuscationStrategy), parsed))
        {
            strategy = parsed;
            return true;
        }

        return false;
    }
}
