using ArtTechGallery.API.Storage;
using ArtTechGallery.Core.Models;
using ArtTechGallery.Core.Storage;
using ArtTechGallery.Infrastructure.Data;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace ArtTechGallery.API.Controllers;

[ApiController]
[Route("api/artworks/{artworkId:guid}/images")]
public sealed class ArtworkImagesController(AppDbContext dbContext, IArtworkStorage storage,
    ManagedArtworkImageUrls imageUrls) : ControllerBase
{
    [HttpGet("{version}.{extension}")]
    public async Task<IActionResult> Get(Guid artworkId, string version, string extension, CancellationToken cancellationToken)
    {
        if (!ArtworkImageReference.TryCreate(artworkId, version, extension.ToLowerInvariant(), out var reference)) return NotFound();
        var expected = imageUrls.Create(reference!);
        var visible = await dbContext.Artworks.AsNoTracking().VisibleToPublic()
            .AnyAsync(x => x.Id == artworkId && x.ImageUrl == expected, cancellationToken);
        if (!visible) return NotFound();
        var content = await storage.OpenReadAsync(reference!, cancellationToken);
        if (content is null) return NotFound();
        Response.Headers.XContentTypeOptions = "nosniff";
        return File(content, reference!.Extension == "jpg" ? "image/jpeg" : "image/png");
    }
}
