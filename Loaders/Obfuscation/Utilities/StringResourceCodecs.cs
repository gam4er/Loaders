using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Security.Cryptography;

namespace Loaders.Obfuscation.Utilities
{
    internal sealed class StringRuntimeDependency
    {
        public StringRuntimeDependency(string assemblyReference, string packageId = null, string packageVersion = null)
        {
            AssemblyReference = assemblyReference?.Trim() ?? string.Empty;
            PackageId = packageId?.Trim() ?? string.Empty;
            PackageVersion = packageVersion?.Trim() ?? string.Empty;
        }

        public string AssemblyReference { get; }
        public string PackageId { get; }
        public string PackageVersion { get; }
    }

    internal sealed class StringPayload
    {
        public StringPayload(
            byte codecId,
            int flags,
            int originalCharCount,
            byte[] metadata,
            byte[] data,
            string codecName,
            StringObfuscationStrategy strategy)
        {
            CodecId = codecId;
            Flags = flags;
            OriginalCharCount = originalCharCount;
            Metadata = metadata ?? Array.Empty<byte>();
            Data = data ?? Array.Empty<byte>();
            CodecName = codecName ?? string.Empty;
            Strategy = strategy;
        }

        public byte CodecId { get; }
        public int Flags { get; }
        public int OriginalCharCount { get; }
        public byte[] Metadata { get; }
        public byte[] Data { get; }
        public string CodecName { get; }
        public StringObfuscationStrategy Strategy { get; }
    }

    internal interface IStringPayloadCodec
    {
        byte CodecId { get; }
        string Name { get; }
        StringObfuscationStrategy Strategy { get; }
        IReadOnlyList<StringRuntimeDependency> Dependencies { get; }
        bool CanEncode(string value, byte[] utf16Bytes);
        StringPayload Encode(string value, byte[] utf16Bytes, IStringObfuscationRandom random);
    }

    internal interface IStringObfuscationRandom
    {
        byte[] Bytes(int length);
        int NextInt(int minInclusive, int maxExclusive);
        byte NonZeroByte();
        uint NextUInt32();
        void Shuffle<T>(T[] array);
    }

    internal sealed class CryptoStringObfuscationRandom : IStringObfuscationRandom, IDisposable
    {
        private readonly object _sync = new object();
        private readonly RandomNumberGenerator _rng = RandomNumberGenerator.Create();

        public byte[] Bytes(int length)
        {
            if (length < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(length));
            }

            var result = new byte[length];
            lock (_sync)
            {
                _rng.GetBytes(result);
            }

            return result;
        }

        public int NextInt(int minInclusive, int maxExclusive)
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
                random = NextUInt32();
            }
            while (random > maximumAccepted);

            return minInclusive + (int)(random % range);
        }

        public byte NonZeroByte()
        {
            byte value;
            do
            {
                value = Bytes(1)[0];
            }
            while (value == 0);

            return value;
        }

        public uint NextUInt32()
        {
            return BitConverter.ToUInt32(Bytes(4), 0);
        }

        public void Shuffle<T>(T[] array)
        {
            if (array == null)
            {
                throw new ArgumentNullException(nameof(array));
            }

            for (var i = array.Length - 1; i > 0; i--)
            {
                var j = NextInt(0, i + 1);
                var temporary = array[i];
                array[i] = array[j];
                array[j] = temporary;
            }
        }

        public void Dispose()
        {
            _rng.Dispose();
        }
    }

    internal static class StringPayloadCodecCatalog
    {
        public static IReadOnlyList<IStringPayloadCodec> CreateDefault()
        {
            return new IStringPayloadCodec[]
            {
                new XorCodec(),
                new LcgCodec(),
                new GZipCodec(),
                new GZipLcgCodec(),
                new HexReverseXorCodec(),
                new DecimalDeltaCodec(),
                new Utf16DeltaArraysCodec(),
                new ShuffledUtf16TripletsCodec(),
                new InterleavedMaskPairsCodec(),
                new AffineCodec(),
                new BytePermutationCodec(),
                new UInt64PackingCodec(),
                new GuidPackingCodec(),
                new BigIntegerPackingCodec(),
                new JunkedCodec()
            };
        }

        public static IReadOnlyList<IStringPayloadCodec> SelectCandidates(
            IReadOnlyList<IStringPayloadCodec> codecs,
            string value,
            byte[] utf16Bytes,
            StringObfuscationStrategy? forcedStrategy)
        {
            if (forcedStrategy.HasValue)
            {
                return codecs
                    .Where(codec => codec.Strategy == forcedStrategy.Value && codec.CanEncode(value, utf16Bytes))
                    .ToArray();
            }

            var candidates = new List<StringObfuscationStrategy>
            {
                StringObfuscationStrategy.XorBase64,
                StringObfuscationStrategy.LcgBase64,
                StringObfuscationStrategy.AffineBase64,
                StringObfuscationStrategy.UInt64Packing
            };

            if (utf16Bytes.Length <= 256)
            {
                candidates.Add(StringObfuscationStrategy.HexReverseXor);
                candidates.Add(StringObfuscationStrategy.InterleavedMaskPairs);
                candidates.Add(StringObfuscationStrategy.GuidPacking);
            }

            if (utf16Bytes.Length <= 96)
            {
                candidates.Add(StringObfuscationStrategy.DecimalDelta);
                candidates.Add(StringObfuscationStrategy.Utf16DeltaArrays);
                candidates.Add(StringObfuscationStrategy.ShuffledUtf16Triplets);
                candidates.Add(StringObfuscationStrategy.BytePermutation);
                candidates.Add(StringObfuscationStrategy.JunkedBase64);
            }

            if (utf16Bytes.Length >= 48)
            {
                candidates.Add(StringObfuscationStrategy.GZipBase64);
                candidates.Add(StringObfuscationStrategy.GZipLcgBase64);
            }

            return codecs
                .Where(codec => candidates.Contains(codec.Strategy) && codec.CanEncode(value, utf16Bytes))
                .ToArray();
        }

        private abstract class StringPayloadCodecBase : IStringPayloadCodec
        {
            protected static readonly IReadOnlyList<StringRuntimeDependency> NoDependencies =
                Array.Empty<StringRuntimeDependency>();

            protected static readonly IReadOnlyList<StringRuntimeDependency> CompressionDependency =
                new[] { new StringRuntimeDependency("System.IO.Compression") };

            protected StringPayloadCodecBase(byte codecId, string name, StringObfuscationStrategy strategy)
            {
                CodecId = codecId;
                Name = name;
                Strategy = strategy;
            }

            public byte CodecId { get; }
            public string Name { get; }
            public StringObfuscationStrategy Strategy { get; }
            public virtual IReadOnlyList<StringRuntimeDependency> Dependencies => NoDependencies;

            public virtual bool CanEncode(string value, byte[] utf16Bytes)
            {
                return value != null && utf16Bytes != null;
            }

            public StringPayload Encode(string value, byte[] utf16Bytes, IStringObfuscationRandom random)
            {
                if (value == null)
                {
                    throw new ArgumentNullException(nameof(value));
                }

                if (utf16Bytes == null)
                {
                    throw new ArgumentNullException(nameof(utf16Bytes));
                }

                if (random == null)
                {
                    throw new ArgumentNullException(nameof(random));
                }

                var encoded = EncodeCore(value, utf16Bytes, random);
                return new StringPayload(
                    CodecId,
                    encoded.Flags,
                    value.Length,
                    encoded.Metadata,
                    encoded.Data,
                    Name,
                    Strategy);
            }

            protected abstract EncodedBytes EncodeCore(string value, byte[] utf16Bytes, IStringObfuscationRandom random);

            protected sealed class EncodedBytes
            {
                public EncodedBytes(byte[] metadata, byte[] data, int flags = 0)
                {
                    Metadata = metadata ?? Array.Empty<byte>();
                    Data = data ?? Array.Empty<byte>();
                    Flags = flags;
                }

                public byte[] Metadata { get; }
                public byte[] Data { get; }
                public int Flags { get; }
            }

            protected static byte[] Clone(byte[] bytes)
            {
                var clone = new byte[bytes.Length];
                Buffer.BlockCopy(bytes, 0, clone, 0, bytes.Length);
                return clone;
            }

            protected static byte[] Xor(byte[] data, byte[] key)
            {
                var result = new byte[data.Length];
                for (var i = 0; i < data.Length; i++)
                {
                    result[i] = (byte)(data[i] ^ key[i % key.Length]);
                }

                return result;
            }

            protected static void ApplyLcgXor(byte[] data, uint seed)
            {
                var state = seed;
                for (var i = 0; i < data.Length; i++)
                {
                    state = unchecked(state * 1664525u + 1013904223u);
                    data[i] ^= (byte)(state >> 24);
                }
            }

            protected static byte[] GZip(byte[] data)
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

            protected static void WriteInt32(List<byte> target, int value)
            {
                target.Add((byte)value);
                target.Add((byte)(value >> 8));
                target.Add((byte)(value >> 16));
                target.Add((byte)(value >> 24));
            }

            protected static void WriteUInt32(List<byte> target, uint value)
            {
                target.Add((byte)value);
                target.Add((byte)(value >> 8));
                target.Add((byte)(value >> 16));
                target.Add((byte)(value >> 24));
            }

            protected static void WriteUInt64(List<byte> target, ulong value)
            {
                for (var i = 0; i < 8; i++)
                {
                    target.Add((byte)(value >> (8 * i)));
                }
            }

            protected static void WriteUInt16(List<byte> target, ushort value)
            {
                target.Add((byte)value);
                target.Add((byte)(value >> 8));
            }

            protected static byte[] UInt32Metadata(params uint[] values)
            {
                var bytes = new List<byte>(values.Length * 4);
                foreach (var value in values)
                {
                    WriteUInt32(bytes, value);
                }

                return bytes.ToArray();
            }
        }

        private sealed class XorCodec : StringPayloadCodecBase
        {
            public XorCodec()
                : base(1, "XorBase64Binary", StringObfuscationStrategy.XorBase64)
            {
            }

            protected override EncodedBytes EncodeCore(string value, byte[] utf16Bytes, IStringObfuscationRandom random)
            {
                var key = random.Bytes(random.NextInt(9, 25));
                return new EncodedBytes(key, Xor(utf16Bytes, key));
            }
        }

        private sealed class LcgCodec : StringPayloadCodecBase
        {
            public LcgCodec()
                : base(2, "LcgBase64Binary", StringObfuscationStrategy.LcgBase64)
            {
            }

            protected override EncodedBytes EncodeCore(string value, byte[] utf16Bytes, IStringObfuscationRandom random)
            {
                var data = Clone(utf16Bytes);
                var seed = random.NextUInt32();
                ApplyLcgXor(data, seed);
                return new EncodedBytes(UInt32Metadata(seed), data);
            }
        }

        private sealed class GZipCodec : StringPayloadCodecBase
        {
            public GZipCodec()
                : base(3, "GZipBase64Binary", StringObfuscationStrategy.GZipBase64)
            {
            }

            public override IReadOnlyList<StringRuntimeDependency> Dependencies => CompressionDependency;

            protected override EncodedBytes EncodeCore(string value, byte[] utf16Bytes, IStringObfuscationRandom random)
            {
                return new EncodedBytes(Array.Empty<byte>(), GZip(utf16Bytes));
            }
        }

        private sealed class GZipLcgCodec : StringPayloadCodecBase
        {
            public GZipLcgCodec()
                : base(4, "GZipLcgBase64Binary", StringObfuscationStrategy.GZipLcgBase64)
            {
            }

            public override IReadOnlyList<StringRuntimeDependency> Dependencies => CompressionDependency;

            protected override EncodedBytes EncodeCore(string value, byte[] utf16Bytes, IStringObfuscationRandom random)
            {
                var data = GZip(utf16Bytes);
                var seed = random.NextUInt32();
                ApplyLcgXor(data, seed);
                return new EncodedBytes(UInt32Metadata(seed), data);
            }
        }

        private sealed class HexReverseXorCodec : StringPayloadCodecBase
        {
            public HexReverseXorCodec()
                : base(5, "HexReverseXorBinary", StringObfuscationStrategy.HexReverseXor)
            {
            }

            protected override EncodedBytes EncodeCore(string value, byte[] utf16Bytes, IStringObfuscationRandom random)
            {
                var key = random.NonZeroByte();
                var data = new byte[utf16Bytes.Length];
                for (var i = 0; i < utf16Bytes.Length; i++)
                {
                    data[utf16Bytes.Length - i - 1] = (byte)(utf16Bytes[i] ^ key);
                }

                return new EncodedBytes(new[] { key }, data);
            }
        }

        private sealed class DecimalDeltaCodec : StringPayloadCodecBase
        {
            public DecimalDeltaCodec()
                : base(6, "DecimalDeltaBinary", StringObfuscationStrategy.DecimalDelta)
            {
            }

            protected override EncodedBytes EncodeCore(string value, byte[] utf16Bytes, IStringObfuscationRandom random)
            {
                var seed = random.NextInt(257, 4096);
                var step = random.NextInt(17, 251);
                var data = new List<byte>(utf16Bytes.Length * 2);

                for (var i = 0; i < utf16Bytes.Length; i++)
                {
                    var mask = 256 + (int)((seed + (long)i * step) % 997);
                    WriteUInt16(data, checked((ushort)(utf16Bytes[i] + mask)));
                }

                return new EncodedBytes(UInt32Metadata((uint)seed, (uint)step), data.ToArray());
            }
        }

        private sealed class Utf16DeltaArraysCodec : StringPayloadCodecBase
        {
            public Utf16DeltaArraysCodec()
                : base(7, "Utf16DeltaArraysBinary", StringObfuscationStrategy.Utf16DeltaArrays)
            {
            }

            protected override EncodedBytes EncodeCore(string value, byte[] utf16Bytes, IStringObfuscationRandom random)
            {
                var data = new List<byte>(value.Length * 8);
                for (var i = 0; i < value.Length; i++)
                {
                    var delta = random.NextInt(257, 8193);
                    WriteUInt32(data, (uint)(value[i] + delta));
                    WriteUInt32(data, (uint)delta);
                }

                return new EncodedBytes(Array.Empty<byte>(), data.ToArray());
            }
        }

        private sealed class ShuffledUtf16TripletsCodec : StringPayloadCodecBase
        {
            public ShuffledUtf16TripletsCodec()
                : base(8, "ShuffledUtf16TripletsBinary", StringObfuscationStrategy.ShuffledUtf16Triplets)
            {
            }

            protected override EncodedBytes EncodeCore(string value, byte[] utf16Bytes, IStringObfuscationRandom random)
            {
                var rows = new Triplet[value.Length];
                for (var i = 0; i < value.Length; i++)
                {
                    var mask = random.NextInt(1, 65536);
                    rows[i] = new Triplet(i, value[i] ^ mask, mask);
                }

                random.Shuffle(rows);
                var data = new List<byte>(rows.Length * 12);
                foreach (var row in rows)
                {
                    WriteUInt32(data, (uint)row.Index);
                    WriteUInt32(data, (uint)row.Masked);
                    WriteUInt32(data, (uint)row.Mask);
                }

                return new EncodedBytes(Array.Empty<byte>(), data.ToArray());
            }

            private struct Triplet
            {
                public Triplet(int index, int masked, int mask)
                {
                    Index = index;
                    Masked = masked;
                    Mask = mask;
                }

                public int Index { get; }
                public int Masked { get; }
                public int Mask { get; }
            }
        }

        private sealed class InterleavedMaskPairsCodec : StringPayloadCodecBase
        {
            public InterleavedMaskPairsCodec()
                : base(9, "InterleavedMaskPairsBinary", StringObfuscationStrategy.InterleavedMaskPairs)
            {
            }

            protected override EncodedBytes EncodeCore(string value, byte[] utf16Bytes, IStringObfuscationRandom random)
            {
                var masks = random.Bytes(utf16Bytes.Length);
                var data = new byte[utf16Bytes.Length * 2];
                for (var i = 0; i < utf16Bytes.Length; i++)
                {
                    data[i * 2] = masks[i];
                    data[i * 2 + 1] = (byte)(utf16Bytes[i] ^ masks[i]);
                }

                return new EncodedBytes(Array.Empty<byte>(), data);
            }
        }

        private sealed class AffineCodec : StringPayloadCodecBase
        {
            public AffineCodec()
                : base(10, "AffineBase64Binary", StringObfuscationStrategy.AffineBase64)
            {
            }

            protected override EncodedBytes EncodeCore(string value, byte[] utf16Bytes, IStringObfuscationRandom random)
            {
                var multiplier = random.NextInt(3, 256) | 1;
                var inverse = ModularInverse256(multiplier);
                var offset = random.NextInt(1, 256);
                var data = utf16Bytes.Select(b => (byte)((b * multiplier + offset) & 255)).ToArray();
                return new EncodedBytes(new[] { (byte)inverse, (byte)offset }, data);
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

                throw new InvalidOperationException(
                    string.Format(CultureInfo.InvariantCulture, "No modular inverse for {0}.", value));
            }
        }

        private sealed class BytePermutationCodec : StringPayloadCodecBase
        {
            public BytePermutationCodec()
                : base(11, "BytePermutationBinary", StringObfuscationStrategy.BytePermutation)
            {
            }

            protected override EncodedBytes EncodeCore(string value, byte[] utf16Bytes, IStringObfuscationRandom random)
            {
                var order = Enumerable.Range(0, utf16Bytes.Length).ToArray();
                random.Shuffle(order);

                var shuffled = new byte[utf16Bytes.Length];
                var inverse = new int[utf16Bytes.Length];
                for (var outputIndex = 0; outputIndex < order.Length; outputIndex++)
                {
                    var originalIndex = order[outputIndex];
                    shuffled[outputIndex] = utf16Bytes[originalIndex];
                    inverse[originalIndex] = outputIndex;
                }

                var metadata = new List<byte>(inverse.Length * 4);
                foreach (var valueIndex in inverse)
                {
                    WriteInt32(metadata, valueIndex);
                }

                return new EncodedBytes(metadata.ToArray(), shuffled);
            }
        }

        private sealed class UInt64PackingCodec : StringPayloadCodecBase
        {
            public UInt64PackingCodec()
                : base(12, "UInt64PackingBinary", StringObfuscationStrategy.UInt64Packing)
            {
            }

            protected override EncodedBytes EncodeCore(string value, byte[] utf16Bytes, IStringObfuscationRandom random)
            {
                var key = random.NonZeroByte();
                var blocks = new ulong[(utf16Bytes.Length + 7) / 8];
                for (var i = 0; i < utf16Bytes.Length; i++)
                {
                    blocks[i / 8] |= (ulong)(byte)(utf16Bytes[i] ^ key) << (8 * (i % 8));
                }

                var data = new List<byte>(blocks.Length * 8);
                foreach (var block in blocks)
                {
                    WriteUInt64(data, block);
                }

                return new EncodedBytes(new[] { key }, data.ToArray());
            }
        }

        private sealed class GuidPackingCodec : StringPayloadCodecBase
        {
            public GuidPackingCodec()
                : base(13, "GuidPackingBinary", StringObfuscationStrategy.GuidPacking)
            {
            }

            protected override EncodedBytes EncodeCore(string value, byte[] utf16Bytes, IStringObfuscationRandom random)
            {
                var key = random.NonZeroByte();
                var data = new byte[((utf16Bytes.Length + 15) / 16) * 16];
                for (var i = 0; i < utf16Bytes.Length; i++)
                {
                    data[i] = (byte)(utf16Bytes[i] ^ key);
                }

                return new EncodedBytes(new[] { key }, data);
            }
        }

        private sealed class BigIntegerPackingCodec : StringPayloadCodecBase
        {
            public BigIntegerPackingCodec()
                : base(14, "BigIntegerPackingBinary", StringObfuscationStrategy.BigIntegerPacking)
            {
            }

            protected override EncodedBytes EncodeCore(string value, byte[] utf16Bytes, IStringObfuscationRandom random)
            {
                var key = random.NonZeroByte();
                var data = new byte[utf16Bytes.Length + 1];
                for (var i = 0; i < utf16Bytes.Length; i++)
                {
                    data[i] = (byte)(utf16Bytes[i] ^ key);
                }

                // This codec keeps the old BigInteger byte-container shape without
                // persisting the decimal BigInteger representation in generated C#.
                data[data.Length - 1] = 1;
                return new EncodedBytes(new[] { key }, data);
            }
        }

        private sealed class JunkedCodec : StringPayloadCodecBase
        {
            public JunkedCodec()
                : base(15, "JunkedBase64Binary", StringObfuscationStrategy.JunkedBase64)
            {
            }

            protected override EncodedBytes EncodeCore(string value, byte[] utf16Bytes, IStringObfuscationRandom random)
            {
                var key = random.NonZeroByte();
                var data = new byte[utf16Bytes.Length * 3];
                var junk = random.Bytes(data.Length);
                Buffer.BlockCopy(junk, 0, data, 0, junk.Length);

                for (var i = 0; i < utf16Bytes.Length; i++)
                {
                    data[i * 3] = (byte)(utf16Bytes[i] ^ key);
                }

                return new EncodedBytes(new[] { key }, data);
            }
        }
    }
}
