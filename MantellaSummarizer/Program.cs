using System;
using System.IO;
using System.Threading.Tasks;
using System.Threading;
using System.Linq;
using MantellaSummarizer;
using Newtonsoft.Json;
using System.Text.RegularExpressions;

class LastUpdateInfo
{
    [JsonProperty("lastUpdated")]
    public DateTime LastUpdated { get; set; }

    [JsonProperty("summaryFileSize")]
    public long SummaryFileSize { get; set; }
}

class Program
{
    private static int _maxTokens;
    private static string? _playerName;
    private const string SUMMARY_FILE_PATTERN = "*_summary_1.txt";
    private const string CHARACTER_NAME_SEPARATOR = "-";
    private static readonly SemaphoreSlim _semaphore = new SemaphoreSlim(3, 7); // Max 7 threads?! :\

    static async Task Main(string[] args)
    {
        try
        {
            // Check for required directories
            if (!CheckRequiredDirectories())
            {
                Console.WriteLine("❌ Error: You must put this program on the correct mantella/Data folder for your game eg: Data/Fallout4 or Data/Skyrim");
                Console.WriteLine("Press any key to exit...");
                Console.ReadKey();
                return;
            }

            // Load configuration from config.json
            var config = await LoadConfigurationAsync();
            if (config == null)
            {
                return; // Exit if config is not loaded or needs editing
            }
            _maxTokens = config.MaxTokens;
            _playerName = config.PlayerName;
            
            var client = new DeepSeekClient(config.ApiKey);

            await ShowMenuAndProcess(client, config.CurrentCharacter);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"❌ Critical application error: {ex.Message}");
            Environment.Exit(1);
        }
    }

    private static bool CheckRequiredDirectories()
    {
        var currentDirectory = Directory.GetCurrentDirectory();
        var characterOverridesPath = Path.Combine(currentDirectory, "character_overrides");
        var conversationsPath = Path.Combine(currentDirectory, "conversations");

        return Directory.Exists(characterOverridesPath) && Directory.Exists(conversationsPath);
    }

    private static async Task<AppConfiguration?> LoadConfigurationAsync()
    {
        var configPath = Path.Combine(Directory.GetCurrentDirectory(), "config.json");

        if (!File.Exists(configPath))
        {
            return await CreateConfigurationAsync(configPath);
        }

        var json = await File.ReadAllTextAsync(configPath);
        var config = JsonConvert.DeserializeObject<AppConfiguration>(json);

        if (string.IsNullOrWhiteSpace(config.ApiKey) || config.ApiKey == "YOUR-DEEPSEEK-KEY")
        {
            Console.WriteLine("📝 You must edit your config.json to put the Api-Key from deepseek api");
            Console.WriteLine("Press any key to exit...");
            Console.ReadKey();
            return null;
        }

        if (config.CurrentCharacter == "default" && string.IsNullOrWhiteSpace(config.PlayerName))
        {
             Console.WriteLine("📝 Your config is from an old version, please re-create it.");
             File.Delete(configPath);
             return await CreateConfigurationAsync(configPath);
        }


        return config;
    }

    private static async Task<AppConfiguration?> CreateConfigurationAsync(string configPath)
    {
        AppConfiguration newConfig;
        while (true)
        {
            Console.Write("Enter your character's name: ");
            var characterName = Console.ReadLine();

            if (string.IsNullOrWhiteSpace(characterName))
            {
                Console.WriteLine("Character name cannot be empty.");
                continue;
            }

            var conversationsDirectory = Path.Combine(Directory.GetCurrentDirectory(), "conversations");
            var characterConversationPath = Path.Combine(conversationsDirectory, characterName);

            if (Directory.Exists(characterConversationPath))
            {
                // Skyrim workflow
                newConfig = new AppConfiguration { CurrentCharacter = characterName, PlayerName = characterName };
                break;
            }
            else
            {
                // Potential Fallout 4 workflow
                Console.Write($"Character folder '{characterName}' not found. Are you playing Fallout 4? (Y/n): ");
                var isFallout = Console.ReadLine();

                if (string.IsNullOrEmpty(isFallout) || isFallout.Equals("y", StringComparison.OrdinalIgnoreCase))
                {
                    newConfig = new AppConfiguration { CurrentCharacter = "default", PlayerName = characterName };
                    break;
                }
                else
                {
                    Console.WriteLine("Character name is incorrect. Please write it again.");
                }
            }
        }

        var jsonContent = JsonConvert.SerializeObject(newConfig, Formatting.Indented);
        await File.WriteAllTextAsync(configPath, jsonContent);

        Console.WriteLine("📝 You must edit your config.json to put the Api-Key from deepseek api");
        Console.WriteLine("Press any key to exit...");
        Console.ReadKey();
        return null;
    }


    /// <summary>
    /// Shows the menu and processes based on user selection
    /// </summary>
    /// <param name="client">DeepSeek API client</param>
    /// <private>string currentCharacter</private>
    private static async Task ShowMenuAndProcess(DeepSeekClient client, string currentCharacter)
    {
        var config = await LoadConfigurationAsync(); // Reload config in case it was changed

        while (true)
        {
            Console.Clear();
            Console.WriteLine("| Mantella Summarizer |");
            Console.WriteLine("========================");
            Console.WriteLine($"Player: {config.PlayerName} | Character Folder: {config.CurrentCharacter}");
            Console.WriteLine();
            Console.WriteLine("Choose an option:");
            Console.WriteLine("1. [ENTER] Update only characters with changed conversations");
            Console.WriteLine("2. Update all characters");
            Console.WriteLine("3. Change Character");
            Console.WriteLine("4. Exit program");
            Console.WriteLine();
            Console.Write("Your choice (1-4, or press ENTER for option 1): ");

            var input = Console.ReadLine();
            
            if (string.IsNullOrEmpty(input) || input == "1")
            {
                Console.WriteLine($"\n🔍 Processing characters with changed conversations...");
                await HandleFolders(client, ProcessingMode.ChangedOnly, config.CurrentCharacter);
                Console.WriteLine("\nPress any key to return to menu...");
                Console.ReadKey();
            }
            else if (input == "2")
            {
                Console.WriteLine("\n🔄 Processing all characters...");
                await HandleFolders(client, ProcessingMode.AllCharacters, config.CurrentCharacter);
                Console.WriteLine("\nPress any key to return to menu...");
                Console.ReadKey();
            }
            else if (input == "3")
            {
                await ChangeCharacterAsync(config);
                config = await LoadConfigurationAsync(); // Reload config to reflect changes
            }
            else if (input == "4")
            {
                Console.WriteLine("\n👋 Goodbye!");
                return;
            }
            else
            {
                Console.WriteLine("\n❌ Invalid option. Press any key to try again...");
                Console.ReadKey();
            }
        }
    }

    private static async Task ChangeCharacterAsync(AppConfiguration config)
    {
        var conversationsPath = Path.Combine(Directory.GetCurrentDirectory(), "conversations");
        if (!Directory.Exists(conversationsPath))
        {
            Console.WriteLine($"\n❌ The conversations directory was not found at: {conversationsPath}");
            Console.WriteLine("Press any key to continue...");
            Console.ReadKey();
            return;
        }

        var characterFolders = Directory.GetDirectories(conversationsPath)
                                        .Select(Path.GetFileName)
                                        .ToList();

        if (characterFolders.Count == 0)
        {
            Console.WriteLine("\nℹ️ No character folders found in the conversations directory.");
            Console.WriteLine("Press any key to continue...");
            Console.ReadKey();
            return;
        }

        Console.WriteLine("\nPlease select a character:");
        for (int i = 0; i < characterFolders.Count; i++)
        {
            Console.WriteLine($"{i + 1}. {characterFolders[i]}");
        }

        Console.Write("\nEnter the number of the character: ");
        if (int.TryParse(Console.ReadLine(), out int selection) && selection > 0 && selection <= characterFolders.Count)
        {
            var selectedFolder = characterFolders[selection - 1];
            config.CurrentCharacter = selectedFolder;

            if (selectedFolder.Equals("default", StringComparison.OrdinalIgnoreCase))
            {
                Console.Write("Enter the player name: ");
                config.PlayerName = Console.ReadLine();
            }
            else
            {
                // Remove trailing digits to suggest a clean name (e.g., "Arya1" -> "Arya")
                var suggestedName = Regex.Replace(selectedFolder, @"\d+$", "");

                Console.Write($"Is the player name also '{suggestedName}'? (Y/n): ");
                var response = Console.ReadLine();
                if (string.IsNullOrEmpty(response) || response.Equals("y", StringComparison.OrdinalIgnoreCase))
                {
                    config.PlayerName = suggestedName;
                }
                else
                {
                    Console.Write("Enter the player name: ");
                    config.PlayerName = Console.ReadLine();
                }
            }

            await SaveConfigurationAsync(config);
            Console.WriteLine($"\n✅ Character changed to: {config.CurrentCharacter} | Player: {config.PlayerName}");
        }
        else
        {
            Console.WriteLine("\n❌ Invalid selection.");
        }
        Console.WriteLine("Press any key to continue...");
        Console.ReadKey();
    }

    private static async Task SaveConfigurationAsync(AppConfiguration config)
    {
        var configPath = Path.Combine(Directory.GetCurrentDirectory(), "config.json");
        var jsonContent = JsonConvert.SerializeObject(config, Formatting.Indented);
        await File.WriteAllTextAsync(configPath, jsonContent);
    }

    /// <summary>
    /// Processing mode enumeration
    /// </summary>
    private enum ProcessingMode
    {
        ChangedOnly,
        AllCharacters
    }

    /// <summary>
    /// Processes all character directories in the current directory with controlled parallelism.
    /// </summary>
    /// <param name="client">DeepSeek API client</param>
    /// <param name="mode">Processing mode</param>
    private static async Task HandleFolders(DeepSeekClient client, ProcessingMode mode, string currentCharacter)
    {
        try
        {
            var conversationsDirectory = Path.Combine(Directory.GetCurrentDirectory(), "conversations", currentCharacter);

            if (!Directory.Exists(conversationsDirectory))
            {
                Console.WriteLine($"ℹ️  Character directory not found: {conversationsDirectory}");
                return;
            }

            var npcDirectories = Directory.GetDirectories(conversationsDirectory);

            if (npcDirectories.Length == 0)
            {
                Console.WriteLine($"ℹ️  No NPC directories found in '{conversationsDirectory}'.");
                return;
            }

            Console.WriteLine($"🚀 Starting parallel processing of {npcDirectories.Length} NPC directories...\n");

            var tasks = npcDirectories.Select(async directory =>
            {
                await _semaphore.WaitAsync();
                try
                {
                    return await ProcessNpcDirectory(client, directory, mode);
                }
                finally
                {
                    _semaphore.Release();
                }
            });

            var results = await Task.WhenAll(tasks);
            var processedCount = results.Count(r => r);
            var errorCount = results.Length - processedCount;

            Console.WriteLine($"\n✨ Parallel processing completed!");
            Console.WriteLine($"   📊 Processed NPCs: {processedCount}");
            Console.WriteLine($"   ⚠️  Errors: {errorCount}");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"❌ Error processing directories: {ex.Message}");
            throw;
        }
    }

    /// <summary>
    /// Processes a specific NPC directory.
    /// </summary>
    /// <param name="client">DeepSeek API client</param>
    /// <param name="npcDirectory">NPC directory path</param>
    /// <param name="mode">Processing mode</param>
    /// <returns>True if processed successfully, false otherwise</returns>
    private static async Task<bool> ProcessNpcDirectory(DeepSeekClient client, string npcDirectory, ProcessingMode mode)
    {
        try
        {
            var npcFolderName = Path.GetFileName(npcDirectory);
            var cleanNpcName = ExtractCleanCharacterName(npcFolderName);

            // Find the summary file, which should be named like "NPC Name_summary_1.txt"
            var summaryFile = Directory.GetFiles(npcDirectory, $"{cleanNpcName}{SUMMARY_FILE_PATTERN.Substring(1)}").FirstOrDefault();

            if (summaryFile == null)
            {
                // Fallback for any file ending with _summary_1.txt if the named one isn't found
                summaryFile = Directory.GetFiles(npcDirectory, SUMMARY_FILE_PATTERN).FirstOrDefault();
            }

            if (summaryFile == null)
            {
                Console.WriteLine($"⚠️  No summary file found for: {cleanNpcName}");
                return false;
            }

            return await ProcessNpcSummary(client, summaryFile, cleanNpcName, mode);
        }
        catch (Exception ex)
        {
            var dirName = Path.GetFileName(npcDirectory);
            Console.WriteLine($"❌ Error processing NPC directory '{dirName}': {ex.Message}");
            return false;
        }
    }

    /// <summary>
    /// Processes an NPC's summary file based on token count and update history.
    /// </summary>
    /// <param name="client">DeepSeek API client</param>
    /// <param name="summaryFile">Summary file path</param>
    /// <param name="cleanNpcName">Clean NPC name</param>
    /// <param name="mode">Processing mode</param>
    /// <returns>True if processed successfully, false otherwise</returns>
    private static async Task<bool> ProcessNpcSummary(DeepSeekClient client, string summaryFile, string cleanNpcName, ProcessingMode mode)
    {
        try
        {
            var lastUpdatedFile = Path.Combine(Path.GetDirectoryName(summaryFile), "lastUpdated.json");
            var summaryFileInfo = new FileInfo(summaryFile);
            long currentSummarySize = summaryFileInfo.Length;
            long lastSummarySize = -1;

            if (File.Exists(lastUpdatedFile))
            {
                try
                {
                    var lastUpdateJson = await File.ReadAllTextAsync(lastUpdatedFile);
                    var lastUpdateInfo = JsonConvert.DeserializeObject<LastUpdateInfo>(lastUpdateJson);
                    if (lastUpdateInfo != null)
                    {
                        lastSummarySize = lastUpdateInfo.SummaryFileSize;
                    }
                }
                catch (JsonException)
                {
                    // Handle case where lastUpdated.json is malformed or from an old version
                    Console.WriteLine($"   ⚠️  Could not parse 'lastUpdated.json' for {cleanNpcName}. Assuming it needs an update.");
                    lastSummarySize = -1; // Force update
                }
            }

            var content = await File.ReadAllTextAsync(summaryFile);

            if (string.IsNullOrWhiteSpace(content))
            {
                Console.WriteLine($"⚠️  Empty summary file for {cleanNpcName}, skipping.");
                return false;
            }

            var tokenCount = client.GetTokenCount(content);

            // Core logic based on file size and processing mode:
            bool needsUpdate = (mode == ProcessingMode.AllCharacters) || (currentSummarySize != lastSummarySize);

            if (!needsUpdate)
            {
                Console.WriteLine($"✅ SKIPPING: {cleanNpcName} - no changes detected.");
                return true;
            }

            // From here, we know an update is needed.
            // First, check if the summary needs to be condensed due to token limits.
            if (tokenCount >= _maxTokens)
            {
                Console.WriteLine($"🔄 Summary for {cleanNpcName} is too large ({tokenCount}/{_maxTokens}). Re-summarizing...");
                
                await BackupSummaryFile(summaryFile);
                var newSummary = await client.GetNewSummary(cleanNpcName, _playerName, content);

                if (!string.IsNullOrEmpty(newSummary))
                {
                    await File.WriteAllTextAsync(summaryFile, newSummary);
                    content = newSummary; // Use the new summary for the override update
                    tokenCount = client.GetTokenCount(content); // Recalculate token count
                    Console.WriteLine($"   ✅ Re-summarized {Path.GetFileName(summaryFile)}");
                }
                else
                {
                    Console.WriteLine($"   ❌ Failed to re-summarize {cleanNpcName}, skipping override update for this character.");
                    return false; // Don't proceed if re-summarization failed
                }
            }

            // Finally, update the character override file.
            var npcFolderName = Path.GetFileName(Path.GetDirectoryName(summaryFile));
            var characterOverridesDir = Path.Combine(Directory.GetCurrentDirectory(), "character_overrides");
            var overrideFile = Path.Combine(characterOverridesDir, $"{cleanNpcName}.json");
            // Get the final size of the summary file before updating the override
            long finalSummarySize = new FileInfo(summaryFile).Length;
            var npcDirectoryPath = Path.GetDirectoryName(summaryFile);
            return await UpdateCharacterOverride(client, content, cleanNpcName, npcDirectoryPath, overrideFile, tokenCount, finalSummarySize);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"❌ Error processing summary for '{cleanNpcName}': {ex.Message}");
            return false;
        }
    }

    /// <summary>
    /// Creates a backup of the summary file with rotation (keeps max 2).
    /// </summary>
    /// <param name="summaryFilePath">The full path to the summary file.</param>
    private static async Task BackupSummaryFile(string summaryFilePath)
    {
        try
        {
            var backupDir = Path.Combine(Path.GetDirectoryName(summaryFilePath), "backup");
            Directory.CreateDirectory(backupDir);

            var backup1 = Path.Combine(backupDir, "summary_backup_1.txt");
            var backup2 = Path.Combine(backupDir, "summary_backup_2.txt");

            // Rotation logic
            if (File.Exists(backup2))
            {
                File.Delete(backup2);
            }
            if (File.Exists(backup1))
            {
                File.Move(backup1, backup2);
            }
            
            // Use ReadAllBytesAsync and WriteAllBytesAsync for a true async copy
            var content = await File.ReadAllBytesAsync(summaryFilePath);
            await File.WriteAllBytesAsync(backup1, content);
            
            Console.WriteLine($"   💾 Backed up {Path.GetFileName(summaryFilePath)}");
        }
        catch (Exception ex)
        {
            // Log the error but don't stop the main process
            Console.WriteLine($"   ⚠️  Could not create backup for {Path.GetFileName(summaryFilePath)}: {ex.Message}");
        }
    }

    /// <summary>
    /// Extracts the clean character name by removing the hexadecimal code.
    /// </summary>
    /// <param name="characterName">Full character name (ex: "Anya Korolova - 0017EA")</param>
    /// <returns>Clean character name (ex: "Anya Korolova")</returns>
    private static string ExtractCleanCharacterName(string characterName)
    {
        int lastHyphenIndex = characterName.LastIndexOf(CHARACTER_NAME_SEPARATOR, StringComparison.Ordinal);
        
        // If a hyphen is found and it's not the first character
        if (lastHyphenIndex > 0)
        {
            // Return the part of the string before the last hyphen
            return characterName.Substring(0, lastHyphenIndex).Trim();
        }
        
        // If no hyphen is found, return the original name
        return characterName;
    }

    /// <summary>
    /// Generates a new character override JSON file.
    /// </summary>
    /// <param name="client">DeepSeek API client</param>
    /// <param name="content">The summary content to process</param>
    /// <param name="cleanNpcName">Clean NPC name</param>
    /// <param name="npcDirectoryPath">The path to the original NPC conversation directory</param>
    /// <param name="overrideFile">The path to the character override file</param>
    /// <param name="tokenCount">Token count</param>
    /// <returns>True if processed successfully, false otherwise</returns>
    private static async Task<bool> UpdateCharacterOverride(DeepSeekClient client, string content, string cleanNpcName, string npcDirectoryPath, string overrideFile, int tokenCount, long summaryFileSize)
    {
        try
        {
            Console.Write($"🔄 Updating character override for: {cleanNpcName} ({tokenCount}/{_maxTokens} tokens)... ");

            var summary = await client.GetSummary(cleanNpcName, _playerName, content, overrideFile);

            if (!string.IsNullOrEmpty(summary))
            {
                Directory.CreateDirectory(Path.GetDirectoryName(overrideFile));
                await File.WriteAllTextAsync(overrideFile, summary);

                // The correct path for lastUpdated.json is inside the original NPC directory.
                var lastUpdatedFile = Path.Combine(npcDirectoryPath, "lastUpdated.json");
                var updateInfo = new LastUpdateInfo
                {
                    LastUpdated = DateTime.UtcNow,
                    SummaryFileSize = summaryFileSize
                };
                await File.WriteAllTextAsync(lastUpdatedFile, JsonConvert.SerializeObject(updateInfo, Formatting.Indented));


                Console.WriteLine("✅ override updated!");
                return true;
            }
            else
            {
                Console.WriteLine("❌ failed to get summary for override.");
                return false;
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"❌ error during override update: {ex.Message}");
            return false;
        }
    }
}