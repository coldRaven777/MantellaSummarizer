using System;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;
using Newtonsoft.Json;
using System.IO;

public class DeepSeekClient
{
    private readonly HttpClient _httpClient;
    private readonly string _apiKey;
    private const string ApiUrl = "https://api.deepseek.com/chat/completions";
    const int QUEST_GIVER_CHANCE = 60; // % chance of being a quest giver
    private Random _randomizer { get; } = new Random();
    
    public DeepSeekClient(string apiKey)
    {
        _apiKey = apiKey;
        _httpClient = new HttpClient();
        _httpClient.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", _apiKey);
    }

    private async Task<string> GetCompletionAsync(string prompt)
    {
        var requestBody = new
        {
            model = "deepseek-chat",
            messages = new[]
            {
                new { role = "user", content = prompt }
            }
        };

        var jsonRequest = JsonConvert.SerializeObject(requestBody);
        var content = new StringContent(jsonRequest, Encoding.UTF8, "application/json");

        var response = await _httpClient.PostAsync(ApiUrl, content);

        if (response.IsSuccessStatusCode)
        {
            var jsonResponse = await response.Content.ReadAsStringAsync();
            
            try
            {
                dynamic responseObject = JsonConvert.DeserializeObject(jsonResponse);
                
                // Validate response structure
                if (responseObject?.choices == null || responseObject.choices.Count == 0)
                {
                    throw new InvalidOperationException("Invalid API response: No choices returned");
                }
                
                var messageContent = responseObject.choices[0]?.message?.content?.ToString();
                if (string.IsNullOrEmpty(messageContent))
                {
                    throw new InvalidOperationException("Invalid API response: Empty message content");
                }
                
                return messageContent;
            }
            catch (JsonException ex)
            {
                throw new InvalidOperationException($"Failed to parse API response: {ex.Message}");
            }
        }
        else
        {
            var errorContent = await response.Content.ReadAsStringAsync();
            
            // Try to parse error details from the response
            string errorMessage = $"API request failed with status {response.StatusCode}";
            try
            {
                dynamic errorObject = JsonConvert.DeserializeObject(errorContent);
                if (errorObject?.error?.message != null)
                {
                    errorMessage = $"API Error: {errorObject.error.message}";
                }
            }
            catch
            {
                // If we can't parse the error, use the raw content
                errorMessage = $"API Error ({response.StatusCode}): {errorContent}";
            }
            
            throw new HttpRequestException(errorMessage);
        }
    }
    
    public async Task<string?> GetSummary(string character, string playername, string content, string overrideFile)
    {
        try
        {
            string oldBiography = "";
            if (File.Exists(overrideFile))
            {
                var jsonContent = await File.ReadAllTextAsync(overrideFile);
                if (!string.IsNullOrWhiteSpace(jsonContent))
                {
                    try
                    {
                        dynamic existingData = JsonConvert.DeserializeObject(jsonContent);
                        oldBiography = existingData?.bio;
                    }
                    catch (JsonException)
                    {
                        oldBiography = ""; 
                    }
                }
            }

            string profilingPrompt = ProfilingPrompt(character, playername, QUEST_GIVER_CHANCE) + "\n\n" + content;
            string characterProfile = await GetCompletionAsync(profilingPrompt);

            string introductionPrompt = CharacterIntroductionForProfiling(oldBiography, character) + "\n\n" + content;
            string characterIntroduction = await GetCompletionAsync(introductionPrompt);

            string bio = characterProfile + "\n\n" + characterIntroduction;

            var characterData = new
            {
                name = character,
                bio = bio
            };

            return JsonConvert.SerializeObject(characterData, Formatting.Indented);
        }
        catch (Exception ex)
        {
            // Log the error but don't return it as content
            Console.WriteLine($"❌ Error getting summary for {character}: {ex.Message}");
            throw; // Re-throw to let the caller handle it
        }
    }

    public async Task<string?> GetNewSummary(string character, string playername, string content)
    {
        try
        {
            string summarizingPrompt = SummarizingPrompt(character, playername, QUEST_GIVER_CHANCE) + "\n\n" + content;
            return await GetCompletionAsync(summarizingPrompt);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"❌ Error getting new summary for {character}: {ex.Message}");
            throw; // Re-throw to let the caller handle it
        }
    }

    public int GetTokenCount(string text)
    {
        if (string.IsNullOrEmpty(text))
        {
            return 0;
        }

        // For English-like text: ~4 chars per token
        // For non-English: ~3 chars per token (more conservative estimate)
        // For code: can be as low as 1-2 chars per token

        // This is a simplified estimate - actual tokenization would require a full tokenizer
        int estimatedTokenCount = text.Length / (IsLikelyNonEnglish(text) ? 3 : 4);

        // Ensure at least 1 token for very short strings
        return Math.Max(1, estimatedTokenCount);
    }

    // Example helper method (implementation would depend on your needs)
    private static bool IsLikelyNonEnglish(string text)
    {
        // Simple heuristic - could be expanded
        return text.Any(c => c > 127);
    }

    private string ProfilingPrompt(string characterName, string playerName, int questGiverChance)
    {
        return $"""
        Update {characterName}'s profile using their memories. Follow EXACTLY:
        **Rules**:
        - Two field types: 
          • Frequent changes: Update often (e.g., location, occupation).
          • Static fields: Rarely change (e.g., race, core ideology).
        - Use "UNKNOWN" for missing info.
        - Only named individuals in Relationships (no groups/titles).
        - Output PLAIN TEXT. No JSON/formatting.
        - NEVER use double curly braces in output.

        **Profile Format**:
        Character Name: {characterName}
        Age: [number/age group/UNKNOWN]
        Current Occupation: [job title]
        Race: [race/UNKNOWN]
        Last known location: [location]
        Religion: [beliefs/UNKNOWN]
        Ideology: [brief phrase/UNKNOWN]
        Literacy Level: [lore-appropriate deduction]
        Personality Traits: [comma-separated list]
        Moral Compass: [Good/Evil/Neutral/etc]
        Traumas: [list/None]
        Fears: [list]
        Hobbies: [list]
        Physical Status: [injuries/scars]
        Sexual Status: [libido + partners list]
        ********RELATIONSHIPS********:
        **[Person1]**: [relationship type]
        **[Person2]**: [relationship type]

        Use memories below:
        """;
    }

    private string CharacterIntroductionForProfiling(string oldBiography, string characterName)
    {
        string oldClause = string.IsNullOrEmpty(oldBiography) ? "" :
            $"""If memories DON'T contradict/add concrete facts, RETURN PREVIOUS BIOGRAPHY *EXACTLY* (no changes) Only update SPECIFIC sentences when new facts justify it. Keep formatting identical.""";
    
    return $"""
        Create {characterName}'s biography using memories. {oldClause}
        **Rules**:
        - Max 1500 characters
        - Third-person introduction (like telling a friend)
        - Cover: origins, personality, motivations
        - NO chronology/physical description
        - Output ONLY the biography text nothing else
        """;
    }


    private string SummarizingPrompt(string characterName, string playerName, int questGiverChance)
    {
        return $"""
        Update {characterName}'s summary using new memories and previous summary.
        **Golden Rules**:
        1. If new memories add NO facts/contradictions → RETURN PREVIOUS SUMMARY *EXACTLY*.
        2. If updating → Change ONLY sentences needing update. Keep 90%+ text identical.
        3. Output ONLY the summary (no explanations).

        **Structure**:
        - 5-10 chronological paragraphs
        - First paragraph: How {characterName} met {playerName} and entered the story.
        - Last paragraph: Recent events (most detailed and can be longer)
        - Always use FULL names (no pronouns)
        - 1 fact per sentence

        **Example Update Logic**:  
        Previous: "Ana farmed crops."  
        New Memory adds the following new fact: "Ana repaired water pump."  
        Output: "Ana farmed crops. Ana repaired water pump."

        Process memories below:
        """;
    }
}
