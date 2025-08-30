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
    Analyze all known memories for {characterName} and construct a detailed character profile. This character exists in a harsh RPG world, either the fantasy realm of Skyrim or the post-apocalyptic wasteland of Fallout.

    **CRITICAL OUTPUT RULES:**
    - Output must be in plain text only. Never use JSON, Markdown, code blocks, or any other formatting.
    - Absolutely NEVER use double curly braces.
    - For any unknown information, use "UNKNOWN". Do not invent facts not supported by memories.
    - Relationships must only list specific, named individuals. No groups like "Whiterun Guards" or "Brotherhood of Steel. Animals or Robots are OK".

    **PROFILE CONSTRUCTION RULES:**
    1.  **Field Types:**
        - **Static Fields:** (Race, Core Ideology, Personality Archetype, Age) These are fundamental and rarely change. Deduce them from the biography.
        - **Dynamic Fields:** (Occupation, Location, Injuries) Update these frequently based on recent memories.
    2.  **Virginity & Sexual Status Rules (Follow this logic exactly):**
        - **Assume VIRGIN if ANY of the following are true:**
          - Is male and under 18 years old.
          - Is female and under 25 years old.
          - Is young unmarried female Nord or Breton (Skyrim-specific).
          - Is young unmarried female from a major organized settlement (e.g., Diamond City, Megaton, The Strip, Goodneighbor, Vault City).
          - The character's biography or demeanor suggests extreme innocence, religious piety, or social isolation.
        - **Assume NON-VIRGIN if ANY of the following are true (Females only):**
          - There is a known history of rape or exploitation (common in these harsh worlds).
          - They have had confirmed romantic relationships or partners.
          - They are characterized as overtly "flirty," "jaded," "world-weary," or "promiscuous" and are over 15 years old.
          - Their occupation implies it (e.g., Prostitute, Companion, certain Bard roles).
    3. **Literacy Rules:**
         - **Illiterate:** Cannot read/write. Settlers by default are illiterate unless specified otherwise.Most nords and wastelanders are illiterate.
            - **Functional:** Can read/write basic texts. Older wastelanders, traders, and intelligent soldiers.
            - **Educated:** Can read/write complex texts. Nobles, high-ranking officers, Diamond City residents, and Vault dwellers and brotherhood members.
            - **Highly Educated/Scholar:** Can read/write advanced texts. Mages, Necromancers, high-ranking officials, scientists,Institute People, and some merchants.

    4.  **Personality & Style:**
        - **Archetype:** Derive a clear personality archetype (e.g., Tsundere, Kuudere, Jaded Survivor, Devout Fanatic, Sleazy Politician, Hopeless Romantic, Tribal Warrior) from their traits and biography. It must fit the game's tone.
        - **Style of Conversation:** This is a static field. Provide THREE distinct example phrases they would say, reflecting their archetype and world. Examples:
          - (Jaded Wastelander): "Keep your caps close and your knife closer." AND "Seen a hundred like you come through here. Most don't come back." AND "Clean water's more valuable than friendship out here. Remember that."
          - (Haughty Elf Mage): "Your mortal mind could scarcely comprehend my art." AND "Do not touch that. It is older than your entire lineage." AND "Ugh, the smell of commoners is so... pungent today."
    5.  **Relationships:**
        - For each important named person, list them as: `[Name]: [Relationship Type]: [Subjective 3rd-person description of their appearance, vibe, and attractiveness].`
        - Example: `Lucy: Crush: Lucy is a resilient settler with kind eyes and a warm smile that contrasts with her dirt-smudged face. She has a hopeful vibe that makes him want to protect her.`

    **PROFILE OUTPUT FORMAT -- PLAIN TEXT ONLY:**

    Character Name: {characterName}
    Age: [Number or estimate]
    Current Occupation: [Job Title/UNKNOWN]
    Race: [Race/UNKNOWN] (e.g., Nord, Synth, Ghoul, Super Mutant)
    Last Known Location: [Specific Location]
    Religion: [Deity/Beliefs/UNKNOWN]
    Ideology: [Brief phrase describing core motivation/UNKNOWN]
    Literacy Level: [Illiterate/Functional/Educated/Scholar - based on world]
    Personality Traits: [Trait1, Trait2, Trait3] (e.g., Cynical, Brave, Greedy)
    Personality Archetype: [Derived Archetype]
    Style of Conversation: [1. Example phrase 1] AND [2. Example phrase 2] AND [3. Example phrase 3]
    Moral Compass: [Good/Neutral/Evil/Chaotic/etc]
    Traumas: [List specific events or None]
    Fears: [List]
    Hobbies: [List/UNKNOWN]
    Physical Status: [Current injuries, scars, health/Healthy]
    Sexual Status: [Virgin/Non-Virgin]. Libido: [Low/Medium/High]. Partners: [None/Known Names/UNKNOWN]

    ********RELATIONSHIPS********:
    [Person1]: [Relationship Type]: [Description]
    [Person2]: [Relationship Type]: [Description]

    ****Final checklist before you start: ******
    [] Is the profile in plain text with no formatting?
    [] Are all fields filled with either specific info or "UNKNOWN"?
    [] Are static fields only changed if absolutely certain?
    [] Are dynamic fields updated based on recent memories?
    [] Is the personality archetype fitting the character's traits and world?
    [] Are there exactly THREE distinct example phrases in "Style of Conversation"?
    [] Are relationships listed with specific names and detailed descriptions?
    [] Is the sexual status determined by the strict rules provided?
    [] Is the literacy level appropriate for the character's background and world?
    [] Is the output within 1500 characters?
    [] Are you ONLY outputting the profile text, nothing else?
    [] Did you follow all instructions exactly as given?
    ********END CHECKLIST********

    Use the following memories to update this profile:
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
        - Third-person introduction (like telling a friend about the character)
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
        4. It MUST ALWAYS START WITH THIS followed by a new line: ##These are {characterName}'s Exclusive memories:##

        **Structure**:
        - 5-10 chronological paragraphs
        - First paragraph: How {characterName} met {playerName} and entered the story.
        - Last paragraph: Recent events (most detailed and can be longer)
        - Always use FULL names (no pronouns, never use Her or His ,or He or She, Always use the Names, even if it gets repetitive)
        - 1 fact per sentence

        **Example Update Logic**:  
        Previous: "Ana farmed crops."  
        New Memory adds the following new fact: "Ana repaired water pump."  
        Output: "Ana farmed crops. Ana repaired water pump."

        Process memories below:
        """;
    }
}
