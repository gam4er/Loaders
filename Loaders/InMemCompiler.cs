using System;
using System.Collections.Generic;
using System.Diagnostics;
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
using Microsoft.VisualStudio.PlatformUI;

class InMemCompiler
{
    static void Main()
    {
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

        
        //Dictionary<string, string> methodMap = new Dictionary<string, string>();
        //Dictionary<string, SyntaxTree> syntaxTrees = new Dictionary<string, SyntaxTree>();

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
        classMap.Remove("CommandBase");
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

        /*
        Dictionary<string, string> all = classMap;

        var rem = classMap.Keys.ToList().Where(k => k.Contains("DTO")).ToList();
        foreach (var r in rem)
            classMap.Remove(r);

        classMap.Remove("SeatbeltOptions");
        classMap.Remove("Advapi32");        
        classMap.Remove("ArpEntry");
        classMap.Remove("AsrRule");
        classMap.Remove("AsrSettings");
        classMap.Remove("AuditEntry");
        classMap.Remove("AuditPolicyGPO");
        classMap.Remove("Bookmark");
        classMap.Remove("CredentialFileInfo");
        classMap.Remove("DirectoryQuery");
        classMap.Remove("Download");
        classMap.Remove("ExplorerRunCommand");
        classMap.Remove("FileZillaConfig");
        classMap.Remove("InternetSettingsKey");
        classMap.Remove("Iphlpapi");
        classMap.Remove("MasterKey");
        classMap.Remove("McAfeeSite");
        classMap.Remove("Module");
        classMap.Remove("MTPuTTYConfig");
        classMap.Remove("NetAadJoinInfo");
        classMap.Remove("OneDriveSyncProvider");
        classMap.Remove("OutlookDownload");
        classMap.Remove("PluginAccess");
        classMap.Remove("RDPClientSettings");
        classMap.Remove("RDPConnection");
        classMap.Remove("RDPServerSettings");
        classMap.Remove("RegistryKeyValue");
        classMap.Remove("RegistryUtil");
        classMap.Remove("Rpcrt4");
        classMap.Remove("SafeRpcBindingHandle");
        classMap.Remove("SafeRpcInquiryHandle");
        classMap.Remove("SafeRpcStringHandle");
        classMap.Remove("ScheduledTaskAction");
        classMap.Remove("ScheduledTaskPrincipal");
        classMap.Remove("ScheduledTaskTrigger");
        classMap.Remove("SuperPuttyConfig");
        classMap.Remove("TextOutputSink");
        classMap.Remove("TypedUrl");
        classMap.Remove("VaultEntry");
        classMap.Remove("VaultItemValue");
        classMap.Remove("WifiProfileEntry");
        classMap.Remove("Win32Error");
        classMap.Remove("WindowsDefenderSettings");
        classMap.Remove("WindowsFirewallProfileSettings");
        classMap.Remove("WindowsFirewallRule");
        classMap.Remove("Workspace");
        */

        //var projectPath = @"D:\Documents\GitHub\Seatbelt\Seatbelt.sln";
        //ClassRenamer.RenameClassesInFiles(classMap, syntaxTrees.Keys.ToList());

        //ClassRenamer.RenameClassesInFiles(all, syntaxTrees.Keys.ToList());



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
