using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Nodes;
using ArtTechGallery.API.Authorization;
using ArtTechGallery.Core.DTOs;
using ArtTechGallery.Core.Models;
using Microsoft.AspNetCore.Hosting;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Xunit;

namespace ArtTechGallery.API.Tests;

public sealed class ArtistProfileTests(PostgresFixture fixture) : IClassFixture<PostgresFixture>
{
    private const string Route = "/api/artist/profile";
    private const string TestPassword = "Integration-Test-123!";

    private async Task<(HttpClient Client, string Email)> Register()
    {
        var client = fixture.Factory.CreateClient();
        string email = $"{Guid.NewGuid():N}@example.test";
        Assert.Equal(HttpStatusCode.OK, (await client.PostAsJsonAsync("/register", new { email, password = TestPassword })).StatusCode);
        var login = await client.PostAsJsonAsync("/login?useCookies=false", new { email, password = TestPassword });
        Assert.Equal(HttpStatusCode.OK, login.StatusCode);
        var tokens = (await login.Content.ReadFromJsonAsync<JsonObject>())!;
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", tokens["accessToken"]!.GetValue<string>());
        return (client, email);
    }

    private static async Task<OwnArtistProfileDto> Create(HttpClient client, string name = "Artist")
    {
        var response = await client.PostAsJsonAsync(Route, new { displayName = name, bio = "Biography" });
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        Assert.EndsWith(Route, response.Headers.Location!.ToString());
        return (await response.Content.ReadFromJsonAsync<OwnArtistProfileDto>())!;
    }

    private static async Task AssertProblem(HttpResponseMessage response, HttpStatusCode status, bool validation = false)
    {
        Assert.Equal(status, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        var body = (await response.Content.ReadFromJsonAsync<JsonObject>())!;
        Assert.Equal((int)status, body["status"]!.GetValue<int>());
        if (validation) Assert.NotNull(body["errors"]);
    }

    [Fact]
    public async Task RegisterLoginCreateReadUpdateAndAnonymousRead()
    {
        var (client, _) = await Register();
        using (client)
        {
            var profile = await Create(client, "  Artist name  ");
            Assert.Equal("Artist name", profile.DisplayName);
            Assert.True(profile.IsActive);
            Assert.Equal("", profile.ProfileImageUrl);
            Assert.NotEqual(Guid.Empty, profile.Id);
            Assert.Matches("^[a-f0-9]{16}$", profile.ProfileCode);
            var own = await client.GetFromJsonAsync<OwnArtistProfileDto>(Route);
            Assert.Equal(profile.Id, own!.Id);
            var updated = await client.PutAsJsonAsync(Route, new { displayName = " Updated name ", bio = "New bio" });
            Assert.Equal(HttpStatusCode.OK, updated.StatusCode);
            var result = (await updated.Content.ReadFromJsonAsync<OwnArtistProfileDto>())!;
            Assert.Equal("Updated name", result.DisplayName);
            Assert.Equal("New bio", result.Bio);
            Assert.Equal(profile.Id, result.Id);
            Assert.Equal(profile.ProfileCode, result.ProfileCode);
            Assert.Equal(profile.CreatedAt, result.CreatedAt);
            using var anonymous = fixture.Factory.CreateClient();
            var publicProfile = await anonymous.GetFromJsonAsync<ArtistProfileDto>("/api/profiles/" + profile.ProfileCode);
            Assert.Equal(result.DisplayName, publicProfile!.DisplayName);
            Assert.Equal(result.Bio, publicProfile.Bio);
            var json = (await client.GetFromJsonAsync<JsonObject>(Route))!;
            Assert.Equal(new[] { "bio", "createdAt", "displayName", "id", "isActive", "profileCode", "profileImageUrl" },
                json.Select(x => x.Key).Order().ToArray());
        }
    }

    [Fact]
    public async Task UsersAreIsolatedAndMissingProfileIs404()
    {
        var (a, _) = await Register();
        var (b, _) = await Register();
        using (a) using (b)
        {
            var first = await Create(a, "A");
            await AssertProblem(await b.GetAsync(Route), HttpStatusCode.NotFound);
            await AssertProblem(await b.PutAsJsonAsync(Route, new { displayName = "Cannot edit A" }), HttpStatusCode.NotFound);
            var second = await Create(b, "B");
            Assert.NotEqual(first.Id, second.Id);
            Assert.NotEqual(first.ProfileCode, second.ProfileCode);
            Assert.Equal(HttpStatusCode.OK, (await b.PutAsJsonAsync(Route, new { displayName = "B updated" })).StatusCode);
            Assert.Equal("A", (await a.GetFromJsonAsync<OwnArtistProfileDto>(Route))!.DisplayName);
            Assert.Equal(second.Id, (await b.GetFromJsonAsync<OwnArtistProfileDto>(Route))!.Id);
            Assert.Equal(HttpStatusCode.NotFound, (await b.PutAsJsonAsync(Route + "/" + first.Id, new { displayName = "Attack" })).StatusCode);
            await AssertProblem(await a.PostAsJsonAsync(Route, new { displayName = "Duplicate" }), HttpStatusCode.Conflict);
        }
    }

    [Theory]
    [InlineData("GET")]
    [InlineData("POST")]
    [InlineData("PUT")]
    public async Task MissingAndInvalidBearerAre401(string method)
    {
        using var client = fixture.Factory.CreateClient();
        foreach (string? token in new string?[] { null, "invalid-token" })
        {
            client.DefaultRequestHeaders.Authorization = token is null ? null : new("Bearer", token);
            using var request = new HttpRequestMessage(new HttpMethod(method), Route)
            { Content = JsonContent.Create(new { displayName = "Artist" }) };
            await AssertProblem(await client.SendAsync(request), HttpStatusCode.Unauthorized);
        }
    }

    [Fact]
    public async Task InactiveAndDeletedUsersCannotManageWithPreviouslyIssuedTokens()
    {
        var (client, email) = await Register();
        using (client)
        {
            await Create(client);
            await fixture.InDatabase(async db =>
            {
                var user = await db.Users.SingleAsync(x => x.Email == email);
                user.IsActive = false;
                await db.SaveChangesAsync();
            });
            await AssertProblem(await client.GetAsync(Route), HttpStatusCode.Forbidden);
            await AssertProblem(await client.PostAsJsonAsync(Route, new { displayName = "Artist" }), HttpStatusCode.Forbidden);
            await AssertProblem(await client.PutAsJsonAsync(Route, new { displayName = "Artist" }), HttpStatusCode.Forbidden);
            await fixture.InDatabase(async db =>
            {
                db.Users.Remove(await db.Users.SingleAsync(x => x.Email == email));
                await db.SaveChangesAsync();
            });
            await AssertProblem(await client.GetAsync(Route), HttpStatusCode.Forbidden);
        }
    }

    [Fact]
    public async Task InactiveProfileIsReadableButCannotBeEditedOrRecreated()
    {
        var (client, email) = await Register();
        using (client)
        {
            var profile = await Create(client);
            await fixture.InDatabase(async db =>
            {
                var stored = await db.ArtistProfiles.SingleAsync(x => x.Id == profile.Id);
                stored.IsActive = false;
                await db.SaveChangesAsync();
            });
            Assert.False((await client.GetFromJsonAsync<OwnArtistProfileDto>(Route))!.IsActive);
            await AssertProblem(await client.PutAsJsonAsync(Route, new { displayName = "Reactivate" }), HttpStatusCode.Forbidden);
            await AssertProblem(await client.PostAsJsonAsync(Route, new { displayName = "Recreate" }), HttpStatusCode.Conflict);
        }
    }

    [Theory]
    [InlineData("userId")]
    [InlineData("ownerId")]
    [InlineData("id")]
    [InlineData("profileCode")]
    [InlineData("isActive")]
    [InlineData("createdAt")]
    [InlineData("profileImageUrl")]
    [InlineData("imageUrl")]
    [InlineData("unknown")]
    public async Task ProtectedAndUnknownFieldsAreRejected(string field)
    {
        var (client, _) = await Register();
        using (client)
        {
            var payload = new JsonObject { ["displayName"] = "Artist", [field] = "forbidden" };
            await AssertProblem(await client.PostAsJsonAsync(Route, payload), HttpStatusCode.BadRequest, true);
            await AssertProblem(await client.GetAsync(Route), HttpStatusCode.NotFound);
            var before = await Create(client);
            await AssertProblem(await client.PutAsJsonAsync(Route, payload), HttpStatusCode.BadRequest, true);
            var after = await client.GetFromJsonAsync<OwnArtistProfileDto>(Route);
            Assert.Equal(JsonSerializer.Serialize(before), JsonSerializer.Serialize(after));
        }
    }

    [Fact]
    public async Task ValidationBoundariesApplyToBothCreateAndUpdate()
    {
        var (client, _) = await Register();
        using (client)
        {
            var invalid = new object[] {
                new { bio = "Missing display name" }, new { displayName = (string?)null },
                new { displayName = " \t " }, new { displayName = new string('x', 201) },
                new { displayName = "Valid", bio = new string('x', 2001) }
            };
            foreach (var payload in invalid)
                await AssertProblem(await client.PostAsJsonAsync(Route, payload), HttpStatusCode.BadRequest, true);
            var response = await client.PostAsJsonAsync(Route, new { displayName = " " + new string('x', 200) + " ", bio = new string('b', 2000) });
            Assert.Equal(HttpStatusCode.Created, response.StatusCode);
            foreach (var payload in invalid)
                await AssertProblem(await client.PutAsJsonAsync(Route, payload), HttpStatusCode.BadRequest, true);
            foreach (var payload in new object[] { new { displayName = "a" }, new { displayName = "a", bio = (string?)null }, new { displayName = "a", bio = "" } })
            {
                var update = await client.PutAsJsonAsync(Route, payload);
                Assert.Equal(HttpStatusCode.OK, update.StatusCode);
                Assert.Equal("", (await update.Content.ReadFromJsonAsync<OwnArtistProfileDto>())!.Bio);
            }
        }
    }

    [Fact]
    public async Task UpdatePreservesAllServerOwnedFields()
    {
        var (client, _) = await Register();
        using (client)
        {
            var created = await Create(client);
            Guid owner = Guid.Empty;
            DateTime createdAt = DateTime.UtcNow.AddYears(-2);
            await fixture.InDatabase(async db =>
            {
                var stored = await db.ArtistProfiles.SingleAsync(x => x.Id == created.Id);
                owner = stored.UserId;
                stored.ProfileImageUrl = "https://images.example.test/custom.png";
                stored.CreatedAt = createdAt;
                await db.SaveChangesAsync();
            });
            var before = (await client.GetFromJsonAsync<OwnArtistProfileDto>(Route))!;
            Assert.Equal(HttpStatusCode.OK, (await client.PutAsJsonAsync(Route, new { displayName = "Edited", bio = "Edited" })).StatusCode);
            await fixture.InDatabase(async db =>
            {
                var stored = await db.ArtistProfiles.SingleAsync(x => x.Id == created.Id);
                Assert.Equal(owner, stored.UserId);
                Assert.Equal(before.CreatedAt, stored.CreatedAt);
                Assert.Equal(before.ProfileCode, stored.ProfileCode);
                Assert.Equal(before.ProfileImageUrl, stored.ProfileImageUrl);
                Assert.Equal(before.IsActive, stored.IsActive);
            });
        }
    }

    private sealed class SequenceCodes(params string[] codes) : ProfileCodeGenerator
    {
        public int Calls;
        public override string Generate() => codes[Math.Min(Interlocked.Increment(ref Calls) - 1, codes.Length - 1)];
    }

    private sealed class ConcurrentCodes : ProfileCodeGenerator, IDisposable
    {
        private readonly Barrier barrier = new(2);
        public override string Generate()
        {
            if (!barrier.SignalAndWait(TimeSpan.FromSeconds(20))) throw new TimeoutException("Both creates must reach the insert.");
            return base.Generate();
        }
        public void Dispose() => barrier.Dispose();
    }

    [Fact]
    public async Task ConcurrentCreatesProduceOneProfileAndOneConflict()
    {
        var (authenticated, email) = await Register();
        using (authenticated)
        using (var codes = new ConcurrentCodes())
        using (var factory = fixture.Factory.WithWebHostBuilder(builder => builder.ConfigureServices(services =>
        {
            services.RemoveAll<ProfileCodeGenerator>();
            services.AddSingleton<ProfileCodeGenerator>(codes);
        })))
        using (var client = factory.CreateClient())
        {
            client.DefaultRequestHeaders.Authorization = authenticated.DefaultRequestHeaders.Authorization;
            var responses = await Task.WhenAll(
                Task.Run(() => client.PostAsJsonAsync(Route, new { displayName = "First" })),
                Task.Run(() => client.PostAsJsonAsync(Route, new { displayName = "Second" })));
            Assert.Single(responses, x => x.StatusCode == HttpStatusCode.Created);
            await AssertProblem(Assert.Single(responses, x => x.StatusCode == HttpStatusCode.Conflict), HttpStatusCode.Conflict);
            await fixture.InDatabase(async db => Assert.Equal(1,
                await db.ArtistProfiles.CountAsync(x => x.User.Email == email)));
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task CodeCollisionsAreRetriedAndBounded(bool exhaust)
    {
        var (existingClient, _) = await Register();
        using var existing = existingClient;
        var taken = await Create(existing);
        var (authenticated, email) = await Register();
        using var auth = authenticated;
        var unique = new ProfileCodeGenerator().Generate();
        var codes = exhaust ? new SequenceCodes(taken.ProfileCode) : new SequenceCodes(taken.ProfileCode, unique);
        using var factory = fixture.Factory.WithWebHostBuilder(builder => builder.ConfigureServices(services =>
        {
            services.RemoveAll<ProfileCodeGenerator>();
            services.AddSingleton<ProfileCodeGenerator>(codes);
        }));
        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = auth.DefaultRequestHeaders.Authorization;
        var response = await client.PostAsJsonAsync(Route, new { displayName = "Collision test" });
        if (exhaust)
        {
            await AssertProblem(response, HttpStatusCode.Conflict);
            Assert.Equal(5, codes.Calls);
            await fixture.InDatabase(async db => Assert.False(await db.ArtistProfiles.AnyAsync(x => x.User.Email == email)));
        }
        else
        {
            Assert.Equal(HttpStatusCode.Created, response.StatusCode);
            Assert.Equal(unique, (await response.Content.ReadFromJsonAsync<OwnArtistProfileDto>())!.ProfileCode);
            Assert.Equal(2, codes.Calls);
        }
        Assert.Equal(taken.Id, (await existing.GetFromJsonAsync<OwnArtistProfileDto>(Route))!.Id);
    }

    [Fact]
    public async Task PublicEndpointsRemainAnonymousAndHideInactiveOwners()
    {
        var (client, email) = await Register();
        using (client)
        {
            var profile = await Create(client);
            string exhibitionCode = new ProfileCodeGenerator().Generate();
            var artworkId = Guid.NewGuid();
            await fixture.InDatabase(async db =>
            {
                db.Exhibitions.Add(new Exhibition
                {
                    ArtistProfileId = profile.Id, Title = "Exhibition", ExhibitionCode = exhibitionCode,
                    Status = ExhibitionStatus.Published,
                    Artworks = [new Artwork { Id = artworkId, Title = "Artwork", WidthCm = 80, HeightCm = 60, ImageUrl = "https://example.test/image.jpg" }]
                });
                await db.SaveChangesAsync();
            });
            using var anonymous = fixture.Factory.CreateClient();
            var paths = new[] { "/api/profiles/" + profile.ProfileCode, "/api/exhibitions/" + exhibitionCode, "/api/artworks/" + artworkId };
            foreach (var path in paths) Assert.Equal(HttpStatusCode.OK, (await anonymous.GetAsync(path)).StatusCode);
            await fixture.InDatabase(async db =>
            {
                (await db.Users.SingleAsync(x => x.Email == email)).IsActive = false;
                await db.SaveChangesAsync();
            });
            foreach (var path in paths) await AssertProblem(await anonymous.GetAsync(path), HttpStatusCode.NotFound);
        }
    }
}
