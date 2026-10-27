using System;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using MediaBrowser.Controller.Entities.Movies;
using MediaBrowser.Controller.Entities.TV;
using MediaBrowser.Controller.Providers;
using Microsoft.Extensions.Logging;
using JellyfinMovieInfo = MediaBrowser.Controller.Providers.MovieInfo;
using JellyfinSeriesInfo = MediaBrowser.Controller.Providers.SeriesInfo;

namespace Jellyfin.Plugin.AutoOrganize.Core;

/// <summary>
/// Resolves localized titles through the metadata providers already configured in Jellyfin.
/// </summary>
public sealed class MetadataResolver
{
    private readonly IProviderManager _providerManager;
    private readonly ILogger _logger;

    public MetadataResolver(IProviderManager providerManager, ILogger logger)
    {
        _providerManager = providerManager;
        _logger = logger;
    }

    public async Task<MovieInfo> ResolveMovieAsync(MovieInfo parsed, string locale, CancellationToken cancellationToken)
    {
        var (language, country) = ParseLocale(locale);
        var query = new RemoteSearchQuery<JellyfinMovieInfo>
        {
            SearchInfo = new JellyfinMovieInfo
            {
                Name = parsed.Title,
                Year = parsed.Year,
                MetadataLanguage = language,
                MetadataCountryCode = country,
                IsAutomated = true
            },
            IncludeDisabledProviders = false
        };

        try
        {
            var results = (await _providerManager.GetRemoteSearchResults<Movie, JellyfinMovieInfo>(query, cancellationToken)
                .ConfigureAwait(false)).ToList();
            var best = parsed.Year.HasValue
                ? results.FirstOrDefault(result => result.ProductionYear == parsed.Year) ?? results.FirstOrDefault()
                : results.FirstOrDefault();

            if (best?.Name is null)
            {
                return parsed;
            }

            _logger.LogInformation(
                "Auto Organize metadata match: {Input} -> {Title} ({Year}) via {Provider}",
                parsed.Title,
                best.Name,
                best.ProductionYear,
                best.SearchProviderName);

            return parsed with { Title = best.Name, Year = best.ProductionYear ?? parsed.Year };
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Auto Organize metadata lookup failed for movie {Title}; using parsed filename", parsed.Title);
            return parsed;
        }
    }

    public async Task<EpisodeInfo> ResolveSeriesAsync(EpisodeInfo parsed, string locale, CancellationToken cancellationToken)
    {
        var (language, country) = ParseLocale(locale);
        var query = new RemoteSearchQuery<JellyfinSeriesInfo>
        {
            SearchInfo = new JellyfinSeriesInfo
            {
                Name = parsed.Series,
                MetadataLanguage = language,
                MetadataCountryCode = country,
                IsAutomated = true
            },
            IncludeDisabledProviders = false
        };

        try
        {
            var best = (await _providerManager.GetRemoteSearchResults<Series, JellyfinSeriesInfo>(query, cancellationToken)
                .ConfigureAwait(false)).FirstOrDefault();
            if (best?.Name is null)
            {
                return parsed;
            }

            _logger.LogInformation(
                "Auto Organize metadata match: {Input} -> {Title} via {Provider}",
                parsed.Series,
                best.Name,
                best.SearchProviderName);

            int? tmdbSeriesId = parsed.TmdbSeriesId;
            if (best.ProviderIds is not null &&
                best.ProviderIds.TryGetValue("Tmdb", out var rawId) &&
                int.TryParse(rawId, NumberStyles.None, CultureInfo.InvariantCulture, out var parsedId))
            {
                tmdbSeriesId = parsedId;
            }

            return parsed with
            {
                Series = best.Name,
                SeriesYear = best.ProductionYear ?? parsed.SeriesYear,
                TmdbSeriesId = tmdbSeriesId
            };
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Auto Organize metadata lookup failed for series {Title}; using parsed filename", parsed.Series);
            return parsed;
        }
    }

    private static (string Language, string? Country) ParseLocale(string locale)
    {
        if (string.IsNullOrWhiteSpace(locale))
        {
            return ("en", null);
        }

        try
        {
            var culture = CultureInfo.GetCultureInfo(locale);
            string? country = null;
            if (!culture.IsNeutralCulture)
            {
                country = new RegionInfo(culture.Name).TwoLetterISORegionName;
            }

            return (culture.TwoLetterISOLanguageName, country);
        }
        catch (CultureNotFoundException)
        {
            return (locale, null);
        }
    }
}
