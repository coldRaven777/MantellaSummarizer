using Newtonsoft.Json;

namespace MantellaSummarizer
{
    public class AppConfiguration
    {
        [JsonProperty("apiKey")]
        public string ApiKey { get; set; } = "YOUR-DEEPSEEK-KEY";

        [JsonProperty("maxTokens")]
        public int MaxTokens { get; set; } = 4500;

        [JsonProperty("currentCharacter")]
        public string CurrentCharacter { get; set; } = "default";
        
        [JsonProperty("playerName")]
        public string PlayerName { get; set; } = "";
    }
}