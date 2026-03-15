using System.ComponentModel;
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
        Assert.True(options.PollMediaFolders);
    }

    [Fact]
    public void PollMediaFolders_DescriptionExplainsPollingAndEventBehavior()
    {
        var property = typeof(PluginOptions).GetProperty(nameof(PluginOptions.PollMediaFolders));
        var description = property?.GetCustomAttributes(typeof(DescriptionAttribute), inherit: false)
            .OfType<DescriptionAttribute>()
            .SingleOrDefault();

        Assert.NotNull(description);
        Assert.Equal(
            "When checked, triggered subtitle requests poll their media folders for subtitle changes using the queue poll interval. When unchecked, the plugin waits for Emby media or folder update events and only runs subtitle comparisons for matching updates.",
            description!.Description);
    }

    [Fact]
    public void BazarrBaseUrl_DescriptionIncludesExampleAndEmptyGuidance()
    {
        var property = typeof(PluginOptions).GetProperty(nameof(PluginOptions.BazarrBaseUrl));
        var description = property?.GetCustomAttributes(typeof(DescriptionAttribute), inherit: false)
            .OfType<DescriptionAttribute>()
            .SingleOrDefault();

        Assert.NotNull(description);
        Assert.Equal(
            "Optional path base when Bazarr is published behind a reverse proxy. Example: /bazarr. Leave this empty when Bazarr is not behind a reverse proxy.",
            description!.Description);
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
