namespace ArtTechGallery.Core.DTOs;

public sealed class OwnExhibitionDto
{
    public Guid Id { get; init; }
    public string ExhibitionCode { get; init; } = string.Empty;
    public string Title { get; init; } = string.Empty;
    public string Description { get; init; } = string.Empty;
    public int SortOrder { get; init; }
    public string Status { get; init; } = string.Empty;
    public DateTime CreatedAt { get; init; }
}
