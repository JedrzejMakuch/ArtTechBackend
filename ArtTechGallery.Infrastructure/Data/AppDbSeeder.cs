using ArtTechGallery.Core.Models;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace ArtTechGallery.Infrastructure.Data;

public static class AppDbSeeder
{
    private const string DemoEmail = "demo@arttechgallery.local";
    private const string DemoPassword = "Demo123!";
    public const string DevelopmentAssetRequestPath = "/dev-assets/artworks";

    public static Uri? ValidateDevelopmentPublicBaseUrl(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;

        if (!Uri.TryCreate(value.Trim(), UriKind.Absolute, out Uri? uri)
            || (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps)
            || string.IsNullOrEmpty(uri.Host)
            || uri.Host is "0.0.0.0" or "[::]" or "::" or "*" or "+"
            || uri.AbsolutePath != "/"
            || uri.UserInfo.Length != 0 || uri.Query.Length != 0 || uri.Fragment.Length != 0
            || uri.AbsoluteUri.Length > 900)
        {
            throw new InvalidOperationException(
                "DevelopmentDemo:PublicBaseUrl must be an absolute HTTP(S) origin reachable " +
                "by the viewer, without credentials, path, query or fragment. " +
                "Do not use a wildcard listening address.");
        }

        return uri;
    }

    private static string DemoImageUrl(Uri? baseUri, string fileName, string placeholderName)
        => baseUri is null ? $"https://example.com/images/{placeholderName}"
            : new Uri(baseUri, $"{DevelopmentAssetRequestPath}/{fileName}").AbsoluteUri;

    private static bool IsManagedDemoUrl(string value, string fileName, string placeholderName)
    {
        if (value == $"https://example.com/images/{placeholderName}") return true;
        return Uri.TryCreate(value, UriKind.Absolute, out Uri? uri)
            && (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps)
            && uri.UserInfo.Length == 0 && uri.Query.Length == 0 && uri.Fragment.Length == 0
            && uri.AbsolutePath == $"{DevelopmentAssetRequestPath}/{fileName}";
    }

    private static async Task RepairDemoImageUrlsAsync(AppDbContext dbContext, Uri baseUri)
    {
        var artworks = await dbContext.Artworks
            .Where(x => x.Exhibition.ExhibitionCode == "colors-of-nature"
                && x.Exhibition.ArtistProfile.ProfileCode == "demo-artist"
                && x.Exhibition.ArtistProfile.User.Email == DemoEmail)
            .ToListAsync();

        foreach (Artwork artwork in artworks)
        {
            (string File, string Placeholder)? image = artwork.Title switch
            {
                "Poranny las" => ("morning-forest.jpg", "morning-forest.jpg"),
                "Ciche jezioro" => ("quiet-lake.png", "quiet-lake.jpg"),
                "Górska droga" => ("mountain-road.jpg", "mountain-road.jpg"),
                _ => null
            };
            if (image is not { } match
                || !IsManagedDemoUrl(artwork.ImageUrl, match.File, match.Placeholder)) continue;

            string desiredUrl = DemoImageUrl(baseUri, match.File, match.Placeholder);
            if (artwork.ImageUrl != desiredUrl) artwork.ImageUrl = desiredUrl;
        }

        if (dbContext.ChangeTracker.HasChanges()) await dbContext.SaveChangesAsync();
    }

    public static async Task SeedAsync(
        AppDbContext dbContext,
        UserManager<User> userManager,
        Uri? developmentPublicBaseUri = null)
    {
        // Validate again for callers other than startup; a missing origin keeps legacy seeding.
        developmentPublicBaseUri = ValidateDevelopmentPublicBaseUrl(developmentPublicBaseUri?.OriginalString);
        const string profileCode = "demo-artist";

        bool seedAlreadyExists = await dbContext.ArtistProfiles
            .AnyAsync(x => x.ProfileCode == profileCode);

        if (seedAlreadyExists)
        {
            if (developmentPublicBaseUri is not null)
                await RepairDemoImageUrlsAsync(dbContext, developmentPublicBaseUri);
            return;
        }

        User? user = await userManager.FindByEmailAsync(DemoEmail);

        if (user is null)
        {
            user = new User
            {
                UserName = DemoEmail,
                Email = DemoEmail,
                EmailConfirmed = true,
                IsActive = true,
                CreatedAt = DateTime.UtcNow
            };

            IdentityResult result = await userManager.CreateAsync(
                user,
                DemoPassword);

            if (!result.Succeeded)
            {
                string errors = string.Join(
                    ", ",
                    result.Errors.Select(x => x.Description));

                throw new InvalidOperationException(
                    $"Nie udało się utworzyć użytkownika testowego: {errors}");
            }
        }

        var artistProfile = new ArtistProfile
        {
            UserId = user.Id,
            DisplayName = "Anna Nowak",
            Bio = "Artystka tworząca współczesne malarstwo inspirowane naturą.",
            ProfileImageUrl = "https://example.com/images/demo-artist.jpg",
            ProfileCode = profileCode,
            IsActive = true,
            CreatedAt = DateTime.UtcNow,
            Exhibitions =
            [
                new Exhibition
                {
                    Title = "Kolory natury",
                    Description = "Testowa wystawa obrazów inspirowanych krajobrazem.",
                    ExhibitionCode = "colors-of-nature",
                    SortOrder = 1,
                    IsActive = true,
                    CreatedAt = DateTime.UtcNow,
                    Artworks =
                    [
                        new Artwork
                        {
                            Title = "Poranny las",
                            Description = "Obraz przedstawiający las o poranku.",
                            CreationYear = 2025,
                            WidthCm = 80.00m,
                            HeightCm = 60.00m,
                            ImageUrl = DemoImageUrl(developmentPublicBaseUri, "morning-forest.jpg", "morning-forest.jpg"),
                            SortOrder = 1,
                            IsActive = true,
                            CreatedAt = DateTime.UtcNow
                        },
                        new Artwork
                        {
                            Title = "Ciche jezioro",
                            Description = "Spokojny pejzaż jeziora o zachodzie słońca.",
                            CreationYear = 2026,
                            WidthCm = 100.00m,
                            HeightCm = 70.00m,
                            ImageUrl = DemoImageUrl(developmentPublicBaseUri, "quiet-lake.png", "quiet-lake.jpg"),
                            SortOrder = 2,
                            IsActive = true,
                            CreatedAt = DateTime.UtcNow
                        },
                        new Artwork
                        {
                            Title = "Górska droga",
                            Description = "Droga prowadząca przez surowy górski krajobraz.",
                            CreationYear = 2026,
                            WidthCm = 50.00m,
                            HeightCm = 70.00m,
                            ImageUrl = DemoImageUrl(developmentPublicBaseUri, "mountain-road.jpg", "mountain-road.jpg"),
                            SortOrder = 3,
                            IsActive = true,
                            CreatedAt = DateTime.UtcNow
                        }
                    ]
                }
            ]
        };

        dbContext.ArtistProfiles.Add(artistProfile);

        await dbContext.SaveChangesAsync();
    }
}
