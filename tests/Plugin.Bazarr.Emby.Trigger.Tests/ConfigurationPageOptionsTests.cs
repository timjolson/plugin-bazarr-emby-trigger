using Emby.Web.GenericEdit.Elements;
using Emby.Web.GenericEdit.Validation;
using Plugin.Bazarr.Emby.Trigger.Options;
using Plugin.Bazarr.Emby.Trigger.Ui;

namespace Plugin.Bazarr.Emby.Trigger.Tests;

public class ConfigurationPageOptionsTests
{
    [Fact]
    public void FromStoredOptions_CreatesInlineTestConnectionButtonAndMasksApiKey()
    {
        var stored = new PluginOptions
        {
            BazarrHost = "bazarr.local",
            BazarrPort = 6767,
            BazarrBaseUrl = "/bazarr",
            BazarrApiKey = "secret",
            PollMediaFolders = false,
        };

        var page = ConfigurationPageOptions.FromStoredOptions(stored, "http://bazarr.local:6767/bazarr/api");

        Assert.Equal(string.Empty, page.BazarrApiKey);
        Assert.Equal("Test Connection", page.TestConnectionButton.Caption);
        Assert.Equal("TestConnection", page.TestConnectionButton.Data1);
        Assert.False(page.PollMediaFolders);
        Assert.Contains("http://bazarr.local:6767/bazarr/api", page.ConnectionStatus.StatusText);
        Assert.Contains("Leave the API key field empty to keep the saved key.", page.ConnectionStatus.StatusText);
    }

    [Fact]
    public void ToStoredOptions_PreservesExistingApiKeyWhenUiLeavesPasswordBlank()
    {
        var stored = new PluginOptions
        {
            BazarrHost = "old-host",
            BazarrPort = 6767,
            BazarrApiKey = "secret",
        };

        var page = new ConfigurationPageOptions
        {
            BazarrHost = "new-host",
            BazarrPort = 6768,
            BazarrApiKey = string.Empty,
            PollMediaFolders = false,
        };

        var persisted = page.ToStoredOptions(stored);

        Assert.Equal("new-host", persisted.BazarrHost);
        Assert.Equal(6768, persisted.BazarrPort);
        Assert.Equal("secret", persisted.BazarrApiKey);
        Assert.False(persisted.PollMediaFolders);
    }

    [Fact]
    public void SetConnectionState_UpdatesInlineStatusItem()
    {
        var page = new ConfigurationPageOptions();

        page.SetConnectionState(ItemStatus.Succeeded, "Bazarr responded successfully.");

        Assert.Equal(ItemStatus.Succeeded, page.ConnectionStatus.Status);
        Assert.Equal("Bazarr responded successfully.", page.ConnectionStatus.StatusText);
    }

    [Fact]
    public void ValidateOrThrow_RequiresApiKeyWhenNoSavedKeyExists()
    {
        var page = new ConfigurationPageOptions
        {
            HasStoredApiKey = false,
            BazarrApiKey = string.Empty,
        };

        Assert.Throws<ValidationException>(() => page.ValidateOrThrow());
    }

    [Fact]
    public void ValidateOrThrow_AllowsEmptyApiKeyWhenSavedKeyExists()
    {
        var page = new ConfigurationPageOptions
        {
            HasStoredApiKey = true,
            BazarrApiKey = string.Empty,
        };

        page.ValidateOrThrow();
    }
}
