namespace CreativeLongform.Api.Tests;

/// <summary>
/// Smoke tests for the unauthenticated <c>/health</c> endpoint used by load balancers and deployment checks.
/// </summary>
public sealed class HealthEndpointTests : IClassFixture<CreativeLongformApiFixture>
{
    private readonly CreativeLongformApiFixture _factory;

    public HealthEndpointTests(CreativeLongformApiFixture factory)
    {
        _factory = factory;
    }

    /// <summary>
    /// <para><b>System under test:</b> HTTP GET <c>/health</c> on the hosted API.</para>
    /// <para><b>Test case:</b> Default test client requests the health route.</para>
    /// <para><b>Expected result:</b> 2xx status and response body containing &quot;ok&quot; (case-insensitive).</para>
    /// <para><b>Why it matters:</b> Confirms the app starts, middleware runs, and ops can detect a live instance without auth or DB-heavy work.</para>
    /// </summary>
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
