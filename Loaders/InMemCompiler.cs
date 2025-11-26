using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using System.Xml.Linq;
using Loaders.Obfuscation.Rewriters;
using Loaders.Obfuscation.Services;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Emit;
using Microsoft.CodeAnalysis.Formatting;

internal static class InMemCompiler
{
    private const string SourceFolder = "d:\\Documents\\GitHub\\Seatbelt\\Seatbelt\\";
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

    private static async Task Main()
    {
        var projectPaths = LoadProject(SourceFolder, ProjectFileName);
        await ObfuscateStringLiteralsAsync(projectPaths.CsFiles).ConfigureAwait(false);
        await AddMethodOverloadsAsync(projectPaths.CsFiles).ConfigureAwait(false);

        var classMap = CollectClassNameMap(projectPaths.CsFiles);
        await ApplyClassObfuscationAsync(projectPaths.CsFiles, classMap).ConfigureAwait(false);
        await ClassRenamer.RenameClassesAsync(classMap, projectPaths.CsFiles).ConfigureAwait(false);

        await CompileAsync(projectPaths.CsFiles, projectPaths.References).ConfigureAwait(false);
    }

    private static (IReadOnlyList<string> CsFiles, IReadOnlyList<string> References) LoadProject(string sourceFolder, string csprojName)
    {
        XDocument csproj = XDocument.Load(Path.Combine(sourceFolder, csprojName));
        XNamespace ns = csproj.Root?.Name.Namespace ?? throw new InvalidOperationException("Invalid csproj content");

        var csFiles = csproj
            .Descendants(ns + "Compile")
            .Attributes("Include")
            .Select(a => Path.Combine(Path.GetDirectoryName(Path.Combine(sourceFolder, csprojName)) ?? string.Empty, a.Value))
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

    private static async Task ObfuscateStringLiteralsAsync(IEnumerable<string> csFiles)
    {
        foreach (var csFile in csFiles)
        {
            if (csFile.Contains("AssemblyInfo.cs", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var sourceCode = await File.ReadAllTextAsync(csFile).ConfigureAwait(false);
            var syntaxTree = CSharpSyntaxTree.ParseText(sourceCode);

            syntaxTree = StringLiteralObfuscationService.RemoveCommentsAndEnsureUsings(syntaxTree);
            syntaxTree = StringLiteralObfuscationService.ObfuscateStrings(syntaxTree);

            await File.WriteAllTextAsync(csFile, syntaxTree.GetRoot().ToFullString()).ConfigureAwait(false);
        }
    }

    private static async Task AddMethodOverloadsAsync(IEnumerable<string> csFiles)
    {
        foreach (var csFile in csFiles)
        {
            var sourceCode = await File.ReadAllTextAsync(csFile).ConfigureAwait(false);
            var syntaxTree = CSharpSyntaxTree.ParseText(sourceCode);
            var rewriter = new MethodOverloadRewriter();
            var newRoot = rewriter.Visit(syntaxTree.GetRoot());
            var formattedRoot = Formatter.Format(newRoot, new AdhocWorkspace());
            await File.WriteAllTextAsync(csFile, formattedRoot.ToFullString()).ConfigureAwait(false);
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

    private static async Task ApplyClassObfuscationAsync(IEnumerable<string> csFiles, IReadOnlyDictionary<string, string> classMap)
    {
        var rewriter = new ClassObfuscationRewriter(classMap);

        foreach (var csFile in csFiles)
        {
            var sourceCode = await File.ReadAllTextAsync(csFile).ConfigureAwait(false);
            var syntaxTree = CSharpSyntaxTree.ParseText(sourceCode);
            var newRoot = rewriter.Visit(syntaxTree.GetRoot());
            await File.WriteAllTextAsync(csFile, newRoot.ToFullString()).ConfigureAwait(false);
        }
    }

    private static async Task CompileAsync(IEnumerable<string> csFiles, IReadOnlyList<string> references)
    {
        var metadataReferences = new List<MetadataReference>();

        foreach (var reference in references)
        {
            var assemblies = GAC.FindAssemblyForNamespace(reference);
            if (assemblies != null)
            {
                metadataReferences.AddRange(assemblies.Select(assembly => MetadataReference.CreateFromFile(assembly.Location)));
            }
            else
            {
                Console.WriteLine($"Assembly for namespace '{reference}' not found.");
            }
        }

        var eventingAssemblies = GAC.FindAssemblyForNamespace("System.Diagnostics.Eventing.Reader");
        metadataReferences.AddRange(eventingAssemblies.Select(assembly => MetadataReference.CreateFromFile(assembly.Location)));

        var options = new CSharpCompilationOptions(
            OutputKind.ConsoleApplication,
            optimizationLevel: OptimizationLevel.Release,
            allowUnsafe: false);

        var compilation = CSharpCompilation.Create(Path.GetRandomFileName(), options: options);
        var syntaxTrees = csFiles.ToDictionary(file => file, file => CSharpSyntaxTree.ParseText(File.ReadAllText(file)));
        compilation = compilation.AddSyntaxTrees(syntaxTrees.Values);
        compilation = compilation.AddReferences(metadataReferences);

        string outputPath = Path.Combine(Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location) ?? string.Empty, "Output.exe");

        using var ms = new MemoryStream();
        EmitResult result = compilation.Emit(ms);

        if (!result.Success)
        {
            IEnumerable<Diagnostic> failures = result.Diagnostics.Where(diagnostic => diagnostic.IsWarningAsError || diagnostic.Severity == DiagnosticSeverity.Error);
            Console.WriteLine($"Compilation failed occurs {failures.Count()} errors");
            foreach (Diagnostic diagnostic in failures)
            {
                Console.Error.WriteLine("{0}: {1}, {2}", diagnostic.Id, diagnostic.GetMessage(), diagnostic.Location);
            }
            return;
        }

        File.WriteAllBytes(outputPath, ms.ToArray());
        Assembly.Load(ms.ToArray()).EntryPoint?.Invoke(null, new object[] { new[] { "arg1", "arg2", "etc" } });
    }
}
