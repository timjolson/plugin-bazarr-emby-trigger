using System;
using MediaBrowser.Controller.Notifications;
using MediaBrowser.Controller.Plugins;
using MediaBrowser.Model.Logging;

namespace Plugin.Bazarr.Emby.Trigger;

// The entry point exists only to spin up the background queue processor once per server lifetime.
public class ServerEntryPoint : IServerEntryPoint
{
    public ServerEntryPoint(INotificationManager notificationManager, ILogManager logManager)
    {
        if (Plugin.Instance != null)
        {
            PluginRuntime.Initialize(() => Plugin.Instance.Options, notificationManager, logManager, Plugin.Instance.GetPluginDataDirectory());
        }
    }

    public void Run()
    {
    }

    public void Dispose()
    {
        PluginRuntime.Coordinator?.Dispose();
    }
}
