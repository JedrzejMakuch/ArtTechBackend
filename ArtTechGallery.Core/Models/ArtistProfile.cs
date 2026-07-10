namespace ArtTechGallery.Core.Models;

public class ArtistProfile
{
    public Guid Id { get; set; }

    public Guid UserId { get; set; }

    public User User { get; set; } = null!;

    public List<Exhibition> Exhibitions { get; set; } = new();

    public string DisplayName { get; set; } = string.Empty;

    public string Bio { get; set; } = string.Empty;

    public string ProfileImageUrl { get; set; } = string.Empty;

    public string ShareCode { get; set; } = string.Empty;

    public bool IsActive { get; set; } = true;

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}