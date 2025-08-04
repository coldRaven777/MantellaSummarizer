using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Newtonsoft.Json;

namespace MantellaSummarizer
{
    public static class ConfigurationManager
    {
        private const string CONFIG_FILE_NAME = "config.json";

        public static async Task<AppConfiguration> LoadConfigurationAsync()
        {
            var configPath = Path.Combine(Directory.GetCurrentDirectory(), CONFIG_FILE_NAME);

            // If config file doesn't exist, create an empty one
            if (!File.Exists(configPath))
            {
                Console.WriteLine($"⚠️  Configuration file not found. Creating empty '{CONFIG_FILE_NAME}'...");
                await CreateEmptyConfigurationAsync(configPath);
                Console.WriteLine($"📝 Please edit the '{CONFIG_FILE_NAME}' file with your API key and settings before continuing.");
                Console.WriteLine("Press any key to exit...");
                Console.ReadKey();
                Environment.Exit(0);
            }

            try
            {
                var jsonContent = await File.ReadAllTextAsync(configPath);
                var config = JsonConvert.DeserializeObject<AppConfiguration>(jsonContent);

                if (config == null)
                {
                    throw new InvalidOperationException("Failed to deserialize configuration.");
                }

                ValidateConfiguration(config);
                return config;
            }
            catch (JsonException ex)
            {
                throw new InvalidOperationException($"❌ Error reading configuration file: Invalid JSON. {ex.Message}");
            }
            catch (Exception ex)
            {
                throw new InvalidOperationException($"❌ Error loading configuration: {ex.Message}");
            }
        }

        private static async Task CreateEmptyConfigurationAsync(string configPath)
        {
            var emptyConfig = new AppConfiguration();
            var jsonContent = JsonConvert.SerializeObject(emptyConfig, Formatting.Indented);
            await File.WriteAllTextAsync(configPath, jsonContent);
        }

        private static void ValidateConfiguration(AppConfiguration config)
        {
            var errors = new List<string>();

            if (string.IsNullOrWhiteSpace(config.ApiKey))
            {
                errors.Add("API Key is empty or not configured");
            }
            else if (!config.ApiKey.StartsWith("sk-"))
            {
                errors.Add("API Key must start with 'sk-' (DeepSeek API keys typically start with 'sk-' or 'sk-or-v1-')");
            }

            if (config.MaxTokens <= 0)
            {
                errors.Add("MaxTokens must be greater than 0");
            }
            else if (config.MaxTokens > 100000)
            {
                errors.Add("MaxTokens seems too high (recommended maximum: 100,000)");
            }

            if (errors.Count > 0)
            {
                var errorMessage = "❌ Invalid configuration:\n" + string.Join("\n", errors.Select(e => $"   • {e}"));
                errorMessage += $"\n\nPlease correct the 'config.json' file and try again.";
                throw new InvalidOperationException(errorMessage);
            }
        }
    }
}