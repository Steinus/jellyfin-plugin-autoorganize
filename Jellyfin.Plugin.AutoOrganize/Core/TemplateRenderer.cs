using System;
using System.Globalization;
using System.IO;
using System.Text.RegularExpressions;
using System.Collections.Generic;

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
        var result = Expand(template, new Dictionary<string, string>
        {
            ["%mn"] = title, ["%m.n"] = title.Replace(' ', '.'), ["%m_n"] = title.Replace(' ', '_'),
            ["%my"] = year, ["%ext"] = extension.TrimStart('.'), ["%fn"] = info.SourceStem ?? title
        });
        result = result
            .Replace("{title}", title, StringComparison.OrdinalIgnoreCase)
            .Replace("{year}", year, StringComparison.OrdinalIgnoreCase)
            .Replace("{ext}", extension, StringComparison.OrdinalIgnoreCase);
        return CleanupOptionalYear(result, info.Year.HasValue);
    }

    public static string Episode(string template, EpisodeInfo info, string extension, string colonReplacement, bool sourceDirectoryPattern = false)
    {
        var series = FilenameSanitizer.Sanitize(info.Series, colonReplacement);
        var name = FilenameSanitizer.Sanitize(info.EpisodeTitle ?? string.Empty, colonReplacement);
        var result = Expand(template, new Dictionary<string, string>
        {
            ["%sn"] = series, ["%s.n"] = series.Replace(' ', '.'), ["%s_n"] = series.Replace(' ', '_'),
            ["%sy"] = info.SeriesYear?.ToString(CultureInfo.InvariantCulture) ?? string.Empty,
            ["%s"] = info.Season.ToString(CultureInfo.InvariantCulture), ["%0s"] = info.Season.ToString("00", CultureInfo.InvariantCulture),
            ["%e"] = info.Episode.ToString(CultureInfo.InvariantCulture), ["%0e"] = info.Episode.ToString("00", CultureInfo.InvariantCulture),
            ["%ed"] = info.EndingEpisode?.ToString(CultureInfo.InvariantCulture) ?? string.Empty,
            ["%0ed"] = info.EndingEpisode?.ToString("00", CultureInfo.InvariantCulture) ?? string.Empty,
            ["%en"] = name, ["%e.n"] = name.Replace(' ', '.'), ["%e_n"] = name.Replace(' ', '_'),
            ["%ext"] = extension.TrimStart('.'), ["%fn"] = sourceDirectoryPattern ? (info.SourceDirectory ?? series) : (info.SourceStem ?? series)
        });
        result = result
            .Replace("{episodeTitle}", name, StringComparison.OrdinalIgnoreCase)
            .Replace("{endingEpisode:00}", info.EndingEpisode?.ToString("00", CultureInfo.InvariantCulture) ?? string.Empty, StringComparison.OrdinalIgnoreCase)
            .Replace("{series}", series, StringComparison.OrdinalIgnoreCase)
            .Replace("{season:00}", info.Season.ToString("00", CultureInfo.InvariantCulture), StringComparison.OrdinalIgnoreCase)
            .Replace("{episode:00}", info.Episode.ToString("00", CultureInfo.InvariantCulture), StringComparison.OrdinalIgnoreCase)
            .Replace("{season}", info.Season.ToString(CultureInfo.InvariantCulture), StringComparison.OrdinalIgnoreCase)
            .Replace("{episode}", info.Episode.ToString(CultureInfo.InvariantCulture), StringComparison.OrdinalIgnoreCase)
            .Replace("{ext}", extension, StringComparison.OrdinalIgnoreCase);
        // When TMDB has no reliable title, avoid a dangling " - " before .mkv.
        if (string.IsNullOrWhiteSpace(info.EpisodeTitle))
        {
            result = Regex.Replace(result, @"\s+-\s+(?=\.)", string.Empty);
            result = Regex.Replace(result, @"\s+-\s*$", string.Empty);
        }

        return CleanupOptionalYear(result, info.SeriesYear.HasValue);
    }

    private static string Expand(string template, Dictionary<string, string> values)
    {
        return Regex.Replace(template, @"%0ed|%0e|%0s|%m\.n|%m_n|%mn|%my|%s\.n|%s_n|%sn|%sy|%e\.n|%e_n|%en|%ed|%ext|%fn|%s|%e",
            m => values.TryGetValue(m.Value, out var replacement) ? replacement : m.Value);
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
