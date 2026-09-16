using ArtTechGallery.API.Authorization;
using ArtTechGallery.Core.DTOs;
using ArtTechGallery.Core.Models;
using ArtTechGallery.Infrastructure.Data;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using ArtTechGallery.API.Storage;
using ArtTechGallery.Core.Storage;

namespace ArtTechGallery.API.Controllers;

[ApiController]
[Route("api/artist/exhibitions/{exhibitionId:guid}/artworks")]
[Authorize(Policy = ActiveUserRequirement.PolicyName)]
public sealed class ArtistArtworksController(AppDbContext dbContext, UserManager<User> userManager,
    IArtworkStorage storage, ManagedArtworkImageUrls imageUrls,
    ILogger<ArtistArtworksController> logger) : ControllerBase
{
    private Guid CurrentUserId => Guid.Parse(userManager.GetUserId(User)!);
    private IQueryable<Exhibition> OwnedExhibitions => dbContext.Exhibitions
        .Where(x => x.ArtistProfile.UserId == CurrentUserId);
    private IQueryable<Artwork> OwnedArtworks(Guid exhibitionId) => dbContext.Artworks
        .Where(x => x.ExhibitionId == exhibitionId && x.Exhibition.ArtistProfile.UserId == CurrentUserId);

    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<OwnArtworkDto>>> List(Guid exhibitionId, CancellationToken cancellationToken)
    {
        if (!await OwnedExhibitions.AnyAsync(x => x.Id == exhibitionId, cancellationToken)) return Missing();
        var artworks = await OwnedArtworks(exhibitionId).AsNoTracking()
            .OrderBy(x => x.SortOrder).ThenBy(x => x.Id).ToListAsync(cancellationToken);
        return Ok(artworks.Select(ToDto).ToArray());
    }

    [HttpGet("{id:guid}")]
    public async Task<ActionResult<OwnArtworkDto>> Get(Guid exhibitionId, Guid id, CancellationToken cancellationToken)
    {
        var artwork = await OwnedArtworks(exhibitionId).AsNoTracking().SingleOrDefaultAsync(x => x.Id == id, cancellationToken);
        return artwork is null ? Missing() : Ok(ToDto(artwork));
    }

    [HttpPost]
    [Consumes("application/json")]
    public async Task<ActionResult<OwnArtworkDto>> Create(Guid exhibitionId, SaveArtworkRequest request, CancellationToken cancellationToken)
    {
        var exhibition = await OwnedExhibitions.AsNoTracking().Include(x => x.ArtistProfile)
            .SingleOrDefaultAsync(x => x.Id == exhibitionId, cancellationToken);
        if (exhibition is null) return Missing();
        if (!exhibition.ArtistProfile.IsActive) return InactiveProfile();
        if (imageUrls.IsReservedManagedRoute(request.ImageUrl))
            return Validation("imageUrl", "Managed artwork image URLs are server controlled.");
        var artwork = new Artwork { Id = Guid.NewGuid(), ExhibitionId = exhibition.Id };
        SetMetadata(artwork, request);
        dbContext.Artworks.Add(artwork);
        try
        {
            await dbContext.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException exception) when (exception.InnerException is PostgresException
            { SqlState: PostgresErrorCodes.ForeignKeyViolation, ConstraintName: "FK_Artworks_Exhibitions_ExhibitionId" })
        {
            return Missing(); // Parent was removed after the ownership check.
        }
        await dbContext.Entry(artwork).ReloadAsync(cancellationToken);
        return CreatedAtAction(nameof(Get), new { exhibitionId, id = artwork.Id }, ToDto(artwork));
    }

    [HttpPut("{id:guid}")]
    [Consumes("application/json")]
    public async Task<ActionResult<OwnArtworkDto>> Update(Guid exhibitionId, Guid id, SaveArtworkRequest request, CancellationToken cancellationToken)
    {
        await using var transaction = await dbContext.Database.BeginTransactionAsync(cancellationToken);
        await AcquireManagedImageLock(id, cancellationToken);
        var artwork = await OwnedArtworks(exhibitionId).Include(x => x.Exhibition.ArtistProfile)
            .SingleOrDefaultAsync(x => x.Id == id, cancellationToken);
        if (artwork is null) return Missing();
        if (!artwork.Exhibition.ArtistProfile.IsActive) return InactiveProfile();
        if (imageUrls.IsReservedManagedRoute(request.ImageUrl)
            && !string.Equals(request.ImageUrl, artwork.ImageUrl, StringComparison.Ordinal))
            return Validation("imageUrl", "Managed artwork image URLs are server controlled.");
        var previousImageUrl = artwork.ImageUrl;
        imageUrls.TryParse(previousImageUrl, artwork.Id, out var previous);
        SetMetadata(artwork, request);
        try { await dbContext.SaveChangesAsync(cancellationToken); }
        catch (DbUpdateConcurrencyException) { return Missing(); }
        await transaction.CommitAsync(cancellationToken);
        if (previous is not null && !string.Equals(previousImageUrl, artwork.ImageUrl, StringComparison.Ordinal))
            await TryDelete(previous, CancellationToken.None);
        return Ok(ToDto(artwork));
    }

    [HttpPost]
    [Consumes("multipart/form-data")]
    [RequestSizeLimit(ArtworkImageValidator.MaximumBytes + 64 * 1024)]
    public async Task<ActionResult<OwnArtworkDto>> CreateUpload(Guid exhibitionId, CancellationToken cancellationToken)
    {
        var exhibition = await OwnedExhibitions.AsNoTracking().Include(x => x.ArtistProfile)
            .SingleOrDefaultAsync(x => x.Id == exhibitionId, cancellationToken);
        if (exhibition is null) return Missing();
        if (!exhibition.ArtistProfile.IsActive) return InactiveProfile();
        var input = await MultipartArtworkRequestReader.ReadAsync(Request, true, ModelState, cancellationToken);
        if (input is null) return ValidationProblem(ModelState);

        var artwork = new Artwork { Id = Guid.NewGuid(), ExhibitionId = exhibition.Id };
        SetMetadata(artwork, input.Metadata);
        var reference = NewReference(artwork.Id, input.Image!);
        artwork.ImageUrl = imageUrls.Create(reference);
        await storage.WriteAsync(reference, new MemoryStream(input.Image!.Content, false), cancellationToken);
        dbContext.Artworks.Add(artwork);
        try
        {
            await dbContext.SaveChangesAsync(cancellationToken);
            await dbContext.Entry(artwork).ReloadAsync(cancellationToken);
            return CreatedAtAction(nameof(Get), new { exhibitionId, id = artwork.Id }, ToDto(artwork));
        }
        catch (DbUpdateException exception) when (exception.InnerException is PostgresException
            { SqlState: PostgresErrorCodes.ForeignKeyViolation, ConstraintName: "FK_Artworks_Exhibitions_ExhibitionId" })
        {
            await TryDelete(reference, cancellationToken); return Missing();
        }
        catch { await TryDelete(reference, CancellationToken.None); throw; }
    }

    [HttpPut("{id:guid}")]
    [Consumes("multipart/form-data")]
    [RequestSizeLimit(ArtworkImageValidator.MaximumBytes + 64 * 1024)]
    public async Task<ActionResult<OwnArtworkDto>> UpdateUpload(Guid exhibitionId, Guid id, CancellationToken cancellationToken)
    {
        await using var transaction = await dbContext.Database.BeginTransactionAsync(cancellationToken);
        await AcquireManagedImageLock(id, cancellationToken);
        var artwork = await OwnedArtworks(exhibitionId).Include(x => x.Exhibition.ArtistProfile)
            .SingleOrDefaultAsync(x => x.Id == id, cancellationToken);
        if (artwork is null) return Missing();
        if (!artwork.Exhibition.ArtistProfile.IsActive) return InactiveProfile();
        var input = await MultipartArtworkRequestReader.ReadAsync(Request, false, ModelState, cancellationToken);
        if (input is null) return ValidationProblem(ModelState);

        ArtworkImageReference? added = null;
        var currentImageUrl = artwork.ImageUrl;
        imageUrls.TryParse(artwork.ImageUrl, artwork.Id, out var previous);
        SetMetadata(artwork, input.Metadata);
        if (input.Image is not null)
        {
            added = NewReference(artwork.Id, input.Image);
            await storage.WriteAsync(added, new MemoryStream(input.Image.Content, false), cancellationToken);
            artwork.ImageUrl = imageUrls.Create(added);
        }
        else artwork.ImageUrl = currentImageUrl;
        try
        {
            await dbContext.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
        }
        catch
        {
            if (added is not null) await TryDelete(added, CancellationToken.None);
            throw;
        }
        if (added is not null && previous is not null) await TryDelete(previous, CancellationToken.None);
        return Ok(ToDto(artwork));
    }

    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> Delete(Guid exhibitionId, Guid id, CancellationToken cancellationToken)
    {
        await using var transaction = await dbContext.Database.BeginTransactionAsync(cancellationToken);
        await AcquireManagedImageLock(id, cancellationToken);
        var artwork = await OwnedArtworks(exhibitionId).Include(x => x.Exhibition.ArtistProfile)
            .SingleOrDefaultAsync(x => x.Id == id, cancellationToken);
        if (artwork is null) return Missing();
        if (!artwork.Exhibition.ArtistProfile.IsActive) return InactiveProfile();
        imageUrls.TryParse(artwork.ImageUrl, artwork.Id, out var managed);
        dbContext.Artworks.Remove(artwork);
        try { await dbContext.SaveChangesAsync(cancellationToken); await transaction.CommitAsync(cancellationToken); }
        catch (DbUpdateConcurrencyException) { return Missing(); }
        if (managed is not null) await TryDelete(managed, CancellationToken.None);
        return NoContent();
    }

    private ObjectResult Missing() => Problem(statusCode: StatusCodes.Status404NotFound,
        title: "Exhibition or artwork not found");
    private ObjectResult InactiveProfile() => Problem(statusCode: StatusCodes.Status403Forbidden,
        title: "Artist profile inactive", detail: "An inactive profile cannot modify artworks.");
    private ActionResult<OwnArtworkDto> Validation(string field, string message)
    {
        ModelState.AddModelError(field, message); return ValidationProblem(ModelState);
    }
    private static ArtworkImageReference NewReference(Guid artworkId, ValidatedArtworkImage image) =>
        new(artworkId, Guid.NewGuid().ToString("N"), image.Extension);
    private async Task TryDelete(ArtworkImageReference reference, CancellationToken cancellationToken)
    {
        try { await storage.DeleteIfExistsAsync(reference, cancellationToken); }
        catch (Exception exception) { logger.LogError(exception, "Could not remove managed artwork image {ImageKey}", reference.Key); }
    }
    private async Task AcquireManagedImageLock(Guid artworkId, CancellationToken cancellationToken)
    {
        var bytes = artworkId.ToByteArray();
        var key = BitConverter.ToInt64(bytes, 0) ^ BitConverter.ToInt64(bytes, 8);
        await dbContext.Database.ExecuteSqlInterpolatedAsync($"SELECT pg_advisory_xact_lock({key})", cancellationToken);
    }
    private static void SetMetadata(Artwork artwork, SaveArtworkRequest request)
    {
        artwork.Title = request.Title;
        artwork.Description = request.Description ?? string.Empty;
        artwork.CreationYear = request.CreationYear;
        artwork.WidthCm = request.WidthCm;
        artwork.HeightCm = request.HeightCm;
        artwork.ImageUrl = request.ImageUrl;
        artwork.SortOrder = request.SortOrder;
    }
    private static OwnArtworkDto ToDto(Artwork artwork) => new()
    {
        Id = artwork.Id, ExhibitionId = artwork.ExhibitionId, Title = artwork.Title,
        Description = artwork.Description, CreationYear = artwork.CreationYear,
        WidthCm = artwork.WidthCm, HeightCm = artwork.HeightCm, ImageUrl = artwork.ImageUrl,
        SortOrder = artwork.SortOrder, IsActive = artwork.IsActive, CreatedAt = artwork.CreatedAt
    };
}
