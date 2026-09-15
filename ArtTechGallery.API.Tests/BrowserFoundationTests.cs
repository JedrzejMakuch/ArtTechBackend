using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json.Nodes;
using ArtTechGallery.Core.Models;
using Microsoft.AspNetCore.Authentication.BearerToken;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace ArtTechGallery.API.Tests;

public sealed class BrowserFoundationTests(PostgresFixture fixture) : IClassFixture<PostgresFixture>
{
    private const string Origin = "https://panel.example.test";

    [Theory]
    [InlineData("/register", "POST")]
    [InlineData("/login", "POST")]
    [InlineData("/refresh", "POST")]
    [InlineData("/api/artist/profile", "GET")]
    [InlineData("/api/artist/profile", "POST")]
    [InlineData("/api/artist/profile", "PUT")]
    [InlineData("/api/artist/exhibitions/00000000-0000-0000-0000-000000000001/artworks/00000000-0000-0000-0000-000000000002", "DELETE")]
    public async Task AllowedOriginPreflightSupportsPanelRequests(string path, string method)
    {
        using var factory = fixture.Factory.WithWebHostBuilder(b => b.ConfigureAppConfiguration((_, c) =>
            c.AddInMemoryCollection(new Dictionary<string, string?> { ["Cors:AllowedOrigins:0"] = Origin })));
        using var client = factory.CreateClient();
        using var request = new HttpRequestMessage(HttpMethod.Options, path);
        request.Headers.Add("Origin", Origin);
        request.Headers.Add("Access-Control-Request-Method", method);
        request.Headers.Add("Access-Control-Request-Headers", "authorization,content-type");
        using var response = await client.SendAsync(request);
        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        Assert.Equal(Origin, Assert.Single(response.Headers.GetValues("Access-Control-Allow-Origin")));
        Assert.Contains(method, string.Join(",", response.Headers.GetValues("Access-Control-Allow-Methods")));
        Assert.False(response.Headers.Contains("Access-Control-Allow-Credentials"));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task MissingPolicyOrUnlistedOriginIsNotAllowed(bool configured)
    {
        using var factory = fixture.Factory.WithWebHostBuilder(b => b.ConfigureAppConfiguration((_, c) =>
            c.AddInMemoryCollection(configured
                ? new Dictionary<string, string?> { ["Cors:AllowedOrigins:0"] = Origin }
                : new Dictionary<string, string?>())));
        using var client = factory.CreateClient();
        using var request = new HttpRequestMessage(HttpMethod.Options, "/api/artist/profile");
        request.Headers.Add("Origin", "https://unlisted.example.test");
        request.Headers.Add("Access-Control-Request-Method", "PUT");
        using var response = await client.SendAsync(request);
        Assert.False(response.Headers.Contains("Access-Control-Allow-Origin"));
    }

    [Fact]
    public async Task UnauthorizedResponseIsReadableByAllowedBrowser()
    {
        using var factory = fixture.Factory.WithWebHostBuilder(b => b.ConfigureAppConfiguration((_, c) =>
            c.AddInMemoryCollection(new Dictionary<string, string?> { ["Cors:AllowedOrigins:0"] = Origin })));
        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Add("Origin", Origin);
        using var response = await client.GetAsync("/api/artist/profile");
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Equal(Origin, Assert.Single(response.Headers.GetValues("Access-Control-Allow-Origin")));
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
    }

    [Fact]
    public async Task RefreshProducesUsableTokensAndRejectsInvalidOrChangedSecurityStamp()
    {
        using var client = fixture.Factory.CreateClient();
        var credentials = new { email = $"{Guid.NewGuid():N}@example.test", password = "Test-Password-123!" };
        Assert.Equal(HttpStatusCode.OK, (await client.PostAsJsonAsync("/register", credentials)).StatusCode);
        var login = await client.PostAsJsonAsync("/login?useCookies=false", credentials);
        var tokens = (await login.Content.ReadFromJsonAsync<JsonObject>())!;
        var refresh = await client.PostAsJsonAsync("/refresh", new { refreshToken = tokens["refreshToken"]!.GetValue<string>() });
        Assert.Equal(HttpStatusCode.OK, refresh.StatusCode);
        var renewed = (await refresh.Content.ReadFromJsonAsync<JsonObject>())!;
        Assert.Equal("Bearer", renewed["tokenType"]!.GetValue<string>());
        Assert.True(renewed["expiresIn"]!.GetValue<int>() > 0);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", renewed["accessToken"]!.GetValue<string>());
        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync("/api/artist/profile")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.PostAsJsonAsync("/refresh", new { refreshToken = "invalid" })).StatusCode);
        using var scope = fixture.Factory.Services.CreateScope();
        var users = scope.ServiceProvider.GetRequiredService<UserManager<User>>();
        await users.UpdateSecurityStampAsync((await users.FindByEmailAsync(credentials.email))!);
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.PostAsJsonAsync("/refresh", new { refreshToken = renewed["refreshToken"]!.GetValue<string>() })).StatusCode);
    }

    [Fact]
    public async Task ExpiredRefreshTokenIsRejected()
    {
        using var factory = fixture.Factory.WithWebHostBuilder(b => b.ConfigureServices(services =>
            services.Configure<BearerTokenOptions>(IdentityConstants.BearerScheme, o => o.RefreshTokenExpiration = TimeSpan.FromSeconds(-1))));
        using var client = factory.CreateClient();
        var credentials = new { email = $"{Guid.NewGuid():N}@example.test", password = "Test-Password-123!" };
        await client.PostAsJsonAsync("/register", credentials);
        var login = await client.PostAsJsonAsync("/login?useCookies=false", credentials);
        var tokens = (await login.Content.ReadFromJsonAsync<JsonObject>())!;
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.PostAsJsonAsync("/refresh", new { refreshToken = tokens["refreshToken"]!.GetValue<string>() })).StatusCode);
    }
}
