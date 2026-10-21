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

    public OrganizeService(ILogger logger, IProviderManager providerManager)
    {
        _logger = logger;
        _metadataResolver = new MetadataResolver(providerManager, logger);
    }

    public int CountCandidates(PluginConfiguration config)
    {
        return EnumerateVideoFiles(config.MoviesSource).Count() + EnumerateVideoFiles(config.SeriesSource).Count();
    }

    public async Task ProcessMoviesAsync(PluginConfiguration config, Action itemCompleted, CancellationToken cancellationToken)
    {
        if (!IsPairConfigured(config.MoviesSource, config.MoviesDestination))
        {
            return;
        }

        ValidatePaths(config.MoviesSource, config.MoviesDestination);
        foreach (var source in EnumerateVideoFiles(config.MoviesSource))
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                if (!IsStable(source, config.MinimumFileAgeMinutes))
                {
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
                Execute(source, destination, config);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Auto Organize failed for movie {Source}", source);
            }
            finally
            {
                itemCompleted();
            }
        }
    }

    public async Task ProcessSeriesAsync(PluginConfiguration config, Action itemCompleted, CancellationToken cancellationToken)
    {
        if (!IsPairConfigured(config.SeriesSource, config.SeriesDestination))
        {
            return;
        }

        ValidatePaths(config.SeriesSource, config.SeriesDestination);
        foreach (var source in EnumerateVideoFiles(config.SeriesSource))
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                if (!IsStable(source, config.MinimumFileAgeMinutes))
                {
                    continue;
                }

                var info = MediaFileParser.ParseEpisode(source);
                if (info is null)
                {
                    _logger.LogWarning("Auto Organize could not parse an episode number from {Source}; skipping", source);
                    continue;
                }

                if (config.UseJellyfinMetadataProviders)
                {
                    info = await _metadataResolver.ResolveSeriesAsync(info, config.Language, cancellationToken).ConfigureAwait(false);
                }

                var ext = Path.GetExtension(source);
                var seriesFolder = FilenameSanitizer.Sanitize(
                    TemplateRenderer.Episode(config.SeriesFolderTemplate, info, string.Empty, config.ReplaceColonWith),
                    config.ReplaceColonWith);
                var seasonFolder = FilenameSanitizer.Sanitize(
                    TemplateRenderer.Episode(config.SeasonFolderTemplate, info, string.Empty, config.ReplaceColonWith),
                    config.ReplaceColonWith);
                var fileName = FilenameSanitizer.Sanitize(
                    TemplateRenderer.Episode(config.EpisodeFileTemplate, info, ext, config.ReplaceColonWith),
                    config.ReplaceColonWith);
                var destination = Path.Combine(config.SeriesDestination, seriesFolder, seasonFolder, fileName);
                Execute(source, destination, config);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Auto Organize failed for episode {Source}", source);
            }
            finally
            {
                itemCompleted();
            }
        }
    }

    private void Execute(string source, string destination, PluginConfiguration config)
    {
        if (File.Exists(destination))
        {
            _logger.LogWarning("Auto Organize destination already exists; skipping: {Destination}", destination);
            return;
        }

        _logger.LogInformation("Auto Organize: {Operation} {Source} -> {Destination}{DryRun}",
            config.Operation,
            source,
            destination,
            config.DryRun ? " [DRY RUN]" : string.Empty);

        if (config.DryRun)
        {
            return;
        }

        var directory = Path.GetDirectoryName(destination)
            ?? throw new InvalidOperationException("Destination directory could not be determined.");
        Directory.CreateDirectory(directory);

        switch (config.Operation)
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
                throw new InvalidOperationException($"Unsupported file operation: {config.Operation}");
        }

        if (config.MoveAssociatedFiles)
        {
            ProcessAssociatedFiles(source, destination, config);
        }

        if (config.DeleteEmptyFolders && config.Operation == FileOperation.Move)
        {
            DeleteEmptyParents(Path.GetDirectoryName(source), config.MoviesSource, config.SeriesSource);
        }
    }

    private void ProcessAssociatedFiles(string originalSource, string videoDestination, PluginConfiguration config)
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

            switch (config.Operation)
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
