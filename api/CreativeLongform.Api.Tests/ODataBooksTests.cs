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
}
