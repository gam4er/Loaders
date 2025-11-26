using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Formatting;

namespace Loaders.Obfuscation.Services
{
    /// <summary>
    /// Generates synthetic classes with overloaded methods to increase analysis noise.
    /// </summary>
    internal static class NoiseClassAppender
    {
        private static readonly IReadOnlyList<string> SeedNames = new[]
        {
            "AspNetCoreRuntime",
            "RoslynWorkspace",
            "UnityFramework",
            "BlazorHost",
            "PowerShellCore",
            "OrleansCluster",
            "SignalRHub",
            "EFCoreProvider",
            "XamarinHarness",
            "AzureClient",
        };

        public static async Task AppendAsync(IReadOnlyList<string> csFiles, int classCount = 3)
        {
            if (csFiles is null || csFiles.Count == 0)
            {
                throw new ArgumentException("No C# files supplied for noise injection.", nameof(csFiles));
            }

            string targetFile = csFiles.FirstOrDefault(file => !file.Contains("AssemblyInfo.cs", StringComparison.OrdinalIgnoreCase))
                                 ?? csFiles.First();

            var sourceCode = await File.ReadAllTextAsync(targetFile).ConfigureAwait(false);
            var syntaxTree = CSharpSyntaxTree.ParseText(sourceCode);
            var root = (CompilationUnitSyntax)syntaxTree.GetRoot();

            var namespaceDeclaration = root.Members.OfType<NamespaceDeclarationSyntax>().FirstOrDefault();
            var noiseClasses = Enumerable.Range(0, classCount).Select(CreateNoiseClass).ToArray();

            CompilationUnitSyntax updatedRoot;
            if (namespaceDeclaration != null)
            {
                var updatedNamespace = namespaceDeclaration.AddMembers(noiseClasses);
                updatedRoot = root.ReplaceNode(namespaceDeclaration, updatedNamespace);
            }
            else
            {
                updatedRoot = root.AddMembers(noiseClasses);
            }

            var formattedRoot = Formatter.Format(updatedRoot, new AdhocWorkspace());
            await File.WriteAllTextAsync(targetFile, formattedRoot.ToFullString()).ConfigureAwait(false);
        }

        private static ClassDeclarationSyntax CreateNoiseClass(int index)
        {
            string baseName = SeedNames[index % SeedNames.Count];
            string className = $"{baseName}{Guid.NewGuid().ToString("N")[..6]}";

            var methods = new MemberDeclarationSyntax[]
            {
                CreateOverloadedMethod("Drift", "int", "int depth", new[]
                {
                    "var checksum = depth ^ unchecked((int)System.DateTime.UtcNow.Ticks);",
                    "for (int i = 0; i < 4; i++) { checksum = unchecked((checksum << 1) + (i * 17)); }",
                    "return checksum;",
                }),
                CreateOverloadedMethod("Drift", "string", "string payload, int entropy", new[]
                {
                    "var shadow = payload + entropy.ToString();",
                    "for (int i = 0; i < 3; i++) { shadow = string.Concat(shadow, entropy.ToString("X")); }",
                    "return shadow;",
                }),
                CreateOverloadedMethod("Ping", "bool", "double scale", new[]
                {
                    "var marker = System.BitConverter.DoubleToInt64Bits(scale) ^ unchecked((long)System.DateTime.Now.Ticks);",
                    "if ((marker & 1) == 0) { marker ^= 0x5F5E0FF; } else { marker ^= 0xABCDEF; }",
                    "return marker % 3 == 0;",
                }),
            };

            return SyntaxFactory.ClassDeclaration(className)
                .AddModifiers(SyntaxFactory.Token(SyntaxKind.InternalKeyword))
                .AddMembers(methods)
                .NormalizeWhitespace();
        }

        private static MethodDeclarationSyntax CreateOverloadedMethod(string methodName, string returnType, string parameters, IEnumerable<string> statements)
        {
            var parameterList = SyntaxFactory.ParseParameterList($"({parameters})");
            var bodyStatements = statements
                .Select(statement => SyntaxFactory.ParseStatement(statement))
                .ToArray();

            return SyntaxFactory.MethodDeclaration(SyntaxFactory.ParseTypeName(returnType), methodName)
                .AddModifiers(SyntaxFactory.Token(SyntaxKind.PublicKeyword))
                .WithParameterList(parameterList)
                .WithBody(SyntaxFactory.Block(bodyStatements))
                .NormalizeWhitespace();
        }
    }
}
