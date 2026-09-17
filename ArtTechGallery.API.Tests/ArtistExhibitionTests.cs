using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Nodes;
using ArtTechGallery.API.Authorization;
using ArtTechGallery.Core.DTOs;
using ArtTechGallery.Core.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Xunit;

namespace ArtTechGallery.API.Tests;

public sealed class ArtistExhibitionTests(PostgresFixture fixture) : IClassFixture<PostgresFixture>
{
    private const string Route = "/api/artist/exhibitions";

    private async Task<HttpClient> Register(bool createProfile = true)
    {
        var client = fixture.Factory.CreateClient();
        var credentials = new { email = $"{Guid.NewGuid():N}@example.test", password = "Integration-Test-123!" };
        Assert.Equal(HttpStatusCode.OK, (await client.PostAsJsonAsync("/register", credentials)).StatusCode);
        var login = await client.PostAsJsonAsync("/login?useCookies=false", credentials);
        Assert.Equal(HttpStatusCode.OK, login.StatusCode);
        var tokens = (await login.Content.ReadFromJsonAsync<JsonObject>())!;
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", tokens["accessToken"]!.GetValue<string>());
        if (createProfile)
            Assert.Equal(HttpStatusCode.Created, (await client.PostAsJsonAsync("/api/artist/profile", new { displayName = "Artist" })).StatusCode);
        return client;
    }

    private static async Task<OwnExhibitionDto> Create(HttpClient client, string title = "Exhibition", int sortOrder = 0)
    {
        var response = await client.PostAsJsonAsync(Route, new { title, description = "Description", sortOrder });
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var result = (await response.Content.ReadFromJsonAsync<OwnExhibitionDto>())!;
        Assert.EndsWith(Route + "/" + result.Id, response.Headers.Location!.ToString());
        return result;
    }

    private static Task<HttpResponseMessage> Transition(HttpClient client, Guid id, string action)
        => client.PostAsJsonAsync($"{Route}/{id}/{action}", new { });

    private static async Task Problem(HttpResponseMessage response, HttpStatusCode expected, bool validation = false)
    {
        Assert.Equal(expected, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        var json = (await response.Content.ReadFromJsonAsync<JsonObject>())!;
        Assert.Equal((int)expected, json["status"]!.GetValue<int>());
        if (validation) Assert.NotNull(json["errors"]);
    }

    [Fact]
    public async Task LifecyclePreservesCodesAndPublicUnityContractAcrossAllVisibilityPaths()
    {
        using var owner = await Register();
        using var anonymous = fixture.Factory.CreateClient();
        var profile = (await owner.GetFromJsonAsync<OwnArtistProfileDto>("/api/artist/profile"))!;
        Assert.Empty((await owner.GetFromJsonAsync<OwnExhibitionDto[]>(Route))!);
        var created = await Create(owner, "  Exhibition  ", 3);
        Assert.Equal("draft", created.Status);
        Assert.Equal("Exhibition", created.Title);
        Assert.Matches("^[a-f0-9]{16}$", created.ExhibitionCode);
        Assert.NotEqual(created.Id.ToString("N"), created.ExhibitionCode);
        var firstArtwork = Guid.NewGuid();
        var secondArtwork = Guid.NewGuid();
        await fixture.InDatabase(async db =>
        {
            db.Artworks.AddRange(
                new Artwork { Id = secondArtwork, ExhibitionId = created.Id, Title = "Second", SortOrder = 2 },
                new Artwork { Id = firstArtwork, ExhibitionId = created.Id, Title = "First", Description = "Artwork description", CreationYear = 2026,
                    WidthCm = 80.25m, HeightCm = 60.5m, ImageUrl = "https://example.test/art.png", SortOrder = 1 },
                new Artwork { ExhibitionId = created.Id, Title = "Hidden", IsActive = false });
            await db.SaveChangesAsync();
        });

        async Task Hidden()
        {
            await Problem(await anonymous.GetAsync("/api/exhibitions/" + created.ExhibitionCode), HttpStatusCode.NotFound);
            await Problem(await anonymous.GetAsync("/api/artworks/" + firstArtwork), HttpStatusCode.NotFound);
            Assert.Empty((await anonymous.GetFromJsonAsync<ArtistProfileDto>("/api/profiles/" + profile.ProfileCode))!.Exhibitions);
        }
        await Hidden();
        await Problem(await Transition(owner, created.Id, "deactivate"), HttpStatusCode.Conflict);

        foreach (string expectedState in new[] { "draft", "published", "deactivated", "published" })
        {
            if (expectedState != "draft")
            {
                string action = expectedState == "published" ? "publish" : "deactivate";
                var response = await Transition(owner, created.Id, action);
                Assert.Equal(HttpStatusCode.OK, response.StatusCode);
                Assert.Equal(expectedState, (await response.Content.ReadFromJsonAsync<OwnExhibitionDto>())!.Status);
                await Problem(await Transition(owner, created.Id, action), HttpStatusCode.Conflict);
            }
            var update = await owner.PutAsJsonAsync(Route + "/" + created.Id, new { title = " Edited ", sortOrder = 4 });
            Assert.Equal(HttpStatusCode.OK, update.StatusCode);
            var edited = (await update.Content.ReadFromJsonAsync<OwnExhibitionDto>())!;
            Assert.Equal(expectedState, edited.Status);
            Assert.Equal("Edited", edited.Title);
            Assert.Equal("", edited.Description);
            Assert.Equal(created.Id, edited.Id);
            Assert.Equal(created.ExhibitionCode, edited.ExhibitionCode);
            Assert.Equal(created.CreatedAt, edited.CreatedAt);
            var read = (await owner.GetFromJsonAsync<OwnExhibitionDto>(Route + "/" + created.Id))!;
            Assert.Equal(JsonSerializer.Serialize(edited), JsonSerializer.Serialize(read));
            Assert.Equal(created.Id, Assert.Single((await owner.GetFromJsonAsync<OwnExhibitionDto[]>(Route))!).Id);
            if (expectedState != "published") { await Hidden(); continue; }
            var json = (await anonymous.GetFromJsonAsync<JsonObject>("/api/exhibitions/" + created.ExhibitionCode))!;
            Assert.Equal(new[] { "artistDisplayName", "artistProfileCode", "artworks", "description", "exhibitionCode", "id", "title" },
                json.Select(x => x.Key).Order().ToArray());
            var artworks = json["artworks"]!.AsArray();
            Assert.Equal(2, artworks.Count);
            Assert.Equal(new[] { "creationYear", "description", "heightCm", "id", "imageUrl", "sortOrder", "title", "widthCm" },
                artworks[0]!.AsObject().Select(x => x.Key).Order().ToArray());
            var result = json.Deserialize<ExhibitionDto>(new JsonSerializerOptions(JsonSerializerDefaults.Web))!;
            Assert.Equal(new[] { firstArtwork, secondArtwork }, result.Artworks.Select(x => x.Id));
            Assert.Equal(80.25m, result.Artworks[0].WidthCm);
            Assert.Equal(60.5m, result.Artworks[0].HeightCm);
            Assert.Equal(2026, result.Artworks[0].CreationYear);
            Assert.Equal("Artwork description", result.Artworks[0].Description);
            Assert.Equal("https://example.test/art.png", result.Artworks[0].ImageUrl);
            Assert.Equal(profile.ProfileCode, result.ArtistProfileCode);
            Assert.Equal(profile.DisplayName, result.ArtistDisplayName);
            Assert.Equal(HttpStatusCode.OK, (await anonymous.GetAsync("/api/artworks/" + firstArtwork)).StatusCode);
            Assert.Equal(2, Assert.Single((await anonymous.GetFromJsonAsync<ArtistProfileDto>("/api/profiles/" + profile.ProfileCode))!.Exhibitions).ArtworksCount);
        }
    }

    [Fact]
    public async Task PublicProfileListsOnlyPublishedExhibitionsInDeterministicOrder()
    {
        using var owner = await Register();
        using var anonymous = fixture.Factory.CreateClient();
        var profile = (await owner.GetFromJsonAsync<OwnArtistProfileDto>("/api/artist/profile"))!;
        var draft = await Create(owner, "Draft", 0);
        var firstTied = await Create(owner, "First tied", 4);
        var secondTied = await Create(owner, "Second tied", 4);
        var empty = await Create(owner, "Empty", 8);
        var deactivated = await Create(owner, "Deactivated", 1);
        foreach (var exhibition in new[] { firstTied, secondTied, empty, deactivated })
            Assert.Equal(HttpStatusCode.OK, (await Transition(owner, exhibition.Id, "publish")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await Transition(owner, deactivated.Id, "deactivate")).StatusCode);
        await fixture.InDatabase(async db =>
        {
            db.Artworks.AddRange(
                new Artwork { ExhibitionId = firstTied.Id, Title = "Visible", IsActive = true },
                new Artwork { ExhibitionId = firstTied.Id, Title = "Hidden", IsActive = false },
                new Artwork { ExhibitionId = secondTied.Id, Title = "Also visible", IsActive = true });
            await db.SaveChangesAsync();
        });

        var response = await anonymous.GetAsync("/api/profiles/" + profile.ProfileCode);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var json = (await response.Content.ReadFromJsonAsync<JsonObject>())!;
        Assert.Equal(new[] { "bio", "displayName", "exhibitions", "id", "profileCode", "profileImageUrl" },
            json.Select(x => x.Key).Order().ToArray());
        var summaries = json["exhibitions"]!.AsArray();
        Assert.All(summaries, item => Assert.Equal(
            new[] { "artworksCount", "description", "exhibitionCode", "id", "sortOrder", "title" },
            item!.AsObject().Select(x => x.Key).Order().ToArray()));
        var result = json.Deserialize<ArtistProfileDto>(new JsonSerializerOptions(JsonSerializerDefaults.Web))!;
        var expectedTies = new[] { firstTied, secondTied }.OrderBy(x => x.Id).Select(x => x.Id);
        Assert.Equal(expectedTies.Append(empty.Id), result.Exhibitions.Select(x => x.Id));
        Assert.Equal(new[] { 1, 1, 0 }, result.Exhibitions.Select(x => x.ArtworksCount));
        Assert.DoesNotContain(result.Exhibitions, x => x.Id == draft.Id || x.Id == deactivated.Id);

        using var unpublishedOwner = await Register();
        var unpublishedProfile = (await unpublishedOwner.GetFromJsonAsync<OwnArtistProfileDto>("/api/artist/profile"))!;
        var emptyResponse = await anonymous.GetAsync("/api/profiles/" + unpublishedProfile.ProfileCode);
        Assert.Equal(HttpStatusCode.OK, emptyResponse.StatusCode);
        Assert.Empty((await emptyResponse.Content.ReadFromJsonAsync<ArtistProfileDto>())!.Exhibitions);
    }

    [Fact]
    public async Task OwnershipMissingProfilesAndListOrdering()
    {
        using var owner = await Register();
        using var other = await Register();
        using var noProfile = await Register(false);
        var first = await Create(owner, "First", 10);
        var second = await Create(owner, "Second", 1);
        Assert.Equal(new[] { second.Id, first.Id }, (await owner.GetFromJsonAsync<OwnExhibitionDto[]>(Route))!.Select(x => x.Id));
        Assert.Empty((await other.GetFromJsonAsync<OwnExhibitionDto[]>(Route))!);
        await Problem(await noProfile.GetAsync(Route), HttpStatusCode.NotFound);
        await Problem(await noProfile.PostAsJsonAsync(Route, new { title = "Missing profile" }), HttpStatusCode.NotFound);
        foreach (var client in new[] { other, noProfile })
        foreach (var id in new[] { first.Id, Guid.NewGuid() })
        {
            await Problem(await client.GetAsync(Route + "/" + id), HttpStatusCode.NotFound);
            await Problem(await client.PutAsJsonAsync(Route + "/" + id, new { title = "Attack" }), HttpStatusCode.NotFound);
            await Problem(await Transition(client, id, "publish"), HttpStatusCode.NotFound);
            await Problem(await Transition(client, id, "deactivate"), HttpStatusCode.NotFound);
        }
        Assert.Equal("First", (await owner.GetFromJsonAsync<OwnExhibitionDto>(Route + "/" + first.Id))!.Title);
        Assert.Equal("draft", (await owner.GetFromJsonAsync<OwnExhibitionDto>(Route + "/" + first.Id))!.Status);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task InactiveUserOrProfileBlocksWritesAndPublicVisibility(bool inactiveUser)
    {
        using var owner = await Register();
        using var anonymous = fixture.Factory.CreateClient();
        var exhibition = await Create(owner);
        Assert.Equal(HttpStatusCode.OK, (await Transition(owner, exhibition.Id, "publish")).StatusCode);
        var profile = (await owner.GetFromJsonAsync<OwnArtistProfileDto>("/api/artist/profile"))!;
        var artworkId = Guid.NewGuid();
        await fixture.InDatabase(async db =>
        {
            db.Artworks.Add(new Artwork { Id = artworkId, ExhibitionId = exhibition.Id, Title = "Artwork" });
            var stored = await db.ArtistProfiles.Include(x => x.User).SingleAsync(x => x.Id == profile.Id);
            if (inactiveUser) stored.User.IsActive = false; else stored.IsActive = false;
            await db.SaveChangesAsync();
        });
        foreach (var path in new[] { "/api/profiles/" + profile.ProfileCode, "/api/exhibitions/" + exhibition.ExhibitionCode, "/api/artworks/" + artworkId })
            await Problem(await anonymous.GetAsync(path), HttpStatusCode.NotFound);
        if (inactiveUser)
        {
            await Problem(await owner.GetAsync(Route), HttpStatusCode.Forbidden);
            await Problem(await owner.GetAsync(Route + "/" + exhibition.Id), HttpStatusCode.Forbidden);
        }
        else
        {
            Assert.Single((await owner.GetFromJsonAsync<OwnExhibitionDto[]>(Route))!);
            Assert.Equal("published", (await owner.GetFromJsonAsync<OwnExhibitionDto>(Route + "/" + exhibition.Id))!.Status);
        }
        await Problem(await owner.PostAsJsonAsync(Route, new { title = "Blocked" }), HttpStatusCode.Forbidden);
        await Problem(await owner.PutAsJsonAsync(Route + "/" + exhibition.Id, new { title = "Blocked" }), HttpStatusCode.Forbidden);
        await Problem(await Transition(owner, exhibition.Id, "publish"), HttpStatusCode.Forbidden);
        await Problem(await Transition(owner, exhibition.Id, "deactivate"), HttpStatusCode.Forbidden);
    }

    [Theory]
    [InlineData("GET", "")]
    [InlineData("POST", "")]
    [InlineData("GET", "/00000000-0000-0000-0000-000000000001")]
    [InlineData("PUT", "/00000000-0000-0000-0000-000000000001")]
    [InlineData("POST", "/00000000-0000-0000-0000-000000000001/publish")]
    [InlineData("POST", "/00000000-0000-0000-0000-000000000001/deactivate")]
    public async Task AllManagementEndpointsRequireBearer(string method, string suffix)
    {
        using var client = fixture.Factory.CreateClient();
        foreach (string? token in new string?[] { null, "invalid-token" })
        {
            client.DefaultRequestHeaders.Authorization = token is null ? null : new("Bearer", token);
            using var request = new HttpRequestMessage(new HttpMethod(method), Route + suffix) { Content = JsonContent.Create(new { title = "Exhibition" }) };
            await Problem(await client.SendAsync(request), HttpStatusCode.Unauthorized);
        }
    }

    [Theory]
    [InlineData("userId")]
    [InlineData("artistProfileId")]
    [InlineData("id")]
    [InlineData("exhibitionCode")]
    [InlineData("status")]
    [InlineData("isActive")]
    [InlineData("createdAt")]
    [InlineData("artworks")]
    [InlineData("unknown")]
    public async Task UnknownAndServerControlledFieldsAreRejected(string field)
    {
        using var client = await Register();
        var payload = new JsonObject { ["title"] = "Exhibition", [field] = "forbidden" };
        await Problem(await client.PostAsJsonAsync(Route, payload), HttpStatusCode.BadRequest, true);
        Assert.Empty((await client.GetFromJsonAsync<OwnExhibitionDto[]>(Route))!);
        var before = await Create(client);
        await Problem(await client.PutAsJsonAsync(Route + "/" + before.Id, payload), HttpStatusCode.BadRequest, true);
        foreach (string action in new[] { "publish", "deactivate" })
            await Problem(await client.PostAsJsonAsync($"{Route}/{before.Id}/{action}", new JsonObject { [field] = "forbidden" }), HttpStatusCode.BadRequest, true);
        Assert.Equal(JsonSerializer.Serialize(before), JsonSerializer.Serialize(await client.GetFromJsonAsync<OwnExhibitionDto>(Route + "/" + before.Id)));
    }

    [Fact]
    public async Task MetadataValidationBoundaries()
    {
        using var client = await Register();
        object[] invalid = [new { }, new { title = (string?)null }, new { title = " \t " }, new { title = new string('x', 201) },
            new { title = "Valid", description = new string('x', 2001) }, new { title = "Valid", sortOrder = -1 }];
        foreach (var payload in invalid)
            await Problem(await client.PostAsJsonAsync(Route, payload), HttpStatusCode.BadRequest, true);
        var response = await client.PostAsJsonAsync(Route, new { title = " " + new string('x', 200) + " ", description = new string('d', 2000), sortOrder = int.MaxValue });
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var before = (await response.Content.ReadFromJsonAsync<OwnExhibitionDto>())!;
        foreach (var payload in invalid)
            await Problem(await client.PutAsJsonAsync(Route + "/" + before.Id, payload), HttpStatusCode.BadRequest, true);
        foreach (var description in new string?[] { null, "" })
        {
            var update = await client.PutAsJsonAsync(Route + "/" + before.Id, new { title = "Valid", description });
            Assert.Equal(HttpStatusCode.OK, update.StatusCode);
            Assert.Equal("", (await update.Content.ReadFromJsonAsync<OwnExhibitionDto>())!.Description);
        }
    }

    private sealed class SequenceCodes(params string[] codes) : ExhibitionCodeGenerator
    {
        public int Calls;
        public override string Generate() => codes[Math.Min(Interlocked.Increment(ref Calls) - 1, codes.Length - 1)];
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task CodeCollisionRetriesAreBounded(bool exhaust)
    {
        using var owner = await Register();
        var taken = await Create(owner);
        var unique = new ExhibitionCodeGenerator().Generate();
        var codes = exhaust ? new SequenceCodes(taken.ExhibitionCode) : new SequenceCodes(taken.ExhibitionCode, unique);
        using var factory = fixture.Factory.WithWebHostBuilder(builder => builder.ConfigureServices(services =>
        {
            services.RemoveAll<ExhibitionCodeGenerator>();
            services.AddSingleton<ExhibitionCodeGenerator>(codes);
        }));
        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = owner.DefaultRequestHeaders.Authorization;
        var response = await client.PostAsJsonAsync(Route, new { title = "Collision" });
        if (exhaust) await Problem(response, HttpStatusCode.Conflict);
        else
        {
            Assert.Equal(HttpStatusCode.Created, response.StatusCode);
            Assert.Equal(unique, (await response.Content.ReadFromJsonAsync<OwnExhibitionDto>())!.ExhibitionCode);
        }
        Assert.Equal(exhaust ? 5 : 2, codes.Calls);
        Assert.Equal(exhaust ? 1 : 2, (await owner.GetFromJsonAsync<OwnExhibitionDto[]>(Route))!.Length);
    }

    private sealed class ConcurrentSaves : SaveChangesInterceptor
    {
        private int calls;
        private readonly TaskCompletionSource ready = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public override async ValueTask<InterceptionResult<int>> SavingChangesAsync(
            DbContextEventData eventData, InterceptionResult<int> result, CancellationToken cancellationToken = default)
        {
            if (eventData.Context!.ChangeTracker.Entries<Exhibition>().Any(x => x.State == EntityState.Modified))
            {
                if (Interlocked.Increment(ref calls) == 2) ready.TrySetResult();
                await ready.Task.WaitAsync(TimeSpan.FromSeconds(20), cancellationToken);
            }
            return result;
        }
    }

    [Fact]
    public async Task ConcurrentPublicationHasOneWinnerAndOneConflict()
    {
        using var owner = await Register();
        var exhibition = await Create(owner);
        var saves = new ConcurrentSaves();
        using var factory = fixture.Factory.WithWebHostBuilder(builder => builder.ConfigureServices(services =>
            services.ConfigureDbContext<ArtTechGallery.Infrastructure.Data.AppDbContext>(options => options.AddInterceptors(saves))));
        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = owner.DefaultRequestHeaders.Authorization;
        var responses = await Task.WhenAll(Transition(client, exhibition.Id, "publish"), Transition(client, exhibition.Id, "publish"));
        Assert.Single(responses, x => x.StatusCode == HttpStatusCode.OK);
        await Problem(Assert.Single(responses, x => x.StatusCode == HttpStatusCode.Conflict), HttpStatusCode.Conflict);
        var result = (await owner.GetFromJsonAsync<OwnExhibitionDto>(Route + "/" + exhibition.Id))!;
        Assert.Equal("published", result.Status);
        Assert.Equal(exhibition.ExhibitionCode, result.ExhibitionCode);
    }
}
