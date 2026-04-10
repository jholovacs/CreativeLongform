using System.Net;
using System.Text.Json;

namespace CreativeLongform.Api.Tests;

/// <summary>
/// Integration tests for OData read endpoints used by the Angular app (<c>Books</c>, <c>WorldElements</c> filters).
/// </summary>
public sealed class ODataBooksTests : IClassFixture<CreativeLongformApiFixture>
{
    private readonly CreativeLongformApiFixture _factory;

    public ODataBooksTests(CreativeLongformApiFixture factory)
    {
        _factory = factory;
    }

    /// <summary>
    /// <para><b>System under test:</b> <c>GET /odata/Books</c> with development seed data.</para>
    /// <para><b>Test case:</b> Request first page of books with <c>$top=5</c>.</para>
    /// <para><b>Expected result:</b> JSON object with a non-empty <c>value</c> array.</para>
    /// <para><b>Why it matters:</b> The home page and scene workflow depend on OData expand of chapters/scenes; a broken Books endpoint blocks the whole UI.</para>
    /// </summary>
    [Fact]
    public async Task Get_odata_books_returns_seed_book()
    {
        var client = _factory.CreateClient();
        var res = await client.GetAsync("/odata/Books?$top=5");
        res.EnsureSuccessStatusCode();
        var doc = await JsonDocument.ParseAsync(await res.Content.ReadAsStreamAsync());
        var root = doc.RootElement;
        Assert.Equal(JsonValueKind.Object, root.ValueKind);
        Assert.True(root.TryGetProperty("value", out var value));
        Assert.Equal(JsonValueKind.Array, value.ValueKind);
        Assert.True(value.GetArrayLength() > 0);
    }

    /// <summary>
    /// <para><b>System under test:</b> OData <c>$filter</c> parsing for <c>WorldElements</c> (world-building search UI).</para>
    /// <para><b>Test case:</b> Complex filter with <c>tolower</c>, <c>contains</c>, and <c>cast(kind,'Edm.String')</c> against an empty book id.</para>
    /// <para><b>Expected result:</b> HTTP 200 (filter parses and executes; may return zero rows).</para>
    /// <para><b>Why it matters:</b> Prevents regressions where OData rejects the exact filter string the SPA builds for element search.</para>
    /// </summary>
    [Fact]
    public async Task Get_odata_world_elements_case_insensitive_kind_filter_parses()
    {
        var client = _factory.CreateClient();
        var emptyBook = "00000000-0000-0000-0000-000000000000";
        var filter =
            $"bookId eq {emptyBook} and (contains(tolower(title),'x') or contains(tolower(summary),'x') or contains(tolower(cast(kind,'Edm.String')),'x') or contains(tolower(detail),'x'))";
        var url = "/odata/WorldElements?$filter=" + Uri.EscapeDataString(filter) + "&$top=1&$count=true";
        var res = await client.GetAsync(url);
        Assert.Equal(HttpStatusCode.OK, res.StatusCode);
    }
}
