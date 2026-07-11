namespace ArtTechGallery.Core.DTOs;

public sealed class ArtworkDetailsDto
{
    public Guid Id { get; init; }

    public string Title { get; init; } = string.Empty;

    public string Description { get; init; } = string.Empty;

    public int CreationYear { get; init; }

    public decimal WidthCm { get; init; }

    public decimal HeightCm { get; init; }

    public string ImageUrl { get; init; } = string.Empty;

    public string ExhibitionCode { get; init; } = string.Empty;

    public string ExhibitionTitle { get; init; } = string.Empty;

    public string ArtistProfileCode { get; init; } = string.Empty;

    public string ArtistDisplayName { get; init; } = string.Empty;
}