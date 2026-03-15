using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using Emby.Web.GenericEdit;
using Emby.Web.GenericEdit.Validation;
using MediaBrowser.Model.Attributes;

namespace Plugin.Bazarr.Emby.Trigger.Options;

public class PluginOptions : EditableOptionsBase
{
    public override string EditorTitle => "Bazarr Emby Trigger";

    public override string EditorDescription => "Configure Bazarr connectivity, queue behavior, cache timing, and debugging options for the Bazarr-backed Emby subtitle provider.";

    [DisplayName("Bazarr host")]
    [Description("Hostname or IP address for the Bazarr server. Do not include a scheme such as http://.")]
    [Required]
    public string BazarrHost { get; set; } = "localhost";

    [DisplayName("Bazarr port")]
    [Description("TCP port for the Bazarr server.")]
    public int BazarrPort { get; set; } = 6767;

    [DisplayName("Reverse proxy / base URI")]
    [Description("Optional path base when Bazarr is published behind a reverse proxy, for example /bazarr.")]
    public string BazarrBaseUrl { get; set; } = string.Empty;

    [DisplayName("Bazarr API key")]
    [Description("Stored server-side and always sent in the X-API-KEY header.")]
    [IsPassword]
    public string BazarrApiKey { get; set; } = string.Empty;

    [DisplayName("Searches per hour")]
    [Description("Maximum number of subtitle searches this plugin may send to Bazarr per hour before requests are queued.")]
    public int SearchesPerHour { get; set; } = 12;

    [DisplayName("Bazarr metadata cache TTL (minutes)")]
    [Description("How long Bazarr movie, series, and episode metadata stays cached before being refreshed.")]
    public int MetadataCacheTtlMinutes { get; set; } = 30;

    [DisplayName("Queue poll interval (seconds)")]
    [Description("How often the background worker checks queued requests and subtitle arrival.")]
    public int QueuePollIntervalSeconds { get; set; } = 30;

    [DisplayName("Subtitle detection timeout (minutes)")]
    [Description("How long to wait for a new subtitle file before the request is marked as timed out.")]
    public int SearchTimeoutMinutes { get; set; } = 20;

    [DisplayName("Verbose plugin logging")]
    [Description("Logs matching decisions, rate limiting, Bazarr requests, snapshot comparisons, and notification decisions without exposing secrets.")]
    public bool VerboseLogging { get; set; } = true;

    [DisplayName("Additional request headers")]
    [Description("Optional development or reverse-proxy headers. Enter one per line in the form Header: Value.")]
    [EditMultiline(4)]
    public string CustomRequestHeaders { get; set; } = string.Empty;

    public IDictionary<string, string> ParseCustomHeaders()
    {
        return (CustomRequestHeaders ?? string.Empty)
            .Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries)
            .Select(line => line.Split(new[] { ':' }, 2, StringSplitOptions.RemoveEmptyEntries))
            .Where(parts => parts.Length == 2)
            .ToDictionary(parts => parts[0].Trim(), parts => parts[1].Trim(), StringComparer.OrdinalIgnoreCase);
    }

    protected override void Validate(ValidationContext context)
    {
        if (string.IsNullOrWhiteSpace(BazarrHost))
        {
            context.AddValidationError(nameof(BazarrHost), "Bazarr host is required.");
        }

        if (BazarrHost.Contains("://"))
        {
            context.AddValidationError(nameof(BazarrHost), "Bazarr host must not include a URL scheme.");
        }

        if (BazarrPort < 1 || BazarrPort > 65535)
        {
            context.AddValidationError(nameof(BazarrPort), "Bazarr port must be between 1 and 65535.");
        }

        if (SearchesPerHour < 1)
        {
            context.AddValidationError(nameof(SearchesPerHour), "Searches per hour must be at least 1.");
        }

        if (MetadataCacheTtlMinutes < 1)
        {
            context.AddValidationError(nameof(MetadataCacheTtlMinutes), "Metadata cache TTL must be at least 1 minute.");
        }

        if (QueuePollIntervalSeconds < 5)
        {
            context.AddValidationError(nameof(QueuePollIntervalSeconds), "Queue poll interval must be at least 5 seconds.");
        }

        if (SearchTimeoutMinutes < 1)
        {
            context.AddValidationError(nameof(SearchTimeoutMinutes), "Subtitle detection timeout must be at least 1 minute.");
        }
    }
}
