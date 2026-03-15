using System.Net.Http;
using System.Threading.Tasks;
using Emby.Web.GenericEdit.Elements;
using MediaBrowser.Model.Plugins;
using MediaBrowser.Model.Plugins.UI.Views;
using Plugin.Bazarr.Emby.Trigger.Integration;
using Plugin.Bazarr.Emby.Trigger.Storage;
using Plugin.Bazarr.Emby.Trigger.UiBaseClasses.Views;

namespace Plugin.Bazarr.Emby.Trigger.Ui;

internal sealed class MainPageView : PluginPageView
{
    private static readonly HttpClient SharedHttpClient = new HttpClient
    {
        Timeout = System.TimeSpan.FromSeconds(30),
    };

    private readonly PluginOptionsStore optionsStore;
    private readonly BazarrClient bazarrClient;

    public MainPageView(PluginInfo pluginInfo, PluginOptionsStore optionsStore)
        : base(pluginInfo.Id)
    {
        this.optionsStore = optionsStore;
        bazarrClient = new BazarrClient(SharedHttpClient);
        ContentData = CreateEditableOptions(optionsStore.GetOptions());
    }

    private ConfigurationPageOptions PageOptions => (ConfigurationPageOptions)ContentData;

    public override async Task<IPluginUIView?> RunCommand(string itemId, string commandId, string data)
    {
        switch (commandId)
        {
            case "TestConnection":
                await HandleTestConnectionAsync().ConfigureAwait(false);
                return this;
        }

        return await base.RunCommand(itemId, commandId, data).ConfigureAwait(false);
    }

    public override Task<IPluginUIView> OnSaveCommand(string itemId, string commandId, string data)
    {
        PageOptions.ValidateOrThrow();
        optionsStore.SaveOptions(PageOptions.ToStoredOptions(optionsStore.GetOptions()));
        Plugin.Instance?.Logger.Info("Bazarr Emby Trigger options updated.");
        ContentData = CreateEditableOptions(optionsStore.GetOptions());
        return Task.FromResult<IPluginUIView>(this);
    }

    private async Task HandleTestConnectionAsync()
    {
        var storedOptions = optionsStore.GetOptions();
        var testOptions = PageOptions.ToStoredOptions(storedOptions);
        var endpointSummary = BuildEndpointSummary(testOptions);

        PageOptions.TestConnectionButton.IsEnabled = false;
        PageOptions.SetConnectionState(ItemStatus.InProgress, $"Testing {endpointSummary}...");
        RaiseUIViewInfoChanged();

        try
        {
            var result = await bazarrClient.TestConnectionAsync(testOptions, default).ConfigureAwait(false);
            PageOptions.SetConnectionState(
                result.Success ? ItemStatus.Succeeded : ItemStatus.Failed,
                $"{result.Message} Endpoint: {result.Endpoint}");
        }
        catch (HttpRequestException ex)
        {
            PageOptions.SetConnectionState(ItemStatus.Failed, $"Connection failed: {ex.Message}. Endpoint: {endpointSummary}");
        }
        finally
        {
            PageOptions.TestConnectionButton.IsEnabled = true;
            RaiseUIViewInfoChanged();
        }
    }

    private static ConfigurationPageOptions CreateEditableOptions(Options.PluginOptions storedOptions)
    {
        return ConfigurationPageOptions.FromStoredOptions(storedOptions, BuildEndpointSummary(storedOptions));
    }

    private static string BuildEndpointSummary(Options.PluginOptions options)
    {
        return BazarrClient.BuildEndpointSummary(options);
    }
}
