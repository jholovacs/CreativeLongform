namespace CreativeLongform.Api.Tests;

public sealed class HealthEndpointTests : IClassFixture<CreativeLongformApiFixture>
{
    private readonly CreativeLongformApiFixture _factory;

    public HealthEndpointTests(CreativeLongformApiFixture factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task Get_health_returns_ok_with_status()
    {
        var client = _factory.CreateClient();
        var res = await client.GetAsync("/health");
        res.EnsureSuccessStatusCode();
        var body = await res.Content.ReadAsStringAsync();
        Assert.Contains("ok", body, StringComparison.OrdinalIgnoreCase);
    }
}
