using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Xml.Linq;
using Loaders.Obfuscation.Rewriters;
using Loaders.Obfuscation.Services;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Emit;
using Microsoft.CodeAnalysis.Formatting;

/// <summary>
/// Entry point for the Seatbelt obfuscation pipeline.
///
/// The compiler copies the original Seatbelt project into an isolated
/// working directory, runs all obfuscation steps over the copy and then
/// compiles and executes the obfuscated binary in-memory.
/// </summary>
internal static class InMemCompiler
{
    private const string SourceFolder = "C:\\Users\\gam4er\\Documents\\GitHub\\Seatbelt_orig\\Seatbelt\\";
    private const string OutputFolder = "C:\\Users\\gam4er\\Documents\\GitHub\\Seatbelt_obf\\Seatbelt\\";
    private const string ProjectFileName = "Seatbelt.csproj";

    // Types that must not be renamed to avoid breaking interop/framework behavior.
    private static readonly IReadOnlyCollection<string> ExcludedClasses = new HashSet<string>
    {
        "Runtime",
        //"TextFormatterBase",
        //"CommandOutputTypeAttribute",
        //"CommandOutputType",
        "Advapi32",
        //"WindowsFirewallProfileSettings",
        "Principal",
        //"WindowsDefenderSettings",
        //"AsrRule",
        //"AsrSettings",
        "AuditEntry",
        "Iphlpapi",
        //"MTPuTTYConfig",
        "Kernel32",
        "Ntdll",
        "RegistryUtil",
        "ExtensionMethods",
        "MiscUtil",
        "Shell32",
        "SecurityUtil",
        "NetAadJoinInfo",
        "Secur32",
        "FileUtil",
    };

    private static void Main()
    {
        // 1) Copy the original Seatbelt project into an isolated working folder
        //    so the source tree is never modified in-place.
        PrepareOutputProject();

        // 2) Load the copied csproj and enumerate all C# files and assembly references.
        var projectPaths = LoadProject(OutputFolder, ProjectFileName);

        // 3) Rewrite string literals and remove comments in the working copy.
        ObfuscateStringLiterals(projectPaths.CsFiles);

        // 4) Inject harmless method overloads to increase control-flow noise.
        AddMethodOverloads(projectPaths.CsFiles);

        // 5) Collect all class declarations (except excluded infrastructure types)
        //    and build a deterministic obfuscated name map.
        var classMap = CollectClassNameMap(projectPaths.CsFiles);

        // 6) First, perform semantic renames using Roslyn symbol APIs so all type
        //    references (base types, fields, parameters, object creations, etc.)
        //    are updated consistently.
        ClassRenamer.RenameClasses(classMap, projectPaths.CsFiles);

        // 7) Optionally, run a narrow syntactic pass to fix up remaining
        //    constructor calls like "new ClassName(...)" that semantic
        //    renaming may have missed in edge cases.
        SimpleConstructorRenameService.RenameConstructors(classMap, projectPaths.CsFiles);

        // 8) Compile the obfuscated project and execute the resulting assembly
        //    in-memory.
        Compile(projectPaths.CsFiles, projectPaths.References);
    }

    // Recursively copy the source project into the output folder so we never touch the original sources.
    private static void PrepareOutputProject()
    {
        if (Directory.Exists(OutputFolder))
        {
            Directory.Delete(OutputFolder, recursive: true);
        }

        Directory.CreateDirectory(OutputFolder);

        foreach (var directory in Directory.GetDirectories(SourceFolder, "*", SearchOption.AllDirectories))
        {
            var relative = GetRelativePath(SourceFolder, directory);
            Directory.CreateDirectory(Path.Combine(OutputFolder, relative));
        }

        foreach (var file in Directory.GetFiles(SourceFolder, "*", SearchOption.AllDirectories))
        {
            var relative = GetRelativePath(SourceFolder, file);
            var destination = Path.Combine(OutputFolder, relative);
            var destinationDir = Path.GetDirectoryName(destination);
            if (!string.IsNullOrEmpty(destinationDir))
            {
                Directory.CreateDirectory(destinationDir);
            }

            File.Copy(file, destination, overwrite: true);
        }
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

    private static (IReadOnlyList<string> CsFiles, IReadOnlyList<string> References) LoadProject(string projectRoot, string csprojName)
    {
        var csprojPath = Path.Combine(projectRoot, csprojName);
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
            .Attributes("Include")
            .Select(a => a.Value)
            .ToList();

        if (!csFiles.Any())
        {
            throw new InvalidOperationException("No C# files found in the specified project.");
        }

        return (csFiles, references);
    }

    private static void ObfuscateStringLiterals(IEnumerable<string> csFiles)
    {
        foreach (var csFile in csFiles)
        {
            if (csFile.IndexOf("AssemblyInfo.cs", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                continue;
            }

            var sourceCode = File.ReadAllText(csFile);
            var syntaxTree = CSharpSyntaxTree.ParseText(sourceCode);

            syntaxTree = StringLiteralObfuscationService.RemoveCommentsAndEnsureUsings(syntaxTree);
            syntaxTree = StringLiteralObfuscationService.ObfuscateStrings(syntaxTree);

            File.WriteAllText(csFile, syntaxTree.GetRoot().ToFullString());
        }
    }

    private static void AddMethodOverloads(IEnumerable<string> csFiles)
    {
        foreach (var csFile in csFiles)
        {
            var sourceCode = File.ReadAllText(csFile);
            var syntaxTree = CSharpSyntaxTree.ParseText(sourceCode);
            var rewriter = new MethodOverloadRewriter();
            var newRoot = rewriter.Visit(syntaxTree.GetRoot());
            var formattedRoot = Formatter.Format(newRoot, new AdhocWorkspace());
            File.WriteAllText(csFile, formattedRoot.ToFullString());
        }
    }

    private static Dictionary<string, string> CollectClassNameMap(IEnumerable<string> csFiles)
    {
        var classCollector = new ClassCollectionRewriter();

        foreach (var csFile in csFiles)
        {
            var sourceCode = File.ReadAllText(csFile);
            var syntaxTree = CSharpSyntaxTree.ParseText(sourceCode);
            classCollector.Visit(syntaxTree.GetRoot());
        }

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

    private static void Compile(IEnumerable<string> csFiles, IReadOnlyList<string> references)
    {
        var metadataReferences = new List<MetadataReference>();

        foreach (var reference in references)
        {
            var assemblies = Loaders.GAC.FindAssemblyForNamespace(reference);
            if (assemblies != null)
            {
                metadataReferences.AddRange(assemblies.Select(assembly => MetadataReference.CreateFromFile(assembly.Location)));
            }
            else
            {
                Console.WriteLine($"Assembly for namespace '{reference}' not found.");
            }
        }

        var eventingAssemblies = Loaders.GAC.FindAssemblyForNamespace("System.Diagnostics.Eventing.Reader");
        if (eventingAssemblies != null)
        {
            metadataReferences.AddRange(eventingAssemblies.Select(assembly => MetadataReference.CreateFromFile(assembly.Location)));
        }

        var options = new CSharpCompilationOptions(
            OutputKind.ConsoleApplication,
            optimizationLevel: OptimizationLevel.Release,
            allowUnsafe: false);

        // Ensure common framework assemblies used by injected noise code are referenced explicitly.
        var systemConfigurationAssembly = typeof(System.Configuration.ConfigurationElementCollection).Assembly.Location;
        metadataReferences.Add(MetadataReference.CreateFromFile(systemConfigurationAssembly));

        var compilation = CSharpCompilation.Create(Path.GetRandomFileName(), options: options);
        var syntaxTrees = csFiles.ToDictionary(file => file, file => CSharpSyntaxTree.ParseText(File.ReadAllText(file)));
        compilation = compilation.AddSyntaxTrees(syntaxTrees.Values);
        compilation = compilation.AddReferences(metadataReferences);

        var outputPath = Path.Combine(OutputFolder, "Output.exe");

        Directory.CreateDirectory(OutputFolder);

        using var ms = new MemoryStream();
        EmitResult result = compilation.Emit(ms);

        if (!result.Success)
        {
            IEnumerable<Diagnostic> failures = result.Diagnostics.Where(diagnostic => diagnostic.IsWarningAsError || diagnostic.Severity == DiagnosticSeverity.Error);
            Console.WriteLine($"Compilation failed occurs {failures.Count()} errors");

            var compilationErrorsDirectory = Path.GetDirectoryName(CompilationErrorsLogPath);
            if (!string.IsNullOrEmpty(compilationErrorsDirectory))
            {
                Directory.CreateDirectory(compilationErrorsDirectory);
            }

            using var logWriter = new StreamWriter(CompilationErrorsLogPath, append: false);
            foreach (Diagnostic diagnostic in failures)
            {
                Console.Error.WriteLine("{0}: {1}, {2}", diagnostic.Id, diagnostic.GetMessage(), diagnostic.Location);
                logWriter.WriteLine("{0}: {1}, {2}", diagnostic.Id, diagnostic.GetMessage(), diagnostic.Location);
            }
            return;
        }

        File.WriteAllBytes(outputPath, ms.ToArray());
        Assembly.Load(ms.ToArray()).EntryPoint?.Invoke(null, new object[] { new[] { "--help" } });
    }
}
