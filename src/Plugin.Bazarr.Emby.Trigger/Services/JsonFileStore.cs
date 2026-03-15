using System;
using System.IO;
using System.Runtime.Serialization.Json;
using System.Text;

namespace Plugin.Bazarr.Emby.Trigger.Services;

internal static class JsonFileStore
{
    public static T? Read<T>(string path) where T : class
    {
        if (!File.Exists(path))
        {
            return null;
        }

        using (var stream = File.OpenRead(path))
        {
            var serializer = new DataContractJsonSerializer(typeof(T));
            return serializer.ReadObject(stream) as T;
        }
    }

    public static T ReadString<T>(string json) where T : class
    {
        using (var stream = new MemoryStream(Encoding.UTF8.GetBytes(json)))
        {
            var serializer = new DataContractJsonSerializer(typeof(T));
            return (T)serializer.ReadObject(stream);
        }
    }

    public static void Write<T>(string path, T value)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path) ?? ".");
        using (var stream = File.Create(path))
        {
            var serializer = new DataContractJsonSerializer(typeof(T));
            serializer.WriteObject(stream, value);
        }
    }
}
