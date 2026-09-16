namespace ArtTechGallery.API.Storage;

public sealed class ArtworkStorageOptions
{
    public const string Section = "ArtworkStorage";
    public string RootPath { get; set; } = "../RuntimeData/ArtworkImages";
    public string PublicBaseUrl { get; set; } = string.Empty;
}
