using ArtTechGallery.Core.Models;

namespace ArtTechGallery.Infrastructure.Data;

public static class PublicVisibilityQueryExtensions
{
    public static IQueryable<ArtistProfile> VisibleToPublic(this IQueryable<ArtistProfile> profiles) =>
        profiles.Where(profile => profile.IsActive && profile.User.IsActive);

    public static IQueryable<Exhibition> VisibleToPublic(this IQueryable<Exhibition> exhibitions) =>
        exhibitions.Where(exhibition => exhibition.Status == ExhibitionStatus.Published
            && exhibition.ArtistProfile.IsActive && exhibition.ArtistProfile.User.IsActive);

    public static IQueryable<Artwork> VisibleToPublic(this IQueryable<Artwork> artworks) =>
        artworks.Where(artwork => artwork.IsActive && artwork.Exhibition.Status == ExhibitionStatus.Published
            && artwork.Exhibition.ArtistProfile.IsActive && artwork.Exhibition.ArtistProfile.User.IsActive);
}
