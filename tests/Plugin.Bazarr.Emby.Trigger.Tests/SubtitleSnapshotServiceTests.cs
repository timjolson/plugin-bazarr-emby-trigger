using Plugin.Bazarr.Emby.Trigger.Services;

namespace Plugin.Bazarr.Emby.Trigger.Tests;

public class SubtitleSnapshotServiceTests
{
    [Fact]
    public void HasNewOrModifiedSubtitle_ReturnsTrueWhenNewSubtitleAppears()
    {
        var service = new SubtitleSnapshotService();
        var directory = Directory.CreateTempSubdirectory();
        try
        {
            var mediaPath = Path.Combine(directory.FullName, "Movie (2024).mkv");
            File.WriteAllText(mediaPath, "video");
            var baseline = service.Capture(mediaPath);

            File.WriteAllText(Path.Combine(directory.FullName, "Movie (2024).en.srt"), "subtitle");

            Assert.True(service.HasNewOrModifiedSubtitle(mediaPath, baseline));
        }
        finally
        {
            directory.Delete(true);
        }
    }

    [Fact]
    public void Capture_IgnoresUnrelatedSubtitleFiles()
    {
        var service = new SubtitleSnapshotService();
        var directory = Directory.CreateTempSubdirectory();
        try
        {
            var mediaPath = Path.Combine(directory.FullName, "Episode.mkv");
            File.WriteAllText(mediaPath, "video");
            File.WriteAllText(Path.Combine(directory.FullName, "Episode.en.srt"), "match");
            File.WriteAllText(Path.Combine(directory.FullName, "Different.en.srt"), "ignore");

            var snapshot = service.Capture(mediaPath);

            Assert.Single(snapshot);
            Assert.EndsWith("Episode.en.srt", snapshot[0].Path);
        }
        finally
        {
            directory.Delete(true);
        }
    }
}
