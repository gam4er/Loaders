using System;
using System.Collections.Generic;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Emit;
using Loaders.Obfuscation.Utilities;

namespace Loaders.Obfuscation.Services
{
    internal sealed class ProjectStringObfuscationRequest
    {
        public ProjectStringObfuscationRequest(
            ProjectFileInfo projectInfo,
            IObfuscatedNameProvider nameProvider,
            StringObfuscationStrategy? strategy)
        {
            ProjectInfo = projectInfo ?? throw new ArgumentNullException(nameof(projectInfo));
            NameProvider = nameProvider ?? throw new ArgumentNullException(nameof(nameProvider));
            Strategy = strategy;
        }

        public ProjectFileInfo ProjectInfo { get; }
        public IObfuscatedNameProvider NameProvider { get; }
        public StringObfuscationStrategy? Strategy { get; }
    }

    internal sealed class ProjectStringObfuscationResult
    {
        public ProjectStringObfuscationResult(
            IReadOnlyList<RewrittenSourceFile> sources,
            SyntaxTree loaderSyntaxTree,
            string loaderSourcePath,
            string resourcePath,
            string manifestResourceName,
            byte[] resourceBytes,
            StringObfuscationStatistics statistics,
            IReadOnlyList<StringObfuscationDiagnostic> diagnostics,
            IReadOnlyList<StringRuntimeDependency> dependencies,
            IReadOnlyCollection<byte> usedCodecIds)
        {
            Sources = sources ?? Array.Empty<RewrittenSourceFile>();
            LoaderSyntaxTree = loaderSyntaxTree;
            LoaderSourcePath = loaderSourcePath ?? string.Empty;
            ResourcePath = resourcePath ?? string.Empty;
            ManifestResourceName = manifestResourceName ?? string.Empty;
            ResourceBytes = resourceBytes ?? Array.Empty<byte>();
            Statistics = statistics ?? new StringObfuscationStatistics();
            Diagnostics = diagnostics ?? Array.Empty<StringObfuscationDiagnostic>();
            Dependencies = dependencies ?? Array.Empty<StringRuntimeDependency>();
            UsedCodecIds = usedCodecIds ?? Array.Empty<byte>();
        }

        public IReadOnlyList<RewrittenSourceFile> Sources { get; }
        public SyntaxTree LoaderSyntaxTree { get; }
        public string LoaderSourcePath { get; }
        public string ResourcePath { get; }
        public string ManifestResourceName { get; }
        public byte[] ResourceBytes { get; }
        public StringObfuscationStatistics Statistics { get; }
        public IReadOnlyList<StringObfuscationDiagnostic> Diagnostics { get; }
        public IReadOnlyList<StringRuntimeDependency> Dependencies { get; }
        public IReadOnlyCollection<byte> UsedCodecIds { get; }
        public bool HasResource => ResourceBytes.Length > 0 && !string.IsNullOrWhiteSpace(ManifestResourceName);

        public ResourceDescription CreateRoslynResource()
        {
            if (!HasResource)
            {
                return null;
            }

            var bytes = ResourceBytes;
            return new ResourceDescription(
                ManifestResourceName,
                () => new System.IO.MemoryStream(bytes, writable: false),
                isPublic: false);
        }
    }

    internal sealed class RewrittenSourceFile
    {
        public RewrittenSourceFile(string path, int replacedCount, int skippedCount)
        {
            Path = path ?? string.Empty;
            ReplacedCount = replacedCount;
            SkippedCount = skippedCount;
        }

        public string Path { get; }
        public int ReplacedCount { get; }
        public int SkippedCount { get; }
    }

    internal sealed class StringObfuscationStatistics
    {
        private readonly Dictionary<string, int> _skippedByReason = new Dictionary<string, int>(StringComparer.Ordinal);
        private readonly Dictionary<string, int> _codecCounts = new Dictionary<string, int>(StringComparer.Ordinal);

        public int LiteralOccurrences { get; set; }
        public int RewrittenOccurrences { get; set; }
        public int UniqueStrings { get; set; }
        public int DeduplicatedOccurrences { get; set; }
        public int GeneratedSourceBytes { get; set; }
        public int ResourceBytes { get; set; }
        public long ObfuscationMilliseconds { get; set; }

        public IReadOnlyDictionary<string, int> SkippedByReason => _skippedByReason;
        public IReadOnlyDictionary<string, int> CodecCounts => _codecCounts;

        public int SkippedOccurrences
        {
            get
            {
                var total = 0;
                foreach (var value in _skippedByReason.Values)
                {
                    total += value;
                }

                return total;
            }
        }

        public void AddSkipped(string reason)
        {
            if (string.IsNullOrWhiteSpace(reason))
            {
                reason = "unknown";
            }

            _skippedByReason.TryGetValue(reason, out var count);
            _skippedByReason[reason] = count + 1;
        }

        public void AddCodec(string codecName)
        {
            if (string.IsNullOrWhiteSpace(codecName))
            {
                codecName = "unknown";
            }

            _codecCounts.TryGetValue(codecName, out var count);
            _codecCounts[codecName] = count + 1;
        }
    }

    internal sealed class StringObfuscationDiagnostic
    {
        public StringObfuscationDiagnostic(string path, string reason, string message)
        {
            Path = path ?? string.Empty;
            Reason = reason ?? string.Empty;
            Message = message ?? string.Empty;
        }

        public string Path { get; }
        public string Reason { get; }
        public string Message { get; }
    }
}
