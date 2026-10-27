using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Jellyfin.Plugin.AutoOrganize.Configuration;
using MediaBrowser.Common.Configuration;
using MediaBrowser.Common.Plugins;
using MediaBrowser.Model.Plugins;
using MediaBrowser.Model.Serialization;

namespace Jellyfin.Plugin.AutoOrganize;

/// <summary>
/// Auto Organize plugin entry point.
/// </summary>
public sealed class Plugin : BasePlugin<PluginConfiguration>, IHasWebPages
{
    private readonly object _activitySync = new();

    /// <summary>
    /// Initializes a new instance of the <see cref="Plugin"/> class.
    /// </summary>
    public Plugin(IApplicationPaths applicationPaths, IXmlSerializer xmlSerializer)
        : base(applicationPaths, xmlSerializer)
    {
        Instance = this;
    }

    /// <inheritdoc />
    public override string Name => "Auto Organize";

    /// <inheritdoc />
    public override Guid Id => Guid.Parse("b0515a71-eba2-4cca-9670-6aedba961caa");

    /// <summary>
    /// Gets the plugin instance.
    /// </summary>
    public static Plugin? Instance { get; private set; }

    /// <summary>
    /// Appends a persistent organizer activity entry.
    /// </summary>
    public void AddActivity(
        string mediaType,
        string status,
        string source,
        string? destination,
        string message)
    {
        lock (_activitySync)
        {
            Configuration.ActivityLog ??= [];
            Configuration.ActivityLog.Insert(0, new ActivityLogEntry
            {
                TimestampUtc = DateTime.UtcNow,
                MediaType = mediaType,
                Status = status,
                Source = source,
                Destination = destination ?? string.Empty,
                Message = message
            });

            const int maxEntries = 500;
            if (Configuration.ActivityLog.Count > maxEntries)
            {
                Configuration.ActivityLog = Configuration.ActivityLog.Take(maxEntries).ToList();
            }

            SaveConfiguration();
        }
    }

    /// <inheritdoc />
    public IEnumerable<PluginPageInfo> GetPages()
    {
        return
        [
            new PluginPageInfo
            {
                Name = "AutoOrganize",
                DisplayName = "Auto Organize",
                EnableInMainMenu = true,
                EmbeddedResourcePath = string.Format(
                    CultureInfo.InvariantCulture,
                    "{0}.Configuration.logsPage.html",
                    GetType().Namespace)
            }
        ];
    }
}
