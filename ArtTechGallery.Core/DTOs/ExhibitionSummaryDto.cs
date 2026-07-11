namespace ArtTechGallery.Core.DTOs;

public sealed class ExhibitionSummaryDto
{
    public Guid Id { get; init; }

    public string ExhibitionCode { get; init; } = string.Empty;

    public string Title { get; init; } = string.Empty;

    public string Description { get; init; } = string.Empty;

    public int SortOrder { get; init; }

    public int ArtworksCount { get; init; }
}