using System;
using System.Collections.Generic;
using MediaBrowser.Common;
using MediaBrowser.Controller.Plugins;
using MediaBrowser.Model.Logging;
using MediaBrowser.Model.Plugins;
using Plugin.Bazarr.Emby.Trigger.Options;

namespace Plugin.Bazarr.Emby.Trigger;

public sealed class Plugin : BasePluginSimpleUI<PluginOptions>, IHasWebPages
{
    public static Plugin? Instance { get; private set; }

    private readonly Guid id = new Guid("108c48a7-f1dc-4b2c-831b-a5eee53e997d");
    private readonly ILogger logger;
    private string savedApiKey = string.Empty;

    public Plugin(IApplicationHost applicationHost, ILogManager logManager)
        : base(applicationHost)
    {
        Instance = this;
        logger = logManager.GetLogger(Name);
        logger.Info("Bazarr Emby Trigger plugin loaded.");
    }

    public override string Name => "Bazarr Emby Trigger";

    public override string Description => "Registers as an Emby subtitle provider and delegates subtitle searches to Bazarr.";

    public override Guid Id => id;

    public PluginOptions Options => GetOptions();

    public IEnumerable<PluginPageInfo> GetPages()
    {
        return new[]
        {
            new PluginPageInfo
            {
                Name = "bazarr-emby-trigger-connection-tools",
                DisplayName = "Bazarr Connection Tools",
                EmbeddedResourcePath = GetType().Namespace + ".Ui.connectionPage.html",
                IsMainConfigPage = false,
                MenuSection = "server",
            },
        };
    }

    public string GetPluginDataDirectory()
    {
        return DataFolderPath;
    }

    protected override PluginOptions OnBeforeShowUI(PluginOptions options)
    {
        if (!string.IsNullOrWhiteSpace(options.BazarrApiKey))
        {
            savedApiKey = options.BazarrApiKey;
        }
        return new PluginOptions
        {
            BazarrHost = options.BazarrHost,
            BazarrPort = options.BazarrPort,
            BazarrBaseUrl = options.BazarrBaseUrl,
            BazarrApiKey = string.Empty,
            SearchesPerHour = options.SearchesPerHour,
            MetadataCacheTtlMinutes = options.MetadataCacheTtlMinutes,
            QueuePollIntervalSeconds = options.QueuePollIntervalSeconds,
            SearchTimeoutMinutes = options.SearchTimeoutMinutes,
            VerboseLogging = options.VerboseLogging,
            CustomRequestHeaders = options.CustomRequestHeaders,
        };
    }

    protected override bool OnOptionsSaving(PluginOptions options)
    {
        if (string.IsNullOrWhiteSpace(options.BazarrApiKey))
        {
            options.BazarrApiKey = savedApiKey;
        }

        return base.OnOptionsSaving(options);
    }

    protected override void OnOptionsSaved(PluginOptions options)
    {
        if (!string.IsNullOrWhiteSpace(options.BazarrApiKey))
        {
            savedApiKey = options.BazarrApiKey;
        }
        logger.Info("Bazarr Emby Trigger options updated.");
    }
}
