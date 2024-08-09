using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Xml.Linq;

using Loaders;

using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Emit;

class InMemCompiler
{
    static void Main()
    {
        string sourceFolder = "d:\\Documents\\GitHub\\Seatbelt\\Seatbelt\\";

        XDocument csproj = XDocument.Load(sourceFolder + "Seatbelt.csproj");
        XNamespace ns = csproj.Root.Name.Namespace;

        // Получаем пути к исходным файлам
        var csFiles = csproj.Descendants(ns + "Compile").Attributes("Include").Select(a => Path.Combine(Path.GetDirectoryName(sourceFolder + "Seatbelt.csproj"), a.Value)).ToList();

        // Получаем зависимости
        var references = csproj.Descendants(ns + "Reference").Attributes("Include").Select(a => a.Value).ToList();

        if (csFiles.Count == 0)
        {
            Console.WriteLine("No C# files found in the specified project.");
            return;
        }

        Dictionary<string, string> classMap = new Dictionary<string, string>();
        Dictionary<string, string> methodMap = new Dictionary<string, string>();
        Dictionary<string, SyntaxTree> syntaxTrees = new Dictionary<string, SyntaxTree>();

        foreach (var csFile in csFiles)
        {
            string sourceCode = File.ReadAllText(csFile);
            SyntaxTree syntaxTree = CSharpSyntaxTree.ParseText(sourceCode);            

            if (csFile.Contains("AssemblyInfo.cs"))
            {
                //syntaxTrees [csFile] = syntaxTree;
                continue;
            }

            syntaxTree = ObfuscateStringLiterals.AddUsingsNoComments(syntaxTree);
            syntaxTree = ObfuscateStringLiterals.Obfuscate(syntaxTree);  // Обфускация

            syntaxTrees [csFile] = syntaxTree;

            var obfuscatedCode = syntaxTree.GetRoot().ToFullString();
            File.WriteAllText(csFile, obfuscatedCode);

        }

        List<MetadataReference> metadataReferences = new List<MetadataReference>();

        // Добавляем зависимости из .csproj
        foreach (var reference in references)
        {
            var assembly = GAC.FindAssemblyForNamespace(reference);
            if (assembly != null)
            {
                foreach (var a in assembly)
                {
                    metadataReferences.Add(MetadataReference.CreateFromFile(a.Location));
                }
            }
            else
            {
                Console.WriteLine($"Assembly for namespace '{reference}' not found.");
            }
        }
        var assemblies = GAC.FindAssemblyForNamespace("System.Diagnostics.Eventing.Reader");
        metadataReferences.AddRange(assemblies.Select(n => MetadataReference.CreateFromFile(n.Location)));

        var options = new CSharpCompilationOptions(
            OutputKind.ConsoleApplication,
            optimizationLevel: OptimizationLevel.Debug,
            allowUnsafe: true);

        var compilation = CSharpCompilation.Create(Path.GetRandomFileName(), options: options);

        compilation = compilation.AddSyntaxTrees(syntaxTrees.Select(t => t.Value));

        compilation = compilation.AddReferences(metadataReferences);

        string outputPath = Path.Combine(Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location), "Output.exe");

        using (var ms = new MemoryStream())
        {
            EmitResult result = compilation.Emit(ms);

            if (!result.Success)
            {
                IEnumerable<Diagnostic> failures = result.Diagnostics.Where(diagnostic =>
                    diagnostic.IsWarningAsError ||
                    diagnostic.Severity == DiagnosticSeverity.Error);

                Console.WriteLine($"Compilation failed occurs {failures.Count()} errors");

                foreach (Diagnostic diagnostic in failures)
                {
                    Console.Error.WriteLine("{0}: {1}, {2}", diagnostic.Id, diagnostic.GetMessage(), diagnostic.Location);
                }
            }
            else
            {
                Assembly.Load(ms.ToArray()).EntryPoint.Invoke(null, new object [] { new string [] { "arg1", "arg2", "etc" } });
                return;
            }
        }
    }
}
