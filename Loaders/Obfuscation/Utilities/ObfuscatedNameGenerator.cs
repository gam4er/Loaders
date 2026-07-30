using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using Microsoft.CodeAnalysis.CSharp;

namespace Loaders.Obfuscation.Utilities
{
    /// <summary>
    /// Factory for obfuscated identifier providers.
    /// </summary>
    internal static class ObfuscatedNameGenerator
    {
        private static readonly HashObfuscatedNameProvider DefaultProvider = new HashObfuscatedNameProvider();

        public static IObfuscatedNameProvider CreateProvider(bool useBeLeo)
        {
            return useBeLeo
                ? (IObfuscatedNameProvider)new BeLeoNameProvider()
                : DefaultProvider;
        }

        public static string Generate(string originalName)
        {
            return DefaultProvider.Generate(originalName);
        }
    }

    internal sealed class HashObfuscatedNameProvider : IObfuscatedNameProvider
    {
        private const string Prefix = "Microsoft";

        public string Generate(string originalName)
        {
            if (string.IsNullOrWhiteSpace(originalName))
            {
                throw new ArgumentException("Original name must be provided.", nameof(originalName));
            }

            using var sha256 = SHA256.Create();
            byte[] hashBytes = sha256.ComputeHash(Encoding.UTF8.GetBytes(originalName));
            string shortHash = BitConverter.ToString(hashBytes).Replace("-", string.Empty).Substring(3, 13);
            return Prefix + shortHash;
        }
    }

    internal sealed class BeLeoNameProvider : IObfuscatedNameProvider
    {
        private const string ResourceName = "Loaders.Resources.WarAndPeace.txt";
        private const int RequiredWordCount = 3;
        private const int MaxAttempts = 100000;

        private static readonly Regex SplitRegex = new Regex(
            @"[^\p{L}\p{Nd}]+",
            RegexOptions.Compiled | RegexOptions.CultureInvariant);

        private readonly Random _random = new Random();
        private readonly List<string> _lines;
        private readonly HashSet<string> _usedIdentifiers = new HashSet<string>(StringComparer.Ordinal);

        public BeLeoNameProvider()
        {
            _lines = LoadLines();
            if (_lines.Count == 0)
            {
                throw new InvalidOperationException("Embedded War and Peace resource is empty.");
            }
        }

        public string Generate(string originalName)
        {
            for (var attempt = 0; attempt < MaxAttempts && _lines.Count > 0; attempt++)
            {
                var lineIndex = _random.Next(_lines.Count);
                var words = SplitWords(_lines[lineIndex]).ToList();
                if (words.Count < RequiredWordCount)
                {
                    _lines.RemoveAt(lineIndex);
                    continue;
                }

                var candidate = ToPascalCase(words.OrderBy(_ => _random.Next()).Take(RequiredWordCount));
                if (IsValidIdentifier(candidate) && _usedIdentifiers.Add(candidate))
                {
                    return candidate;
                }
            }

            throw new InvalidOperationException(
                "Could not generate a unique BeLeo identifier from the embedded War and Peace text.");
        }

        private static List<string> LoadLines()
        {
            var assembly = Assembly.GetExecutingAssembly();
            using var stream = assembly.GetManifestResourceStream(ResourceName);
            if (stream == null)
            {
                throw new InvalidOperationException(
                    $"Embedded resource '{ResourceName}' was not found. Check the project EmbeddedResource item.");
            }

            var lines = new List<string>();
            using var reader = new StreamReader(stream, Encoding.UTF8, detectEncodingFromByteOrderMarks: true);
            while (!reader.EndOfStream)
            {
                var line = reader.ReadLine();
                if (!string.IsNullOrWhiteSpace(line))
                {
                    lines.Add(line);
                }
            }

            return lines;
        }

        private static IEnumerable<string> SplitWords(string line)
        {
            return SplitRegex
                .Split(line)
                .Where(word => word.Length > 3)
                .Where(word => char.IsLetter(word[0]));
        }

        private static string ToPascalCase(IEnumerable<string> words)
        {
            var builder = new StringBuilder();
            foreach (var word in words)
            {
                if (word.Length == 0)
                {
                    continue;
                }

                builder.Append(char.ToUpperInvariant(word[0]));
                if (word.Length > 1)
                {
                    builder.Append(word.Substring(1));
                }
            }

            return builder.ToString();
        }

        private static bool IsValidIdentifier(string identifier)
        {
            return SyntaxFacts.IsValidIdentifier(identifier) &&
                   SyntaxFacts.GetKeywordKind(identifier) == Microsoft.CodeAnalysis.CSharp.SyntaxKind.None &&
                   SyntaxFacts.GetContextualKeywordKind(identifier) == Microsoft.CodeAnalysis.CSharp.SyntaxKind.None;
        }
    }
}
