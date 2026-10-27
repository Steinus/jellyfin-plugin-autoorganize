using System;
using System.IO;
using System.Text.RegularExpressions;

namespace Jellyfin.Plugin.AutoOrganize.Core;

/// <summary>
/// Conservative filename parser used before metadata matching is added.
/// </summary>
public static partial class MediaFileParser
{
    /// <summary>
    /// Attempts to parse a movie title and optional year.
    /// </summary>
    public static MovieInfo ParseMovie(string filePath)
    {
        var name = Path.GetFileNameWithoutExtension(filePath);
        var match = MovieYearRegex().Match(name);
        if (match.Success)
        {
            return new MovieInfo(CleanTitle(match.Groups["title"].Value), int.Parse(match.Groups["year"].Value), name);
        }

        return new MovieInfo(CleanTitle(name), null, name);
    }

    /// <summary>
    /// Attempts to parse a series title, season and episode.
    /// </summary>
    public static EpisodeInfo? ParseEpisode(string filePath)
    {
        var name = Path.GetFileNameWithoutExtension(filePath);
        var match = EpisodeRegex().Match(name);
        if (!match.Success)
        {
            return null;
        }

        var series = CleanTitle(match.Groups["title"].Value);
        int? year = null;
        var yearMatch = Regex.Match(series, @"(?:19|20)\d{2}$");
        if (yearMatch.Success && int.TryParse(yearMatch.Value, out var parsedYear))
        {
            year = parsedYear;
            series = series[..yearMatch.Index].Trim(' ', '-', '.', '_', '(', ')');
        }

        int? endingEpisode = match.Groups["ending"].Success
            ? int.Parse(match.Groups["ending"].Value)
            : null;
        return new EpisodeInfo(
            series,
            int.Parse(match.Groups["season"].Value),
            int.Parse(match.Groups["episode"].Value),
            SeriesYear: year,
            EndingEpisode: endingEpisode,
            SourceStem: name,
            SourceDirectory: Path.GetFileName(Path.GetDirectoryName(filePath)),
            OriginalSeriesTitle: series);
    }

    private static string CleanTitle(string value)
    {
        value = SeparatorRegex().Replace(value, " ");
        return WhitespaceRegex().Replace(value, " ").Trim(' ', '-', '.', '_');
    }

    [GeneratedRegex(@"^(?<title>.+?)[\s._\-(\[]+(?<year>(?:19|20)\d{2})(?:[\s._\-)\]].*)?$", RegexOptions.IgnoreCase)]
    private static partial Regex MovieYearRegex();

    [GeneratedRegex(@"^(?<title>.+?)[\s._-]+S(?<season>\d{1,2})E(?<episode>\d{1,3})(?:[\s._-]*(?:E|x)(?<ending>\d{1,3}))?(?:\b|[\s._-])", RegexOptions.IgnoreCase)]
    private static partial Regex EpisodeRegex();

    [GeneratedRegex(@"[._]+")]
    private static partial Regex SeparatorRegex();

    [GeneratedRegex(@"\s+")]
    private static partial Regex WhitespaceRegex();
}

/// <summary>Parsed movie information.</summary>
public sealed record MovieInfo(string Title, int? Year, string? SourceStem = null);

/// <summary>Parsed episode information.</summary>
public sealed record EpisodeInfo(
    string Series,
    int Season,
    int Episode,
    string? EpisodeTitle = null,
    int? SeriesYear = null,
    int? EndingEpisode = null,
    string? SourceStem = null,
    string? SourceDirectory = null,
    int? TmdbSeriesId = null,
    string? TmdbLookupMessage = null,
    string? OriginalSeriesTitle = null);
