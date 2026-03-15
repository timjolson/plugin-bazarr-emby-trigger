using Plugin.Bazarr.Emby.Trigger.Options;
using Plugin.Bazarr.Emby.Trigger.Integration;
using Emby.Web.GenericEdit.Validation;

namespace Plugin.Bazarr.Emby.Trigger.Tests;

public class PluginOptionsTests
{
    [Fact]
    public void Defaults_UseHttpLocalhostAndEmptyBaseUri()
    {
        var options = new PluginOptions();

        Assert.Equal("http://localhost", options.BazarrHost);
        Assert.Equal(string.Empty, options.BazarrBaseUrl);
    }

    [Fact]
    public void ParseCustomHeaders_IgnoresMalformedLinesAndTrimsValues()
    {
        var options = new PluginOptions
        {
            CustomRequestHeaders = "X-Test: value\nInvalidHeader\nX-Forwarded-Proto: https"
        };

        var headers = options.ParseCustomHeaders();

        Assert.Equal(2, headers.Count);
        Assert.Equal("value", headers["X-Test"]);
        Assert.Equal("https", headers["X-Forwarded-Proto"]);
    }

    [Fact]
    public void ParseCustomHeaders_HandlesEmptyWhitespaceAndMultipleColons()
    {
        var options = new PluginOptions
        {
            CustomRequestHeaders = "\n   \nEmptyValue:\nAuthorization: Bearer:token\nTrailing: value   "
        };

        var headers = options.ParseCustomHeaders();

        Assert.Equal(2, headers.Count);
        Assert.False(headers.ContainsKey("EmptyValue"));
        Assert.Equal("Bearer:token", headers["Authorization"]);
        Assert.Equal("value", headers["Trailing"]);
    }

    [Fact]
    public void ValidateOrThrow_AllowsHttpUrlHostWithoutPath()
    {
        var options = new PluginOptions
        {
            BazarrHost = "http://localhost",
        };

        options.ValidateOrThrow();
    }

    [Fact]
    public void ValidateOrThrow_RejectsHostUrlWithPath()
    {
        var options = new PluginOptions
        {
            BazarrHost = "http://localhost/bazarr",
        };

        Assert.Throws<ValidationException>(() => options.ValidateOrThrow());
    }

    [Fact]
    public void BuildEndpointSummary_UsesSchemeFromConfiguredHost()
    {
        var options = new PluginOptions
        {
            BazarrHost = "https://bazarr.example.com",
            BazarrPort = 6767,
            BazarrBaseUrl = string.Empty,
        };

        var endpoint = BazarrClient.BuildEndpointSummary(options);

        Assert.Equal("https://bazarr.example.com:6767/api", endpoint);
    }
}
