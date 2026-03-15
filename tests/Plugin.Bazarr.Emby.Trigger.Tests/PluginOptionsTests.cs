using Plugin.Bazarr.Emby.Trigger.Options;

namespace Plugin.Bazarr.Emby.Trigger.Tests;

public class PluginOptionsTests
{
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
}
