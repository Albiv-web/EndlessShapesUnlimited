using System;
using System.IO;

namespace EndlessShapes2
{
    internal static class FilePathInput
    {
        internal static string Normalize(string value)
        {
            string normalized = value?.Trim() ?? string.Empty;
            if (normalized.Length >= 2 &&
                ((normalized[0] == '"' && normalized[normalized.Length - 1] == '"') ||
                 (normalized[0] == '\'' && normalized[normalized.Length - 1] == '\'')))
            {
                normalized = normalized.Substring(1, normalized.Length - 2).Trim();
            }
            return Environment.ExpandEnvironmentVariables(normalized);
        }

        internal static string MissingFileMessage(string kind, string path)
        {
            string fileName;
            try
            {
                fileName = Path.GetFileName(path);
            }
            catch
            {
                fileName = string.Empty;
            }

            if (string.IsNullOrWhiteSpace(fileName))
                fileName = "the selected file";
            else
                fileName = $"'{fileName}'";
            return $"The {kind} file {fileName} was not found. " +
                   "Paste the complete path, including every folder.";
        }

        internal static bool IsExpectedInputFailure(Exception exception) =>
            exception is ArgumentException ||
            exception is InvalidDataException ||
            exception is IOException ||
            exception is UnauthorizedAccessException;
    }
}
