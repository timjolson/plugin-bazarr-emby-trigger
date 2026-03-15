using System;
using System.Runtime.Serialization;

namespace Plugin.Bazarr.Emby.Trigger.Models;

[DataContract]
public class SubtitleFileFingerprint
{
    [DataMember(Order = 1)] public string Path { get; set; } = string.Empty;
    [DataMember(Order = 2)] public long Size { get; set; }
    [DataMember(Order = 3)] public DateTime LastWriteUtc { get; set; }
}
