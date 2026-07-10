namespace ArtTechGallery.Core.Models;

public class Artwork
{
    public Guid Id { get; set; }
    public Exhibition Exhibition { get; set; } = null!;
    public Guid ExhibitionId { get; set; }

    public string Title { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public int CreationYear { get; set; }
    public decimal WidthCm { get; set; }
    public decimal HeightCm { get; set; }

    public string ImageUrl { get; set; } = string.Empty;
    public int SortOrder { get; set; }
    public bool IsActive { get; set; } = true;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
