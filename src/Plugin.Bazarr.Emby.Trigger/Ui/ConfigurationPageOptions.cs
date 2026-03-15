using System;
using Emby.Web.GenericEdit.Elements;
using Plugin.Bazarr.Emby.Trigger.Options;
using Plugin.Bazarr.Emby.Trigger.Services;

namespace Plugin.Bazarr.Emby.Trigger.Ui;

public sealed class ConfigurationPageOptions : PluginOptions
{
    public StatusItem ConnectionStatus { get; set; } = new StatusItem("Connection status", "Ready to test the current settings.", ItemStatus.Unavailable);

    public ButtonItem TestConnectionButton { get; set; } = new ButtonItem("Test Connection")
    {
        Icon = IconNames.run_circle,
        Data1 = "TestConnection",
    };

    public static ConfigurationPageOptions FromStoredOptions(PluginOptions storedOptions, string endpointSummary)
    {
        var hasApiKey = !string.IsNullOrWhiteSpace(storedOptions.BazarrApiKey);

        return new ConfigurationPageOptions
        {
            BazarrHost = storedOptions.BazarrHost,
            BazarrPort = storedOptions.BazarrPort,
            BazarrBaseUrl = storedOptions.BazarrBaseUrl,
            BazarrApiKey = string.Empty,
            SearchesPerHour = storedOptions.SearchesPerHour,
            MetadataCacheTtlMinutes = storedOptions.MetadataCacheTtlMinutes,
            QueuePollIntervalSeconds = storedOptions.QueuePollIntervalSeconds,
            SearchTimeoutMinutes = storedOptions.SearchTimeoutMinutes,
            VerboseLogging = storedOptions.VerboseLogging,
            CustomRequestHeaders = storedOptions.CustomRequestHeaders,
            ConnectionStatus = new StatusItem(
                "Connection status",
                hasApiKey
                    ? $"Saved endpoint: {endpointSummary}"
                    : $"Saved endpoint: {endpointSummary} (API key not configured yet).",
                ItemStatus.Unavailable),
        };
    }

    public PluginOptions ToStoredOptions(PluginOptions currentStoredOptions)
    {
        return new PluginOptions
        {
            BazarrHost = BazarrHost,
            BazarrPort = BazarrPort,
            BazarrBaseUrl = BazarrBaseUrl,
            BazarrApiKey = string.IsNullOrWhiteSpace(BazarrApiKey) ? currentStoredOptions.BazarrApiKey : BazarrApiKey,
            SearchesPerHour = SearchesPerHour,
            MetadataCacheTtlMinutes = MetadataCacheTtlMinutes,
            QueuePollIntervalSeconds = QueuePollIntervalSeconds,
            SearchTimeoutMinutes = SearchTimeoutMinutes,
            VerboseLogging = VerboseLogging,
            CustomRequestHeaders = CustomRequestHeaders,
        };
    }

    public void SetConnectionState(ItemStatus status, string message)
    {
        ConnectionStatus.Status = status;
        ConnectionStatus.StatusText = message;
    }
}
