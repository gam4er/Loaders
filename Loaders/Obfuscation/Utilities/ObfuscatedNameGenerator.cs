using System;
using System.Security.Cryptography;
using System.Text;

namespace Loaders.Obfuscation.Utilities
{
    /// <summary>
    /// Generates deterministic obfuscated identifiers for class names.
    ///
    /// The current implementation uses a truncated SHA-256 hash with a
    /// fixed prefix ("O_") so that the same original name always maps to
    /// the same obfuscated identifier within a run.
    /// </summary>
    internal static class ObfuscatedNameGenerator
    {
        private const string Prefix = "Microsoft";

        public static string Generate(string originalName)
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
}
