using System;
using System.Collections.Generic;
using MediaBrowser.Common.Plugins;
using MediaBrowser.Controller;
using MediaBrowser.Model.Logging;
using MediaBrowser.Model.Plugins;
using MediaBrowser.Model.Plugins.UI;
using Plugin.Bazarr.Emby.Trigger.Options;
using Plugin.Bazarr.Emby.Trigger.Storage;
using Plugin.Bazarr.Emby.Trigger.Ui;
using Plugin.Bazarr.Emby.Trigger.UiBaseClasses;

namespace Plugin.Bazarr.Emby.Trigger;

public sealed class Plugin : BasePlugin, IHasUIPages
{
    public static Plugin? Instance { get; private set; }

    private readonly Guid id = new Guid("108c48a7-f1dc-4b2c-831b-a5eee53e997d");
    private readonly ILogger logger;
    private readonly IServerApplicationHost applicationHost;
    private readonly PluginOptionsStore optionsStore;
    private List<IPluginUIPageController>? pages;

    public Plugin(IServerApplicationHost applicationHost, ILogManager logManager)
    {
        Instance = this;
        this.applicationHost = applicationHost;
        logger = logManager.GetLogger(Name);
        optionsStore = new PluginOptionsStore(applicationHost, logger, Name);
        logger.Info("Bazarr Emby Trigger plugin loaded.");
    }

    public override string Name => "Bazarr Emby Trigger";

    public override string Description => "Registers as an Emby subtitle provider and delegates subtitle searches to Bazarr.";

    public override Guid Id => id;

    public PluginOptions Options => optionsStore.GetOptions();

    internal ILogger Logger => logger;

    public IReadOnlyCollection<IPluginUIPageController> UIPageControllers
    {
        get
        {
            if (pages == null)
            {
                pages = new List<IPluginUIPageController>
                {
                    new MainPageController(GetPluginInfo(), applicationHost, optionsStore),
                };
            }

            return pages.AsReadOnly();
        }
    }

    public string GetPluginDataDirectory()
    {
        return DataFolderPath;
    }
}
