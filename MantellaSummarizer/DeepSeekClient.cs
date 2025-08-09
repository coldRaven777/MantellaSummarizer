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
        bool isQuestGiver = _randomizer.Next(0, 100) <= questGiverChance;

        return $@"
            YOU WILL CREATE AN UPDATED PROFILE FOR THE CHARACTER BELOW BASED ON THEIR MEMORIES.
            ###RULES (NON NEGOTIABLE; DO NOT DEVIATE; MANDATORY):
            1. There are two types of fields in the profile: 
               - **Fields that change a lot**: These fields are updated frequently based on the character's experiences and memories.
                - **Fields that change very little**: These fields are updated rarely, if at all, and are more static in nature.
            2. The profile must be updated based on the character's memories and experiences. If information is missing or unkown, you will simply put 'UNKNOWN' in the field. 
            3. the profile must follow the format below without any deviations and strictly adhere to the rules.
            4. It will be a simple text profile, not a JSON or any other format.
            5. Everything inside {{}} is a field where you will put the information. (YOU WILL NOT UNDER ANY CIRCUNSTANCES USE THE {{}} IN YOUR RESPONSE, IT IS JUST A PLACEHOLDER FOR YOU TO KNOW WHERE TO PUT THE INFORMATION).
            ####PROFILE FORMAT (DO NOT DEVIATE)####:
            **Character Name**: {characterName},
            **Age**: {{number, age group or 'UNKNOWN'}},
            **Current Occupation**: {{Current occupation based on the summary, like 'Blacksmith', 'Hunter', 'Raider', 'Soldier', etc.}},
            **Race**: {{Human, Elf, Dwarf, Synth,etc. or 'UNKNOWN'}},
            **Last known location**: {{Last known location of the character based on the memories given}}
            **Religion**: {{Religious values based on the summary or UNKOWN}},
            **Ideology** {{keep it short, like: 'conservative nord values', or 'Imperial Supporter' or 'Corvega Raider' or 'Communist' or 'Theocratic' etc.. based on the character's core values found in the summary, use UNKOWN if none is implicit or explicit}},
            **Literacy Level**: {{Take into consideration the reality of the lore: in skyrim, most nords are illetrate unless they are nobles or merchants, and most nords do not value wisdom, and in Fallout, wastelanders do not read because they spend more time to survive, use your own discretion to deduce the level of literacy based on the background and character evolution}}
            **Personality Traits**: {{List of personality traits based on the summary, like 'brave', 'cowardly', 'honest', 'dishonest', 'greedy', 'generous', etc.}},
            **Moral Compass**: {{Good, evil, neutral, unhinged, etc}}
            **Traumas**: {{List of traumas based on the summary, like 'PTSD', 'Survivor's guilt', 'Loss of a loved one', etc., or None}},
            **Fears**: {{List of fears based on the summmary and the context, some fears are obvious like a woman in the wasteland or in the dangerous roads of skyrim will fear rape, while a soldier will fear death, or a child will fear the dark other fears will be based on the summary and the character evolution.}},
            **Hobbies**: {{List of hobbies based on the summary, like 'reading', 'hunting', 'fishing', 'crafting', etc.}},
            **Physical Status**: {{Pains, injuries, scars, etc. based on the summary, like 'missing left arm', 'scar on the face', 'bad back', etc.}},
            **Sexual Status**: {{level or horniness, lust, or sexual frustration based on the summary, like 'sexually frustrated', 'horny', 'satisfied', 'celibate', as well as the list of sexual partners based on the summary, like 'married to John', 'lover of Sarah', 'one night stand with Mike', etc.}},
            ********RELATIONSHIPS********:
             {{YOU WILL ITERATE OVER ALL THE RELATIONSHIPS AND CREATE A LIST WITH THE FOLLOWING FORMAT:}}
            **{{name of the person}}**: {{type of relationship, father, friend, foe, enemy, boss, brother with incestuous involvment, etc, keep it short}}
            {{END OF THE ITERATION}}
            Below is the summary of the character's memories and experiences, you will use it to update the profile:";

        }

    private string CharacterIntroductionForProfiling(string oldBiography, string characterName)
    {
        var oldClause = string.IsNullOrEmpty(oldBiography)
            ? ""
    : $@"You will also receive the previous biography of {characterName}. 
    ***THIS IS A MANDATORY RULE:*** If the provided memories do NOT contradict or add specific, concrete facts that require updating, 
    you MUST return the PREVIOUS BIOGRAPHY EXACTLY AS-IS, character-for-character, without adding, removing, or rewording anything. 
    No synonyms, no paraphrasing, no formatting changes. EXACTLY the same text.
    If an update IS required (WHICH OFTEN HAPPENS DURING CHARACTER DEVELOPMENT), you MUST change ONLY the specific sentences or phrases directly justified by the new memories, 
    and you MUST keep all other text completely identical to the old biography in both wording and formatting.
    Failure to follow this rule is unacceptable.";

        return $@"
        YOU WILL CREATE a very detailed (non physical) character description of {characterName} based on the character memories I provide.
        {oldClause}
        You are not allowed to write anything else, just the biography, no more added text. This is EXTREMELY IMPORTANT!
        You are only allowed to write max 1500 characters.
        It must be very detailed and explain who {characterName} is, where {characterName} comes from, and {characterName}'s personality. 
        Avoid chronology. Just describe {characterName} as if you were introducing {characterName} to a trusted friend.";
            }


    private string SummarizingPrompt(string characterName, string playerName, int questGiverChance)
    {
        bool isQuestGiver = _randomizer.Next(0, 100) <= questGiverChance;

        return $@"""
        YOU WILL CREATE A SUMMARY OF {characterName} BASED ON {characterName}'s MEMORIES (provided below).
        YOU WILL RECEIVE: (A) the PREVIOUS SUMMARY and (B) NEW MEMORIES.

        ### ABSOLUTE RULE (MANDATORY – NON-NEGOTIABLE)
        - If the new memories do NOT add concrete, new facts or contradict existing ones, you MUST return the PREVIOUS SUMMARY **exactly as-is** (character-for-character). No paraphrasing, no reformatting, no synonym swaps.
        - If the new memories DO add concrete, new facts or corrections, change ONLY the minimum necessary sentences to incorporate those facts. Keep all other wording and formatting identical to the previous summary.
        - If a fact already exists in the previous summary (even if the new memories phrase it differently), KEEP the original sentence verbatim.

        ### EXAMPLE (FOLLOW THIS PATTERN EXACTLY)
        [PREVIOUS SUMMARY]
        \""\""\"" 
        Ana Patricia was a desperate and starving young woman when Nate first found her and brought her into Sanctuary. As a farmer’s daughter skilled in agriculture and basic medicine, Ana Patricia quickly proved invaluable to the settlement. Nate’s tragic past—losing Nora and Shaun—resonated with her, deepening her loyalty. When Nate gifted Ana Patricia a house, she tearfully accepted, personalizing it with flowers and blushing at the gesture of Long Johns, revealing her admiration.

        During morning patrols, Ana Patricia openly kissed Nate, drawing playful teasing from Robert and Mike. Old Paul’s plea to find Maggie reinforced the settlement’s unity, strengthening Ana Patricia’s resolve. Nate’s pre-war meal for the group deepened her devotion, and his speeches about justice cemented her belief in his leadership. Ana Patricia’s enthusiastic pledges and flustered reactions amused Codsworth, who often noted her open admiration as she hurried to the fields.

        In private moments, Ana Patricia expressed unwavering loyalty to Nate, following him to a hidden house where their relationship turned intimate. Ana Patricia trusted Nate completely, willingly submitting to his advances, feeling safe and cherished. The emotional and physical connection between them grew stronger, with Ana Patricia finding solace in Nate’s protection.

        Now a key figure in Sanctuary’s farming efforts, Ana Patricia works tirelessly, her gratitude and affection for Nate fueling her dedication. The community respects her contributions, and Ana Patricia remains steadfast in her loyalty, standing by Nate’s side against the wasteland’s threats.
        \""\""\""

        [NEW MEMORIES]
        \""\""\""
        Recently, Nate and Ana Patricia shared an intimate conversation where Nate expressed protective devotion, and Ana Patricia responded with deep affection. Nate ensured contraception, and Ana Patricia eagerly suggested fetching water together, showing enthusiasm. As they prepared to return to work, Nate styled Ana Patricia’s hair into a ponytail, boosting her confidence. Ana Patricia promised to work diligently, visibly happier and more secure under his care.
        \""\""\""

        [CORRECT OUTPUT (ADD ONLY NEW FACTS; KEEP ALL PREVIOUS TEXT IDENTICAL)]
        \""\""\"" 
        Ana Patricia was a desperate and starving young woman when Nate first found her and brought her into Sanctuary. As a farmer’s daughter skilled in agriculture and basic medicine, Ana Patricia quickly proved invaluable to the settlement. Nate’s tragic past—losing Nora and Shaun—resonated with her, deepening her loyalty. When Nate gifted Ana Patricia a house, she tearfully accepted, personalizing it with flowers and blushing at the gesture of Long Johns, revealing her admiration.

        During morning patrols, Ana Patricia openly kissed Nate, drawing playful teasing from Robert and Mike. Old Paul’s plea to find Maggie reinforced the settlement’s unity, strengthening Ana Patricia’s resolve. Nate’s pre-war meal for the group deepened her devotion, and his speeches about justice cemented her belief in his leadership. Ana Patricia’s enthusiastic pledges and flustered reactions amused Codsworth, who often noted her open admiration as she hurried to the fields.

        In private moments, Ana Patricia expressed unwavering loyalty to Nate, following him to a hidden house where their relationship turned intimate. Ana Patricia trusted Nate completely, willingly submitting to his advances, feeling safe and cherished. The emotional and physical connection between them grew stronger, with Ana Patricia finding solace in Nate’s protection.

        Now a key figure in Sanctuary’s farming efforts, Ana Patricia works tirelessly, her gratitude and affection for Nate fueling her dedication. The community respects her contributions, and Ana Patricia remains steadfast in her loyalty, standing by Nate’s side against the wasteland’s threats.

        Recently, Nate expressed protective devotion and Ana Patricia responded with deep affection. Nate ensured contraception. Ana Patricia suggested fetching water together. Nate styled Ana Patricia’s hair into a ponytail. Ana Patricia promised to work diligently and appeared happier and more secure under Nate’s care.
        \""\""\""
        ### END OF EXAMPLE

        ### RULES (NON-NEGOTIABLE; DO NOT DEVIATE; MANDATORY)
        1. Write in third person, centered on {characterName}.
        2. Update ONLY if new memories add new facts or contradictions; otherwise return the previous summary verbatim.
        3. 5–10 paragraphs; each paragraph contains factual statements about the character’s experiences and memories.
        4. Output MUST contain ONLY the summary (no extra text).
        5. Write for easy recall by an AI Assistant.
        6. The memory is chronological; respect that order.
        7. First chapter: quick recall of how {characterName} was first introduced to the story and how {characterName} met {playerName}. 
        8. Last chapter: longest; you may write as current events with as much detail as needed (only for the last chapter).
        9. NEVER use pronouns; always use names.
        10. Each sentence states a single isolated fact within its chapter.

        ## INPUTS:
        [PREVIOUS SUMMARY WILL BE HERE]
        [NEW MEMORIES WILL BE HERE]
        """;

    }
}
