namespace CreativeLongform.Application.Tests.Infrastructure;

internal static class WorldBuildingTestFixtures
{
    public const string HarborBatchJson =
        """
        {
          "elements": [
            { "kind": "Geography", "title": "Harbor Town", "summary": "A coastal trading city.", "detail": null, "slug": null },
            { "kind": "Character", "title": "Mara", "summary": "A restless clerk.", "detail": null, "slug": null }
          ],
          "suggestedLinks": [
            { "fromTitle": "Mara", "toTitle": "Harbor Town", "relationLabel": "Lives in" }
          ]
        }
        """;

    public static string LinkSuggestJson(string fromTitle, string toTitle, string relationLabel) =>
        $$"""
        {
          "suggestedLinks": [
            { "fromTitle": "{{fromTitle}}", "toTitle": "{{toTitle}}", "relationLabel": "{{relationLabel}}" }
          ]
        }
        """;

    public static string SynopsisPickJson(params Guid[] elementIds)
    {
        var ids = string.Join(", ", elementIds.Select(id => $"\"{id}\""));
        return $$"""{"elementIds":[{{ids}}]}""";
    }

    public static string CanonReviewAddLinkJson(string from, string to, string relation) =>
        $$"""
        {
          "proposals": [
            { "kind": "add_link", "fromTitle": "{{from}}", "toTitle": "{{to}}", "relationLabel": "{{relation}}", "rationale": "Missing tie" }
          ]
        }
        """;

    public static string CanonReviewRemoveLinkJson(Guid linkId) =>
        $$"""
        {
          "proposals": [
            { "kind": "remove_link", "linkId": "{{linkId:D}}", "rationale": "Contradicts summary" }
          ]
        }
        """;

    public static string CanonReviewChangeRelationJson(Guid linkId, string newLabel) =>
        $$"""
        {
          "proposals": [
            { "kind": "change_relation", "linkId": "{{linkId:D}}", "newRelationLabel": "{{newLabel}}", "rationale": "Clearer label" }
          ]
        }
        """;

    public static string GlossaryAlternatesJson(Guid elementId, params string[] names)
    {
        var arr = string.Join(", ", names.Select(n => $"\"{n}\""));
        return $$"""{"entries":[{"elementId":"{{elementId:D}}","alternateNames":[{{arr}}]}]}""";
    }
}
