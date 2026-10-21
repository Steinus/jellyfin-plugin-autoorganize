using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Plugin.AutoOrganize.Core;
using MediaBrowser.Controller.Providers;
using MediaBrowser.Model.Tasks;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.AutoOrganize.Tasks;

/// <summary>
/// Scheduled task that imports media from configured watch folders.
/// </summary>
public sealed class AutoOrganizeTask : IScheduledTask
{
    private readonly ILogger<AutoOrganizeTask> _logger;
    private readonly IProviderManager _providerManager;

    public AutoOrganizeTask(ILogger<AutoOrganizeTask> logger, IProviderManager providerManager)
    {
        _logger = logger;
        _providerManager = providerManager;
    }

    public string Name => "Auto Organize media";

    public string Key => "AutoOrganizeMedia";

    public string Description => "Scans Auto Organize watch folders and imports eligible media files.";

    public string Category => "Library";

    public async Task ExecuteAsync(IProgress<double> progress, CancellationToken cancellationToken)
    {
        var plugin = Plugin.Instance ?? throw new InvalidOperationException("Auto Organize plugin is not initialized.");
        var config = plugin.Configuration;
        if (!config.MoviesEnabled && !config.SeriesEnabled)
        {
            _logger.LogInformation("Auto Organize movie and series processing are both disabled.");
            progress.Report(100);
            return;
        }

        var service = new OrganizeService(_logger, _providerManager, plugin);
        var total = Math.Max(1, service.CountCandidates(config));
        var completed = 0;

        plugin.AddActivity("System", "Info", string.Empty, null, "Запущена задача Auto Organize.");

        void Completed()
        {
            completed++;
            progress.Report(Math.Min(100d, 100d * completed / total));
        }

        await service.ProcessMoviesAsync(config, Completed, cancellationToken).ConfigureAwait(false);
        await service.ProcessSeriesAsync(config, Completed, cancellationToken).ConfigureAwait(false);

        plugin.AddActivity("System", "Info", string.Empty, null, "Задача Auto Organize завершена.");
        progress.Report(100);
    }

    public IEnumerable<TaskTriggerInfo> GetDefaultTriggers()
    {
        return
        [
            new TaskTriggerInfo
            {
                Type = TaskTriggerInfoType.IntervalTrigger,
                IntervalTicks = TimeSpan.FromMinutes(10).Ticks
            }
        ];
    }
}
