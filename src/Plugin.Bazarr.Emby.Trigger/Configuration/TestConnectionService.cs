using System;
using System.Net.Http;
using MediaBrowser.Model.Services;
using Plugin.Bazarr.Emby.Trigger.Integration;
using Plugin.Bazarr.Emby.Trigger.Options;

namespace Plugin.Bazarr.Emby.Trigger.Configuration;

[Route("/Plugins/BazarrEmbyTrigger/ConnectionInfo", "GET", Summary = "Gets the saved Bazarr connection summary for the connection tools page.")]
public class ConnectionInfoRequest : IReturn<TestConnectionResult>
{
}

[Route("/Plugins/BazarrEmbyTrigger/TestConnection", "POST", Summary = "Tests the saved Bazarr connection without putting the API key in a URL.")]
public class TestConnectionRequest : IReturn<TestConnectionResult>
{
}

public class TestConnectionResult
{
    public bool Success { get; set; }
    public string Message { get; set; } = string.Empty;
    public string Endpoint { get; set; } = string.Empty;
    public bool HasApiKey { get; set; }
}

public class TestConnectionService : IService
{
    private static readonly HttpClient SharedHttpClient = new HttpClient
    {
        Timeout = TimeSpan.FromSeconds(30),
    };

    public object Get(ConnectionInfoRequest request)
    {
        var options = Plugin.Instance?.Options ?? new PluginOptions();
        return new TestConnectionResult
        {
            Success = true,
            Message = "Loaded saved Bazarr settings.",
            Endpoint = BazarrClient.BuildEndpointSummary(options),
            HasApiKey = !string.IsNullOrWhiteSpace(options.BazarrApiKey),
        };
    }

    public object Post(TestConnectionRequest request)
    {
        var options = Plugin.Instance?.Options ?? new PluginOptions();
        var client = new BazarrClient(SharedHttpClient, Plugin.Instance?.Logger);
        var result = client.TestConnectionAsync(options, default).GetAwaiter().GetResult();
        return new TestConnectionResult
        {
            Success = result.Success,
            Message = result.Message,
            Endpoint = result.Endpoint,
            HasApiKey = !string.IsNullOrWhiteSpace(options.BazarrApiKey),
        };
    }
}
