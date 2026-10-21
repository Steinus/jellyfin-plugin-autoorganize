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
/// Plugin configuration.
/// </summary>
public sealed class PluginConfiguration : BasePluginConfiguration
{
    /// <summary>
    /// Initializes a new instance of the <see cref="PluginConfiguration"/> class.
    /// </summary>
    public PluginConfiguration()
    {
        Enabled = false;
        DryRun = true;
        MoviesSource = string.Empty;
        MoviesDestination = string.Empty;
        SeriesSource = string.Empty;
        SeriesDestination = string.Empty;
        Operation = FileOperation.Move;
        Language = "ru-RU";
        UseJellyfinMetadataProviders = true;
        ReplaceColonWith = " - ";
        MinimumFileAgeMinutes = 5;
        MovieFolderTemplate = "{title} ({year})";
        MovieFileTemplate = "{title} ({year}){ext}";
        SeriesFolderTemplate = "{series}";
        SeasonFolderTemplate = "Season {season:00}";
        EpisodeFileTemplate = "{series} - S{season:00}E{episode:00}{ext}";
        MoveAssociatedFiles = true;
        DeleteEmptyFolders = false;
    }

    public bool Enabled { get; set; }

    public bool DryRun { get; set; }

    public string MoviesSource { get; set; }

    public string MoviesDestination { get; set; }

    public string SeriesSource { get; set; }

    public string SeriesDestination { get; set; }

    public FileOperation Operation { get; set; }

    public string Language { get; set; }

    public bool UseJellyfinMetadataProviders { get; set; }

    public string ReplaceColonWith { get; set; }

    public int MinimumFileAgeMinutes { get; set; }

    public string MovieFolderTemplate { get; set; }

    public string MovieFileTemplate { get; set; }

    public string SeriesFolderTemplate { get; set; }

    public string SeasonFolderTemplate { get; set; }

    public string EpisodeFileTemplate { get; set; }

    public bool MoveAssociatedFiles { get; set; }

    public bool DeleteEmptyFolders { get; set; }
}
