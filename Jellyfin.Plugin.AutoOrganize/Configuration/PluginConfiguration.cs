using System;
using System.Collections.Generic;
using MediaBrowser.Model.Plugins;

namespace Jellyfin.Plugin.AutoOrganize.Configuration;

/// <summary>
/// How imported media is written to the destination library.
/// </summary>
public enum FileOperation
{
    /// <summary>Move the source file.</summary>
    Move,

    /// <summary>Copy the source file.</summary>
    Copy,

    /// <summary>Create a hardlink to the source file.</summary>
    Hardlink
}

/// <summary>
/// Persistent activity entry shown on the Auto Organize log page.
/// </summary>
public sealed class ActivityLogEntry
{
    public DateTime TimestampUtc { get; set; }

    public string MediaType { get; set; } = string.Empty;

    public string Status { get; set; } = string.Empty;

    public string Source { get; set; } = string.Empty;

    public string Destination { get; set; } = string.Empty;

    public string Message { get; set; } = string.Empty;
}

/// <summary>
/// Plugin configuration.
/// </summary>
public sealed class PluginConfiguration : BasePluginConfiguration
{
    /// <summary>
    /// Initializes a new instance of the <see cref="PluginConfiguration"/> class.
    /// </summary>
    public PluginConfiguration()
    {
        MoviesEnabled = false;
        SeriesEnabled = false;
        MoviesDryRun = true;
        SeriesDryRun = true;

        MoviesSource = string.Empty;
        MoviesDestination = string.Empty;
        SeriesSource = string.Empty;
        SeriesDestination = string.Empty;

        MoviesOperation = FileOperation.Move;
        SeriesOperation = FileOperation.Move;

        Language = "ru-RU";
        UseJellyfinMetadataProviders = true;
        ReplaceColonWith = " - ";

        MoviesMinimumFileAgeMinutes = 5;
        SeriesMinimumFileAgeMinutes = 5;

        MovieFolderTemplate = "{title} ({year})";
        MovieFileTemplate = "{title} ({year}){ext}";
        SeriesFolderTemplate = "{series}";
        SeasonFolderTemplate = "Season {season:00}";
        EpisodeFileTemplate = "{series} - S{season:00}E{episode:00}{ext}";
        MultiEpisodeFileTemplate = "%sn - S%0sE%0ex%0ed - %en.%ext";

        MoviesMoveAssociatedFiles = true;
        SeriesMoveAssociatedFiles = true;
        MoviesDeleteEmptyFolders = false;
        SeriesDeleteEmptyFolders = false;

        ActivityLog = [];

        // Legacy 0.1.0 values kept for config compatibility.
        Enabled = false;
        DryRun = true;
        Operation = FileOperation.Move;
        MinimumFileAgeMinutes = 5;
        MoveAssociatedFiles = true;
        DeleteEmptyFolders = false;
    }

    public bool MoviesEnabled { get; set; }

    public bool SeriesEnabled { get; set; }

    public bool MoviesDryRun { get; set; }

    public bool SeriesDryRun { get; set; }

    public string MoviesSource { get; set; }

    public string MoviesDestination { get; set; }

    public string SeriesSource { get; set; }

    public string SeriesDestination { get; set; }

    public FileOperation MoviesOperation { get; set; }

    public FileOperation SeriesOperation { get; set; }

    public string Language { get; set; }

    public bool UseJellyfinMetadataProviders { get; set; }

    public string ReplaceColonWith { get; set; }

    public int MoviesMinimumFileAgeMinutes { get; set; }

    public int SeriesMinimumFileAgeMinutes { get; set; }

    public string MovieFolderTemplate { get; set; }

    public string MovieFileTemplate { get; set; }

    public string SeriesFolderTemplate { get; set; }

    public string SeasonFolderTemplate { get; set; }

    public string EpisodeFileTemplate { get; set; }

    public string MultiEpisodeFileTemplate { get; set; }

    public bool MoviesMoveAssociatedFiles { get; set; }

    public bool SeriesMoveAssociatedFiles { get; set; }

    public bool MoviesDeleteEmptyFolders { get; set; }

    public bool SeriesDeleteEmptyFolders { get; set; }

    public List<ActivityLogEntry> ActivityLog { get; set; }

    // Legacy 0.1.0 settings retained so existing XML config can still be loaded.
    public bool Enabled { get; set; }

    public bool DryRun { get; set; }

    public FileOperation Operation { get; set; }

    public int MinimumFileAgeMinutes { get; set; }

    public bool MoveAssociatedFiles { get; set; }

    public bool DeleteEmptyFolders { get; set; }
}
