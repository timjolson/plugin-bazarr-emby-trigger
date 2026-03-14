using System;
using System.Net.Http;
using MediaBrowser.Model.Services;
using Plugin.Bazarr.Emby.Trigger.Integration;

namespace Plugin.Bazarr.Emby.Trigger.Configuration;

[Route("/Plugins/BazarrEmbyTrigger/TestConnection", "POST", Summary = "Tests connectivity to Bazarr without putting the API key in a URL.")]
public class TestConnectionRequest : IReturn<TestConnectionResult>
{
    public string BazarrHost { get; set; } = string.Empty;
    public int BazarrPort { get; set; }
    public string BazarrBaseUrl { get; set; } = string.Empty;
    public string BazarrApiKey { get; set; } = string.Empty;
    public string CustomRequestHeaders { get; set; } = string.Empty;
}

public class TestConnectionResult
{
    public bool Success { get; set; }
    public string Message { get; set; } = string.Empty;
    public string Endpoint { get; set; } = string.Empty;
}

public class TestConnectionService : IService
{
    public object Post(TestConnectionRequest request)
    {
        var configuration = new PluginConfiguration
        {
            BazarrHost = request.BazarrHost,
            BazarrPort = request.BazarrPort,
            BazarrBaseUrl = request.BazarrBaseUrl,
            BazarrApiKey = request.BazarrApiKey,
            CustomRequestHeaders = request.CustomRequestHeaders,
        };

        using (var httpClient = new HttpClient())
        {
            var client = new BazarrClient(httpClient);
            var result = client.TestConnectionAsync(configuration, default).GetAwaiter().GetResult();
            return new TestConnectionResult
            {
                Success = result.Success,
                Message = result.Message,
                Endpoint = result.Endpoint,
            };
        }
    }
}
