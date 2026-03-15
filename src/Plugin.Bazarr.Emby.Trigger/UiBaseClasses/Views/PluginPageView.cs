using System.Threading.Tasks;
using MediaBrowser.Model.Plugins.UI.Views;

namespace Plugin.Bazarr.Emby.Trigger.UiBaseClasses.Views;

internal abstract class PluginPageView : PluginViewBase, IPluginPageView
{
    protected PluginPageView(string pluginId)
        : base(pluginId)
    {
    }

    public bool ShowSave { get; set; } = true;

    public bool ShowBack { get; set; }

    public bool AllowSave { get; set; } = true;

    public bool AllowBack { get; set; } = true;

    public virtual Task<IPluginUIView> OnSaveCommand(string itemId, string commandId, string data)
    {
        return Task.FromResult<IPluginUIView>(this);
    }
}
