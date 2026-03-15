using System;
using System.Threading.Tasks;
using MediaBrowser.Model.Dto;
using MediaBrowser.Model.Events;
using MediaBrowser.Model.GenericEdit;
using MediaBrowser.Model.Plugins.UI.Views;
using MediaBrowser.Model.Plugins.UI.Views.Enums;

namespace Plugin.Bazarr.Emby.Trigger.UiBaseClasses.Views;

internal abstract class PluginViewBase : IPluginUIView, IPluginViewWithOptions
{
    protected PluginViewBase(string pluginId)
    {
        PluginId = pluginId;
    }

    public event EventHandler<GenericEventArgs<IPluginUIView>>? UIViewInfoChanged;

    public virtual string Caption => ContentData.EditorTitle;

    public virtual string SubCaption => ContentData.EditorDescription;

    public string PluginId { get; }

    public IEditableObject ContentData
    {
        get => contentDataCore;
        set => contentDataCore = value;
    }

    public UserDto? User { get; set; }

    public string? RedirectViewUrl { get; set; }

    public Uri? HelpUrl { get; set; }

    public QueryCloseAction QueryCloseAction { get; set; }

    public WizardHidingBehavior WizardHidingBehavior { get; set; }

    public CompactViewAppearance CompactViewAppearance { get; set; }

    public DialogSize DialogSize { get; set; }

    public string? OKButtonCaption { get; set; }

    public DialogAction PrimaryDialogAction { get; set; }

    private IEditableObject contentDataCore = default!;

    public virtual bool IsCommandAllowed(string commandKey)
    {
        return true;
    }

    public virtual Task<IPluginUIView?> RunCommand(string itemId, string commandId, string data)
    {
        return Task.FromResult<IPluginUIView?>(null);
    }

    public virtual Task Cancel()
    {
        return Task.CompletedTask;
    }

    public virtual void OnDialogResult(IPluginUIView dialogView, bool completedOk, object? data)
    {
    }

    protected void RaiseUIViewInfoChanged()
    {
        UIViewInfoChanged?.Invoke(this, new GenericEventArgs<IPluginUIView>(this));
    }

    public virtual PluginViewOptions ViewOptions => new PluginViewOptions
    {
        HelpUrl = HelpUrl,
        CompactViewAppearance = CompactViewAppearance,
        QueryCloseAction = QueryCloseAction,
        DialogSize = DialogSize,
        OKButtonCaption = OKButtonCaption,
        PrimaryDialogAction = PrimaryDialogAction,
        WizardHidingBehavior = WizardHidingBehavior,
    };
}
