using System;
using System.Collections.Generic;
using System.Linq;
using MediaBrowser.Model.Plugins;

namespace Plugin.Bazarr.Emby.Trigger.Configuration;

public class PluginConfiguration : BasePluginConfiguration
{
    public string BazarrHost { get; set; } = "localhost";

    public int BazarrPort { get; set; } = 6767;

    public string BazarrBaseUrl { get; set; } = string.Empty;

    public string BazarrApiKey { get; set; } = string.Empty;

    public int SearchesPerHour { get; set; } = 12;

    public int MetadataCacheTtlMinutes { get; set; } = 30;

    public int QueuePollIntervalSeconds { get; set; } = 30;

    public int SearchTimeoutMinutes { get; set; } = 20;

    public bool VerboseLogging { get; set; } = true;

    public string CustomRequestHeaders { get; set; } = string.Empty;

    public IDictionary<string, string> ParseCustomHeaders()
    {
        return (CustomRequestHeaders ?? string.Empty)
            .Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries)
            .Select(line => line.Split(new[] { ':' }, 2, StringSplitOptions.RemoveEmptyEntries))
            .Where(parts => parts.Length == 2)
            .ToDictionary(parts => parts[0].Trim(), parts => parts[1].Trim(), StringComparer.OrdinalIgnoreCase);
    }
}
