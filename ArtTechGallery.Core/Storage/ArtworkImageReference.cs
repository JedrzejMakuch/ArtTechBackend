namespace ArtTechGallery.Core.Storage;

public sealed record ArtworkImageReference(Guid ArtworkId, string Version, string Extension)
{
    public string Key => $"artworks/{ArtworkId:N}/{Version}.{Extension}";

    public static bool TryCreate(Guid artworkId, string version, string extension, out ArtworkImageReference? reference)
    {
        reference = null;
        if (artworkId == Guid.Empty || version.Length != 32 || !Guid.TryParseExact(version, "N", out _)
            || extension is not ("jpg" or "png")) return false;
        reference = new(artworkId, version.ToLowerInvariant(), extension);
        return true;
    }
}
