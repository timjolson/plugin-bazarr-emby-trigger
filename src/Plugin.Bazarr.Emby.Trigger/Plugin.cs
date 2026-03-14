using System;
using System.Collections.Generic;
using System.IO;
using MediaBrowser.Common.Configuration;
using MediaBrowser.Common.Plugins;
using MediaBrowser.Model.Logging;
using MediaBrowser.Model.Plugins;
using MediaBrowser.Model.Serialization;
using Plugin.Bazarr.Emby.Trigger.Configuration;

namespace Plugin.Bazarr.Emby.Trigger;

public sealed class Plugin : BasePlugin<PluginConfiguration>, IHasWebPages
{
    public static Plugin? Instance { get; private set; }

    private readonly Guid id = new Guid("108c48a7-f1dc-4b2c-831b-a5eee53e997d");
    private readonly ILogger logger;

    public Plugin(IApplicationPaths applicationPaths, IXmlSerializer xmlSerializer, ILogManager logManager)
        : base(applicationPaths, xmlSerializer)
    {
        Instance = this;
        logger = logManager.GetLogger(Name);
        logger.Info("Bazarr Emby Trigger plugin loaded.");
    }

    public override string Name => "Bazarr Emby Trigger";

    public override string Description => "Registers as an Emby subtitle provider and delegates subtitle searches to Bazarr.";

    public override Guid Id => id;

    public IEnumerable<PluginPageInfo> GetPages()
    {
        return new[]
        {
            new PluginPageInfo
            {
                Name = "bazarr-emby-trigger",
                DisplayName = "Bazarr Emby Trigger",
                EmbeddedResourcePath = GetType().Namespace + ".Ui.configPage.html",
                IsMainConfigPage = true,
                MenuSection = "server",
            },
        };
    }

    public string GetPluginDataDirectory()
    {
        var configPath = ConfigurationFilePath ?? Path.Combine(AppContext.BaseDirectory, "config", "plugins", "bazarr-emby-trigger.xml");
        return Path.Combine(Path.GetDirectoryName(configPath) ?? AppContext.BaseDirectory, "bazarr-emby-trigger");
    }
}
