using System.Net;
using System.Text.Json;

namespace CreativeLongform.Api.Tests;

public sealed class ODataBooksTests : IClassFixture<CreativeLongformApiFixture>
{
    private readonly CreativeLongformApiFixture _factory;

    public ODataBooksTests(CreativeLongformApiFixture factory)
    {
        _factory = factory;
    }

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
