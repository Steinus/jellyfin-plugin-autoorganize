using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.AutoOrganize.Core;

/// <summary>
/// Resolves localized TV episode titles from TMDB. Reuses an exact TMDB provider
/// identifier from Jellyfin when available, otherwise searches by series title.
/// </summary>
public sealed class TmdbEpisodeResolver
{
    private static readonly HttpClient Client = new() { Timeout = TimeSpan.FromSeconds(15) };
    private readonly ConcurrentDictionary<string, int> _seriesIds = new(StringComparer.OrdinalIgnoreCase);
    private readonly ILogger _logger;

    public TmdbEpisodeResolver(ILogger logger) => _logger = logger;

    public async Task<EpisodeInfo> ResolveAsync(EpisodeInfo info, string apiKey, string locale, CancellationToken token)
    {
        if (string.IsNullOrWhiteSpace(apiKey))
        {
            return info with { TmdbLookupMessage = "TMDB: API-ключ не указан." };
        }

        try
        {
            var requestedLocale = string.IsNullOrWhiteSpace(locale) ? "ru-RU" : locale.Trim();
            var id = info.TmdbSeriesId ?? await FindSeriesIdAsync(info, apiKey, requestedLocale, token).ConfigureAwait(false);
            if (!id.HasValue)
            {
                return info with
                {
                    TmdbLookupMessage = "TMDB: не удалось однозначно определить сериал «" + info.Series +
                        "». Проверьте название и год сериала в Jellyfin."
                };
            }

            foreach (var language in new[] { requestedLocale, "en-US" }.Distinct(StringComparer.OrdinalIgnoreCase))
            {
                var url = "https://api.themoviedb.org/3/tv/" + id.Value.ToString(CultureInfo.InvariantCulture)
                    + "/season/" + info.Season.ToString(CultureInfo.InvariantCulture)
                    + "/episode/" + info.Episode.ToString(CultureInfo.InvariantCulture)
                    + "?language=" + Uri.EscapeDataString(language);
                using var episode = await FetchAsync(url, apiKey, token).ConfigureAwait(false);
                var name = GetString(episode.RootElement, "name");
                if (!HasMeaningfulTitle(name))
                {
                    continue;
                }

                _logger.LogInformation(
                    "Auto Organize TMDB: {Series} S{Season:00}E{Episode:00} matched TMDB {Id} ({Language}): {Title}",
                    info.Series, info.Season, info.Episode, id.Value, language, name);
                return info with { EpisodeTitle = name!.Trim(), TmdbLookupMessage = null };
            }

            return info with
            {
                TmdbLookupMessage = "TMDB: у «" + info.Series + "» S" +
                    info.Season.ToString("00", CultureInfo.InvariantCulture) + "E" +
                    info.Episode.ToString("00", CultureInfo.InvariantCulture) +
                    " пока нет названия на выбранном или английском языке."
            };
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested)
        {
            throw;
        }
        catch (HttpRequestException ex)
        {
            // Never log HttpRequestException.Message: it can include the URL and a v3 API key.
            var status = ex.StatusCode;
            var details = status switch
            {
                HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden => "TMDB: неверный API-ключ или недостаточно прав (HTTP " + (int)status.Value + ").",
                HttpStatusCode.NotFound => "TMDB: сезон или серия отсутствует в базе (HTTP 404).",
                _ => "TMDB: HTTP-ошибка " + (status.HasValue ? ((int)status.Value).ToString(CultureInfo.InvariantCulture) : "сети") + "."
            };
            _logger.LogWarning("Auto Organize TMDB lookup failed for {Series} S{Season}E{Episode}: {Status}",
                info.Series, info.Season, info.Episode, status);
            return info with { TmdbLookupMessage = details };
        }
        catch (OperationCanceledException)
        {
            return info with { TmdbLookupMessage = "TMDB: превышено время ожидания ответа (15 секунд)." };
        }
        catch (Exception ex)
        {
            _logger.LogWarning("Auto Organize TMDB lookup failed for {Series} S{Season}E{Episode}: {ErrorType}",
                info.Series, info.Season, info.Episode, ex.GetType().Name);
            return info with { TmdbLookupMessage = "TMDB: ошибка обработки ответа (" + ex.GetType().Name + ")." };
        }
    }

    private async Task<int?> FindSeriesIdAsync(EpisodeInfo info, string apiKey, string language, CancellationToken token)
    {
        var cacheKey = info.Series + "|" + info.OriginalSeriesTitle + "|" + info.SeriesYear + "|" + language;
        if (_seriesIds.TryGetValue(cacheKey, out var cachedId))
        {
            return cachedId;
        }

        var queries = new[] { info.Series, info.OriginalSeriesTitle }
            .Where(s => !string.IsNullOrWhiteSpace(s))
            .Select(s => s!)
            .Distinct(StringComparer.OrdinalIgnoreCase);
        var exactIds = new HashSet<int>();
        var uniqueCandidates = new HashSet<int>();

        foreach (var query in queries)
        {
            var url = "https://api.themoviedb.org/3/search/tv?query=" + Uri.EscapeDataString(query)
                + "&language=" + Uri.EscapeDataString(language)
                + (info.SeriesYear.HasValue
                    ? "&first_air_date_year=" + info.SeriesYear.Value.ToString(CultureInfo.InvariantCulture)
                    : string.Empty);
            using var search = await FetchAsync(url, apiKey, token).ConfigureAwait(false);
            if (!search.RootElement.TryGetProperty("results", out var results) ||
                results.ValueKind != JsonValueKind.Array)
            {
                continue;
            }

            var candidates = results.EnumerateArray()
                .Where(item => item.TryGetProperty("id", out _) && YearMatches(item, info.SeriesYear))
                .ToList();
            // TMDB searches alternative names too. A single result with the correct
            // production year is a reasonable fallback for differently localized titles.
            if (candidates.Count == 1 && info.SeriesYear.HasValue)
            {
                uniqueCandidates.Add(candidates[0].GetProperty("id").GetInt32());
            }

            foreach (var item in candidates)
            {
                var title = GetString(item, "name");
                var original = GetString(item, "original_name");
                if (SameName(title, query) || SameName(original, query) ||
                    SameName(title, info.Series) || SameName(original, info.Series))
                {
                    exactIds.Add(item.GetProperty("id").GetInt32());
                }
            }
        }

        // Only choose an unambiguous candidate. Never select the first search hit.
        int? selected = exactIds.Count == 1 ? exactIds.Single() :
            exactIds.Count == 0 && uniqueCandidates.Count == 1 ? uniqueCandidates.Single() : null;
        if (selected.HasValue)
        {
            _seriesIds.TryAdd(cacheKey, selected.Value);
            _logger.LogInformation("Auto Organize TMDB: matched series {Series} to TV ID {Id}", info.Series, selected.Value);
        }
        return selected;
    }

    private static bool YearMatches(JsonElement item, int? requestedYear)
    {
        if (!requestedYear.HasValue) return true;
        var firstAirDate = GetString(item, "first_air_date");
        return firstAirDate?.StartsWith(requestedYear.Value.ToString(CultureInfo.InvariantCulture), StringComparison.Ordinal) == true;
    }

    private static string? GetString(JsonElement obj, string name)
    {
        return obj.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString() : null;
    }

    private static bool SameName(string? first, string? second)
    {
        if (string.IsNullOrWhiteSpace(first) || string.IsNullOrWhiteSpace(second)) return false;
        static string Normalize(string value) => new(value.Where(char.IsLetterOrDigit).Select(char.ToUpperInvariant).ToArray());
        return string.Equals(Normalize(first), Normalize(second), StringComparison.Ordinal);
    }

    private static bool HasMeaningfulTitle(string? title)
    {
        return !string.IsNullOrWhiteSpace(title) &&
            !Regex.IsMatch(title.Trim(), @"^(?:Episode|Эпизод|Серия)\s*#?\s*\d+$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
    }

    private static async Task<JsonDocument> FetchAsync(string url, string apiKey, CancellationToken token)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        // TMDB v4 Read Access Tokens are JWT-like; v3 API keys are simple opaque strings.
        if (apiKey.Contains('.', StringComparison.Ordinal))
        {
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiKey.Trim());
        }
        else
        {
            request.RequestUri = new Uri(url + (url.Contains('?', StringComparison.Ordinal) ? "&" : "?")
                + "api_key=" + Uri.EscapeDataString(apiKey.Trim()));
        }

        using var response = await Client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, token).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
        await using var stream = await response.Content.ReadAsStreamAsync(token).ConfigureAwait(false);
        return await JsonDocument.ParseAsync(stream, cancellationToken: token).ConfigureAwait(false);
    }
}
