using ArtTechGallery.Core.Models;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace ArtTechGallery.Infrastructure.Data;

public static class AppDbSeeder
{
    private const string DemoEmail = "demo@arttechgallery.local";
    private const string DemoPassword = "Demo123!";

    public static async Task SeedAsync(
        AppDbContext dbContext,
        UserManager<User> userManager)
    {
        const string profileCode = "demo-artist";

        bool seedAlreadyExists = await dbContext.ArtistProfiles
            .AnyAsync(x => x.ProfileCode == profileCode);

        if (seedAlreadyExists)
        {
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
                            ImageUrl = "https://example.com/images/morning-forest.jpg",
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
                            ImageUrl = "https://example.com/images/quiet-lake.jpg",
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
                            ImageUrl = "https://example.com/images/mountain-road.jpg",
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