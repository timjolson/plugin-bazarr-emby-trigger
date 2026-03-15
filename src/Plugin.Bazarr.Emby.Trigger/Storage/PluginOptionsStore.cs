using System;
using System.IO;
using MediaBrowser.Common;
using MediaBrowser.Common.Configuration;
using MediaBrowser.Model.IO;
using MediaBrowser.Model.Logging;
using MediaBrowser.Model.Serialization;
using Plugin.Bazarr.Emby.Trigger.Options;

namespace Plugin.Bazarr.Emby.Trigger.Storage;

internal sealed class PluginOptionsStore
{
    private readonly object syncRoot = new object();
    private readonly ILogger logger;
    private readonly IJsonSerializer jsonSerializer;
    private readonly IFileSystem fileSystem;
    private readonly string optionsFilePath;
    private readonly string pluginFullName;
    private PluginOptions? options;

    public PluginOptionsStore(IApplicationHost applicationHost, ILogger logger, string pluginFullName)
    {
        this.logger = logger;
        this.pluginFullName = pluginFullName;
        jsonSerializer = applicationHost.Resolve<IJsonSerializer>();
        fileSystem = applicationHost.Resolve<IFileSystem>();

        var applicationPaths = applicationHost.Resolve<IApplicationPaths>();
        if (!fileSystem.DirectoryExists(applicationPaths.PluginConfigurationsPath))
        {
            fileSystem.CreateDirectory(applicationPaths.PluginConfigurationsPath);
        }

        optionsFilePath = Path.Combine(applicationPaths.PluginConfigurationsPath, $"{pluginFullName}.json");
    }

    public PluginOptions GetOptions()
    {
        lock (syncRoot)
        {
            if (options != null)
            {
                return options;
            }

            var tempOptions = new PluginOptions();
            try
            {
                if (!fileSystem.FileExists(optionsFilePath))
                {
                    options = tempOptions;
                    return options;
                }

                using (var stream = fileSystem.OpenRead(optionsFilePath))
                {
                    options = tempOptions.DeserializeFromJsonStream(stream, jsonSerializer) as PluginOptions ?? tempOptions;
                }
            }
            catch (Exception ex)
            {
                logger.ErrorException("Error loading plugin options for {0} from {1}", ex, pluginFullName, optionsFilePath);
                options = tempOptions;
            }

            return options;
        }
    }

    public void SaveOptions(PluginOptions newOptions)
    {
        if (newOptions == null)
        {
            throw new ArgumentNullException(nameof(newOptions));
        }

        lock (syncRoot)
        {
            using (var stream = fileSystem.GetFileStream(optionsFilePath, FileOpenMode.Create, FileAccessMode.Write))
            {
                jsonSerializer.SerializeToStream(newOptions, stream, new JsonSerializerOptions { Indent = true });
            }

            options = newOptions;
        }
    }
}
