using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading;
using System.Xml.Linq;

using Loaders;

using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Emit;
using Microsoft.CodeAnalysis.Formatting;
using Microsoft.VisualStudio.PlatformUI;

class InMemCompiler
{
    static void Main()
    {
        //Console.WriteLine( RandomMethodInvoker.RandomMethod());

        string sourceFolder = "d:\\Documents\\GitHub\\Seatbelt\\Seatbelt\\";
        //string sourceFolder = "D:\\Documents\\GitHub\\WhiskerOrig\\Whisker\\";

        XDocument csproj = XDocument.Load(sourceFolder + "Seatbelt.csproj");
        //XDocument csproj = XDocument.Load(sourceFolder + "Whisker.csproj");
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

        foreach (var csFile in csFiles)
        {
            string sourceCode = File.ReadAllText(csFile);
            SyntaxTree syntaxTree = CSharpSyntaxTree.ParseText(sourceCode);            
            
            if (csFile.Contains("AssemblyInfo.cs"))            
                continue;
                        
            syntaxTree = ObfuscateStringLiterals.AddUsingsNoComments(syntaxTree);
            syntaxTree = ObfuscateStringLiterals.Obfuscate(syntaxTree);  // Обфускация
            var obfuscatedCode = syntaxTree.GetRoot().ToFullString();
            File.WriteAllText(csFile, obfuscatedCode);
        }


        foreach (var csFile in csFiles)
        {
            string sourceCode = File.ReadAllText(csFile);

            SyntaxTree tree = CSharpSyntaxTree.ParseText(sourceCode);
            var root = tree.GetRoot();

            var rewriter = new MethodOverloadRewriter();
            var newRoot = rewriter.Visit(root);

            var formattedRoot = Formatter.Format(newRoot, new AdhocWorkspace());
            //Console.WriteLine(formattedRoot.ToFullString());
            File.WriteAllText(csFile, formattedRoot.ToFullString());

        }

        //return;

        Dictionary<string, string> classMap = new Dictionary<string, string>();
        ClassesEnum classCollector = new ClassesEnum();
        foreach (var csFile in csFiles) 
        {
            string sourceCode = File.ReadAllText(csFile);
            SyntaxTree syntaxTree = CSharpSyntaxTree.ParseText(sourceCode);
            classCollector.Visit(syntaxTree.GetRoot());
        }
        
        classMap = classCollector.GetClassMap().OrderBy(n => n.Key).ToDictionary(n => n.Key, n=> n.Value);

        classMap.Remove("Runtime");
        classMap.Remove("TextFormatterBase");
        classMap.Remove("CommandOutputTypeAttribute");
        classMap.Remove("CommandOutputType");
        //classMap.Remove("CommandBase");
        //classMap.Remove("CommandDTOBase");
        classMap.Remove("Advapi32");
        classMap.Remove("WindowsFirewallProfileSettings");
        classMap.Remove("Principal");
        classMap.Remove("WindowsDefenderSettings");
        classMap.Remove("AsrRule");
        classMap.Remove("AsrSettings");
        classMap.Remove("AuditEntry");
        classMap.Remove("Iphlpapi");
        classMap.Remove("MTPuTTYConfig");
        classMap.Remove("Kernel32");
        classMap.Remove("Ntdll");
        classMap.Remove("MTPuTTYConfig");
        classMap.Remove("RegistryUtil");
        classMap.Remove("ExtensionMethods");
        classMap.Remove("MiscUtil");
        classMap.Remove("Shell32");
        classMap.Remove("SecurityUtil");
        classMap.Remove("MiscUtil");
        classMap.Remove("NetAadJoinInfo");
        classMap.Remove("Secur32");
        classMap.Remove("FileUtil");

        //Dictionary<string, SyntaxTree> newST = new Dictionary<string, SyntaxTree>();
        //ClassObfuscatorAndNormalizer renamer = new ClassObfuscatorAndNormalizer(all.Where(n => !classMap.ContainsKey( n.Key)).ToDictionary(n => n.Key, n => n.Value));

        ClassObfuscatorAndNormalizer renamer = new ClassObfuscatorAndNormalizer(classMap);
        foreach (var csFile in csFiles)
        {
            string sourceCode = File.ReadAllText(csFile);
            SyntaxTree syntaxTree = CSharpSyntaxTree.ParseText(sourceCode);
            var newRoot = renamer.Visit(syntaxTree.GetRoot());            
            File.WriteAllText(csFile, newRoot.ToFullString());
        }
        
        ClassRenamer.RenameClassesInFiles(classMap, csFiles);

        foreach (var csFile in csFiles)
        {
            string sourceCode = File.ReadAllText(csFile);
            foreach (var t in classMap)
            {
                string existingStr = "(" + t.Key + ")";
                string newStr = "(" + t.Value + ")";
                sourceCode = sourceCode.
                    Replace(existingStr,newStr).
                    Replace("List<" + t.Key + ">","List<" + t.Value + ">").
                    Replace("new "+ t.Key + "(", "new " + t.Value + "(");

                //new ScheduledTaskTrigger();
                //new Bookmark(
                //new List<AntiVirusDTO>();
                //new SortedDictionary<uint, ArpTableDTO>();
                //internal class O_AD14D66A : CommandDTOBase
                //O_CA3CF3AC CurrentWifiProfileEntry = new WifiProfileEntry
                //var adapterIdToInterfaceMap = new SortedDictionary<uint, ArpTableDTO>();
                //var sections = IniFileHelper.ReadSections(classicFilePath);
            }
            try
            {
                File.WriteAllText(csFile, sourceCode);
            }
            catch (Exception)
            {
                Thread.Sleep(5000);
                File.WriteAllText(csFile, sourceCode);
            }
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
            optimizationLevel: OptimizationLevel.Release,
            allowUnsafe: false);

        var compilation = CSharpCompilation.Create(Path.GetRandomFileName(), options: options);

        Dictionary<string, SyntaxTree> syntaxTrees = new Dictionary<string, SyntaxTree>();

        foreach (var csFile in csFiles)
        {
            string sourceCode = File.ReadAllText(csFile);
            SyntaxTree syntaxTree = CSharpSyntaxTree.ParseText(sourceCode);
            syntaxTrees [csFile] = syntaxTree;
        }

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
                //ms.Seek(0, SeekOrigin.Begin);
                File.WriteAllBytes(outputPath,ms.ToArray());
                Assembly.Load(ms.ToArray()).EntryPoint.Invoke(null, new object [] { new string [] { "arg1", "arg2", "etc" } });
                return;
            }
        }
    }
}
