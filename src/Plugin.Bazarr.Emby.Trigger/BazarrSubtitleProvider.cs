using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using MediaBrowser.Controller.Providers;
using MediaBrowser.Controller.Subtitles;
using MediaBrowser.Model.Providers;

namespace Plugin.Bazarr.Emby.Trigger;

public class BazarrSubtitleProvider : ISubtitleProvider
{
    public string Name => "Bazarr Emby Trigger";

    public IEnumerable<VideoContentType> SupportedMediaTypes => new[] { VideoContentType.Movie, VideoContentType.Episode };

    // Emby expects subtitle providers to return a result list, but this plugin deliberately uses the provider
    // entry point only as a trigger. Returning an empty list keeps Emby happy while Bazarr performs the work.
    public async Task<IEnumerable<RemoteSubtitleInfo>> Search(SubtitleSearchRequest request, CancellationToken cancellationToken)
    {
        var coordinator = PluginRuntime.Coordinator;
        if (coordinator == null)
        {
            PluginRuntime.Logger?.Warn("Subtitle search was requested before the Bazarr runtime finished initializing.");
            return Enumerable.Empty<RemoteSubtitleInfo>();
        }

        await coordinator.QueueAsync(request, cancellationToken).ConfigureAwait(false);
        return Enumerable.Empty<RemoteSubtitleInfo>();
    }

    public Task<SubtitleResponse> GetSubtitles(string id, CancellationToken cancellationToken)
    {
        return Task.FromResult(new SubtitleResponse
        {
            Format = "srt",
            Language = string.Empty,
            IsForced = false,
            Stream = new MemoryStream(),
        });
    }
}
