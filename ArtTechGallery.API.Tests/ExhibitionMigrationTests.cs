using ArtTechGallery.Core.Models;
using ArtTechGallery.Infrastructure.Data;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace ArtTechGallery.API.Tests;

// Separate random database: migration rollback never affects other tests or application data.
public sealed class ExhibitionMigrationTests(PostgresFixture fixture) : IClassFixture<PostgresFixture>
{
    [Fact]
    public async Task LegacyUpgradePreservesVisibilityDataAndCodesAndRollbackKeepsDraftsHidden()
    {
        await using var scope = fixture.Factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var users = scope.ServiceProvider.GetRequiredService<UserManager<User>>();
        await AppDbSeeder.SeedAsync(db, users);
        var demo = await db.Exhibitions.AsNoTracking().Include(x => x.Artworks).SingleAsync();
        Assert.Equal(ExhibitionStatus.Published, demo.Status);
        var hiddenId = Guid.NewGuid();
        var hiddenCode = "legacy-hidden";
        db.Exhibitions.Add(new Exhibition { Id = hiddenId, ArtistProfileId = demo.ArtistProfileId,
            Title = "Hidden", ExhibitionCode = hiddenCode, Status = ExhibitionStatus.Deactivated });
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();
        var migrator = db.GetService<IMigrator>();
        await migrator.MigrateAsync("20260711121353_UpdateShareCode");
        Assert.True(await db.Database.SqlQueryRaw<bool>("SELECT \"IsActive\" AS \"Value\" FROM \"Exhibitions\" WHERE \"ExhibitionCode\" = 'colors-of-nature'").SingleAsync());
        Assert.False(await db.Database.SqlQueryRaw<bool>("SELECT \"IsActive\" AS \"Value\" FROM \"Exhibitions\" WHERE \"ExhibitionCode\" = 'legacy-hidden'").SingleAsync());
        await migrator.MigrateAsync();
        var restored = await db.Exhibitions.AsNoTracking().Include(x => x.Artworks).SingleAsync(x => x.Id == demo.Id);
        Assert.Equal(ExhibitionStatus.Published, restored.Status);
        Assert.Equal(demo.ExhibitionCode, restored.ExhibitionCode);
        Assert.Equal(demo.ArtistProfileId, restored.ArtistProfileId);
        Assert.Equal(demo.Title, restored.Title);
        Assert.Equal(demo.Description, restored.Description);
        Assert.Equal(demo.CreatedAt, restored.CreatedAt);
        Assert.Equal(demo.SortOrder, restored.SortOrder);
        Assert.Equal(demo.Artworks.OrderBy(x => x.Id).Select(x => (x.Id, x.ImageUrl, x.WidthCm, x.HeightCm, x.SortOrder)),
            restored.Artworks.OrderBy(x => x.Id).Select(x => (x.Id, x.ImageUrl, x.WidthCm, x.HeightCm, x.SortOrder)));
        Assert.Equal(ExhibitionStatus.Deactivated, (await db.Exhibitions.AsNoTracking().SingleAsync(x => x.Id == hiddenId)).Status);

        // Omission of Status at the database level must also be safe by default.
        var draftId = Guid.NewGuid();
        await db.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO "Exhibitions" ("Id", "ArtistProfileId", "Title", "Description", "ExhibitionCode", "SortOrder", "CreatedAt")
            VALUES ({draftId}, {demo.ArtistProfileId}, 'Draft', '', 'default-draft', 0, {DateTime.UtcNow})
            """);
        Assert.Equal(ExhibitionStatus.Draft, (await db.Exhibitions.AsNoTracking().SingleAsync(x => x.Id == draftId)).Status);
        var error = await Assert.ThrowsAsync<Npgsql.PostgresException>(() => db.Database.ExecuteSqlRawAsync(
            "UPDATE \"Exhibitions\" SET \"Status\" = 99 WHERE \"ExhibitionCode\" = 'default-draft'"));
        Assert.Equal(Npgsql.PostgresErrorCodes.CheckViolation, error.SqlState);
        await migrator.MigrateAsync("20260711121353_UpdateShareCode");
        Assert.False(await db.Database.SqlQueryRaw<bool>("SELECT \"IsActive\" AS \"Value\" FROM \"Exhibitions\" WHERE \"ExhibitionCode\" = 'default-draft'").SingleAsync());
        await migrator.MigrateAsync();

        // Existing demo seeding repairs URLs only; it must never republish a deactivated exhibition.
        var trackedDemo = await db.Exhibitions.SingleAsync(x => x.Id == demo.Id);
        trackedDemo.Status = ExhibitionStatus.Deactivated;
        await db.SaveChangesAsync();
        await AppDbSeeder.SeedAsync(db, users, new Uri("http://localhost:5188"));
        await AppDbSeeder.SeedAsync(db, users, new Uri("http://localhost:5188"));
        await db.Entry(trackedDemo).ReloadAsync();
        Assert.Equal(ExhibitionStatus.Deactivated, trackedDemo.Status);
        Assert.Equal(demo.ExhibitionCode, trackedDemo.ExhibitionCode);
    }
}
