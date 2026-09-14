using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;

namespace ArtTechGallery.Core.DTOs;

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed class SaveArtistProfileRequest
{
    private string _displayName = string.Empty;

    [Required]
    [StringLength(200, MinimumLength = 1)]
    public string DisplayName
    {
        get => _displayName;
        set => _displayName = value?.Trim() ?? string.Empty;
    }

    [StringLength(2000)]
    public string? Bio { get; set; }
}
