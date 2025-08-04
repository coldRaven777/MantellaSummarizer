using Newtonsoft.Json;

namespace MantellaSummarizer
{
    public class AppConfiguration
    {
        [JsonProperty("apiKey")]
        public string ApiKey { get; set; } = "";

        [JsonProperty("maxTokens")]
        public int MaxTokens { get; set; } = 0;
    }
}