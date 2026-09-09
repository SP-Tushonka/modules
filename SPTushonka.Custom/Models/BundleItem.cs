using System.Runtime.Serialization;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace SPTushonka.Custom.Models;

[DataContract]
public struct BundleItem
{
    public static readonly JsonSerializerOptions SerializerOptions = new() { PropertyNameCaseInsensitive = true };

    [DataMember(Name = "filename")]
    [JsonPropertyName("filename")]
    public string FileName { get; set; }

    [DataMember(Name = "crc")]
    [JsonPropertyName("crc")]
    public uint Crc { get; set; }

    [DataMember(Name = "dependencies")]
    [JsonPropertyName("dependencies")]
    public string[] Dependencies { get; set; }

    // exclusive to SPT, ignored in EscapeFromTarkov_Data/StreamingAssets/Windows/Windows.json
    [DataMember(Name = "modpath")]
    [JsonPropertyName("modpath")]
    public string ModPath { get; set; }

    public BundleItem(string filename, uint crc, string[] dependencies, string modpath = "")
    {
        FileName = filename;
        Crc = crc;
        Dependencies = dependencies;
        ModPath = modpath;
    }
}
