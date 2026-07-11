namespace ArtTechGallery.Core.DTOs;

public sealed class ArtworkDto
{
    public Guid Id { get; init; }

    public string Title { get; init; } = string.Empty;

    public string Description { get; init; } = string.Empty;

    public int CreationYear { get; init; }

    public decimal WidthCm { get; init; }

    public decimal HeightCm { get; init; }

    public string ImageUrl { get; init; } = string.Empty;

    public int SortOrder { get; init; }
}