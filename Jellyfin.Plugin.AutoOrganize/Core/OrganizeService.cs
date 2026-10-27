using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Plugin.AutoOrganize.Configuration;
using MediaBrowser.Controller.Providers;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.AutoOrganize.Core;

/// <summary>
/// Scans configured watch folders and applies deterministic file operations.
/// </summary>
public sealed class OrganizeService
{
    private static readonly HashSet<string> VideoExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".mkv", ".mp4", ".avi", ".mov", ".m4v", ".ts", ".m2ts", ".wmv", ".webm"
    };

    private static readonly HashSet<string> AssociatedExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".srt", ".ass", ".ssa", ".sub", ".idx", ".nfo", ".jpg", ".jpeg", ".png", ".webp"
    };

    private readonly ILogger _logger;
    private readonly MetadataResolver _metadataResolver;
    private readonly Plugin _plugin;
    private readonly TmdbEpisodeResolver _tmdb;

    public OrganizeService(ILogger logger, IProviderManager providerManager, Plugin plugin)
    {
        _logger = logger;
        _metadataResolver = new MetadataResolver(providerManager, logger);
        _plugin = plugin;
        _tmdb = new TmdbEpisodeResolver(logger);
    }

    public int CountCandidates(PluginConfiguration config)
    {
        var movies = config.MoviesEnabled ? EnumerateVideoFiles(config.MoviesSource).Count() : 0;
        var series = config.SeriesEnabled ? EnumerateVideoFiles(config.SeriesSource).Count() : 0;
        return movies + series;
    }

    public async Task ProcessMoviesAsync(PluginConfiguration config, Action itemCompleted, CancellationToken cancellationToken)
    {
        if (!config.MoviesEnabled || !IsPairConfigured(config.MoviesSource, config.MoviesDestination))
        {
            return;
        }

        ValidatePaths(config.MoviesSource, config.MoviesDestination);
        foreach (var source in EnumerateVideoFiles(config.MoviesSource))
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                if (!IsStable(source, config.MoviesMinimumFileAgeMinutes))
                {
                    _plugin.AddActivity("Movie", "Skipped", source, null, "Файл ещё слишком новый и может продолжать записываться.");
                    continue;
                }

                var info = MediaFileParser.ParseMovie(source);
                if (config.UseJellyfinMetadataProviders)
                {
                    info = await _metadataResolver.ResolveMovieAsync(info, config.Language, cancellationToken).ConfigureAwait(false);
                }

                var ext = Path.GetExtension(source);
                var folder = FilenameSanitizer.Sanitize(
                    TemplateRenderer.Movie(config.MovieFolderTemplate, info, string.Empty, config.ReplaceColonWith),
                    config.ReplaceColonWith);
                var fileName = FilenameSanitizer.Sanitize(
                    TemplateRenderer.Movie(config.MovieFileTemplate, info, ext, config.ReplaceColonWith),
                    config.ReplaceColonWith);
                var destination = Path.Combine(config.MoviesDestination, folder, fileName);

                Execute(
                    "Movie",
                    source,
                    destination,
                    config.MoviesOperation,
                    config.MoviesDryRun,
                    config.MoviesMoveAssociatedFiles,
                    config.MoviesDeleteEmptyFolders,
                    config.MoviesSource,
                    config.SeriesSource);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Auto Organize failed for movie {Source}", source);
                _plugin.AddActivity("Movie", "Error", source, null, ex.Message);
            }
            finally
            {
                itemCompleted();
            }
        }
    }

    public async Task ProcessSeriesAsync(PluginConfiguration config, Action itemCompleted, CancellationToken cancellationToken)
    {
        if (!config.SeriesEnabled || !IsPairConfigured(config.SeriesSource, config.SeriesDestination))
        {
            return;
        }

        ValidatePaths(config.SeriesSource, config.SeriesDestination);
        foreach (var source in EnumerateVideoFiles(config.SeriesSource))
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                if (!IsStable(source, config.SeriesMinimumFileAgeMinutes))
                {
                    _plugin.AddActivity("TV", "Skipped", source, null, "Файл ещё слишком новый и может продолжать записываться.");
                    continue;
                }

                var info = MediaFileParser.ParseEpisode(source);
                if (info is null)
                {
                    _logger.LogWarning("Auto Organize could not parse an episode number from {Source}; skipping", source);
                    _plugin.AddActivity("TV", "Skipped", source, null, "Не удалось определить номер сезона/серии из имени файла.");
                    continue;
                }

                if (config.UseJellyfinMetadataProviders)
                {
                    info = await _metadataResolver.ResolveSeriesAsync(info, config.Language, cancellationToken).ConfigureAwait(false);
                }

                if (config.UseTmdbEpisodeTitles && !string.IsNullOrWhiteSpace(config.TmdbApiKey))
                {
                    info = await _tmdb.ResolveAsync(info, config.TmdbApiKey, config.Language, cancellationToken).ConfigureAwait(false);
                }

                var ext = Path.GetExtension(source);
                var seriesFolder = FilenameSanitizer.Sanitize(
                    TemplateRenderer.Episode(config.SeriesFolderTemplate, info, string.Empty, config.ReplaceColonWith, sourceDirectoryPattern: true),
                    config.ReplaceColonWith);
                var seasonFolder = FilenameSanitizer.Sanitize(
                    TemplateRenderer.Episode(config.SeasonFolderTemplate, info, string.Empty, config.ReplaceColonWith),
                    config.ReplaceColonWith);
                var fileName = FilenameSanitizer.Sanitize(
                    TemplateRenderer.Episode(info.EndingEpisode.HasValue ? config.MultiEpisodeFileTemplate : config.EpisodeFileTemplate, info, ext, config.ReplaceColonWith),
                    config.ReplaceColonWith);
                var destination = Path.Combine(config.SeriesDestination, seriesFolder, seasonFolder, fileName);

                Execute(
                    "TV",
                    source,
                    destination,
                    config.SeriesOperation,
                    config.SeriesDryRun,
                    config.SeriesMoveAssociatedFiles,
                    config.SeriesDeleteEmptyFolders,
                    config.MoviesSource,
                    config.SeriesSource);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Auto Organize failed for episode {Source}", source);
                _plugin.AddActivity("TV", "Error", source, null, ex.Message);
            }
            finally
            {
                itemCompleted();
            }
        }
    }

    private void Execute(
        string mediaType,
        string source,
        string destination,
        FileOperation operation,
        bool dryRun,
        bool processAssociatedFiles,
        bool deleteEmptyFolders,
        string moviesRoot,
        string seriesRoot)
    {
        if (File.Exists(destination))
        {
            _logger.LogWarning("Auto Organize destination already exists; skipping: {Destination}", destination);
            _plugin.AddActivity(mediaType, "Skipped", source, destination, "Файл назначения уже существует.");
            return;
        }

        _logger.LogInformation(
            "Auto Organize: {Operation} {Source} -> {Destination}{DryRun}",
            operation,
            source,
            destination,
            dryRun ? " [DRY RUN]" : string.Empty);

        if (dryRun)
        {
            _plugin.AddActivity(mediaType, "Preview", source, destination, $"Тестовый режим: {operation}, файлы не изменены.");
            return;
        }

        var directory = Path.GetDirectoryName(destination)
            ?? throw new InvalidOperationException("Destination directory could not be determined.");
        Directory.CreateDirectory(directory);

        switch (operation)
        {
            case FileOperation.Move:
                File.Move(source, destination);
                break;
            case FileOperation.Copy:
                File.Copy(source, destination, overwrite: false);
                break;
            case FileOperation.Hardlink:
                HardLinkHelper.Create(source, destination);
                break;
            default:
                throw new InvalidOperationException($"Unsupported file operation: {operation}");
        }

        if (processAssociatedFiles)
        {
            ProcessAssociatedFiles(source, destination, operation);
        }

        if (deleteEmptyFolders && operation == FileOperation.Move)
        {
            DeleteEmptyParents(Path.GetDirectoryName(source), moviesRoot, seriesRoot);
        }

        _plugin.AddActivity(mediaType, "Success", source, destination, OperationMessage(operation));
    }

    private static string OperationMessage(FileOperation operation)
    {
        return operation switch
        {
            FileOperation.Move => "Файл успешно перемещён.",
            FileOperation.Copy => "Файл успешно скопирован.",
            FileOperation.Hardlink => "Hardlink успешно создан.",
            _ => "Операция выполнена."
        };
    }

    private void ProcessAssociatedFiles(string originalSource, string videoDestination, FileOperation operation)
    {
        var sourceDirectory = Path.GetDirectoryName(originalSource);
        if (sourceDirectory is null || !Directory.Exists(sourceDirectory))
        {
            return;
        }

        var sourceStem = Path.GetFileNameWithoutExtension(originalSource);
        var destinationDirectory = Path.GetDirectoryName(videoDestination)!;
        var destinationStem = Path.GetFileNameWithoutExtension(videoDestination);

        foreach (var sidecar in Directory.EnumerateFiles(sourceDirectory, sourceStem + ".*", SearchOption.TopDirectoryOnly))
        {
            if (string.Equals(sidecar, originalSource, StringComparison.Ordinal))
            {
                continue;
            }

            var extension = Path.GetExtension(sidecar);
            if (!AssociatedExtensions.Contains(extension))
            {
                continue;
            }

            var sidecarName = Path.GetFileName(sidecar);
            var suffix = sidecarName.Length > sourceStem.Length ? sidecarName[sourceStem.Length..] : extension;
            var sidecarDestination = Path.Combine(destinationDirectory, destinationStem + suffix);
            if (File.Exists(sidecarDestination))
            {
                continue;
            }

            switch (operation)
            {
                case FileOperation.Move:
                    File.Move(sidecar, sidecarDestination);
                    break;
                case FileOperation.Copy:
                    File.Copy(sidecar, sidecarDestination, overwrite: false);
                    break;
                case FileOperation.Hardlink:
                    HardLinkHelper.Create(sidecar, sidecarDestination);
                    break;
            }
        }
    }

    private static IEnumerable<string> EnumerateVideoFiles(string root)
    {
        if (string.IsNullOrWhiteSpace(root) || !Directory.Exists(root))
        {
            return Enumerable.Empty<string>();
        }

        return Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories)
            .Where(path => VideoExtensions.Contains(Path.GetExtension(path)));
    }

    private static bool IsStable(string path, int minutes)
    {
        if (minutes <= 0)
        {
            return true;
        }

        return DateTime.UtcNow - File.GetLastWriteTimeUtc(path) >= TimeSpan.FromMinutes(minutes);
    }

    private static bool IsPairConfigured(string source, string destination)
        => !string.IsNullOrWhiteSpace(source) && !string.IsNullOrWhiteSpace(destination);

    private static void ValidatePaths(string source, string destination)
    {
        var src = Path.GetFullPath(source).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        var dst = Path.GetFullPath(destination).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        if (dst.StartsWith(src, StringComparison.Ordinal))
        {
            throw new InvalidOperationException("Destination must not be inside its source watch folder.");
        }
    }

    private static void DeleteEmptyParents(string? directory, params string[] roots)
    {
        while (!string.IsNullOrWhiteSpace(directory) && Directory.Exists(directory))
        {
            var full = Path.GetFullPath(directory).TrimEnd(Path.DirectorySeparatorChar);
            if (roots.Any(root => !string.IsNullOrWhiteSpace(root) && string.Equals(
                    full,
                    Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar),
                    StringComparison.Ordinal)))
            {
                return;
            }

            if (Directory.EnumerateFileSystemEntries(directory).Any())
            {
                return;
            }

            var parent = Directory.GetParent(directory)?.FullName;
            Directory.Delete(directory);
            directory = parent;
        }
    }
}
