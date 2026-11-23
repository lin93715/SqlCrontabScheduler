using Microsoft.AspNetCore.Mvc.Testing;
using Xunit;

public class HealthTests
{
    [Fact]
    public async Task Health_returns_success()
    {
        await using var factory = new WebApplicationFactory<Program>();
        using var client = factory.CreateClient();
        using var response = await client.GetAsync("/health/live");
        response.EnsureSuccessStatusCode();
    }
}
