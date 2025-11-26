using System;
using System.Security.Cryptography;
using System.Text;

namespace Loaders.Obfuscation.Utilities
{
    internal static class ObfuscatedNameGenerator
    {
        private const string Prefix = "O_";

        public static string Generate(string originalName)
        {
            if (string.IsNullOrWhiteSpace(originalName))
            {
                throw new ArgumentException("Original name must be provided.", nameof(originalName));
            }

            using var sha256 = SHA256.Create();
            byte[] hashBytes = sha256.ComputeHash(Encoding.UTF8.GetBytes(originalName));
            string shortHash = BitConverter.ToString(hashBytes).Replace("-", string.Empty).Substring(0, 8);
            return Prefix + shortHash;
        }
    }
}
