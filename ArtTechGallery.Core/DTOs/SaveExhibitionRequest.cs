using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;

namespace ArtTechGallery.Core.DTOs;

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed class SaveExhibitionRequest
{
    private string title = string.Empty;

    [Required]
    [StringLength(200, MinimumLength = 1)]
    public string Title
    {
        get => title;
        set => title = value?.Trim() ?? string.Empty;
    }

    [StringLength(2000)]
    public string? Description { get; set; }

    [Range(0, int.MaxValue)]
    public int SortOrder { get; set; }
}
