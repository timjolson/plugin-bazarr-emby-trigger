using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Plugin.Bazarr.Emby.Trigger.Models;

namespace Plugin.Bazarr.Emby.Trigger.Services;

public class SubtitleSnapshotService
{
    private static readonly HashSet<string> SupportedExtensions = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        ".srt", ".ass", ".ssa", ".sub", ".vtt", ".sup",
    };

    // Emby refreshes media after a subtitle lands on disk, so a file-based snapshot is the least invasive
    // state we can keep without touching Emby's own metadata database.
    public List<SubtitleFileFingerprint> Capture(string mediaPath)
    {
        if (string.IsNullOrWhiteSpace(mediaPath) || !File.Exists(mediaPath))
        {
            return new List<SubtitleFileFingerprint>();
        }

        var directory = Path.GetDirectoryName(mediaPath);
        var basename = Path.GetFileNameWithoutExtension(mediaPath);
        if (string.IsNullOrWhiteSpace(directory) || string.IsNullOrWhiteSpace(basename))
        {
            return new List<SubtitleFileFingerprint>();
        }

        return Directory.EnumerateFiles(directory)
            .Where(path => SupportedExtensions.Contains(Path.GetExtension(path)))
            .Where(path => Path.GetFileName(path).StartsWith(basename + ".", StringComparison.OrdinalIgnoreCase))
            .Select(ToFingerprint)
            .OrderBy(item => item.Path, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    public bool HasNewOrModifiedSubtitle(string mediaPath, IReadOnlyCollection<SubtitleFileFingerprint> beforeSnapshot)
    {
        var before = beforeSnapshot.ToDictionary(item => item.Path, StringComparer.OrdinalIgnoreCase);
        var after = Capture(mediaPath);

        return after.Any(item => !before.TryGetValue(item.Path, out var original)
            || original.Size != item.Size
            || original.LastWriteUtc != item.LastWriteUtc);
    }

    private static SubtitleFileFingerprint ToFingerprint(string path)
    {
        var info = new FileInfo(path);
        return new SubtitleFileFingerprint
        {
            Path = path,
            Size = info.Length,
            LastWriteUtc = info.LastWriteTimeUtc,
        };
    }
}
