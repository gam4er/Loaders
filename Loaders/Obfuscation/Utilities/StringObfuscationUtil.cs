using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Numerics;
using System.Security.Cryptography;
using System.Text;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Loaders.Obfuscation.Utilities
{
    internal enum StringObfuscationStrategy
    {
        XorBase64,
        LcgBase64,
        GZipBase64,
        GZipLcgBase64,
        HexReverseXor,
        DecimalDelta,
        Utf16DeltaArrays,
        ShuffledUtf16Triplets,
        InterleavedMaskPairs,
        AffineBase64,
        BytePermutation,
        UInt64Packing,
        GuidPacking,
        BigIntegerPacking,
        JunkedBase64
    }

    internal enum StringObfuscationSelectionMode
    {
        ProjectRandomDefault,
        Fixed,
        PerStringRandom
    }

    internal sealed class StringObfuscationSelection
    {
        private StringObfuscationSelection(
            StringObfuscationSelectionMode mode,
            StringObfuscationStrategy? strategy)
        {
            Mode = mode;
            Strategy = strategy;
        }

        public static StringObfuscationSelection ProjectRandomDefault { get; } =
            new StringObfuscationSelection(StringObfuscationSelectionMode.ProjectRandomDefault, null);

        public static StringObfuscationSelection PerStringRandom { get; } =
            new StringObfuscationSelection(StringObfuscationSelectionMode.PerStringRandom, null);

        public static StringObfuscationSelection Fixed(StringObfuscationStrategy strategy)
        {
            return new StringObfuscationSelection(StringObfuscationSelectionMode.Fixed, strategy);
        }

        public StringObfuscationSelectionMode Mode { get; }
        public StringObfuscationStrategy? Strategy { get; }
    }

    internal static class StringObfuscationUtil
    {
        private static readonly object RngLock = new object();
        private static readonly RandomNumberGenerator Rng = RandomNumberGenerator.Create();

        public static ExpressionSyntax BuildDecodeExpression(string plainText)
        {
            if (plainText == null)
            {
                throw new ArgumentNullException(nameof(plainText));
            }

            var byteLength = Encoding.UTF8.GetByteCount(plainText);
            var candidates = new List<StringObfuscationStrategy>
            {
                StringObfuscationStrategy.XorBase64,
                StringObfuscationStrategy.LcgBase64,
                StringObfuscationStrategy.AffineBase64,
                StringObfuscationStrategy.UInt64Packing
            };

            if (byteLength <= 256)
            {
                candidates.Add(StringObfuscationStrategy.HexReverseXor);
                candidates.Add(StringObfuscationStrategy.InterleavedMaskPairs);
                candidates.Add(StringObfuscationStrategy.GuidPacking);
            }

            if (byteLength <= 96)
            {
                candidates.Add(StringObfuscationStrategy.DecimalDelta);
                candidates.Add(StringObfuscationStrategy.Utf16DeltaArrays);
                candidates.Add(StringObfuscationStrategy.ShuffledUtf16Triplets);
                candidates.Add(StringObfuscationStrategy.BytePermutation);
                candidates.Add(StringObfuscationStrategy.JunkedBase64);
            }

            if (byteLength >= 48)
            {
                candidates.Add(StringObfuscationStrategy.GZipBase64);
                candidates.Add(StringObfuscationStrategy.GZipLcgBase64);
            }

            return BuildDecodeExpression(plainText, candidates[NextInt(0, candidates.Count)]);
        }

        public static ExpressionSyntax BuildDecodeExpression(string plainText, StringObfuscationStrategy strategy)
        {
            if (plainText == null)
            {
                throw new ArgumentNullException(nameof(plainText));
            }

            switch (strategy)
            {
                case StringObfuscationStrategy.XorBase64:
                    return BuildXorBase64(plainText);
                case StringObfuscationStrategy.LcgBase64:
                    return BuildLcgBase64(plainText);
                case StringObfuscationStrategy.GZipBase64:
                    return BuildGZipBase64(plainText);
                case StringObfuscationStrategy.GZipLcgBase64:
                    return BuildGZipLcgBase64(plainText);
                case StringObfuscationStrategy.HexReverseXor:
                    return BuildHexReverseXor(plainText);
                case StringObfuscationStrategy.DecimalDelta:
                    return BuildDecimalDelta(plainText);
                case StringObfuscationStrategy.Utf16DeltaArrays:
                    return BuildUtf16DeltaArrays(plainText);
                case StringObfuscationStrategy.ShuffledUtf16Triplets:
                    return BuildShuffledUtf16Triplets(plainText);
                case StringObfuscationStrategy.InterleavedMaskPairs:
                    return BuildInterleavedMaskPairs(plainText);
                case StringObfuscationStrategy.AffineBase64:
                    return BuildAffineBase64(plainText);
                case StringObfuscationStrategy.BytePermutation:
                    return BuildBytePermutation(plainText);
                case StringObfuscationStrategy.UInt64Packing:
                    return BuildUInt64Packing(plainText);
                case StringObfuscationStrategy.GuidPacking:
                    return BuildGuidPacking(plainText);
                case StringObfuscationStrategy.BigIntegerPacking:
                    return BuildBigIntegerPacking(plainText);
                case StringObfuscationStrategy.JunkedBase64:
                    return BuildJunkedBase64(plainText);
                default:
                    throw new ArgumentOutOfRangeException(nameof(strategy), strategy, null);
            }
        }

        private static ExpressionSyntax BuildXorBase64(string plainText)
        {
            var data = Encoding.UTF8.GetBytes(plainText);
            var key = RandomBytes(NextInt(9, 25));
            var encrypted = Xor(data, key);

            var code =
                $"((System.Func<byte[],byte[],string>)((d,k)=>System.Text.Encoding.UTF8.GetString(System.Linq.Enumerable.ToArray(System.Linq.Enumerable.Select(d,(b,i)=>(byte)(b^k[i%k.Length]))))))(System.Convert.FromBase64String({Quote(Convert.ToBase64String(encrypted))}),System.Convert.FromBase64String({Quote(Convert.ToBase64String(key))}))";

            return Parse(code);
        }

        private static ExpressionSyntax BuildLcgBase64(string plainText)
        {
            var data = Encoding.UTF8.GetBytes(plainText);
            var seed = BitConverter.ToUInt32(RandomBytes(4), 0);
            ApplyLcgXor(data, seed);

            var code =
                $"((System.Func<string>)(()=>{{var d=System.Convert.FromBase64String({Quote(Convert.ToBase64String(data))});uint s={seed.ToString(CultureInfo.InvariantCulture)}u;for(var i=0;i<d.Length;i++){{s=unchecked(s*1664525u+1013904223u);d[i]=(byte)(d[i]^(byte)(s>>24));}}return System.Text.Encoding.UTF8.GetString(d);}}))()";

            return Parse(code);
        }

        private static ExpressionSyntax BuildGZipBase64(string plainText)
        {
            var compressed = GZip(Encoding.UTF8.GetBytes(plainText));

            var code =
                $"((System.Func<string>)(()=>{{using(var i=new System.IO.MemoryStream(System.Convert.FromBase64String({Quote(Convert.ToBase64String(compressed))})))using(var z=new System.IO.Compression.GZipStream(i,System.IO.Compression.CompressionMode.Decompress))using(var o=new System.IO.MemoryStream()){{z.CopyTo(o);return System.Text.Encoding.UTF8.GetString(o.ToArray());}}}}))()";

            return Parse(code);
        }

        private static ExpressionSyntax BuildGZipLcgBase64(string plainText)
        {
            var compressed = GZip(Encoding.UTF8.GetBytes(plainText));
            var seed = BitConverter.ToUInt32(RandomBytes(4), 0);
            ApplyLcgXor(compressed, seed);

            var code =
                $"((System.Func<string>)(()=>{{var d=System.Convert.FromBase64String({Quote(Convert.ToBase64String(compressed))});uint s={seed.ToString(CultureInfo.InvariantCulture)}u;for(var n=0;n<d.Length;n++){{s=unchecked(s*1664525u+1013904223u);d[n]=(byte)(d[n]^(byte)(s>>24));}}using(var i=new System.IO.MemoryStream(d))using(var z=new System.IO.Compression.GZipStream(i,System.IO.Compression.CompressionMode.Decompress))using(var o=new System.IO.MemoryStream()){{z.CopyTo(o);return System.Text.Encoding.UTF8.GetString(o.ToArray());}}}}))()";

            return Parse(code);
        }

        private static ExpressionSyntax BuildHexReverseXor(string plainText)
        {
            var data = Encoding.UTF8.GetBytes(plainText);
            var key = NonZeroRandomByte();
            var transformed = data.Select(b => (byte)(b ^ key)).Reverse().ToArray();
            var hex = string.Concat(transformed.Select(b => b.ToString("X2", CultureInfo.InvariantCulture)));

            var code =
                $"((System.Func<string,string>)(h=>System.Text.Encoding.UTF8.GetString(System.Linq.Enumerable.ToArray(System.Linq.Enumerable.Reverse(System.Linq.Enumerable.Select(System.Linq.Enumerable.Range(0,h.Length/2),i=>(byte)(System.Convert.ToByte(h.Substring(i*2,2),16)^{key.ToString(CultureInfo.InvariantCulture)})))))))({Quote(hex)})";

            return Parse(code);
        }

        private static ExpressionSyntax BuildDecimalDelta(string plainText)
        {
            var data = Encoding.UTF8.GetBytes(plainText);
            var seed = NextInt(257, 4096);
            var step = NextInt(17, 251);
            var values = new int[data.Length];

            for (var i = 0; i < data.Length; i++)
            {
                var mask = 256 + (int)((seed + (long)i * step) % 997);
                values[i] = data[i] + mask;
            }

            var payload = string.Join(";", values.Select(v => v.ToString(CultureInfo.InvariantCulture)));
            var code =
                $"((System.Func<string,string>)(s=>System.Text.Encoding.UTF8.GetString(System.Linq.Enumerable.ToArray(System.Linq.Enumerable.Select(s.Split(new char[]{{';'}},System.StringSplitOptions.RemoveEmptyEntries),(x,i)=>(byte)(System.Int32.Parse(x,System.Globalization.CultureInfo.InvariantCulture)-(256+(int)(({seed.ToString(CultureInfo.InvariantCulture)}+(long)i*{step.ToString(CultureInfo.InvariantCulture)})%997))))))))({Quote(payload)})";

            return Parse(code);
        }

        private static ExpressionSyntax BuildUtf16DeltaArrays(string plainText)
        {
            var masked = new int[plainText.Length];
            var deltas = new int[plainText.Length];

            for (var i = 0; i < plainText.Length; i++)
            {
                deltas[i] = NextInt(257, 8193);
                masked[i] = plainText[i] + deltas[i];
            }

            var code =
                $"((System.Func<int[],int[],string>)((d,k)=>new string(System.Linq.Enumerable.ToArray(System.Linq.Enumerable.Select(d,(v,i)=>(char)(v-k[i]))))))({IntArray(masked)},{IntArray(deltas)})";

            return Parse(code);
        }

        private static ExpressionSyntax BuildShuffledUtf16Triplets(string plainText)
        {
            var rows = new int[plainText.Length][];

            for (var i = 0; i < plainText.Length; i++)
            {
                var mask = NextInt(1, 65536);
                rows[i] = new[] { i, plainText[i] ^ mask, mask };
            }

            Shuffle(rows);
            var flattened = rows.SelectMany(row => row).ToArray();

            var code =
                $"((System.Func<int[],string>)(a=>new string(System.Linq.Enumerable.ToArray(System.Linq.Enumerable.Select(System.Linq.Enumerable.OrderBy(System.Linq.Enumerable.Range(0,a.Length/3),i=>a[i*3]),i=>(char)(a[i*3+1]^a[i*3+2]))))))({IntArray(flattened)})";

            return Parse(code);
        }

        private static ExpressionSyntax BuildInterleavedMaskPairs(string plainText)
        {
            var data = Encoding.UTF8.GetBytes(plainText);
            var pairs = new byte[data.Length * 2];

            var masks = RandomBytes(data.Length);
            for (var i = 0; i < data.Length; i++)
            {
                pairs[i * 2] = masks[i];
                pairs[i * 2 + 1] = (byte)(data[i] ^ masks[i]);
            }

            var code =
                $"((System.Func<byte[],string>)(a=>System.Text.Encoding.UTF8.GetString(System.Linq.Enumerable.ToArray(System.Linq.Enumerable.Select(System.Linq.Enumerable.Range(0,a.Length/2),i=>(byte)(a[i*2]^a[i*2+1]))))))(System.Convert.FromBase64String({Quote(Convert.ToBase64String(pairs))}))";

            return Parse(code);
        }

        private static ExpressionSyntax BuildAffineBase64(string plainText)
        {
            var data = Encoding.UTF8.GetBytes(plainText);
            var multiplier = NextInt(3, 256) | 1;
            var inverse = ModularInverse256(multiplier);
            var offset = NextInt(1, 256);
            var encrypted = data.Select(b => (byte)((b * multiplier + offset) & 255)).ToArray();

            var code =
                $"System.Text.Encoding.UTF8.GetString(System.Linq.Enumerable.ToArray(System.Linq.Enumerable.Select(System.Convert.FromBase64String({Quote(Convert.ToBase64String(encrypted))}),b=>(byte)(((b-{offset.ToString(CultureInfo.InvariantCulture)})*{inverse.ToString(CultureInfo.InvariantCulture)})&255))))";

            return Parse(code);
        }

        private static ExpressionSyntax BuildBytePermutation(string plainText)
        {
            var data = Encoding.UTF8.GetBytes(plainText);
            var order = Enumerable.Range(0, data.Length).ToArray();
            Shuffle(order);

            var shuffled = new byte[data.Length];
            var inverse = new int[data.Length];

            for (var outputIndex = 0; outputIndex < order.Length; outputIndex++)
            {
                var originalIndex = order[outputIndex];
                shuffled[outputIndex] = data[originalIndex];
                inverse[originalIndex] = outputIndex;
            }

            var code =
                $"((System.Func<byte[],int[],string>)((d,p)=>System.Text.Encoding.UTF8.GetString(System.Linq.Enumerable.ToArray(System.Linq.Enumerable.Select(System.Linq.Enumerable.Range(0,p.Length),i=>d[p[i]])))))(System.Convert.FromBase64String({Quote(Convert.ToBase64String(shuffled))}),{IntArray(inverse)})";

            return Parse(code);
        }

        private static ExpressionSyntax BuildUInt64Packing(string plainText)
        {
            var data = Encoding.UTF8.GetBytes(plainText);
            var key = NonZeroRandomByte();
            var blocks = new ulong[(data.Length + 7) / 8];

            for (var i = 0; i < data.Length; i++)
            {
                var transformed = (byte)(data[i] ^ key);
                blocks[i / 8] |= (ulong)transformed << (8 * (i % 8));
            }

            var code =
                $"((System.Func<ulong[],string>)(a=>System.Text.Encoding.UTF8.GetString(System.Linq.Enumerable.ToArray(System.Linq.Enumerable.Select(System.Linq.Enumerable.Range(0,{data.Length.ToString(CultureInfo.InvariantCulture)}),i=>(byte)(((a[i/8]>>(8*(i%8)))&255UL)^{key.ToString(CultureInfo.InvariantCulture)}UL))))))({ULongArray(blocks)})";

            return Parse(code);
        }

        private static ExpressionSyntax BuildGuidPacking(string plainText)
        {
            var data = Encoding.UTF8.GetBytes(plainText);
            var key = NonZeroRandomByte();
            var transformed = data.Select(b => (byte)(b ^ key)).ToArray();
            var guids = new List<string>();

            for (var offset = 0; offset < transformed.Length; offset += 16)
            {
                var chunk = new byte[16];
                Buffer.BlockCopy(transformed, offset, chunk, 0, Math.Min(16, transformed.Length - offset));
                guids.Add(new Guid(chunk).ToString("D"));
            }

            var code =
                $"((System.Func<string[],string>)(g=>System.Text.Encoding.UTF8.GetString(System.Linq.Enumerable.ToArray(System.Linq.Enumerable.Select(System.Linq.Enumerable.Take(System.Linq.Enumerable.SelectMany(g,s=>new System.Guid(s).ToByteArray()),{data.Length.ToString(CultureInfo.InvariantCulture)}),b=>(byte)(b^{key.ToString(CultureInfo.InvariantCulture)}))))))({StringArray(guids)})";

            return Parse(code);
        }

        private static ExpressionSyntax BuildBigIntegerPacking(string plainText)
        {
            var data = Encoding.UTF8.GetBytes(plainText);
            var key = NonZeroRandomByte();
            var packedBytes = new byte[data.Length + 1];

            for (var i = 0; i < data.Length; i++)
            {
                packedBytes[i] = (byte)(data[i] ^ key);
            }

            // Keep the BigInteger payload positive and preserve trailing zero bytes.
            packedBytes[packedBytes.Length - 1] = 1;

            var number = new BigInteger(packedBytes);
            var decimalPayload = number.ToString(CultureInfo.InvariantCulture);

            var code =
                $"System.Text.Encoding.UTF8.GetString(System.Linq.Enumerable.ToArray(System.Linq.Enumerable.Select(System.Linq.Enumerable.Take(System.Numerics.BigInteger.Parse({Quote(decimalPayload)},System.Globalization.NumberStyles.Integer,System.Globalization.CultureInfo.InvariantCulture).ToByteArray(),{data.Length.ToString(CultureInfo.InvariantCulture)}),b=>(byte)(b^{key.ToString(CultureInfo.InvariantCulture)}))))";

            return Parse(code);
        }

        private static ExpressionSyntax BuildJunkedBase64(string plainText)
        {
            var base64 = Convert.ToBase64String(Encoding.UTF8.GetBytes(plainText));
            const string junkAlphabet = "!#$%&()*,-.:;<>?@[]^_{|}~";
            var mixed = new StringBuilder(base64.Length * 3);

            foreach (var c in base64)
            {
                mixed.Append(c);
                mixed.Append(junkAlphabet[NextInt(0, junkAlphabet.Length)]);
                mixed.Append(junkAlphabet[NextInt(0, junkAlphabet.Length)]);
            }

            var code =
                $"System.Text.Encoding.UTF8.GetString(System.Convert.FromBase64String(new string(System.Linq.Enumerable.ToArray(System.Linq.Enumerable.Where({Quote(mixed.ToString())},(c,i)=>(i%3)==0)))))";

            return Parse(code);
        }

        private static byte[] Xor(byte[] data, byte[] key)
        {
            var result = new byte[data.Length];
            for (var i = 0; i < data.Length; i++)
            {
                result[i] = (byte)(data[i] ^ key[i % key.Length]);
            }

            return result;
        }

        private static void ApplyLcgXor(byte[] data, uint seed)
        {
            var state = seed;
            for (var i = 0; i < data.Length; i++)
            {
                state = unchecked(state * 1664525u + 1013904223u);
                data[i] ^= (byte)(state >> 24);
            }
        }

        private static byte[] GZip(byte[] data)
        {
            using (var output = new MemoryStream())
            {
                using (var gzip = new GZipStream(output, CompressionMode.Compress, true))
                {
                    gzip.Write(data, 0, data.Length);
                }

                return output.ToArray();
            }
        }

        private static int ModularInverse256(int value)
        {
            for (var candidate = 1; candidate < 256; candidate += 2)
            {
                if (((value * candidate) & 255) == 1)
                {
                    return candidate;
                }
            }

            throw new InvalidOperationException("The multiplier has no inverse modulo 256.");
        }

        private static byte NonZeroRandomByte()
        {
            byte value;
            do
            {
                value = RandomBytes(1)[0];
            }
            while (value == 0);

            return value;
        }

        private static byte[] RandomBytes(int length)
        {
            var result = new byte[length];
            lock (RngLock)
            {
                Rng.GetBytes(result);
            }

            return result;
        }

        private static int NextInt(int minInclusive, int maxExclusive)
        {
            if (minInclusive >= maxExclusive)
            {
                throw new ArgumentOutOfRangeException(nameof(maxExclusive));
            }

            var range = (uint)(maxExclusive - minInclusive);
            var maximumAccepted = uint.MaxValue - ((uint.MaxValue % range + 1u) % range);
            uint random;

            do
            {
                random = BitConverter.ToUInt32(RandomBytes(4), 0);
            }
            while (random > maximumAccepted);

            return minInclusive + (int)(random % range);
        }

        private static void Shuffle<T>(T[] array)
        {
            for (var i = array.Length - 1; i > 0; i--)
            {
                var j = NextInt(0, i + 1);
                var temporary = array[i];
                array[i] = array[j];
                array[j] = temporary;
            }
        }

        private static ExpressionSyntax Parse(string code)
        {
            var expression = SyntaxFactory.ParseExpression(code);
            var errors = expression.GetDiagnostics()
                .Where(d => d.Severity == Microsoft.CodeAnalysis.DiagnosticSeverity.Error)
                .ToArray();

            if (errors.Length != 0)
            {
                throw new InvalidOperationException(
                    "Generated decoder expression is invalid: " +
                    string.Join(" | ", errors.Select(e => e.ToString())) +
                    Environment.NewLine + code);
            }

            return expression;
        }

        private static string Quote(string value)
        {
            return SyntaxFactory.LiteralExpression(
                SyntaxKind.StringLiteralExpression,
                SyntaxFactory.Literal(value)).ToFullString();
        }

        private static string IntArray(IEnumerable<int> values)
        {
            return "new int[]{" + string.Join(",", values.Select(v => v.ToString(CultureInfo.InvariantCulture))) + "}";
        }

        private static string ULongArray(IEnumerable<ulong> values)
        {
            return "new ulong[]{" + string.Join(",", values.Select(v => v.ToString(CultureInfo.InvariantCulture) + "UL")) + "}";
        }

        private static string StringArray(IEnumerable<string> values)
        {
            return "new string[]{" + string.Join(",", values.Select(Quote)) + "}";
        }
    }
}
