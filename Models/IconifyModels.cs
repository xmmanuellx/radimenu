using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace RadiMenu.Models
{
    public class IconifySearchResult
    {
        [JsonPropertyName("icons")]
        public List<string> Icons { get; set; } = new List<string>();

        [JsonPropertyName("total")]
        public int Total { get; set; }

        [JsonPropertyName("limit")]
        public int Limit { get; set; }
    }

    public class IconifyCollectionResponse
    {
        [JsonPropertyName("prefix")]
        public string Prefix { get; set; } = string.Empty;

        [JsonPropertyName("icons")]
        public Dictionary<string, IconifyIconData> Icons { get; set; } = new Dictionary<string, IconifyIconData>();
    }

    public class IconifyIconData
    {
        [JsonPropertyName("body")]
        public string Body { get; set; } = string.Empty;

        [JsonPropertyName("width")]
        public double? Width { get; set; }

        [JsonPropertyName("height")]
        public double? Height { get; set; }
        
        // Custom property to store the library prefix (e.g., 'mdi', 'fa')
        [JsonIgnore]
        public string Prefix { get; set; } = string.Empty;
        
        // Custom property to store the icon name
        [JsonIgnore]
        public string Name { get; set; } = string.Empty;
    }
}
