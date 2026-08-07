using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace Loaders.Obfuscation.Utilities
{
    internal sealed class StringResourceEntry
    {
        public StringResourceEntry(int token, int cacheIndex, StringPayload payload, byte[] utf16PlainBytes)
        {
            Token = token;
            CacheIndex = cacheIndex;
            Payload = payload ?? throw new ArgumentNullException(nameof(payload));
            Utf16PlainBytes = utf16PlainBytes ?? Array.Empty<byte>();
        }

        public int Token { get; }
        public int CacheIndex { get; }
        public StringPayload Payload { get; }
        public byte[] Utf16PlainBytes { get; }
    }

    internal static class StringResourceSerializer
    {
        // Little-endian "LSR1" plus an explicit version field. The v1 layout is:
        // Header[10 * int32], sorted descriptors[10 * int32], metadata bytes,
        // payload bytes. Descriptor offsets are absolute byte offsets in the blob.
        public const int Magic = 0x3152534C;
        public const int FormatVersion = 1;
        public const int HeaderSize = 40;
        public const int DescriptorSize = 40;

        public static byte[] Serialize(IReadOnlyCollection<StringResourceEntry> entries, IStringObfuscationRandom random)
        {
            if (entries == null)
            {
                throw new ArgumentNullException(nameof(entries));
            }

            if (random == null)
            {
                throw new ArgumentNullException(nameof(random));
            }

            var descriptorEntries = entries.OrderBy(entry => entry.Token).ToArray();
            var payloadEntries = entries.ToArray();
            random.Shuffle(payloadEntries);

            var metadataRegion = new MemoryStream();
            var payloadRegion = new MemoryStream();
            var offsets = new Dictionary<int, EntryOffsets>();

            foreach (var entry in payloadEntries)
            {
                var metadataOffset = checked((int)metadataRegion.Length);
                metadataRegion.Write(entry.Payload.Metadata, 0, entry.Payload.Metadata.Length);
                var payloadOffset = checked((int)payloadRegion.Length);
                payloadRegion.Write(entry.Payload.Data, 0, entry.Payload.Data.Length);

                offsets[entry.Token] = new EntryOffsets(
                    metadataOffset,
                    entry.Payload.Metadata.Length,
                    payloadOffset,
                    entry.Payload.Data.Length);
            }

            var descriptorOffset = HeaderSize;
            var descriptorLength = checked(descriptorEntries.Length * DescriptorSize);
            var metadataOffsetAbsolute = checked(descriptorOffset + descriptorLength);
            var metadataLength = checked((int)metadataRegion.Length);
            var payloadOffsetAbsolute = checked(metadataOffsetAbsolute + metadataLength);
            var payloadLength = checked((int)payloadRegion.Length);

            using (var output = new MemoryStream(payloadOffsetAbsolute + payloadLength))
            using (var writer = new BinaryWriter(output))
            {
                writer.Write(Magic);
                writer.Write(FormatVersion);
                writer.Write(0);
                writer.Write(descriptorEntries.Length);
                writer.Write(descriptorOffset);
                writer.Write(descriptorLength);
                writer.Write(metadataOffsetAbsolute);
                writer.Write(metadataLength);
                writer.Write(payloadOffsetAbsolute);
                writer.Write(payloadLength);

                foreach (var entry in descriptorEntries)
                {
                    var entryOffsets = offsets[entry.Token];
                    writer.Write(entry.Token);
                    writer.Write(entry.CacheIndex);
                    writer.Write((int)entry.Payload.CodecId);
                    writer.Write(entry.Payload.Flags);
                    writer.Write(entry.Payload.OriginalCharCount);
                    writer.Write(metadataOffsetAbsolute + entryOffsets.MetadataOffset);
                    writer.Write(entryOffsets.MetadataLength);
                    writer.Write(payloadOffsetAbsolute + entryOffsets.PayloadOffset);
                    writer.Write(entryOffsets.PayloadLength);
                    writer.Write(unchecked((int)ComputeFnv1a32(entry.Utf16PlainBytes)));
                }

                metadataRegion.Position = 0;
                metadataRegion.CopyTo(output);
                payloadRegion.Position = 0;
                payloadRegion.CopyTo(output);
                return output.ToArray();
            }
        }

        public static uint ComputeFnv1a32(byte[] data)
        {
            unchecked
            {
                uint hash = 2166136261u;
                for (var i = 0; i < data.Length; i++)
                {
                    hash ^= data[i];
                    hash *= 16777619u;
                }

                return hash;
            }
        }

        private sealed class EntryOffsets
        {
            public EntryOffsets(int metadataOffset, int metadataLength, int payloadOffset, int payloadLength)
            {
                MetadataOffset = metadataOffset;
                MetadataLength = metadataLength;
                PayloadOffset = payloadOffset;
                PayloadLength = payloadLength;
            }

            public int MetadataOffset { get; }
            public int MetadataLength { get; }
            public int PayloadOffset { get; }
            public int PayloadLength { get; }
        }
    }
}
