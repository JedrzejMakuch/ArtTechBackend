namespace ArtTechGallery.Core.DTOs;

public sealed class ArtistProfileDto
{
    public Guid Id { get; init; }

    public string ProfileCode { get; init; } = string.Empty;

    public string DisplayName { get; init; } = string.Empty;

    public string Bio { get; init; } = string.Empty;

    public string ProfileImageUrl { get; init; } = string.Empty;

    public IReadOnlyList<ExhibitionSummaryDto> Exhibitions { get; init; } = [];
}