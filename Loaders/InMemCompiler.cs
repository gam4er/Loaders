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

internal static class InMemCompiler
{
    private const string SourceFolder = "C:\\Users\\gam4er\\Documents\\GitHub\\Seatbelt_orig\\Seatbelt\\";
    private const string OutputFolder = "C:\\Users\\gam4er\\Documents\\GitHub\\Seatbelt_obf\\Seatbelt\\";
    private const string ProjectFileName = "Seatbelt.csproj";

    private static readonly IReadOnlyCollection<string> ExcludedClasses = new HashSet<string>
    {
        "Runtime",
        "TextFormatterBase",
        "CommandOutputTypeAttribute",
        "CommandOutputType",
        "Advapi32",
        "WindowsFirewallProfileSettings",
        "Principal",
        "WindowsDefenderSettings",
        "AsrRule",
        "AsrSettings",
        "AuditEntry",
        "Iphlpapi",
        "MTPuTTYConfig",
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
        // Ensure a fresh copy of the project exists under the output folder.
        PrepareOutputProject();

        var projectPaths = LoadProject(OutputFolder, ProjectFileName);
        ObfuscateStringLiterals(projectPaths.CsFiles);
        AddMethodOverloads(projectPaths.CsFiles);

        var classMap = CollectClassNameMap(projectPaths.CsFiles);
        
        // Semantic rename using Roslyn symbol APIs.
        ClassRenamer.RenameClasses(classMap, projectPaths.CsFiles);

        // Optional: syntactic fallback to rename constructor calls like 'new ClassName(...)'
        SimpleConstructorRenameService.RenameConstructors(classMap, projectPaths.CsFiles);

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
            var csvPath = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "class-map.csv"));
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
        Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "compilation-errors.log"));

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
        Assembly.Load(ms.ToArray()).EntryPoint?.Invoke(null, new object[] { new[] { "arg1", "arg2", "etc" } });
    }
}
