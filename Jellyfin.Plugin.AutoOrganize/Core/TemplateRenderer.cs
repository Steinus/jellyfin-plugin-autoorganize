using System;
using System.Globalization;
using System.IO;
using System.Text.RegularExpressions;

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
        var result = ExpandTokens(template, new System.Collections.Generic.Dictionary<string, string>
        {
            ["%mn"] = title, ["%m.n"] = title.Replace(' ', '.'), ["%m_n"] = title.Replace(' ', '_'),
            ["%my"] = year, ["%ext"] = extension.TrimStart('.'), ["%fn"] = title
        });
        result = result
            .Replace("{title}", title, StringComparison.OrdinalIgnoreCase)
            .Replace("{year}", year, StringComparison.OrdinalIgnoreCase)
            .Replace("{ext}", extension, StringComparison.OrdinalIgnoreCase);
        return CleanupOptionalYear(result, info.Year.HasValue);
    }

    public static string Episode(string template, EpisodeInfo info, string extension, string colonReplacement)
    {
        var series = FilenameSanitizer.Sanitize(info.Series, colonReplacement);
        var year = Regex.Match(info.Series, @"(?:19|20)\d{2}").Value;
        var result = ExpandTokens(template, new System.Collections.Generic.Dictionary<string, string>
        {
            ["%sn"] = series, ["%s.n"] = series.Replace(' ', '.'), ["%s_n"] = series.Replace(' ', '_'),
            ["%sy"] = year, ["%s"] = info.Season.ToString(CultureInfo.InvariantCulture),
            ["%0s"] = info.Season.ToString("00", CultureInfo.InvariantCulture),
            ["%e"] = info.Episode.ToString(CultureInfo.InvariantCulture),
            ["%0e"] = info.Episode.ToString("00", CultureInfo.InvariantCulture),
            ["%ed"] = info.Episode.ToString(CultureInfo.InvariantCulture),
            ["%0ed"] = info.Episode.ToString("00", CultureInfo.InvariantCulture),
            ["%en"] = string.Empty, ["%e.n"] = string.Empty, ["%e_n"] = string.Empty,
            ["%ext"] = extension.TrimStart('.'), ["%fn"] = series
        });
        return result
            .Replace("{series}", series, StringComparison.OrdinalIgnoreCase)
            .Replace("{season:00}", info.Season.ToString("00", CultureInfo.InvariantCulture), StringComparison.OrdinalIgnoreCase)
            .Replace("{episode:00}", info.Episode.ToString("00", CultureInfo.InvariantCulture), StringComparison.OrdinalIgnoreCase)
            .Replace("{season}", info.Season.ToString(CultureInfo.InvariantCulture), StringComparison.OrdinalIgnoreCase)
            .Replace("{episode}", info.Episode.ToString(CultureInfo.InvariantCulture), StringComparison.OrdinalIgnoreCase)
            .Replace("{ext}", extension, StringComparison.OrdinalIgnoreCase);
    }

    private static string ExpandTokens(string template, System.Collections.Generic.Dictionary<string, string> values)
    {
        return Regex.Replace(template, @"%0ed|%0e|%0s|%m\.n|%m_n|%mn|%my|%s\.n|%s_n|%sn|%sy|%e\.n|%e_n|%en|%ed|%ext|%fn|%s|%e",
            match => values.TryGetValue(match.Value, out var value) ? value : match.Value);
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
