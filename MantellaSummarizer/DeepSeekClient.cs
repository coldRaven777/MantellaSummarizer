using System;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;
using Newtonsoft.Json;

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
    
    public async Task<string?> GetSummary(string character, string content)
    {
        try
        {
            return await GetCompletionAsync(ProfilingPrompt(character, QUEST_GIVER_CHANCE) + "\n\n" + content);
        }
        catch (Exception ex)
        {
            // Log the error but don't return it as content
            Console.WriteLine($"❌ Error getting summary for {character}: {ex.Message}");
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

    private string ProfilingPrompt(object characterName, int questGiverChance)
    {
        bool isQuestGiver = _randomizer.Next(0, 100) <= questGiverChance;
        string questGiver = isQuestGiver ?
            $"> - MANDATORY: {characterName} WILL GET QUEST GIVER TAG - This is NON NEGOTIABLE " 
            : "";
        if(isQuestGiver)
        {
            Console.WriteLine($"Character {characterName} is a quest giver.");
        }
        return $@"**Updated Profile Data Type Guide**  
                >-  IMPORTANT:  {characterName} is a fictional character in either Fallout 4 or Skyrim Universe and the profile data is based on the character's story and background, so you must keep in mind that.
                > - `Stubborn` (update only if overturned): Race, Age, Personality, Phobias, Traumas, Dreams  
                > - `Mutable` (always update): Occupation, Fears, Location, Emotional/Physical/Sexual Status, Relationships, Tags  
                **Updated Profile TAG system**
                > - `Tags` are used to quickly identify key traits or roles of the character.
                > - Tags are separated by semicolons and can include descriptors like 'Quest Giver', 'Loyal to Player', 'Villager', etc.
                > - Tags will help the AI understand the {characterName}'s role and relationships in the story.\n
                {questGiver}
                > - Tags are mutable and can change as the {characterName}'s role evolves in the story.
                > - List of tags: Quest Giver; Villager; Loyal to Player; Rival; Mentor; Friend; Ally; Enemy; Protector; Traitor; Rapist; Citizen of Whiterun; Citizen of Solitude; Citizen of Riften; Citizen of Markarth; Citizen of Windhelm; Citizen of Dawnstar; Citizen of Falkreath; Citizen of Morthal; Citizen of Winterhold; Citizen of Riverwood; Citizen of Ivarstead; Citizen of Kynesgrove; Citizen of Rorikstead; Citizen of Shor's Stone; Citizen of Riverwood; Citizen of Helgen.; Blacksmith; etc.
                **Updated Profile Format examples (STRICTLY MANDATORY TO FOLLOW FORMAT) - THE NAMES ARE JUST EXAMPLES**
                *****example 1:*****
                #UPDATED PROFILE FOR ERIK - BELOW IS THE STUFF THAT ONLY ERIK KNOWS#  
                -*Age:* 17 
                -*Race*: Human Nord
                -*Tags*: Quest Giver; Loyal to Player 
                -*Occupation:* Arya's personal guard and warrior-in-training under Uthgerd  
                -*Dreams and Ambitions:* To become a warrior strong enough to protect his family and village, and to stand as Arya's equal  
                -*List of Fears:* Failing Arya again, losing her to the Thalmor or assassins, her magic weakening during critical moments  
                -*List of phobias:* None noted  
                -*List of Traumas:* Arya's disappearance, witnessing Lemkil's abuse, nearly losing Arya during the Solitude assault  
                -*Last known location:* Whiterun, preparing to investigate a necromancer near Riverwood  
                -*Personality Traits:* Protective, fiercely loyal, stubbornly determined, increasingly assertive in his role  
                -*Emotional Status:* Proud of Arya's accomplishments but anxious about her safety, frustrated by her recklessness, deeply bonded after recent battles  
                -*Physical status:* Recovered from arrow wounds, muscles sore from relentless training, energized by purpose  
                -*Sexual status:* None mentioned or implied

                **Relationships**  
                -*Arya:* (Sister) Unshakably devoted, protective, in awe of her magic but increasingly assertive as her equal. Secret romantic tension simmers beneath fierce loyalty.  
                -*Uthgerd:* (Mentor) Deep respect, sees her as both teacher and battle-sister after surviving the siege together  
                -*J'zargo:* (Rival) Jealousy tempered by begrudging respect for his role in Arya's projects  
                -*Dagny:* (Villager) Fondness for her crush but deliberately keeping distance due to greater priorities  
                -*Adrianne Avenicci:* (Ally) Admires her craftsmanship, trusts her with Arya's safety  
                -*Jarl Balgruuf:* (Authority) Loyalty solidified after witnessing his support for Arya's inventions  
                -*Solitude Soldiers:* (Enemies) Burning hatred after their assaults on Arya 
                *****Example 2*****

                #UPDATED PROFILE FOR Brenuin -  BELOW IS THE STUFF THAT ONLY Brenuin KNOWS#  
                -*Age:* Middle-aged  
                -*Race:* Human - Redguard
                -*Tags*: Homeless , Paternal, Drunkard
                -*Occupation:* Caretaker of Lucia  
                -*Dreams and Ambitions:* To provide stability and care for Lucia, ensuring she grows up safe and happy  
                -*List of Fears:* Losing Lucia, failing to protect her, poverty  
                -*List of phobias:* None known  
                -*List of Traumas:* Loss of Lucia's mother, past struggles with homelessness  
                -*Last known location:* Whiterun  
                -*Personality Traits:* Kind-hearted, protective, humble, resilient  
                -*Emotional Status:* Grateful but burdened by responsibility  
                -*Physical status:* Weary but healthy, carrying the weight of his duties  
                -*Sexual status:* None mentioned or implied  

                **Relationships**  
                -*Lucia* (Ward, deep paternal affection)  
                -*Arya* (Respectful gratitude, sees her as a benefactor)  
                -*Frothar* (Neutral, cautious respect)  
                -*Erik* (Neutral, slight wariness)  
                -*Uthgerd* (Neutral, distant familiarity)

                **Summary Requirements**:  
                - PERSPECTIVE: Third-Person narrative as `{characterName}`  
                - CONTENT:  
                  - Origin → Present journey  
                   -Include interesting moments
                  - Focus on pivotal character-shaping events  
                  - Include current status  
                - FORMAT:  
                  - Exactly 3-5 paragraphs (NO lists/bullets) 
                  - Each paragraph should consist of various phrases.
                 - Each phrase should be a complete thought, not a single word or fragment that represents a single event.
                  - if there are too many phrases, try to combine them into a single phrase that represents the same event.
                  - each phrase will be a fact that happened, not a prediction
                  -NO pronouns, only use names of the related characters (ex: 'Arya's effort' instead of 'Her effort')
                  - The summary is always in chronological order, each phrase is a fact that happened in the past, not a prediction of the future.
                    -Each fact is a important story point
                    - NO repetition of profile data
                    - NO direct quotes from profile
                  - Strictly ≤ 7100 tokens  
                  - Natural storytelling flow  

                **Output Enforcement**:  
                - NON-NEGOTIABLE: Only generate {{updated profile}} immediately followed by {{new condensed summary}}  
                - REMEMBER the Updated profile must be titled EXACT like this (THIS IS NON -NEGOTIABLE):  
                  > - `#UPDATED PROFILE FOR {characterName}  BELOW IS THE STUFF THAT ONLY {characterName} KNOWS#`
                - TERMINATE after summary - zero additional text  

                **Below is the Updated Profile i want you to rewrite(remember! it representes the story of {characterName} in chronological order!)**
";    }
}
