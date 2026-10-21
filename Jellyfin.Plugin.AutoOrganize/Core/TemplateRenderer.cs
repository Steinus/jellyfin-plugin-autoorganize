using System;
using System.Globalization;
using System.IO;

namespace Jellyfin.Plugin.AutoOrganize.Core;

/// <summary>
/// Renders the small template language used by Auto Organize.
/// </summary>
public static class TemplateRenderer
{
    public static string Movie(string template, MovieInfo info, string extension, string colonReplacement)
    {
        var title = FilenameSanitizer.Sanitize(info.Title, colonReplacement);
        var year = info.Year?.ToString(CultureInfo.InvariantCulture) ?? string.Empty;
        var result = template
            .Replace("{title}", title, StringComparison.OrdinalIgnoreCase)
            .Replace("{year}", year, StringComparison.OrdinalIgnoreCase)
            .Replace("{ext}", extension, StringComparison.OrdinalIgnoreCase);
        return CleanupOptionalYear(result, info.Year.HasValue);
    }

    public static string Episode(string template, EpisodeInfo info, string extension, string colonReplacement)
    {
        var series = FilenameSanitizer.Sanitize(info.Series, colonReplacement);
        return template
            .Replace("{series}", series, StringComparison.OrdinalIgnoreCase)
            .Replace("{season:00}", info.Season.ToString("00", CultureInfo.InvariantCulture), StringComparison.OrdinalIgnoreCase)
            .Replace("{episode:00}", info.Episode.ToString("00", CultureInfo.InvariantCulture), StringComparison.OrdinalIgnoreCase)
            .Replace("{season}", info.Season.ToString(CultureInfo.InvariantCulture), StringComparison.OrdinalIgnoreCase)
            .Replace("{episode}", info.Episode.ToString(CultureInfo.InvariantCulture), StringComparison.OrdinalIgnoreCase)
            .Replace("{ext}", extension, StringComparison.OrdinalIgnoreCase);
    }

    private static string CleanupOptionalYear(string value, bool hasYear)
    {
        if (hasYear)
        {
            return value;
        }

        return value.Replace(" ()", string.Empty, StringComparison.Ordinal)
            .Replace("()", string.Empty, StringComparison.Ordinal)
            .Trim();
    }
}
