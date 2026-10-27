using System;
using System.Collections.Concurrent;
using System.Globalization;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.AutoOrganize.Core;

/// <summary>Looks up verified season and episode names in TMDB, with a per-run cache.</summary>
public sealed class TmdbEpisodeResolver
{
    private static readonly HttpClient Client = new() { Timeout = TimeSpan.FromSeconds(15) };
    private readonly ConcurrentDictionary<string, int> _seriesIds = new();
    private readonly ILogger _logger;

    public TmdbEpisodeResolver(ILogger logger) => _logger = logger;

    public async Task<EpisodeInfo> ResolveAsync(EpisodeInfo info, string apiKey, string locale, CancellationToken token)
    {
        if (string.IsNullOrWhiteSpace(apiKey)) return info;
        try
        {
            var language = Uri.EscapeDataString(string.IsNullOrWhiteSpace(locale) ? "ru-RU" : locale);
            var key = info.Series + "|" + info.SeriesYear + "|" + language;
            if (!_seriesIds.TryGetValue(key, out var id))
            {
                var search = "https://api.themoviedb.org/3/search/tv?query=" + Uri.EscapeDataString(info.Series) + "&language=" + language
                    + (info.SeriesYear.HasValue ? "&first_air_date_year=" + info.SeriesYear.Value.ToString(CultureInfo.InvariantCulture) : "");
                using var document = await FetchAsync(search, apiKey, token).ConfigureAwait(false);
                if (!document.RootElement.TryGetProperty("results", out var results) || results.GetArrayLength() == 0) return info;
                // Do not guess when search is ambiguous: use only a unique match after normalizing titles.
                int? selected = null;
                foreach (var item in results.EnumerateArray())
                {
                    var title = item.TryGetProperty("name", out var name) ? name.GetString() : null;
                    var original = item.TryGetProperty("original_name", out var originalName) ? originalName.GetString() : null;
                    if (!string.Equals(title, info.Series, StringComparison.OrdinalIgnoreCase)
                        && !string.Equals(original, info.Series, StringComparison.OrdinalIgnoreCase)) continue;
                    if (info.SeriesYear.HasValue)
                    {
                        var air = item.TryGetProperty("first_air_date", out var airDate) ? airDate.GetString() : null;
                        if (air is null || !air.StartsWith(info.SeriesYear.Value.ToString(CultureInfo.InvariantCulture), StringComparison.Ordinal)) continue;
                    }
                    if (selected.HasValue) return info;
                    selected = item.GetProperty("id").GetInt32();
                }
                if (!selected.HasValue) return info;
                id = selected.Value;
                _seriesIds.TryAdd(key, id);
            }

            var url = $"https://api.themoviedb.org/3/tv/{id}/season/{info.Season}/episode/{info.Episode}?language={language}";
            using var episode = await FetchAsync(url, apiKey, token).ConfigureAwait(false);
            var titleElement = episode.RootElement.TryGetProperty("name", out var epName) ? epName.GetString() : null;
            if (string.IsNullOrWhiteSpace(titleElement)) return info;
            return info with { EpisodeTitle = titleElement };
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { throw; }
        catch (Exception ex)
        {
            _logger.LogWarning("TMDB episode lookup failed for {Series} S{Season}E{Episode}: {Error}", info.Series, info.Season, info.Episode, ex.GetType().Name);
            return info;
        }
    }

    private static async Task<JsonDocument> FetchAsync(string url, string apiKey, CancellationToken token)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        if (apiKey.Contains('.')) request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiKey.Trim());
        else request.RequestUri = new Uri(url + "&api_key=" + Uri.EscapeDataString(apiKey.Trim()));
        using var response = await Client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, token).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
        await using var stream = await response.Content.ReadAsStreamAsync(token).ConfigureAwait(false);
        return await JsonDocument.ParseAsync(stream, cancellationToken: token).ConfigureAwait(false);
    }
}
