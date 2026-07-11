namespace ArtTechGallery.Core.DTOs;

public sealed class ExhibitionDto
{
    public Guid Id { get; init; }

    public string ExhibitionCode { get; init; } = string.Empty;

    public string Title { get; init; } = string.Empty;

    public string Description { get; init; } = string.Empty;

    public string ArtistDisplayName { get; init; } = string.Empty;

    public string ArtistProfileCode { get; init; } = string.Empty;

    public IReadOnlyList<ArtworkDto> Artworks { get; init; } = [];
}