using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Xml.Linq;
using Loaders.Obfuscation.Rewriters;
using Loaders.Obfuscation.Services;
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
            config.SetExceptionHandler((exception, resolver) =>
            {
                AnsiConsole.WriteException(exception, ExceptionFormats.ShortenPaths);
                return 1;
            });
        });

        return app.Run(args);
    }

    internal static int Run(string sourceFolder, string outputFolder, bool outAssignmentMethods)
    {
        var normalizedSourceFolder = NormalizeDirectoryPath(sourceFolder);
        var normalizedOutputFolder = NormalizeDirectoryPath(outputFolder);
        ValidatePaths(normalizedSourceFolder, normalizedOutputFolder);
        var projectFileName = FindProjectFileName(normalizedSourceFolder);

        PrepareOutputProject(normalizedSourceFolder, normalizedOutputFolder);

        var projectPaths = LoadProject(normalizedOutputFolder, normalizedSourceFolder, projectFileName);

        ObfuscateStringLiterals(projectPaths.CsFiles);
        AddMethodOverloads(projectPaths.CsFiles);

        if (outAssignmentMethods)
        {
            RunFileProgressWithCallback(
                "Rewriting assignments as out-methods",
                projectPaths.CsFiles,
                reportProgress => OutAssignmentMethodService.Rewrite(projectPaths, reportProgress));
        }

        var classMap = CollectClassNameMap(projectPaths.CsFiles);

        RunStatus($"Renaming {classMap.Count} classes", context =>
        {
            context.Status("[yellow]Resolving class symbols[/]");
            ClassRenamer.RenameClasses(classMap, projectPaths.CsFiles);
        });

        RunFileProgressWithCallback(
            "Fixing constructors",
            projectPaths.CsFiles,
            reportProgress => SimpleConstructorRenameService.RenameConstructors(
                classMap,
                projectPaths.CsFiles,
                reportProgress));

        IReadOnlyDictionary<string, string> methodMap = null;
        RunStatus("Preparing method renaming", context =>
        {
            context.Status("[yellow]Collecting method symbols[/]");
            methodMap = MethodRenamer.CollectMethodMap(
                projectPaths.CsFiles,
                skipOutAssignmentHelpers: outAssignmentMethods);
            context.Status($"[yellow]Renaming {methodMap.Count} methods[/]");
            MethodRenamer.RenameMethods(
                methodMap,
                projectPaths.CsFiles,
                skipOutAssignmentHelpers: outAssignmentMethods);
        });

        var outputPath = string.Empty;
        var compilationSucceeded = false;
        RunStatus("Compiling obfuscated project", context =>
        {
            context.Status("[yellow]Emitting executable assembly[/]");
            compilationSucceeded = Compile(projectPaths, normalizedOutputFolder, out outputPath);
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
            Directory.Delete(outputFolder, recursive: true);
        }

        Directory.CreateDirectory(outputFolder);

        foreach (var directory in Directory.GetDirectories(sourceFolder, "*", SearchOption.AllDirectories))
        {
            var relative = GetRelativePath(sourceFolder, directory);
            Directory.CreateDirectory(Path.Combine(outputFolder, relative));
        }

        var files = Directory.GetFiles(sourceFolder, "*", SearchOption.AllDirectories);
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

    private static ProjectFileInfo LoadProject(string projectRoot, string sourceProjectRoot, string csprojName)
    {
        var csprojPath = Path.Combine(projectRoot, csprojName);
        var sourceCsprojPath = Path.Combine(sourceProjectRoot, csprojName);
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

        if (!csFiles.Any())
        {
            throw new InvalidOperationException("No C# files found in the specified project.");
        }

        return new ProjectFileInfo(
            csprojPath,
            sourceCsprojPath,
            csFiles,
            references,
            GetProjectProperty(csproj, ns, "OutputType"),
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

    private static void ObfuscateStringLiterals(IEnumerable<string> csFiles)
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
            syntaxTree = StringLiteralObfuscationService.ObfuscateStrings(syntaxTree);

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

    private static Dictionary<string, string> CollectClassNameMap(IEnumerable<string> csFiles)
    {
        var classCollector = new ClassCollectionRewriter();

        RunFileProgress("Collecting classes", csFiles, csFile =>
        {
            var sourceCode = File.ReadAllText(csFile);
            var syntaxTree = CSharpSyntaxTree.ParseText(sourceCode);
            classCollector.Visit(syntaxTree.GetRoot());
        });

        var map = classCollector.GetClassMap()
            .Where(pair => !ExcludedClasses.Contains(pair.Key))
            .OrderBy(pair => pair.Key)
            .ToDictionary(pair => pair.Key, pair => pair.Value);

        // Persist mapping to CSV to aid debugging / analysis of obfuscation.
        try
        {
            var csvPath = Path.GetFullPath(Path.Combine(".", "class-map.csv"));
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

        return map;
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

    private static readonly string CompilationErrorsLogPath =
        Path.GetFullPath(Path.Combine(".", "compilation-errors.log"));

    private static bool Compile(
        ProjectFileInfo projectInfo,
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
        EmitResult result = compilation.Emit(ms);

        if (!result.Success)
        {
            IEnumerable<Diagnostic> failures = result.Diagnostics.Where(diagnostic => diagnostic.IsWarningAsError || diagnostic.Severity == DiagnosticSeverity.Error);
            AnsiConsole.WriteLine($"Compilation failed with {failures.Count()} errors.");

            var compilationErrorsDirectory = Path.GetDirectoryName(CompilationErrorsLogPath);
            if (!string.IsNullOrEmpty(compilationErrorsDirectory))
            {
                Directory.CreateDirectory(compilationErrorsDirectory);
            }

            using var logWriter = new StreamWriter(CompilationErrorsLogPath, append: false);
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
}

public sealed class ObfuscationCommand : Command<ObfuscationSettings>
{
    protected override int Execute(CommandContext context, ObfuscationSettings settings, CancellationToken cancellationToken)
    {
        return InMemCompiler.Run(settings.Source, settings.Output, settings.OutAssignmentMethods);
    }
}
