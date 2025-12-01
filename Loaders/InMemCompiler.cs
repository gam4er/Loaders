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
        var projectPaths = LoadProject(SourceFolder, OutputFolder, ProjectFileName);
        ObfuscateStringLiterals(projectPaths.CsFiles);
        AddMethodOverloads(projectPaths.CsFiles);

        var classMap = CollectClassNameMap(projectPaths.CsFiles);
        ApplyClassObfuscation(projectPaths.CsFiles, classMap);
        ClassRenamer.RenameClasses(classMap, projectPaths.CsFiles);

        Compile(projectPaths.CsFiles, projectPaths.References);
    }

    private static (IReadOnlyList<string> CsFiles, IReadOnlyList<string> References) LoadProject(string sourceFolder, string outputFolder, string csprojName)
    {
        XDocument csproj = XDocument.Load(Path.Combine(sourceFolder, csprojName));
        XNamespace ns = csproj.Root?.Name.Namespace ?? throw new InvalidOperationException("Invalid csproj content");

        var projectDirectory = Path.GetDirectoryName(Path.Combine(sourceFolder, csprojName)) ?? string.Empty;
        var outputDirectory = Path.GetDirectoryName(Path.Combine(outputFolder, csprojName)) ?? string.Empty;

        if (!string.IsNullOrEmpty(outputDirectory))
        {
            Directory.CreateDirectory(outputDirectory);
        }

        var outputCsprojPath = Path.Combine(outputDirectory, csprojName);
        File.Copy(Path.Combine(sourceFolder, csprojName), outputCsprojPath, true);

        var csFiles = csproj
            .Descendants(ns + "Compile")
            .Attributes("Include")
            .Select(a =>
            {
                var sourcePath = Path.Combine(projectDirectory, a.Value);
                var destinationPath = Path.Combine(outputDirectory, a.Value);

                var destinationFolder = Path.GetDirectoryName(destinationPath);
                if (!string.IsNullOrEmpty(destinationFolder))
                {
                    Directory.CreateDirectory(destinationFolder);
                }

                File.Copy(sourcePath, destinationPath, true);
                return destinationPath;
            })
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

        return classCollector.GetClassMap()
            .Where(pair => !ExcludedClasses.Contains(pair.Key))
            .OrderBy(pair => pair.Key)
            .ToDictionary(pair => pair.Key, pair => pair.Value);
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
