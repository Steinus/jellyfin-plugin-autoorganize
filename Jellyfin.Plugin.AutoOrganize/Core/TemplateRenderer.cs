using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;

namespace Jellyfin.Plugin.AutoOrganize.Core;

/// <summary>
/// Replaces Auto Organize placeholders, including the common FileBot aliases.
/// This is not a full interpreter for FileBot's Groovy format expressions.
/// </summary>
public static class TemplateRenderer
{
    public static string Movie(string template, MovieInfo info, string extension, string colonReplacement)
    {
        var title = FilenameSanitizer.Sanitize(info.Title, colonReplacement);
        var year = info.Year?.ToString(CultureInfo.InvariantCulture) ?? string.Empty;
        var values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["%mn"] = title,
            ["%m.n"] = title.Replace(' ', '.'),
            ["%m_n"] = title.Replace(' ', '_'),
            ["%my"] = year,
            ["%ext"] = extension.TrimStart('.'),
            ["%fn"] = info.SourceStem ?? title,
            ["{title}"] = title,
            ["{year}"] = year,
            ["{ext}"] = extension,
            ["{n}"] = title,
            ["{y}"] = year
        };
        return CleanupOptionalYear(ReplaceTokens(template, values), info.Year.HasValue).Trim();
    }

    public static string Episode(
        string template,
        EpisodeInfo info,
        string extension,
        string colonReplacement,
        bool sourceDirectoryPattern = false)
    {
        var series = FilenameSanitizer.Sanitize(info.Series, colonReplacement);
        // Do not sanitize a missing optional title: the sanitizer uses "_" as an empty-segment fallback.
        var hasTitle = !string.IsNullOrWhiteSpace(info.EpisodeTitle);
        var title = hasTitle ? FilenameSanitizer.Sanitize(info.EpisodeTitle!, colonReplacement) : string.Empty;
        var year = info.SeriesYear?.ToString(CultureInfo.InvariantCulture) ?? string.Empty;
        var season = info.Season.ToString(CultureInfo.InvariantCulture);
        var paddedSeason = info.Season.ToString("00", CultureInfo.InvariantCulture);
        var episode = info.Episode.ToString(CultureInfo.InvariantCulture);
        var paddedEpisode = info.Episode.ToString("00", CultureInfo.InvariantCulture);
        var endingEpisode = info.EndingEpisode?.ToString(CultureInfo.InvariantCulture) ?? string.Empty;
        var endingEpisodePadded = info.EndingEpisode?.ToString("00", CultureInfo.InvariantCulture) ?? string.Empty;
        var originalName = sourceDirectoryPattern
            ? info.SourceDirectory ?? series
            : info.SourceStem ?? series;

        var values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["%sn"] = series,
            ["%s.n"] = series.Replace(' ', '.'),
            ["%s_n"] = series.Replace(' ', '_'),
            ["%sy"] = year,
            ["%s"] = season,
            ["%0s"] = paddedSeason,
            ["%e"] = episode,
            ["%0e"] = paddedEpisode,
            ["%ed"] = endingEpisode,
            ["%0ed"] = endingEpisodePadded,
            ["%en"] = title,
            ["%e.n"] = title.Replace(' ', '.'),
            ["%e_n"] = title.Replace(' ', '_'),
            ["%ext"] = extension.TrimStart('.'),
            ["%fn"] = originalName,
            ["{series}"] = series,
            ["{season}"] = season,
            ["{season:00}"] = paddedSeason,
            ["{episode}"] = episode,
            ["{episode:00}"] = paddedEpisode,
            ["{endingEpisode:00}"] = endingEpisodePadded,
            ["{episodeTitle}"] = title,
            ["{ext}"] = extension,
            // Common FileBot aliases: {n} ({y}) / {'Season 0'+s} / {s00e00} - {t}
            ["{n}"] = series,
            ["{y}"] = year,
            ["{s}"] = season,
            ["{e}"] = episode,
            ["{s00e00}"] = "S" + paddedSeason + "E" + paddedEpisode,
            ["{t}"] = title,
            ["{'Season 0'+s}"] = "Season 0" + season
        };

        var result = ReplaceTokens(template, values);
        if (!hasTitle)
        {
            // A missing episode title must never produce " - _.mkv" or " - .mkv".
            result = Regex.Replace(result, @"\s*[-–—]\s*(?=\.[^./\\]+$)", string.Empty);
            result = Regex.Replace(result, @"\s*[-–—]\s*$", string.Empty);
        }

        return CleanupOptionalYear(result, info.SeriesYear.HasValue).Trim();
    }

    private static string ReplaceTokens(string template, Dictionary<string, string> values)
    {
        var result = template ?? string.Empty;
        // Longest first: replacing %e must not corrupt %en, %ed, or %0ed.
        foreach (var item in values.OrderByDescending(pair => pair.Key.Length))
        {
            result = result.Replace(item.Key, item.Value, StringComparison.OrdinalIgnoreCase);
        }

        return result;
    }

    private static string CleanupOptionalYear(string value, bool hasYear)
    {
        if (hasYear) return value;
        return value.Replace(" ()", string.Empty, StringComparison.Ordinal)
            .Replace("()", string.Empty, StringComparison.Ordinal)
            .Trim();
    }
}
