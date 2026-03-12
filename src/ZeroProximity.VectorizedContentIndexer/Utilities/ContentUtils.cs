using System.Security.Cryptography;
using System.Text;

namespace ZeroProximity.VectorizedContentIndexer.Utilities;

/// <summary>
/// Provides utility methods for content validation, sanitization, and security.
/// </summary>
public static class ContentUtils
{
    /// <summary>
    /// Default maximum file size in bytes (10 MB).
    /// </summary>
    public const long DefaultMaxFileSize = 10 * 1024 * 1024;

    /// <summary>
    /// Default maximum text length for embedding (8192 tokens approximately).
    /// </summary>
    public const int DefaultMaxTextLength = 32768;

    /// <summary>
    /// Computes a SHA256 hash of the given content.
    /// </summary>
    /// <param name="content">The content to hash.</param>
    /// <returns>Hexadecimal hash string (64 characters).</returns>
    /// <exception cref="ArgumentException">Thrown when <paramref name="content"/> is null or whitespace.</exception>
    /// <remarks>
    /// Useful for content deduplication and change detection.
    /// </remarks>
    public static string ComputeHash(string content)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(content);

        var bytes = Encoding.UTF8.GetBytes(content);
        var hash = SHA256.HashData(bytes);
        return Convert.ToHexString(hash);
    }

    /// <summary>
    /// Extracts a title from markdown content or falls back to filename.
    /// </summary>
    /// <param name="content">Markdown content to extract title from.</param>
    /// <param name="filePath">File path for fallback title.</param>
    /// <returns>Extracted title from first H1 header, or filename without extension.</returns>
    /// <exception cref="ArgumentException">Thrown when <paramref name="content"/> or <paramref name="filePath"/> is null or whitespace.</exception>
    public static string ExtractTitle(string content, string filePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(content);
        ArgumentException.ThrowIfNullOrWhiteSpace(filePath);

        // Try to extract title from first markdown header
        var lines = content.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        foreach (var line in lines)
        {
            if (line.StartsWith("# ", StringComparison.Ordinal))
            {
                return line[2..].Trim();
            }
        }

        // Fallback to filename without extension
        return Path.GetFileNameWithoutExtension(filePath);
    }

    /// <summary>
    /// Escapes special characters in LIKE patterns to prevent unintended matches.
    /// </summary>
    /// <param name="pattern">Pattern to escape.</param>
    /// <returns>Escaped pattern with wildcards escaped using backslash.</returns>
    /// <remarks>
    /// Escapes % and _ which are wildcards in SQL LIKE patterns.
    /// </remarks>
    public static string EscapeLikePattern(string pattern)
    {
        if (string.IsNullOrEmpty(pattern))
        {
            return pattern ?? string.Empty;
        }

        // Escape % and _ which are wildcards in SQL LIKE
        return pattern.Replace("%", "\\%").Replace("_", "\\_");
    }

    /// <summary>
    /// Validates that a file path is safe and within allowed base paths.
    /// Prevents directory traversal attacks.
    /// </summary>
    /// <param name="path">The path to validate.</param>
    /// <param name="allowedBasePaths">The allowed base directories.</param>
    /// <returns>The normalized, validated path.</returns>
    /// <exception cref="ArgumentException">Thrown when <paramref name="path"/> is null or empty.</exception>
    /// <exception cref="UnauthorizedAccessException">Thrown when the path is outside allowed directories.</exception>
    public static string ValidatePath(string path, params string[] allowedBasePaths)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);

        var normalizedPath = Path.GetFullPath(path);

        if (allowedBasePaths.Length == 0)
        {
            return normalizedPath;
        }

        foreach (var basePath in allowedBasePaths)
        {
            var normalizedBase = Path.GetFullPath(basePath);
            if (normalizedPath.StartsWith(normalizedBase, StringComparison.OrdinalIgnoreCase))
            {
                return normalizedPath;
            }
        }

        throw new UnauthorizedAccessException(
            $"Path '{path}' is outside allowed directories.");
    }

    /// <summary>
    /// Validates that a file does not exceed the maximum allowed size.
    /// </summary>
    /// <param name="filePath">The path to the file.</param>
    /// <param name="maxSizeBytes">The maximum allowed size in bytes.</param>
    /// <exception cref="FileNotFoundException">Thrown when the file does not exist.</exception>
    /// <exception cref="InvalidOperationException">Thrown when the file exceeds the maximum size.</exception>
    public static void ValidateFileSize(string filePath, long maxSizeBytes = DefaultMaxFileSize)
    {
        var fileInfo = new FileInfo(filePath);
        if (!fileInfo.Exists)
        {
            throw new FileNotFoundException("File not found.", filePath);
        }

        if (fileInfo.Length > maxSizeBytes)
        {
            throw new InvalidOperationException(
                $"File '{filePath}' ({fileInfo.Length:N0} bytes) exceeds maximum allowed size ({maxSizeBytes:N0} bytes).");
        }
    }

    /// <summary>
    /// Sanitizes a query string for FTS5 full-text search to prevent injection.
    /// </summary>
    /// <param name="query">The query to sanitize.</param>
    /// <returns>The sanitized query.</returns>
    public static string SanitizeFts5Query(string query)
    {
        if (string.IsNullOrWhiteSpace(query))
        {
            return string.Empty;
        }

        // Escape FTS5 special characters
        return query
            .Replace("\"", "\"\"")  // Escape quotes
            .Replace("*", "")       // Remove wildcards
            .Replace(":", " ")      // Remove column specifiers
            .Replace("(", " ")      // Remove grouping
            .Replace(")", " ")
            .Replace("AND", "and")  // Lowercase operators
            .Replace("OR", "or")
            .Replace("NOT", "not");
    }

    /// <summary>
    /// Sanitizes a string for use in LIKE patterns to prevent SQL injection.
    /// </summary>
    /// <param name="pattern">The pattern to sanitize.</param>
    /// <returns>The sanitized pattern with wildcards escaped.</returns>
    public static string SanitizeLikePattern(string pattern)
    {
        if (string.IsNullOrWhiteSpace(pattern))
        {
            return string.Empty;
        }

        return pattern
            .Replace("[", "[[]")
            .Replace("%", "[%]")
            .Replace("_", "[_]");
    }

    /// <summary>
    /// Truncates text to a maximum length, preserving word boundaries.
    /// </summary>
    /// <param name="text">The text to truncate.</param>
    /// <param name="maxLength">The maximum length.</param>
    /// <param name="suffix">The suffix to append when truncated.</param>
    /// <returns>The truncated text.</returns>
    public static string TruncateText(string text, int maxLength = DefaultMaxTextLength, string suffix = "...")
    {
        if (string.IsNullOrEmpty(text) || text.Length <= maxLength)
        {
            return text ?? string.Empty;
        }

        var truncated = text[..(maxLength - suffix.Length)];
        var lastSpace = truncated.LastIndexOf(' ');

        if (lastSpace > maxLength / 2)
        {
            truncated = truncated[..lastSpace];
        }

        return truncated + suffix;
    }

    /// <summary>
    /// Normalizes whitespace in text (collapses multiple spaces, trims).
    /// </summary>
    /// <param name="text">The text to normalize.</param>
    /// <returns>The normalized text.</returns>
    public static string NormalizeWhitespace(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return string.Empty;
        }

        return string.Join(' ', text.Split(default(char[]), StringSplitOptions.RemoveEmptyEntries));
    }
}
