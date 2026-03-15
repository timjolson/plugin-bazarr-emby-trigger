using System;
using System.ComponentModel;
using Emby.Web.GenericEdit.Elements;
using Emby.Web.GenericEdit.Validation;
using Plugin.Bazarr.Emby.Trigger.Options;
using Plugin.Bazarr.Emby.Trigger.Services;

namespace Plugin.Bazarr.Emby.Trigger.Ui;

public sealed class ConfigurationPageOptions : PluginOptions
{
    [Browsable(false)]
    public bool HasStoredApiKey { get; set; }

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
            HasStoredApiKey = hasApiKey,
            ConnectionStatus = new StatusItem(
                "Connection status",
                hasApiKey
                    ? $"Saved endpoint: {endpointSummary}. Leave the API key field empty to keep the saved key."
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

    protected override void Validate(ValidationContext context)
    {
        base.Validate(context);

        if (string.IsNullOrWhiteSpace(BazarrApiKey) && !HasStoredApiKey)
        {
            context.AddValidationError(nameof(BazarrApiKey), "A Bazarr API key is required.");
        }
    }
}
