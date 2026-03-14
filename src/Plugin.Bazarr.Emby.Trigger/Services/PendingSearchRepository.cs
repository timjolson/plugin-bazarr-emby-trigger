using System.Collections.Generic;
using System.IO;
using System.Runtime.Serialization;
using Plugin.Bazarr.Emby.Trigger.Models;

namespace Plugin.Bazarr.Emby.Trigger.Services;

public class PendingSearchRepository
{
    private readonly string stateFilePath;

    public PendingSearchRepository(string dataDirectory)
    {
        stateFilePath = Path.Combine(dataDirectory, "pending-searches.json");
    }

    public List<PendingSearchRecord> Load()
    {
        return JsonFileStore.Read<PendingSearchStateDocument>(stateFilePath)?.PendingSearches ?? new List<PendingSearchRecord>();
    }

    public void Save(IEnumerable<PendingSearchRecord> searches)
    {
        JsonFileStore.Write(stateFilePath, new PendingSearchStateDocument { PendingSearches = new List<PendingSearchRecord>(searches) });
    }

    [DataContract]
    private class PendingSearchStateDocument
    {
        [DataMember(Name = "pendingSearches")] public List<PendingSearchRecord> PendingSearches { get; set; } = new List<PendingSearchRecord>();
    }
}
