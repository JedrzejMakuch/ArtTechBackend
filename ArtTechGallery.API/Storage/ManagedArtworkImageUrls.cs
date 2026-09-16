using ArtTechGallery.Core.Storage;

namespace ArtTechGallery.API.Storage;

public sealed class ManagedArtworkImageUrls(Uri publicBaseUri)
{
    public Uri PublicBaseUri { get; } = publicBaseUri;

    public string Create(ArtworkImageReference reference) =>
        new Uri(PublicBaseUri, $"api/artworks/{reference.ArtworkId:D}/images/{reference.Version}.{reference.Extension}").AbsoluteUri;

    public bool TryParse(string value, Guid artworkId, out ArtworkImageReference? reference)
    {
        reference = null;
        if (!Uri.TryCreate(value, UriKind.Absolute, out var uri)
            || !string.Equals(uri.Scheme, PublicBaseUri.Scheme, StringComparison.OrdinalIgnoreCase)
            || !string.Equals(uri.Authority, PublicBaseUri.Authority, StringComparison.OrdinalIgnoreCase)
            || !string.IsNullOrEmpty(uri.Query) || !string.IsNullOrEmpty(uri.Fragment)) return false;
        var prefix = PublicBaseUri.AbsolutePath.TrimEnd('/') + $"/api/artworks/{artworkId:D}/images/";
        if (!uri.AbsolutePath.StartsWith(prefix, StringComparison.Ordinal)) return false;
        var file = uri.AbsolutePath[prefix.Length..];
        var dot = file.LastIndexOf('.');
        return dot > 0 && ArtworkImageReference.TryCreate(artworkId, file[..dot], file[(dot + 1)..], out reference);
    }

    public bool IsReservedManagedRoute(string value) => Uri.TryCreate(value, UriKind.Absolute, out var uri)
        && string.Equals(uri.Scheme, PublicBaseUri.Scheme, StringComparison.OrdinalIgnoreCase)
        && string.Equals(uri.Authority, PublicBaseUri.Authority, StringComparison.OrdinalIgnoreCase)
        && uri.AbsolutePath.StartsWith(PublicBaseUri.AbsolutePath.TrimEnd('/') + "/api/artworks/", StringComparison.Ordinal)
        && uri.AbsolutePath.Contains("/images/", StringComparison.Ordinal);
}
