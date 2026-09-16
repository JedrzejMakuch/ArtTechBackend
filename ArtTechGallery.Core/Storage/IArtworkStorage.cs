namespace ArtTechGallery.Core.Storage;

public interface IArtworkStorage
{
    Task WriteAsync(ArtworkImageReference reference, Stream content, CancellationToken cancellationToken);
    Task<Stream?> OpenReadAsync(ArtworkImageReference reference, CancellationToken cancellationToken);
    Task DeleteIfExistsAsync(ArtworkImageReference reference, CancellationToken cancellationToken);
}
