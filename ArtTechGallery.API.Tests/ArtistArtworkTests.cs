using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Nodes;
using ArtTechGallery.Core.DTOs;
using ArtTechGallery.Core.Models;
using ArtTechGallery.API.Storage;
using ArtTechGallery.Core.Storage;
using ArtTechGallery.Infrastructure.Storage;
using ArtTechGallery.Infrastructure.Data;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using SkiaSharp;
using Xunit;

namespace ArtTechGallery.API.Tests;

public sealed class ArtistArtworkTests(PostgresFixture fixture) : IClassFixture<PostgresFixture>
{
    private sealed class BeforeArtworkSave(Func<Task> action) : SaveChangesInterceptor
    {
        public override async ValueTask<InterceptionResult<int>> SavingChangesAsync(
            DbContextEventData eventData, InterceptionResult<int> result, CancellationToken cancellationToken = default)
        {
            if (eventData.Context!.ChangeTracker.Entries<Artwork>().Any(x =>
                x.State is EntityState.Added or EntityState.Modified or EntityState.Deleted)) await action();
            return result;
        }
    }
    private sealed class FailArtworkSave : SaveChangesInterceptor
    {
        public override ValueTask<InterceptionResult<int>> SavingChangesAsync(DbContextEventData eventData,
            InterceptionResult<int> result, CancellationToken cancellationToken = default) =>
            eventData.Context!.ChangeTracker.Entries<Artwork>().Any(x => x.State is EntityState.Added or EntityState.Modified)
                ? ValueTask.FromException<InterceptionResult<int>>(new InvalidOperationException("Simulated database failure"))
                : ValueTask.FromResult(result);
    }
    private sealed class FailingStorage : IArtworkStorage
    {
        public Task WriteAsync(ArtworkImageReference reference, Stream content, CancellationToken cancellationToken) =>
            throw new IOException("Simulated storage failure");
        public Task<Stream?> OpenReadAsync(ArtworkImageReference reference, CancellationToken cancellationToken) => Task.FromResult<Stream?>(null);
        public Task DeleteIfExistsAsync(ArtworkImageReference reference, CancellationToken cancellationToken) => Task.CompletedTask;
    }

    [Theory]
    [InlineData("create")]
    [InlineData("update")]
    [InlineData("delete")]
    public async Task RemovalBetweenOwnershipCheckAndSaveReturnsNotFound(string operation)
    {
        using var owner = await Register();
        var exhibition = await Exhibition(owner);
        var art = await Create(owner, exhibition.Id);
        var interceptor = new BeforeArtworkSave(() => fixture.InDatabase(async db =>
        {
            if (operation == "create") await db.Exhibitions.Where(x => x.Id == exhibition.Id).ExecuteDeleteAsync();
            else await db.Artworks.Where(x => x.Id == art.Id).ExecuteDeleteAsync();
        }));
        using var factory = fixture.Factory.WithWebHostBuilder(builder => builder.ConfigureServices(services =>
            services.ConfigureDbContext<AppDbContext>(options => options.AddInterceptors(interceptor))));
        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = owner.DefaultRequestHeaders.Authorization;
        var metadata = Metadata(); metadata.Title = "Changed during removal";
        var response = operation switch
        {
            "create" => await client.PostAsJsonAsync(Route(exhibition.Id), metadata),
            "update" => await client.PutAsJsonAsync(Route(exhibition.Id) + "/" + art.Id, metadata),
            _ => await client.DeleteAsync(Route(exhibition.Id) + "/" + art.Id)
        };
        await Problem(response, HttpStatusCode.NotFound);
        await fixture.InDatabase(async db => Assert.False(await db.Artworks.AnyAsync(x => x.ExhibitionId == exhibition.Id)));
    }

    [Fact]
    public async Task DemoSeedPreservesPublicRuntimeDataAcrossRepeatedStartup()
    {
        async Task Seed()
        {
            using var scope = fixture.Factory.Services.CreateScope();
            await AppDbSeeder.SeedAsync(scope.ServiceProvider.GetRequiredService<AppDbContext>(),
                scope.ServiceProvider.GetRequiredService<UserManager<User>>(), new Uri("https://artwork-images.test/"));
        }
        await Seed();
        using var client = fixture.Factory.CreateClient();
        var before = (await client.GetFromJsonAsync<JsonObject>("/api/exhibitions/colors-of-nature"))!;
        var artworks = before["artworks"]!.AsArray();
        Assert.Equal(3, artworks.Count);
        Assert.Equal(new[] { 80m, 100m, 50m }, artworks.Select(x => x!["widthCm"]!.GetValue<decimal>()));
        Assert.Equal(new[] { 60m, 70m, 70m }, artworks.Select(x => x!["heightCm"]!.GetValue<decimal>()));
        Assert.Equal(new[] { 1, 2, 3 }, artworks.Select(x => x!["sortOrder"]!.GetValue<int>()));
        Assert.Equal(new[] { "morning-forest.jpg", "quiet-lake.png", "mountain-road.jpg" },
            artworks.Select(x => new Uri(x!["imageUrl"]!.GetValue<string>()).Segments.Last()));
        await Seed();
        var after = await client.GetFromJsonAsync<JsonObject>("/api/exhibitions/colors-of-nature");
        Assert.True(JsonNode.DeepEquals(before, after));
    }

    private static string Route(Guid exhibitionId) => $"/api/artist/exhibitions/{exhibitionId}/artworks";
    private static SaveArtworkRequest Metadata(int order = 0) => new()
    {
        Title = "Artwork", Description = "Description", CreationYear = 2025,
        WidthCm = 80.25m, HeightCm = 60.5m, ImageUrl = "https://images.example.test/art.png", SortOrder = order
    };
    private async Task<HttpClient> Register(bool profile = true)
    {
        var client = fixture.Factory.CreateClient();
        var credentials = new { email = $"{Guid.NewGuid():N}@example.test", password = "Integration-Test-123!" };
        Assert.Equal(HttpStatusCode.OK, (await client.PostAsJsonAsync("/register", credentials)).StatusCode);
        var login = await client.PostAsJsonAsync("/login?useCookies=false", credentials);
        Assert.Equal(HttpStatusCode.OK, login.StatusCode);
        var tokens = (await login.Content.ReadFromJsonAsync<JsonObject>())!;
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", tokens["accessToken"]!.GetValue<string>());
        if (profile) Assert.Equal(HttpStatusCode.Created,
            (await client.PostAsJsonAsync("/api/artist/profile", new { displayName = "Artist" })).StatusCode);
        return client;
    }
    private static async Task<OwnExhibitionDto> Exhibition(HttpClient client)
    {
        var response = await client.PostAsJsonAsync("/api/artist/exhibitions", new { title = "Exhibition" });
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<OwnExhibitionDto>())!;
    }
    private static async Task<OwnArtworkDto> Create(HttpClient client, Guid parent, SaveArtworkRequest? request = null)
    {
        var response = await client.PostAsJsonAsync(Route(parent), request ?? Metadata());
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var result = (await response.Content.ReadFromJsonAsync<OwnArtworkDto>())!;
        Assert.EndsWith(Route(parent) + "/" + result.Id, response.Headers.Location!.ToString());
        return result;
    }
    private static async Task Problem(HttpResponseMessage response, HttpStatusCode expected, bool validation = false)
    {
        Assert.Equal(expected, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        var json = (await response.Content.ReadFromJsonAsync<JsonObject>())!;
        Assert.Equal((int)expected, json["status"]!.GetValue<int>());
        if (validation) Assert.NotNull(json["errors"]);
    }
    private static async Task Transition(HttpClient client, Guid id, string action) =>
        Assert.Equal(HttpStatusCode.OK, (await client.PostAsJsonAsync($"/api/artist/exhibitions/{id}/{action}", new { })).StatusCode);

    private static MultipartFormDataContent Upload(byte[]? image, string mime = "image/jpeg", string fileName = "ignored.jpg")
    {
        var content = new MultipartFormDataContent();
        content.Add(new StringContent("Uploaded artwork"), "title");
        content.Add(new StringContent("Uploaded description"), "description");
        content.Add(new StringContent("2025"), "creationYear");
        content.Add(new StringContent("80.25"), "widthCm");
        content.Add(new StringContent("60.5"), "heightCm");
        content.Add(new StringContent("4"), "sortOrder");
        if (image is not null)
        {
            var file = new ByteArrayContent(image); file.Headers.ContentType = new(mime);
            content.Add(file, "image", fileName);
        }
        return content;
    }

    private static byte[] Fixture(string name) => File.ReadAllBytes(Path.GetFullPath(Path.Combine(
        AppContext.BaseDirectory, "../../../../ArtTechGallery.API/DevelopmentAssets/Artworks", name)));

    private string StoredPath(string imageUrl, Guid artworkId)
    {
        var file = new Uri(imageUrl).Segments.Last();
        return Path.Combine(fixture.StorageRoot, "artworks", artworkId.ToString("N"), file);
    }

    private static void SameArtwork(OwnArtworkDto expected, OwnArtworkDto? actual)
    {
        Assert.NotNull(actual);
        Assert.Equal((expected.Id, expected.ExhibitionId, expected.Title, expected.Description,
            expected.CreationYear, expected.WidthCm, expected.HeightCm, expected.ImageUrl,
            expected.SortOrder, expected.IsActive, expected.CreatedAt),
            (actual.Id, actual.ExhibitionId, actual.Title, actual.Description,
            actual.CreationYear, actual.WidthCm, actual.HeightCm, actual.ImageUrl,
            actual.SortOrder, actual.IsActive, actual.CreatedAt));
    }

    [Fact]
    public async Task LifecycleCrudAndExactPublicContractsRemainCompatible()
    {
        using var owner = await Register(); using var anonymous = fixture.Factory.CreateClient();
        var exhibition = await Exhibition(owner);
        Assert.Empty((await owner.GetFromJsonAsync<OwnArtworkDto[]>(Route(exhibition.Id)))!);
        var art = await Create(owner, exhibition.Id);
        Assert.True(art.IsActive); Assert.NotEqual(Guid.Empty, art.Id); Assert.Equal(exhibition.Id, art.ExhibitionId);
        SameArtwork(art, await owner.GetFromJsonAsync<OwnArtworkDto>(Route(exhibition.Id) + "/" + art.Id));
        await Problem(await anonymous.GetAsync("/api/artworks/" + art.Id), HttpStatusCode.NotFound);
        await Problem(await anonymous.GetAsync("/api/exhibitions/" + exhibition.ExhibitionCode), HttpStatusCode.NotFound);
        await Transition(owner, exhibition.Id, "publish");
        var second = await Create(owner, exhibition.Id, Metadata(10));
        var edit = Metadata(); edit.Title = "  Updated  "; edit.Description = null; edit.WidthCm = 42.12m;
        var update = await owner.PutAsJsonAsync(Route(exhibition.Id) + "/" + art.Id, edit);
        Assert.Equal(HttpStatusCode.OK, update.StatusCode);
        var saved = (await update.Content.ReadFromJsonAsync<OwnArtworkDto>())!;
        Assert.Equal("Updated", saved.Title); Assert.Equal("", saved.Description); Assert.Equal(art.CreatedAt, saved.CreatedAt);
        Assert.Equal(art.Id, saved.Id); Assert.Equal(art.ExhibitionId, saved.ExhibitionId);
        var json = (await anonymous.GetFromJsonAsync<JsonObject>("/api/exhibitions/" + exhibition.ExhibitionCode))!;
        Assert.Equal(new[] { "artistDisplayName", "artistProfileCode", "artworks", "description", "exhibitionCode", "id", "title" }, json.Select(x => x.Key).Order());
        Assert.Equal(new[] { "creationYear", "description", "heightCm", "id", "imageUrl", "sortOrder", "title", "widthCm" }, json["artworks"]![0]!.AsObject().Select(x => x.Key).Order());
        var publicArt = (await anonymous.GetFromJsonAsync<JsonObject>("/api/artworks/" + art.Id))!;
        Assert.Equal(new[] { "artistDisplayName", "artistProfileCode", "creationYear", "description", "exhibitionCode", "exhibitionTitle", "heightCm", "id", "imageUrl", "title", "widthCm" }, publicArt.Select(x => x.Key).Order());
        Assert.Equal(42.12m, publicArt["widthCm"]!.GetValue<decimal>());
        Assert.Equal(60.5m, publicArt["heightCm"]!.GetValue<decimal>());
        Assert.Equal(2025, publicArt["creationYear"]!.GetValue<int>());
        Assert.Equal(edit.ImageUrl, publicArt["imageUrl"]!.GetValue<string>());
        Assert.Equal(exhibition.ExhibitionCode, publicArt["exhibitionCode"]!.GetValue<string>());
        await Transition(owner, exhibition.Id, "deactivate");
        await Problem(await anonymous.GetAsync("/api/artworks/" + art.Id), HttpStatusCode.NotFound);
        Assert.Equal(HttpStatusCode.OK, (await owner.PutAsJsonAsync(Route(exhibition.Id) + "/" + art.Id, Metadata())).StatusCode);
        var third = await Create(owner, exhibition.Id, Metadata(20));
        Assert.Equal(3, (await owner.GetFromJsonAsync<OwnArtworkDto[]>(Route(exhibition.Id)))!.Length);
        await Transition(owner, exhibition.Id, "publish");
        Assert.Equal(HttpStatusCode.OK, (await anonymous.GetAsync("/api/artworks/" + art.Id)).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await owner.DeleteAsync(Route(exhibition.Id) + "/" + art.Id)).StatusCode);
        await Problem(await owner.DeleteAsync(Route(exhibition.Id) + "/" + art.Id), HttpStatusCode.NotFound);
        await Problem(await owner.GetAsync(Route(exhibition.Id) + "/" + art.Id), HttpStatusCode.NotFound);
        await Problem(await anonymous.GetAsync("/api/artworks/" + art.Id), HttpStatusCode.NotFound);
        var remaining = (await anonymous.GetFromJsonAsync<ExhibitionDto>("/api/exhibitions/" + exhibition.ExhibitionCode))!;
        Assert.Equal(new[] { second.Id, third.Id }, remaining.Artworks.Select(x => x.Id));
        Assert.Equal(exhibition.ExhibitionCode, remaining.ExhibitionCode);
        await fixture.InDatabase(async db =>
        {
            Assert.True(await db.Exhibitions.AnyAsync(x => x.Id == exhibition.Id));
            Assert.False(await db.Artworks.AnyAsync(x => x.Id == art.Id));
        });
    }

    [Fact]
    public async Task DuplicateOrdersGapsInsertAndReorderHaveStablePublicTies()
    {
        using var owner = await Register(); using var anonymous = fixture.Factory.CreateClient(); var exhibition = await Exhibition(owner);
        var first = await Create(owner, exhibition.Id, Metadata(10));
        var second = await Create(owner, exhibition.Id, Metadata(10));
        var third = await Create(owner, exhibition.Id, Metadata(99));
        await Transition(owner, exhibition.Id, "publish");
        // Fixed UUIDs prove the actual SQL tie-breaker, independent of insertion ordering.
        var low = Guid.Parse("00000000-0000-0000-0000-000000000001");
        var high = Guid.Parse("ffffffff-ffff-ffff-ffff-ffffffffffff");
        await fixture.InDatabase(async db =>
        {
            db.Artworks.AddRange(new Artwork { Id = high, ExhibitionId = exhibition.Id, Title = "High", SortOrder = 0 },
                new Artwork { Id = low, ExhibitionId = exhibition.Id, Title = "Low", SortOrder = 0 });
            await db.SaveChangesAsync();
        });
        for (int i = 0; i < 3; i++)
        {
            var own = (await owner.GetFromJsonAsync<OwnArtworkDto[]>(Route(exhibition.Id)))!;
            var visible = (await anonymous.GetFromJsonAsync<ExhibitionDto>("/api/exhibitions/" + exhibition.ExhibitionCode))!.Artworks;
            Assert.Equal(new[] { low, high }, own.Take(2).Select(x => x.Id));
            Assert.Equal(own.Select(x => x.Id), visible.Select(x => x.Id));
        }
        Assert.Equal(HttpStatusCode.OK, (await owner.PutAsJsonAsync(Route(exhibition.Id) + "/" + third.Id, Metadata(5))).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await owner.DeleteAsync(Route(exhibition.Id) + "/" + first.Id)).StatusCode);
        var after = (await owner.GetFromJsonAsync<OwnArtworkDto[]>(Route(exhibition.Id)))!;
        Assert.Equal(new[] { 0, 0, 5, 10 }, after.Select(x => x.SortOrder));
        Assert.Equal(second.Id, after.Last().Id);
    }

    [Fact]
    public async Task ParentOwnershipAndRoutePairAreAlwaysRequired()
    {
        using var owner = await Register(); using var other = await Register(); using var noProfile = await Register(false);
        var first = await Exhibition(owner); var anotherOwn = await Exhibition(owner); var foreign = await Exhibition(other);
        var art = await Create(owner, first.Id);
        foreach (var client in new[] { other, noProfile })
        foreach (var parent in new[] { first.Id, Guid.NewGuid() })
        {
            await Problem(await client.GetAsync(Route(parent)), HttpStatusCode.NotFound);
            await Problem(await client.PostAsJsonAsync(Route(parent), Metadata()), HttpStatusCode.NotFound);
            await Problem(await client.GetAsync(Route(parent) + "/" + art.Id), HttpStatusCode.NotFound);
            await Problem(await client.PutAsJsonAsync(Route(parent) + "/" + art.Id, Metadata(99)), HttpStatusCode.NotFound);
            await Problem(await client.DeleteAsync(Route(parent) + "/" + art.Id), HttpStatusCode.NotFound);
        }
        foreach (var parent in new[] { anotherOwn.Id, foreign.Id })
        {
            await Problem(await owner.GetAsync(Route(parent) + "/" + art.Id), HttpStatusCode.NotFound);
            await Problem(await owner.PutAsJsonAsync(Route(parent) + "/" + art.Id, Metadata()), HttpStatusCode.NotFound);
            await Problem(await owner.DeleteAsync(Route(parent) + "/" + art.Id), HttpStatusCode.NotFound);
        }
        Assert.Equal(0, (await owner.GetFromJsonAsync<OwnArtworkDto>(Route(first.Id) + "/" + art.Id))!.SortOrder);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task InactiveUserOrProfileCannotWriteAndPublicContentIsHidden(bool userInactive)
    {
        using var owner = await Register(); using var anonymous = fixture.Factory.CreateClient(); var exhibition = await Exhibition(owner);
        var art = await Create(owner, exhibition.Id); await Transition(owner, exhibition.Id, "publish");
        await fixture.InDatabase(async db =>
        {
            var parent = await db.Exhibitions.Include(x => x.ArtistProfile.User).SingleAsync(x => x.Id == exhibition.Id);
            if (userInactive) parent.ArtistProfile.User.IsActive = false; else parent.ArtistProfile.IsActive = false;
            await db.SaveChangesAsync();
        });
        await Problem(await owner.PostAsJsonAsync(Route(exhibition.Id), Metadata()), HttpStatusCode.Forbidden);
        await Problem(await owner.PutAsJsonAsync(Route(exhibition.Id) + "/" + art.Id, Metadata()), HttpStatusCode.Forbidden);
        await Problem(await owner.DeleteAsync(Route(exhibition.Id) + "/" + art.Id), HttpStatusCode.Forbidden);
        Assert.Equal(userInactive ? HttpStatusCode.Forbidden : HttpStatusCode.OK, (await owner.GetAsync(Route(exhibition.Id))).StatusCode);
        Assert.Equal(userInactive ? HttpStatusCode.Forbidden : HttpStatusCode.OK, (await owner.GetAsync(Route(exhibition.Id) + "/" + art.Id)).StatusCode);
        await Problem(await anonymous.GetAsync("/api/artworks/" + art.Id), HttpStatusCode.NotFound);
        await Problem(await anonymous.GetAsync("/api/exhibitions/" + exhibition.ExhibitionCode), HttpStatusCode.NotFound);
    }

    [Theory]
    [InlineData("GET", false)]
    [InlineData("POST", false)]
    [InlineData("GET", true)]
    [InlineData("PUT", true)]
    [InlineData("DELETE", true)]
    public async Task AllEndpointsRequireBearer(string method, bool detail)
    {
        using var client = fixture.Factory.CreateClient();
        foreach (string? token in new string?[] { null, "invalid" })
        {
            client.DefaultRequestHeaders.Authorization = token is null ? null : new("Bearer", token);
            using var request = new HttpRequestMessage(new HttpMethod(method), Route(Guid.NewGuid()) + (detail ? "/" + Guid.NewGuid() : ""))
                { Content = JsonContent.Create(Metadata()) };
            await Problem(await client.SendAsync(request), HttpStatusCode.Unauthorized);
        }
    }

    [Theory]
    [InlineData("userId")]
    [InlineData("artistProfileId")]
    [InlineData("exhibitionId")]
    [InlineData("id")]
    [InlineData("isActive")]
    [InlineData("createdAt")]
    [InlineData("storageKey")]
    [InlineData("unknown")]
    public async Task UnknownAndControlledFieldsAreRejected(string field)
    {
        using var owner = await Register(); var exhibition = await Exhibition(owner); var art = await Create(owner, exhibition.Id);
        var json = JsonSerializer.SerializeToNode(Metadata(), new JsonSerializerOptions(JsonSerializerDefaults.Web))!.AsObject();
        json[field] = "forbidden";
        await Problem(await owner.PostAsJsonAsync(Route(exhibition.Id), json), HttpStatusCode.BadRequest, true);
        await Problem(await owner.PutAsJsonAsync(Route(exhibition.Id) + "/" + art.Id, json), HttpStatusCode.BadRequest, true);
        SameArtwork(art, await owner.GetFromJsonAsync<OwnArtworkDto>(Route(exhibition.Id) + "/" + art.Id));
        Assert.Single((await owner.GetFromJsonAsync<OwnArtworkDto[]>(Route(exhibition.Id)))!);
    }

    [Fact]
    public async Task ValidationRejectsInvalidMetadataDimensionsAndImageReferencesWithoutWrites()
    {
        using var owner = await Register(); var exhibition = await Exhibition(owner); var art = await Create(owner, exhibition.Id);
        var invalid = new List<(string Field, JsonNode? Value)>
        {
            ("title", null), ("title", JsonValue.Create(" \t ")), ("title", JsonValue.Create(new string('t',201))),
            ("description", JsonValue.Create(new string('d',2001))), ("creationYear", JsonValue.Create(0)),
            ("creationYear", JsonValue.Create(DateTime.UtcNow.Year + 1)), ("sortOrder", JsonValue.Create(-1))
        };
        foreach (var field in new[] { "widthCm", "heightCm" })
        foreach (var value in new[] { "0", "-1", "0.99", "1000.01", "12.345", "1e100", "\"NaN\"", "\"Infinity\"", "null" })
            invalid.Add((field, JsonNode.Parse(value)));
        foreach (string? value in new string?[] { null, "", "relative.png", "//images.test/a.png", "file:///image.png", "data:image/png;base64,AAA", "javascript:alert(1)",
            "https://user:pass@images.test/a.png", "https://images.test/a.png#fragment", "https://images.test/a b.png", "https://images.test\\a.png", "https://images.test/" + new string('x',1000) })
            invalid.Add(("imageUrl", JsonValue.Create(value)));
        foreach (var (field, value) in invalid)
        {
            var json = JsonSerializer.SerializeToNode(Metadata(), new JsonSerializerOptions(JsonSerializerDefaults.Web))!.AsObject();
            json[field] = value;
            await Problem(await owner.PostAsJsonAsync(Route(exhibition.Id), json), HttpStatusCode.BadRequest, true);
            await Problem(await owner.PutAsJsonAsync(Route(exhibition.Id) + "/" + art.Id, json), HttpStatusCode.BadRequest, true);
        }
        await Problem(await owner.PostAsJsonAsync(Route(exhibition.Id), new { }), HttpStatusCode.BadRequest, true);
        Assert.Single((await owner.GetFromJsonAsync<OwnArtworkDto[]>(Route(exhibition.Id)))!);
        SameArtwork(art, await owner.GetFromJsonAsync<OwnArtworkDto>(Route(exhibition.Id) + "/" + art.Id));
        var boundary = Metadata(int.MaxValue); boundary.WidthCm = 1; boundary.HeightCm = 1000; boundary.Title = new string('t',200);
        boundary.Description = new string('d',2000); boundary.ImageUrl = "http://127.0.0.1:5188/dev-assets/artworks/morning-forest.jpg";
        var accepted = await Create(owner, exhibition.Id, boundary);
        Assert.Equal(boundary.ImageUrl, accepted.ImageUrl); Assert.Equal(1, accepted.WidthCm); Assert.Equal(1000, accepted.HeightCm);
    }

    [Fact]
    public async Task ExistingInactiveArtworkIsOwnerReadableEditableAndDeletableButStaysHidden()
    {
        using var owner = await Register(); using var anonymous = fixture.Factory.CreateClient(); var exhibition = await Exhibition(owner);
        var art = await Create(owner, exhibition.Id); await Transition(owner, exhibition.Id, "publish");
        await fixture.InDatabase(async db => { (await db.Artworks.SingleAsync(x => x.Id == art.Id)).IsActive = false; await db.SaveChangesAsync(); });
        var response = await owner.PutAsJsonAsync(Route(exhibition.Id) + "/" + art.Id, Metadata());
        Assert.Equal(HttpStatusCode.OK, response.StatusCode); Assert.False((await response.Content.ReadFromJsonAsync<OwnArtworkDto>())!.IsActive);
        Assert.Single((await owner.GetFromJsonAsync<OwnArtworkDto[]>(Route(exhibition.Id)))!);
        await Problem(await anonymous.GetAsync("/api/artworks/" + art.Id), HttpStatusCode.NotFound);
        Assert.Empty((await anonymous.GetFromJsonAsync<ExhibitionDto>("/api/exhibitions/" + exhibition.ExhibitionCode))!.Artworks);
        Assert.Equal(HttpStatusCode.NoContent, (await owner.DeleteAsync(Route(exhibition.Id) + "/" + art.Id)).StatusCode);
    }

    [Theory]
    [InlineData("morning-forest.jpg", "image/jpeg")]
    [InlineData("quiet-lake.png", "image/png")]
    public async Task MultipartCreatePersistsValidatedImageAndPublicVisibilityFollowsLifecycle(string fileName, string mime)
    {
        using var owner = await Register(); using var anonymous = fixture.Factory.CreateClient();
        var exhibition = await Exhibition(owner);
        using var request = Upload(Fixture(fileName), mime, "../../untrusted-name.exe");
        var response = await owner.PostAsync(Route(exhibition.Id), request);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var artwork = (await response.Content.ReadFromJsonAsync<OwnArtworkDto>())!;
        Assert.StartsWith($"https://artwork-images.test/api/artworks/{artwork.Id}/images/", artwork.ImageUrl);
        Assert.True(File.Exists(StoredPath(artwork.ImageUrl, artwork.Id)));
        await Problem(await anonymous.GetAsync(artwork.ImageUrl), HttpStatusCode.NotFound);
        await Transition(owner, exhibition.Id, "publish");
        Assert.Single((await anonymous.GetFromJsonAsync<ArtistProfileDto>(
            "/api/profiles/" + (await owner.GetFromJsonAsync<OwnArtistProfileDto>("/api/artist/profile"))!.ProfileCode))!.Exhibitions);
        var image = await anonymous.GetAsync(artwork.ImageUrl);
        Assert.Equal(HttpStatusCode.OK, image.StatusCode);
        Assert.Equal(mime, image.Content.Headers.ContentType?.MediaType);
        Assert.Equal("nosniff", image.Headers.GetValues("X-Content-Type-Options").Single());
        Assert.Equal(Fixture(fileName), await image.Content.ReadAsByteArrayAsync());
        await Transition(owner, exhibition.Id, "deactivate");
        Assert.Empty((await anonymous.GetFromJsonAsync<ArtistProfileDto>(
            "/api/profiles/" + (await owner.GetFromJsonAsync<OwnArtistProfileDto>("/api/artist/profile"))!.ProfileCode))!.Exhibitions);
        await Problem(await anonymous.GetAsync("/api/exhibitions/" + exhibition.ExhibitionCode), HttpStatusCode.NotFound);
        await Problem(await anonymous.GetAsync("/api/artworks/" + artwork.Id), HttpStatusCode.NotFound);
        await Problem(await anonymous.GetAsync(artwork.ImageUrl), HttpStatusCode.NotFound);
        await Transition(owner, exhibition.Id, "publish");
        Assert.Equal(HttpStatusCode.OK, (await anonymous.GetAsync("/api/exhibitions/" + exhibition.ExhibitionCode)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await anonymous.GetAsync("/api/artworks/" + artwork.Id)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await anonymous.GetAsync(artwork.ImageUrl)).StatusCode);
    }

    [Fact]
    public async Task MetadataOnlyEditRetainsImageAndReplacementAndDeletionCleanUpManagedFiles()
    {
        using var owner = await Register(); var exhibition = await Exhibition(owner);
        using var create = Upload(Fixture("morning-forest.jpg"));
        var createdResponse = await owner.PostAsync(Route(exhibition.Id), create);
        var artwork = (await createdResponse.Content.ReadFromJsonAsync<OwnArtworkDto>())!;
        var originalPath = StoredPath(artwork.ImageUrl, artwork.Id);
        var legacyJson = Metadata(); legacyJson.ImageUrl = artwork.ImageUrl;
        Assert.Equal(HttpStatusCode.OK,
            (await owner.PutAsJsonAsync(Route(exhibition.Id) + "/" + artwork.Id, legacyJson)).StatusCode);
        var forged = Metadata(); forged.ImageUrl = $"https://artwork-images.test/api/artworks/{artwork.Id}/images/{Guid.NewGuid():N}.jpg";
        await Problem(await owner.PutAsJsonAsync(Route(exhibition.Id) + "/" + artwork.Id, forged), HttpStatusCode.BadRequest, true);
        using var metadata = Upload(null);
        var metadataResponse = await owner.PutAsync(Route(exhibition.Id) + "/" + artwork.Id, metadata);
        Assert.Equal(HttpStatusCode.OK, metadataResponse.StatusCode);
        Assert.Equal(artwork.ImageUrl, (await metadataResponse.Content.ReadFromJsonAsync<OwnArtworkDto>())!.ImageUrl);
        Assert.True(File.Exists(originalPath));
        using var replacement = Upload(Fixture("quiet-lake.png"), "image/png", "replacement.png");
        var replacedResponse = await owner.PutAsync(Route(exhibition.Id) + "/" + artwork.Id, replacement);
        Assert.Equal(HttpStatusCode.OK, replacedResponse.StatusCode);
        var replaced = (await replacedResponse.Content.ReadFromJsonAsync<OwnArtworkDto>())!;
        Assert.NotEqual(artwork.ImageUrl, replaced.ImageUrl);
        Assert.False(File.Exists(originalPath)); Assert.True(File.Exists(StoredPath(replaced.ImageUrl, artwork.Id)));
        Assert.Equal(HttpStatusCode.NoContent, (await owner.DeleteAsync(Route(exhibition.Id) + "/" + artwork.Id)).StatusCode);
        Assert.False(File.Exists(StoredPath(replaced.ImageUrl, artwork.Id)));
    }

    [Fact]
    public async Task MultipartRejectsMalformedMismatchedOversizedAndStructurallyInvalidRequests()
    {
        using var owner = await Register(); var exhibition = await Exhibition(owner);
        foreach (var content in new[]
        {
            Upload(null), Upload([1,2,3,4], "image/jpeg"), Upload(Fixture("quiet-lake.png"), "image/jpeg"),
            Upload(new byte[ArtworkImageValidator.MaximumBytes + 1], "image/jpeg")
        })
        {
            using (content) await Problem(await owner.PostAsync(Route(exhibition.Id), content), HttpStatusCode.BadRequest, true);
        }
        using var duplicate = Upload(Fixture("morning-forest.jpg")); duplicate.Add(new StringContent("again"), "title");
        await Problem(await owner.PostAsync(Route(exhibition.Id), duplicate), HttpStatusCode.BadRequest, true);
        using var unknown = Upload(Fixture("morning-forest.jpg")); unknown.Add(new StringContent("bad"), "ownerId");
        await Problem(await owner.PostAsync(Route(exhibition.Id), unknown), HttpStatusCode.BadRequest, true);
        using var extra = Upload(Fixture("morning-forest.jpg")); extra.Add(new ByteArrayContent(Fixture("quiet-lake.png")), "secondImage", "b.png");
        await Problem(await owner.PostAsync(Route(exhibition.Id), extra), HttpStatusCode.BadRequest, true);
        Assert.Empty((await owner.GetFromJsonAsync<OwnArtworkDto[]>(Route(exhibition.Id)))!);
    }

    [Fact]
    public async Task OversizedPixelImageAndForgedManagedJsonReferencesAreRejected()
    {
        using var owner = await Register(); var exhibition = await Exhibition(owner);
        using var bitmap = new SKBitmap(4097, 1); using var image = SKImage.FromBitmap(bitmap);
        using var encoded = image.Encode(SKEncodedImageFormat.Png, 100);
        using var content = Upload(encoded.ToArray(), "image/png");
        await Problem(await owner.PostAsync(Route(exhibition.Id), content), HttpStatusCode.BadRequest, true);
        var forged = Metadata(); forged.ImageUrl = $"https://artwork-images.test/api/artworks/{Guid.NewGuid()}/images/{Guid.NewGuid():N}.jpg";
        await Problem(await owner.PostAsJsonAsync(Route(exhibition.Id), forged), HttpStatusCode.BadRequest, true);
        Assert.Equal(HttpStatusCode.NotFound,
            (await fixture.Factory.CreateClient().GetAsync($"/api/artworks/{Guid.NewGuid()}/images/../../appsettings.json")).StatusCode);
    }

    [Fact]
    public async Task UploadOwnershipAndInactiveProfileMatchExistingManagementRules()
    {
        using var owner = await Register(); using var other = await Register(); var exhibition = await Exhibition(owner);
        using (var foreign = Upload(Fixture("morning-forest.jpg")))
            await Problem(await other.PostAsync(Route(exhibition.Id), foreign), HttpStatusCode.NotFound);
        await fixture.InDatabase(async db =>
        {
            var parent = await db.Exhibitions.Include(x => x.ArtistProfile).SingleAsync(x => x.Id == exhibition.Id);
            parent.ArtistProfile.IsActive = false; await db.SaveChangesAsync();
        });
        using (var inactive = Upload(Fixture("morning-forest.jpg")))
            await Problem(await owner.PostAsync(Route(exhibition.Id), inactive), HttpStatusCode.Forbidden);
        using var anonymous = fixture.Factory.CreateClient(); using var unauthenticated = Upload(Fixture("morning-forest.jpg"));
        await Problem(await anonymous.PostAsync(Route(exhibition.Id), unauthenticated), HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task DatabaseFailureRemovesNewlyFinalizedImageWithoutCreatingArtwork()
    {
        using var owner = await Register(); var exhibition = await Exhibition(owner);
        var before = Directory.Exists(fixture.StorageRoot)
            ? Directory.GetFiles(fixture.StorageRoot, "*", SearchOption.AllDirectories).Length : 0;
        using var factory = fixture.Factory.WithWebHostBuilder(builder => builder.ConfigureServices(services =>
            services.ConfigureDbContext<AppDbContext>(options => options.AddInterceptors(new FailArtworkSave()))));
        using var client = factory.CreateClient(); client.DefaultRequestHeaders.Authorization = owner.DefaultRequestHeaders.Authorization;
        using var upload = Upload(Fixture("morning-forest.jpg"));
        Assert.Equal(HttpStatusCode.InternalServerError, (await client.PostAsync(Route(exhibition.Id), upload)).StatusCode);
        var after = Directory.Exists(fixture.StorageRoot)
            ? Directory.GetFiles(fixture.StorageRoot, "*", SearchOption.AllDirectories).Length : 0;
        Assert.Equal(before, after);
        Assert.Empty((await owner.GetFromJsonAsync<OwnArtworkDto[]>(Route(exhibition.Id)))!);
    }

    [Fact]
    public async Task StorageFailureDoesNotCreateAnArtwork()
    {
        using var owner = await Register(); var exhibition = await Exhibition(owner);
        using var factory = fixture.Factory.WithWebHostBuilder(builder => builder.ConfigureServices(services =>
        {
            services.RemoveAll<IArtworkStorage>(); services.AddSingleton<IArtworkStorage, FailingStorage>();
        }));
        using var client = factory.CreateClient(); client.DefaultRequestHeaders.Authorization = owner.DefaultRequestHeaders.Authorization;
        using var upload = Upload(Fixture("morning-forest.jpg"));
        Assert.Equal(HttpStatusCode.InternalServerError, (await client.PostAsync(Route(exhibition.Id), upload)).StatusCode);
        Assert.Empty((await owner.GetFromJsonAsync<OwnArtworkDto[]>(Route(exhibition.Id)))!);
    }

    [Fact]
    public async Task ConcurrentReplacementsLeaveOnlyTheAuthoritativeManagedVersion()
    {
        using var owner = await Register(); var exhibition = await Exhibition(owner);
        using var create = Upload(Fixture("morning-forest.jpg"));
        var createdResponse = await owner.PostAsync(Route(exhibition.Id), create);
        var artwork = (await createdResponse.Content.ReadFromJsonAsync<OwnArtworkDto>())!;
        using var first = Upload(Fixture("quiet-lake.png"), "image/png", "first.png");
        using var second = Upload(Fixture("mountain-road.jpg"), "image/jpeg", "second.jpg");
        var responses = await Task.WhenAll(
            owner.PutAsync(Route(exhibition.Id) + "/" + artwork.Id, first),
            owner.PutAsync(Route(exhibition.Id) + "/" + artwork.Id, second));
        Assert.All(responses, x => Assert.Equal(HttpStatusCode.OK, x.StatusCode));
        var current = (await owner.GetFromJsonAsync<OwnArtworkDto>(Route(exhibition.Id) + "/" + artwork.Id))!;
        var files = Directory.GetFiles(Path.Combine(fixture.StorageRoot, "artworks", artwork.Id.ToString("N")));
        Assert.Single(files);
        Assert.Equal(Path.GetFileName(StoredPath(current.ImageUrl, artwork.Id)), Path.GetFileName(files[0]));
    }

    [Fact]
    public async Task LocalStorageSurvivesRestartAndReconcilesAbandonedTemporaryWrites()
    {
        var artworkId = Guid.NewGuid();
        var reference = new ArtworkImageReference(artworkId, Guid.NewGuid().ToString("N"), "jpg");
        var first = new LocalArtworkStorage(fixture.StorageRoot);
        await first.WriteAsync(reference, new MemoryStream(Fixture("morning-forest.jpg")), default);
        var temporary = StoredPath("https://artwork-images.test/api/artworks/" + artworkId + "/images/" + reference.Version + ".jpg", artworkId) + ".tmp-abandoned";
        await File.WriteAllTextAsync(temporary, "abandoned");
        var restarted = new LocalArtworkStorage(fixture.StorageRoot);
        await using (var reopened = await restarted.OpenReadAsync(reference, default))
        {
            Assert.NotNull(reopened); Assert.Equal(Fixture("morning-forest.jpg").Length, reopened.Length);
        }
        Assert.False(File.Exists(temporary));
        await restarted.DeleteIfExistsAsync(reference, default);
    }
}
