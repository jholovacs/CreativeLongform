using System.Net;
using System.Text.Json;

namespace CreativeLongform.Api.Tests;

/// <summary>
/// Ensures the <c>GenerationRuns</c> OData filter used by the draft workspace (scene id + status) parses and executes.
/// Align with the Angular <c>ODataService.getGenerationRunAwaitingReview</c> query string.
/// </summary>
public sealed class ODataGenerationRunsTests : IClassFixture<CreativeLongformApiFixture>
{
    private readonly CreativeLongformApiFixture _factory;

    public ODataGenerationRunsTests(CreativeLongformApiFixture factory)
    {
        _factory = factory;
    }

    /// <summary>
    /// <para><b>System under test:</b> <c>GET /odata/GenerationRuns</c> with filter on <c>sceneId</c> and enum <c>status</c>.</para>
    /// <para><b>Test case:</b> Load any scene id from <c>/odata/Scenes</c>, then query runs with
    /// <c>sceneId eq {guid} and status eq CreativeLongform.Domain.Enums.GenerationRunStatus'AwaitingUserReview'</c> plus orderby/select.</para>
    /// <para><b>Expected result:</b> HTTP 200 and a JSON <c>value</c> array (possibly empty).</para>
    /// <para><b>Why it matters:</b> The draft workspace must resolve the awaiting-review run without 400s from bad enum literal or Guid quoting; breaks block review workflow.</para>
    /// </summary>
    [Fact]
    public async Task Get_odata_generationRuns_filter_sceneId_and_awaiting_review_enum_succeeds()
    {
        var client = _factory.CreateClient();
        var scenesRes = await client.GetAsync("/odata/Scenes?$top=1&$select=id");
        scenesRes.EnsureSuccessStatusCode();
        var scenesJson = await scenesRes.Content.ReadAsStringAsync();
        using var scenesDoc = JsonDocument.Parse(scenesJson);
        var first = scenesDoc.RootElement.GetProperty("value");
        Assert.Equal(JsonValueKind.Array, first.ValueKind);
        Assert.True(first.GetArrayLength() > 0);
        var sceneId = first[0].GetProperty("id").GetString();
        Assert.False(string.IsNullOrEmpty(sceneId));

        // Unquoted UUID + enum member literal (status is Edm.Enum, not Edm.Int32).
        const string statusAwaiting = "CreativeLongform.Domain.Enums.GenerationRunStatus'AwaitingUserReview'";
        var filter = $"sceneId eq {sceneId} and status eq {statusAwaiting}";
        var url = "/odata/GenerationRuns?$filter=" + Uri.EscapeDataString(filter) + "&$orderby=startedAt desc&$top=1&$select=id";
        var res = await client.GetAsync(url);
        Assert.Equal(HttpStatusCode.OK, res.StatusCode);
        var runsJson = await res.Content.ReadAsStringAsync();
        using var runsDoc = JsonDocument.Parse(runsJson);
        Assert.True(runsDoc.RootElement.TryGetProperty("value", out var value));
        Assert.Equal(JsonValueKind.Array, value.ValueKind);
    }
}
