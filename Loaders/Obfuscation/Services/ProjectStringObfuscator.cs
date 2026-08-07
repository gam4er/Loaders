using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Formatting;
using System.Xml.Linq;
using Loaders.Obfuscation.Rewriters;
using Loaders.Obfuscation.Utilities;

namespace Loaders.Obfuscation.Services
{
    internal static class ProjectStringObfuscator
    {
        public static ProjectStringObfuscationResult ObfuscateProject(ProjectStringObfuscationRequest request)
        {
            if (request == null)
            {
                throw new ArgumentNullException(nameof(request));
            }

            var stopwatch = Stopwatch.StartNew();
            var projectInfo = request.ProjectInfo;
            var statistics = new StringObfuscationStatistics();
            var diagnostics = new List<StringObfuscationDiagnostic>();
            var rewrittenSources = new List<RewrittenSourceFile>();
            var codecs = StringPayloadCodecCatalog.CreateDefault();

            using (var random = new CryptoStringObfuscationRandom())
            {
                var context = new StringResourceBuildContext(
                    codecs,
                    request.Strategy,
                    random);

                var parseOptions = projectInfo.CreateParseOptions();
                var syntaxTrees = projectInfo.CsFiles.ToDictionary(
                    file => file,
                    file => CSharpSyntaxTree.ParseText(File.ReadAllText(file), parseOptions, file));

                var compilation = CSharpCompilation
                    .Create(projectInfo.AssemblyName, options: projectInfo.CreateCompilationOptions())
                    .AddReferences(ProjectReferenceResolver.BuildMetadataReferences(projectInfo))
                    .AddSyntaxTrees(syntaxTrees.Values);

                using (var workspace = new AdhocWorkspace())
                {
                    foreach (var filePath in projectInfo.TransformableCsFiles)
                    {
                        if (!syntaxTrees.TryGetValue(filePath, out var syntaxTree))
                        {
                            continue;
                        }

                        var semanticModel = compilation.GetSemanticModel(syntaxTree);
                        var rewriter = new ProjectStringLiteralRewriter(
                            context,
                            semanticModel,
                            filePath,
                            statistics,
                            diagnostics);

                        var root = syntaxTree.GetRoot();
                        var rewrittenRoot = rewriter.Visit(root);
                        var commentFreeRoot = new CommentRemover().Visit(rewrittenRoot);
                        var formattedRoot = Formatter.Format(commentFreeRoot, workspace);

                        File.WriteAllText(filePath, formattedRoot.ToFullString());
                        rewrittenSources.Add(new RewrittenSourceFile(
                            filePath,
                            rewriter.ReplacedCount,
                            rewriter.SkippedCount));
                    }
                }

                var artifact = context.FinalizeArtifact(projectInfo.ProjectDirectory);
                stopwatch.Stop();
                statistics.ObfuscationMilliseconds = stopwatch.ElapsedMilliseconds;

                if (artifact == null)
                {
                    return new ProjectStringObfuscationResult(
                        rewrittenSources,
                        null,
                        string.Empty,
                        string.Empty,
                        string.Empty,
                        Array.Empty<byte>(),
                        statistics,
                        diagnostics,
                        Array.Empty<StringRuntimeDependency>(),
                        Array.Empty<byte>());
                }

                File.WriteAllText(artifact.LoaderSourcePath, artifact.LoaderSource, Encoding.UTF8);
                File.WriteAllBytes(artifact.ResourcePath, artifact.ResourceBytes);
                StringResourceProjectPatcher.PatchProject(
                    projectInfo.ProjectPath,
                    artifact.LoaderSourcePath,
                    artifact.ResourcePath,
                    artifact.ManifestResourceName,
                    artifact.Dependencies);

                statistics.GeneratedSourceBytes = Encoding.UTF8.GetByteCount(artifact.LoaderSource);
                statistics.ResourceBytes = artifact.ResourceBytes.Length;

                return new ProjectStringObfuscationResult(
                    rewrittenSources,
                    CSharpSyntaxTree.ParseText(artifact.LoaderSource, parseOptions, artifact.LoaderSourcePath),
                    artifact.LoaderSourcePath,
                    artifact.ResourcePath,
                    artifact.ManifestResourceName,
                    artifact.ResourceBytes,
                    statistics,
                    diagnostics,
                    artifact.Dependencies,
                    artifact.UsedCodecIds);
            }
        }

        public static byte[] GetUtf16CodeUnitBytes(string value)
        {
            if (value == null)
            {
                throw new ArgumentNullException(nameof(value));
            }

            var bytes = new byte[value.Length * 2];
            for (var i = 0; i < value.Length; i++)
            {
                var codeUnit = value[i];
                bytes[i * 2] = (byte)codeUnit;
                bytes[i * 2 + 1] = (byte)(codeUnit >> 8);
            }

            return bytes;
        }

        private sealed class ProjectStringLiteralRewriter : CSharpSyntaxRewriter
        {
            private readonly StringResourceBuildContext _context;
            private readonly SemanticModel _semanticModel;
            private readonly string _filePath;
            private readonly StringObfuscationStatistics _statistics;
            private readonly IList<StringObfuscationDiagnostic> _diagnostics;

            public ProjectStringLiteralRewriter(
                StringResourceBuildContext context,
                SemanticModel semanticModel,
                string filePath,
                StringObfuscationStatistics statistics,
                IList<StringObfuscationDiagnostic> diagnostics)
            {
                _context = context ?? throw new ArgumentNullException(nameof(context));
                _semanticModel = semanticModel;
                _filePath = filePath ?? string.Empty;
                _statistics = statistics ?? throw new ArgumentNullException(nameof(statistics));
                _diagnostics = diagnostics ?? throw new ArgumentNullException(nameof(diagnostics));
            }

            public int ReplacedCount { get; private set; }
            public int SkippedCount { get; private set; }

            public override SyntaxNode VisitLiteralExpression(LiteralExpressionSyntax node)
            {
                if (!node.IsKind(SyntaxKind.StringLiteralExpression))
                {
                    return base.VisitLiteralExpression(node);
                }

                _statistics.LiteralOccurrences++;
                var skipReason = StringLiteralEligibility.GetSkipReason(node, _semanticModel);
                if (skipReason != null)
                {
                    Skip(skipReason);
                    return base.VisitLiteralExpression(node);
                }

                var lookup = _context.RegisterAndBuildLookup(node.Token.ValueText, _statistics);
                ReplacedCount++;
                _statistics.RewrittenOccurrences++;
                return lookup.WithTriviaFrom(node);
            }

            public override SyntaxNode VisitInterpolatedStringExpression(InterpolatedStringExpressionSyntax node)
            {
                _statistics.LiteralOccurrences++;
                var skipReason = StringLiteralEligibility.GetSkipReason(node, _semanticModel);
                if (skipReason != null)
                {
                    Skip(skipReason);
                    return node;
                }

                if (!TryBuildCompositeFormat(node, out var formatString, out var interpolationArguments))
                {
                    Skip("unsupported-interpolation-format");
                    return node;
                }

                var formatExpression = _context.RegisterAndBuildLookup(formatString, _statistics);
                ReplacedCount++;
                _statistics.RewrittenOccurrences++;

                if (interpolationArguments.Count == 0)
                {
                    return formatExpression.WithTriviaFrom(node);
                }

                var arguments = new List<ArgumentSyntax>
                {
                    SyntaxFactory.Argument(SyntaxFactory.ParseExpression("System.Globalization.CultureInfo.CurrentCulture")),
                    SyntaxFactory.Argument(formatExpression)
                };

                foreach (var argument in interpolationArguments)
                {
                    arguments.Add(argument);
                }

                var formatCall = SyntaxFactory.InvocationExpression(
                    SyntaxFactory.MemberAccessExpression(
                        SyntaxKind.SimpleMemberAccessExpression,
                        SyntaxFactory.ParseName("System.String"),
                        SyntaxFactory.IdentifierName("Format")))
                    .WithArgumentList(SyntaxFactory.ArgumentList(SyntaxFactory.SeparatedList(arguments)));

                return formatCall.WithTriviaFrom(node);
            }

            private void Skip(string reason)
            {
                SkippedCount++;
                _statistics.AddSkipped(reason);
                _diagnostics.Add(new StringObfuscationDiagnostic(
                    _filePath,
                    reason,
                    "String literal skipped by project-wide string obfuscation policy."));
            }

            private bool TryBuildCompositeFormat(
                InterpolatedStringExpressionSyntax node,
                out string formatString,
                out List<ArgumentSyntax> interpolationArguments)
            {
                var formatBuilder = new StringBuilder();
                interpolationArguments = new List<ArgumentSyntax>();

                foreach (var content in node.Contents)
                {
                    if (content is InterpolatedStringTextSyntax text)
                    {
                        formatBuilder.Append(text.TextToken.ValueText);
                        continue;
                    }

                    if (content is InterpolationSyntax interpolation)
                    {
                        if (!IsSafeInterpolationFormat(interpolation))
                        {
                            formatString = string.Empty;
                            return false;
                        }

                        var argumentIndex = interpolationArguments.Count;
                        formatBuilder.Append(BuildFormatItem(argumentIndex, interpolation));
                        interpolationArguments.Add(SyntaxFactory.Argument((ExpressionSyntax)Visit(interpolation.Expression)));
                    }
                }

                formatString = formatBuilder.ToString();
                return true;
            }

            private static bool IsSafeInterpolationFormat(InterpolationSyntax interpolation)
            {
                var formatClause = interpolation.FormatClause?.ToString();
                return string.IsNullOrEmpty(formatClause) ||
                       (formatClause.IndexOf('{') < 0 && formatClause.IndexOf('}') < 0);
            }

            private static string BuildFormatItem(int argumentIndex, InterpolationSyntax interpolation)
            {
                var builder = new StringBuilder();
                builder.Append('{');
                builder.Append(argumentIndex.ToString(CultureInfo.InvariantCulture));

                if (interpolation.AlignmentClause != null)
                {
                    builder.Append(interpolation.AlignmentClause.ToString());
                }

                if (interpolation.FormatClause != null)
                {
                    builder.Append(interpolation.FormatClause.ToString());
                }

                builder.Append('}');
                return builder.ToString();
            }
        }
    }

    internal sealed class StringResourceBuildContext
    {
        private readonly IReadOnlyList<IStringPayloadCodec> _codecs;
        private readonly StringObfuscationStrategy? _strategy;
        private readonly IStringObfuscationRandom _random;
        private readonly Dictionary<string, Registration> _registrations =
            new Dictionary<string, Registration>(StringComparer.Ordinal);
        private readonly HashSet<int> _tokens = new HashSet<int>();
        private bool _finalized;

        public StringResourceBuildContext(
            IReadOnlyList<IStringPayloadCodec> codecs,
            StringObfuscationStrategy? strategy,
            IStringObfuscationRandom random)
        {
            _codecs = codecs ?? throw new ArgumentNullException(nameof(codecs));
            _strategy = strategy;
            _random = random ?? throw new ArgumentNullException(nameof(random));
            GeneratedNamespace = "N" + RandomHex(8);
            GeneratedTypeName = "T" + RandomHex(8);
            ManifestResourceName = "__m." + RandomHex(16);
        }

        public string GeneratedNamespace { get; }
        public string GeneratedTypeName { get; }
        public string ManifestResourceName { get; }

        public ExpressionSyntax RegisterAndBuildLookup(string value, StringObfuscationStatistics statistics)
        {
            if (_finalized)
            {
                throw new InvalidOperationException("String resource build context has already been finalized.");
            }

            if (!_registrations.TryGetValue(value, out var registration))
            {
                var utf16Bytes = ProjectStringObfuscator.GetUtf16CodeUnitBytes(value);
                var candidates = StringPayloadCodecCatalog.SelectCandidates(_codecs, value, utf16Bytes, _strategy);
                if (candidates.Count == 0)
                {
                    throw new InvalidOperationException("No string payload codec can encode the literal.");
                }

                var codec = candidates[_random.NextInt(0, candidates.Count)];
                var payload = codec.Encode(value, utf16Bytes, _random);
                registration = new Registration(
                    value,
                    AllocateToken(),
                    _registrations.Count,
                    payload,
                    utf16Bytes,
                    codec.Dependencies);
                _registrations.Add(value, registration);
                statistics.UniqueStrings = _registrations.Count;
                statistics.AddCodec(codec.Name);
            }
            else
            {
                statistics.DeduplicatedOccurrences++;
            }

            return SyntaxFactory.ParseExpression(
                "global::" + GeneratedNamespace + "." + GeneratedTypeName + ".Get(unchecked((int)0x" +
                unchecked((uint)registration.Token).ToString("X8", CultureInfo.InvariantCulture) + "))");
        }

        public StringResourceArtifact FinalizeArtifact(string projectDirectory)
        {
            _finalized = true;
            if (_registrations.Count == 0)
            {
                return null;
            }

            var generatedDirectory = Path.Combine(projectDirectory, GeneratedFileClassifier.GeneratedDirectoryName);
            Directory.CreateDirectory(generatedDirectory);

            var fileId = RandomHex(8);
            var loaderPath = Path.Combine(generatedDirectory, "StringStore." + fileId + ".g.cs");
            var resourcePath = Path.Combine(generatedDirectory, "StringStore." + fileId + ".bin");

            var entries = _registrations.Values
                .Select(registration => new StringResourceEntry(
                    registration.Token,
                    registration.CacheIndex,
                    registration.Payload,
                    registration.Utf16Bytes))
                .ToArray();

            var resourceBytes = StringResourceSerializer.Serialize(entries, _random);
            var usedCodecIds = entries.Select(entry => entry.Payload.CodecId).Distinct().OrderBy(id => id).ToArray();
            var loaderSource = StringResourceLoaderGenerator.Generate(
                GeneratedNamespace,
                GeneratedTypeName,
                ManifestResourceName,
                usedCodecIds);

            var dependencies = _registrations.Values
                .SelectMany(registration => registration.Dependencies)
                .GroupBy(dependency => dependency.AssemblyReference + "|" + dependency.PackageId + "|" + dependency.PackageVersion, StringComparer.OrdinalIgnoreCase)
                .Select(group => group.First())
                .ToArray();

            return new StringResourceArtifact(
                loaderPath,
                loaderSource,
                resourcePath,
                ManifestResourceName,
                resourceBytes,
                dependencies,
                usedCodecIds);
        }

        private int AllocateToken()
        {
            int token;
            do
            {
                token = unchecked((int)_random.NextUInt32());
            }
            while (token == 0 || !_tokens.Add(token));

            return token;
        }

        private string RandomHex(int byteCount)
        {
            return BitConverter.ToString(_random.Bytes(byteCount)).Replace("-", string.Empty);
        }

        private sealed class Registration
        {
            public Registration(
                string value,
                int token,
                int cacheIndex,
                StringPayload payload,
                byte[] utf16Bytes,
                IReadOnlyList<StringRuntimeDependency> dependencies)
            {
                Value = value;
                Token = token;
                CacheIndex = cacheIndex;
                Payload = payload;
                Utf16Bytes = utf16Bytes;
                Dependencies = dependencies ?? Array.Empty<StringRuntimeDependency>();
            }

            public string Value { get; }
            public int Token { get; }
            public int CacheIndex { get; }
            public StringPayload Payload { get; }
            public byte[] Utf16Bytes { get; }
            public IReadOnlyList<StringRuntimeDependency> Dependencies { get; }
        }
    }

    internal sealed class StringResourceArtifact
    {
        public StringResourceArtifact(
            string loaderSourcePath,
            string loaderSource,
            string resourcePath,
            string manifestResourceName,
            byte[] resourceBytes,
            IReadOnlyList<StringRuntimeDependency> dependencies,
            IReadOnlyCollection<byte> usedCodecIds)
        {
            LoaderSourcePath = loaderSourcePath ?? string.Empty;
            LoaderSource = loaderSource ?? string.Empty;
            ResourcePath = resourcePath ?? string.Empty;
            ManifestResourceName = manifestResourceName ?? string.Empty;
            ResourceBytes = resourceBytes ?? Array.Empty<byte>();
            Dependencies = dependencies ?? Array.Empty<StringRuntimeDependency>();
            UsedCodecIds = usedCodecIds ?? Array.Empty<byte>();
        }

        public string LoaderSourcePath { get; }
        public string LoaderSource { get; }
        public string ResourcePath { get; }
        public string ManifestResourceName { get; }
        public byte[] ResourceBytes { get; }
        public IReadOnlyList<StringRuntimeDependency> Dependencies { get; }
        public IReadOnlyCollection<byte> UsedCodecIds { get; }
    }
}
