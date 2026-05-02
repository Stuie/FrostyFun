using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace CharacterSelect.Data
{
    public struct SkinEntry
    {
        public string DisplayName;
        public string FilePath;
        public string IconPath;
        public bool NeedsGeneration;
        public string JsonPath;
    }

    public class SkinDefinition
    {
        [JsonPropertyName("transforms")]
        public List<SkinTransform> Transforms { get; set; } = new();
    }

    public class SkinTransform
    {
        [JsonPropertyName("name")]
        public string Name { get; set; }
        [JsonPropertyName("action")]
        public string Action { get; set; }
        [JsonPropertyName("where")]
        public SkinWhere Where { get; set; }
        [JsonPropertyName("color")]
        public float[] Color { get; set; }
        [JsonPropertyName("blend")]
        public float Blend { get; set; }
        [JsonPropertyName("degrees")]
        public float Degrees { get; set; }
    }

    public class SkinWhere
    {
        [JsonPropertyName("brightness_min")]
        public float BrightnessMin { get; set; }
        [JsonPropertyName("brightness_max")]
        public float BrightnessMax { get; set; } = 1.0f;
    }
}
