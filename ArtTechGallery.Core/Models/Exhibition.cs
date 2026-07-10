namespace ArtTechGallery.Core.Models;

public class Exhibition
{
    public Guid Id { get; set; }
    public Guid ArtistProfileId { get; set; }
    public ArtistProfile ArtistProfile { get; set; } = null!;
    public List<Artwork> Artworks { get; set; } = new();

    public string Title { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;

    public string ShareCode { get; set; } = string.Empty;
    public int SortOrder { get; set; }
    public bool IsActive { get; set; } = true;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
