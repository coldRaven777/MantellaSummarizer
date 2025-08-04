// See https://aka.ms/new-console-template for more information

using System;
using System.IO;
using System.Threading.Tasks;
using System.Threading;
using System.Linq;
using MantellaSummarizer;

class Program
{
    private static int _maxTokens;
    private const string SUMMARY_FILE_PATTERN = "*_summary_1.txt";
    private const string CHARACTER_NAME_SEPARATOR = "-";
    private static readonly SemaphoreSlim _semaphore = new SemaphoreSlim(3, 7); // Max 7 threads?! :\

    static async Task Main(string[] args)
    {
        try
        {
            // Load configuration from config.json
            var config = await ConfigurationManager.LoadConfigurationAsync();
            _maxTokens = config.MaxTokens;
            
            var client = new DeepSeekClient(config.ApiKey);

            await ShowMenuAndProcess(client);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"❌ Critical application error: {ex.Message}");
            Environment.Exit(1);
        }
    }

    /// <summary>
    /// Shows the menu and processes based on user selection
    /// </summary>
    /// <param name="client">DeepSeek API client</param>
    private static async Task ShowMenuAndProcess(DeepSeekClient client)
    {
        while (true)
        {
            Console.Clear();
            Console.WriteLine("| Mantella Summarizer |");
            Console.WriteLine("========================");
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
                await HandleFolders(client, ProcessingMode.TokenLimitOnly);
                Console.WriteLine("\nPress any key to return to menu...");
                Console.ReadKey();
            }
            else if (input == "2")
            {
                Console.WriteLine("\n🔄 Processing all characters...");
                await HandleFolders(client, ProcessingMode.AllCharacters);
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
    private static async Task HandleFolders(DeepSeekClient client, ProcessingMode mode)
    {
        try
        {
            var currentDirectory = Directory.GetCurrentDirectory();
            var characterDirectories = Directory.GetDirectories(currentDirectory);

            if (characterDirectories.Length == 0)
            {
                Console.WriteLine("ℹ️  No character directories found in the current directory.");
                return;
            }

            Console.WriteLine($"🚀 Starting parallel processing of {characterDirectories.Length} character directories...\n");

            var tasks = characterDirectories.Select(async directory =>
            {
                await _semaphore.WaitAsync();
                try
                {
                    return await ProcessCharacterDirectory(client, directory, mode);
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
            Console.WriteLine($"   📊 Processed: {processedCount}");
            Console.WriteLine($"   ⚠️  Errors: {errorCount}");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"❌ Error processing directories: {ex.Message}");
            throw;
        }
    }

    /// <summary>
    /// Processes a specific character directory.
    /// </summary>
    /// <param name="client">DeepSeek API client</param>
    /// <param name="characterDirectory">Character directory path</param>
    /// <param name="mode">Processing mode</param>
    /// <returns>True if processed successfully, false otherwise</returns>
    private static async Task<bool> ProcessCharacterDirectory(DeepSeekClient client, string characterDirectory, ProcessingMode mode)
    {
        try
        {
            var characterName = Path.GetFileName(characterDirectory);
            var summaryFiles = Directory.GetFiles(characterDirectory, SUMMARY_FILE_PATTERN);

            if (summaryFiles.Length == 0)
            {
                Console.WriteLine($"⚠️  No summary files found in: {characterName}");
                return false;
            }

            var successCount = 0;
            foreach (var summaryFile in summaryFiles)
            {
                var success = await ProcessSummaryFileWithTokenCheck(client, summaryFile, characterName, mode);
                if (success) successCount++;
            }

            return successCount > 0;
        }
        catch (Exception ex)
        {
            var dirName = Path.GetFileName(characterDirectory);
            Console.WriteLine($"❌ Error processing directory '{dirName}': {ex.Message}");
            return false;
        }
    }

    /// <summary>
    /// Processes a summary file by checking token count.
    /// </summary>
    /// <param name="client">DeepSeek API client</param>
    /// <param name="summaryFile">Summary file path</param>
    /// <param name="characterName">Character name</param>
    /// <param name="mode">Processing mode</param>
    /// <returns>True if processed successfully, false otherwise</returns>
    private static async Task<bool> ProcessSummaryFileWithTokenCheck(DeepSeekClient client, string summaryFile, string characterName, ProcessingMode mode)
    {
        try
        {
            if (!File.Exists(summaryFile))
            {
                Console.WriteLine($"⚠️  File not found: {summaryFile}");
                return false;
            }

            var content = await File.ReadAllTextAsync(summaryFile);

            if (string.IsNullOrWhiteSpace(content))
            {
                Console.WriteLine($"⚠️  Empty file found for {characterName}, skipping.");
                return false;
            }

            var tokenCount = client.GetTokenCount(content);
            var cleanCharacterName = ExtractCleanCharacterName(characterName);

            switch (mode)
            {
                case ProcessingMode.TokenLimitOnly:
                    if (tokenCount >= _maxTokens)
                    {
                        return await ProcessFullSummary(client, summaryFile, cleanCharacterName, tokenCount);
                    }
                    else
                    {
                        Console.WriteLine($"🔄SKIPPING: {cleanCharacterName} ({tokenCount}/{_maxTokens} tokens - below limit)");
                        return true;
                    }

                case ProcessingMode.AllCharacters:
                    return await ProcessFullSummary(client, summaryFile, cleanCharacterName, tokenCount);

                default:
                    Console.WriteLine($"❌ Invalid processing mode");
                    return false;
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"❌ Error processing file '{Path.GetFileName(summaryFile)}': {ex.Message}");
            return false;
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
    /// Processes the complete character summary (when tokens >= MAX_TOKENS).
    /// </summary>
    /// <param name="client">DeepSeek API client</param>
    /// <param name="summaryFile">Summary file path</param>
    /// <param name="cleanCharacterName">Clean character name</param>
    /// <param name="tokenCount">Token count</param>
    /// <returns>True if processed successfully, false otherwise</returns>
    private static async Task<bool> ProcessFullSummary(DeepSeekClient client, string summaryFile, string cleanCharacterName, int tokenCount)
    {
        try
        {
            Console.Write($"🔄 Processing complete summary for: {cleanCharacterName} ({tokenCount}/{_maxTokens} tokens)... ");

            var content = await File.ReadAllTextAsync(summaryFile);
            var summary = await client.GetSummary(cleanCharacterName, content);

            if (!string.IsNullOrEmpty(summary))
            {
                await File.WriteAllTextAsync(summaryFile, summary);
                Console.WriteLine("✅ complete summary finished!");
                return true;
            }
            else
            {
                Console.WriteLine("❌ failed to get summary.");
                return false;
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"❌ error: {ex.Message}");
            // Don't write error to file - just log it and return false
            return false;
        }
    }
}