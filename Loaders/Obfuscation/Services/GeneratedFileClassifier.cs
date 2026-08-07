using System;
using System.IO;

namespace Loaders.Obfuscation.Services
{
    internal static class GeneratedFileClassifier
    {
        public const string GeneratedDirectoryName = "ObfuscationGenerated";

        public static bool IsTransformableSourceFile(string filePath)
        {
            return !IsGeneratedFile(filePath);
        }

        public static bool IsGeneratedFile(string filePath)
        {
            if (string.IsNullOrWhiteSpace(filePath))
            {
                return true;
            }

            var normalized = filePath.Replace(Path.AltDirectorySeparatorChar, Path.DirectorySeparatorChar);
            var generatedSegment = Path.DirectorySeparatorChar + GeneratedDirectoryName + Path.DirectorySeparatorChar;
            if (normalized.IndexOf(generatedSegment, StringComparison.OrdinalIgnoreCase) >= 0)
            {
                return true;
            }

            var fileName = Path.GetFileName(filePath);
            return fileName.EndsWith(".g.cs", StringComparison.OrdinalIgnoreCase) ||
                   fileName.EndsWith(".g.i.cs", StringComparison.OrdinalIgnoreCase) ||
                   fileName.EndsWith(".Designer.cs", StringComparison.OrdinalIgnoreCase) ||
                   fileName.EndsWith(".Generated.cs", StringComparison.OrdinalIgnoreCase) ||
                   IsAssemblyInfoFile(filePath);
        }

        public static bool IsAssemblyInfoFile(string filePath)
        {
            return string.Equals(Path.GetFileName(filePath), "AssemblyInfo.cs", StringComparison.OrdinalIgnoreCase);
        }

        public static bool IsGeneratedArtifact(string filePath)
        {
            if (string.IsNullOrWhiteSpace(filePath))
            {
                return true;
            }

            var normalized = filePath.Replace(Path.AltDirectorySeparatorChar, Path.DirectorySeparatorChar);
            var generatedSegment = Path.DirectorySeparatorChar + GeneratedDirectoryName + Path.DirectorySeparatorChar;
            return normalized.IndexOf(generatedSegment, StringComparison.OrdinalIgnoreCase) >= 0;
        }
    }
}
