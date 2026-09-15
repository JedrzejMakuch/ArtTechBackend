using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;

namespace ArtTechGallery.Core.DTOs;

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed class SaveArtworkRequest : IValidatableObject
{
    private string title = string.Empty;
    private string imageUrl = string.Empty;

    [Required, StringLength(200, MinimumLength = 1)]
    public string Title { get => title; set => title = value?.Trim() ?? string.Empty; }
    [StringLength(2000)]
    public string? Description { get; set; }
    [Range(1, 9999)]
    public int CreationYear { get; set; }
    [Range(typeof(decimal), "1", "1000")]
    public decimal WidthCm { get; set; }
    [Range(typeof(decimal), "1", "1000")]
    public decimal HeightCm { get; set; }
    [Required, StringLength(1000)]
    public string ImageUrl { get => imageUrl; set => imageUrl = value?.Trim() ?? string.Empty; }
    [Range(0, int.MaxValue)]
    public int SortOrder { get; set; }

    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        if (CreationYear > DateTime.UtcNow.Year)
            yield return new("Creation year cannot be in the future.", [nameof(CreationYear)]);
        if (decimal.Round(WidthCm, 2) != WidthCm)
            yield return new("Width must have at most two decimal places in centimeters.", [nameof(WidthCm)]);
        if (decimal.Round(HeightCm, 2) != HeightCm)
            yield return new("Height must have at most two decimal places in centimeters.", [nameof(HeightCm)]);
        // This is an external image reference, not a storage path or an instruction to fetch a URL.
        if (!Uri.TryCreate(ImageUrl, UriKind.Absolute, out var uri)
            || uri.Scheme is not ("http" or "https") || string.IsNullOrEmpty(uri.Host)
            || !string.IsNullOrEmpty(uri.UserInfo) || !string.IsNullOrEmpty(uri.Fragment)
            || ImageUrl.Any(char.IsWhiteSpace) || ImageUrl.Contains('\\'))
            yield return new("Image URL must be an absolute HTTP(S) URL without credentials, fragments, whitespace or backslashes.", [nameof(ImageUrl)]);
    }
}
