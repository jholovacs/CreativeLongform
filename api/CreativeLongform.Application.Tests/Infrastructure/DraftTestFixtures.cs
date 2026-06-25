namespace CreativeLongform.Application.Tests.Infrastructure;

internal static class DraftTestFixtures
{
    /// <summary>Long enough to skip draft expansion when MinWordsOverride is 100.</summary>
    public const string SceneDraft =
        """
        Mara stood at the kitchen window, watching rain streak the glass. The letter lay unopened on the table.
        She had carried it from the harbor without reading it, as if delay could soften whatever words waited inside.
        When thunder rolled over the rooftops, she finally broke the seal and spread the page flat beneath her palm.
        The ink had smeared in one corner, but the handwriting was unmistakable. Her sister had written from the capital,
        and the first line offered no comfort at all.
        """;

    public const string RecommendationsJson =
        """
        {
          "items": [
            {
              "kind": "replace",
              "paragraphStart": 0,
              "paragraphEnd": 0,
              "problem": "Opening tells mood instead of showing action",
              "replacementText": "Rain needled the kitchen window while Mara kept her palm on the unopened letter."
            },
            {
              "kind": "rewrite",
              "paragraphStart": 1,
              "paragraphEnd": 1,
              "problem": "Pacing sags in the middle",
              "rewriteInstruction": "Shorten the hesitation and move to opening the letter one paragraph sooner."
            }
          ]
        }
        """;
}
