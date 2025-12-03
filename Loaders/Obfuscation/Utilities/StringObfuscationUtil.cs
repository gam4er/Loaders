using System;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Loaders.Obfuscation.Utilities
{
    internal static class StringObfuscationUtil
    {
        public static ExpressionSyntax BuildDecodeExpression(string plainText)
        {
            var key = GenerateRandomKey(8);
            var xorEncryptedText = XorEncrypt(plainText, key);
            var base64Key = Convert.ToBase64String(key);

            var code =
                $"Encoding.UTF8.GetString(Convert.FromBase64String(\"{xorEncryptedText}\").Select((value, index) => (byte)(value ^ Convert.FromBase64String(\"{base64Key}\")[index % {key.Length}])).ToArray())";

            return SyntaxFactory.ParseExpression(code);
        }

        private static byte[] GenerateRandomKey(int length)
        {
            var key = new byte[length];
            using var rng = new RNGCryptoServiceProvider();
            rng.GetBytes(key);
            return key;
        }

        private static string XorEncrypt(string text, byte[] key)
        {
            byte[] textBytes = Encoding.UTF8.GetBytes(text);
            byte[] encryptedBytes = new byte[textBytes.Length];

            for (int i = 0; i < textBytes.Length; i++)
            {
                encryptedBytes[i] = (byte)(textBytes[i] ^ key[i % key.Length]);
            }

            return Convert.ToBase64String(encryptedBytes);
        }
    }
}
