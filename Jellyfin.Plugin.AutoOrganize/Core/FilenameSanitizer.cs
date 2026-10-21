using System;
using System.Collections.Generic;
using System.Text;
using System.Text.RegularExpressions;

namespace Jellyfin.Plugin.AutoOrganize.Core;

/// <summary>
/// Produces path segments that are safe to expose through Windows/SMB.
/// </summary>
public static partial class FilenameSanitizer
{
    private static readonly HashSet<string> ReservedNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "CON", "PRN", "AUX", "NUL",
        "COM1", "COM2", "COM3", "COM4", "COM5", "COM6", "COM7", "COM8", "COM9",
        "LPT1", "LPT2", "LPT3", "LPT4", "LPT5", "LPT6", "LPT7", "LPT8", "LPT9"
    };

    /// <summary>
    /// Sanitizes a single directory or file-name segment.
    /// </summary>
    public static string Sanitize(string value, string colonReplacement)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return "_";
        }

        var builder = new StringBuilder(value.Length);
        foreach (var ch in value)
        {
            switch (ch)
            {
                case ':':
                    builder.Append(colonReplacement);
                    break;
                case '<':
                case '>':
                case '"':
                case '/':
                case '\\':
                case '|':
                case '?':
                case '*':
                    builder.Append(' ');
                    break;
                default:
                    builder.Append(char.IsControl(ch) ? ' ' : ch);
                    break;
            }
        }

        var result = WhitespaceRegex().Replace(builder.ToString(), " ").Trim().TrimEnd('.', ' ');
        if (result.Length == 0)
        {
            result = "_";
        }

        var stem = result.Split('.')[0];
        if (ReservedNames.Contains(stem))
        {
            result += "_";
        }

        return result;
    }

    [GeneratedRegex(@"\s+")]
    private static partial Regex WhitespaceRegex();
}
