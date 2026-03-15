using System.Threading.Tasks;
using MediaBrowser.Controller;
using MediaBrowser.Model.Plugins;
using MediaBrowser.Model.Plugins.UI.Views;
using Plugin.Bazarr.Emby.Trigger.Storage;
using Plugin.Bazarr.Emby.Trigger.UiBaseClasses;

namespace Plugin.Bazarr.Emby.Trigger.Ui;

internal sealed class MainPageController : ControllerBase
{
    private readonly PluginInfo pluginInfo;
    private readonly PluginOptionsStore optionsStore;

    public MainPageController(PluginInfo pluginInfo, IServerApplicationHost applicationHost, PluginOptionsStore optionsStore)
        : base(pluginInfo.Id)
    {
        this.pluginInfo = pluginInfo;
        this.optionsStore = optionsStore;
        PageInfo = new PluginPageInfo
        {
            Name = "BazarrEmbyTriggerMainPage",
            DisplayName = "Bazarr Emby Trigger",
            MenuIcon = "list_alt",
            IsMainConfigPage = true,
        };
    }

    public override PluginPageInfo PageInfo { get; }

    public override Task<IPluginUIView> CreateDefaultPageView()
    {
        return Task.FromResult<IPluginUIView>(new MainPageView(pluginInfo, optionsStore));
    }
}
