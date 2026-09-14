namespace ArtTechGallery.Core.DTOs;

public sealed class OwnArtistProfileDto
{
    public Guid Id { get; init; }
    public string ProfileCode { get; init; } = string.Empty;
    public string DisplayName { get; init; } = string.Empty;
    public string Bio { get; init; } = string.Empty;
    public string ProfileImageUrl { get; init; } = string.Empty;
    public bool IsActive { get; init; }
    public DateTime CreatedAt { get; init; }
}
