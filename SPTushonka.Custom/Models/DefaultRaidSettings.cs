using System.Text.Json;
using System.Text.Json.Serialization;
using EFT.Bots;

namespace SPTushonka.Custom.Models;

public class DefaultRaidSettings
{
    public static readonly JsonSerializerOptions SerializerOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        Converters = { new JsonStringEnumConverter() },
    };

    [JsonPropertyName("aiAmount")]
    public EBotAmount AiAmount { get; set; }

    [JsonPropertyName("aiDifficulty")]
    public EBotDifficulty AiDifficulty { get; set; }

    [JsonPropertyName("bossEnabled")]
    public bool BossEnabled { get; set; }

    [JsonPropertyName("scavWars")]
    public bool ScavWars { get; set; }

    [JsonPropertyName("taggedAndCursed")]
    public bool TaggedAndCursed { get; set; }

    [JsonPropertyName("enablePve")]
    public bool EnablePve { get; set; }

    [JsonPropertyName("randomWeather")]
    public bool RandomWeather { get; set; }

    [JsonPropertyName("randomTime")]
    public bool RandomTime { get; set; }
}
