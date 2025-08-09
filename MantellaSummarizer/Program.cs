// See https://aka.ms/new-console-template for more information

using System;
using System.IO;
using System.Threading.Tasks;
using System.Threading;
using System.Linq;
using MantellaSummarizer;
using Newtonsoft.Json;

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
        while (true)
        {
            Console.Clear();
            Console.WriteLine("| Mantella Summarizer |");
            Console.WriteLine("========================");
            Console.WriteLine($"Player: {_playerName} | Character Folder: {currentCharacter}");
            Console.WriteLine();
            Console.WriteLine("Choose an option:");
            Console.WriteLine($"1. [ENTER] Update only characters exceeding {_maxTokens} tokens");
            Console.WriteLine("2. Update all characters");
            Console.WriteLine("3. Exit program");
            Console.WriteLine();
            Console.Write("Your choice (1-3, or press ENTER for option 1): ");

            var input = Console.ReadLine();
            
            if (string.IsNullOrEmpty(input) || input == "1")
            {
                Console.WriteLine($"\n🔍 Processing characters exceeding {_maxTokens} tokens only...");
                await HandleFolders(client, ProcessingMode.TokenLimitOnly, currentCharacter);
                Console.WriteLine("\nPress any key to return to menu...");
                Console.ReadKey();
            }
            else if (input == "2")
            {
                Console.WriteLine("\n🔄 Processing all characters...");
                await HandleFolders(client, ProcessingMode.AllCharacters, currentCharacter);
                Console.WriteLine("\nPress any key to return to menu...");
                Console.ReadKey();
            }
            else if (input == "3")
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

    /// <summary>
    /// Processing mode enumeration
    /// </summary>
    private enum ProcessingMode
    {
        TokenLimitOnly,
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
            var summaryLastWriteTime = File.GetLastWriteTimeUtc(summaryFile);
            bool hasBeenUpdatedBefore = false;

            if (File.Exists(lastUpdatedFile))
            {
                var lastUpdatedTime = File.GetLastWriteTimeUtc(lastUpdatedFile);
                if (lastUpdatedTime >= summaryLastWriteTime)
                {
                    hasBeenUpdatedBefore = true;
                }
            }

            var content = await File.ReadAllTextAsync(summaryFile);

            if (string.IsNullOrWhiteSpace(content))
            {
                Console.WriteLine($"⚠️  Empty summary file for {cleanNpcName}, skipping.");
                return false;
            }

            var tokenCount = client.GetTokenCount(content);

            // Core logic based on your requirements:
            bool needsResummarize = (mode == ProcessingMode.AllCharacters) || (tokenCount >= _maxTokens);
            bool needsOverrideUpdate = (mode == ProcessingMode.AllCharacters) || !hasBeenUpdatedBefore;

            if (needsResummarize)
            {
                Console.WriteLine($"🔄 Summary for {cleanNpcName} needs updating. Re-summarizing...");
                
                // Backup the old summary before overwriting
                await BackupSummaryFile(summaryFile);

                var newSummary = await client.GetNewSummary(cleanNpcName, _playerName, content);

                if (!string.IsNullOrEmpty(newSummary))
                {
                    await File.WriteAllTextAsync(summaryFile, newSummary);
                    content = newSummary; // Use the new summary for the override update
                    tokenCount = client.GetTokenCount(content); // Recalculate token count
                    Console.WriteLine($"   ✅ Re-summarized {Path.GetFileName(summaryFile)}");
                    needsOverrideUpdate = true; // Always update override after re-summarizing
                }
                else
                {
                    Console.WriteLine($"   ❌ Failed to re-summarize {cleanNpcName}, skipping override update for this character.");
                    return false; // Don't proceed if re-summarization failed
                }
            }

            if (needsOverrideUpdate)
            {
                var npcFolderName = Path.GetFileName(Path.GetDirectoryName(summaryFile));
                var characterOverridesDir = Path.Combine(Directory.GetCurrentDirectory(), "character_overrides");
                var overrideFile = Path.Combine(characterOverridesDir, $"{cleanNpcName}.json");
                return await UpdateCharacterOverride(client, content, cleanNpcName, npcFolderName, overrideFile, tokenCount);
            }
            else
            {
                Console.WriteLine($"✅ SKIPPING: {cleanNpcName} ({tokenCount}/{_maxTokens} tokens) - already up-to-date.");
                return true;
            }
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
        var parts = characterName.Split(CHARACTER_NAME_SEPARATOR);
        return parts.Length > 0 ? parts[0].Trim() : characterName;
    }

    /// <summary>
    /// Generates a new character override JSON file.
    /// </summary>
    /// <param name="client">DeepSeek API client</param>
    /// <param name="content">The summary content to process</param>
    /// <param name="cleanNpcName">Clean NPC name</param>
    /// <param name="npcFolderName">The original folder name of the NPC</param>
    /// <param name="overrideFile">The path to the character override file</param>
    /// <param name="tokenCount">Token count</param>
    /// <returns>True if processed successfully, false otherwise</returns>
    private static async Task<bool> UpdateCharacterOverride(DeepSeekClient client, string content, string cleanNpcName, string npcFolderName, string overrideFile, int tokenCount)
    {
        try
        {
            Console.Write($"🔄 Updating character override for: {cleanNpcName} ({tokenCount}/{_maxTokens} tokens)... ");

            var summary = await client.GetSummary(cleanNpcName, _playerName, content, overrideFile);

            if (!string.IsNullOrEmpty(summary))
            {
                Directory.CreateDirectory(Path.GetDirectoryName(overrideFile));
                await File.WriteAllTextAsync(overrideFile, summary);

                // Get the base conversations directory for the current character
                var baseConversationsDir = Path.Combine(Directory.GetCurrentDirectory(), "conversations", _playerName);
                if (_playerName != "default" && !Directory.Exists(baseConversationsDir))
                {
                    // Fallback for Fallout 4 where the playername is not the folder name in conversations
                    baseConversationsDir = Path.Combine(Directory.GetCurrentDirectory(), "conversations", "default");
                }

                // Use the original npcFolderName to create the correct path
                var npcConversationDir = Path.Combine(baseConversationsDir, npcFolderName);
                Directory.CreateDirectory(npcConversationDir); // Ensure directory exists

                var lastUpdatedFile = Path.Combine(npcConversationDir, "lastUpdated.json");

                await File.WriteAllTextAsync(lastUpdatedFile, $"{{\"lastUpdated\": \"{DateTime.UtcNow:O}\"}}");


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