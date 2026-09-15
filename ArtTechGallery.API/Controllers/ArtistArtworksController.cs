using ArtTechGallery.API.Authorization;
using ArtTechGallery.Core.DTOs;
using ArtTechGallery.Core.Models;
using ArtTechGallery.Infrastructure.Data;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace ArtTechGallery.API.Controllers;

[ApiController]
[Route("api/artist/exhibitions/{exhibitionId:guid}/artworks")]
[Authorize(Policy = ActiveUserRequirement.PolicyName)]
public sealed class ArtistArtworksController(AppDbContext dbContext, UserManager<User> userManager) : ControllerBase
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
    public async Task<ActionResult<OwnArtworkDto>> Create(Guid exhibitionId, SaveArtworkRequest request, CancellationToken cancellationToken)
    {
        var exhibition = await OwnedExhibitions.AsNoTracking().Include(x => x.ArtistProfile)
            .SingleOrDefaultAsync(x => x.Id == exhibitionId, cancellationToken);
        if (exhibition is null) return Missing();
        if (!exhibition.ArtistProfile.IsActive) return InactiveProfile();
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
    public async Task<ActionResult<OwnArtworkDto>> Update(Guid exhibitionId, Guid id, SaveArtworkRequest request, CancellationToken cancellationToken)
    {
        var artwork = await OwnedArtworks(exhibitionId).Include(x => x.Exhibition.ArtistProfile)
            .SingleOrDefaultAsync(x => x.Id == id, cancellationToken);
        if (artwork is null) return Missing();
        if (!artwork.Exhibition.ArtistProfile.IsActive) return InactiveProfile();
        SetMetadata(artwork, request);
        try { await dbContext.SaveChangesAsync(cancellationToken); }
        catch (DbUpdateConcurrencyException) { return Missing(); }
        return Ok(ToDto(artwork));
    }

    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> Delete(Guid exhibitionId, Guid id, CancellationToken cancellationToken)
    {
        var artwork = await OwnedArtworks(exhibitionId).Include(x => x.Exhibition.ArtistProfile)
            .SingleOrDefaultAsync(x => x.Id == id, cancellationToken);
        if (artwork is null) return Missing();
        if (!artwork.Exhibition.ArtistProfile.IsActive) return InactiveProfile();
        dbContext.Artworks.Remove(artwork);
        try { await dbContext.SaveChangesAsync(cancellationToken); }
        catch (DbUpdateConcurrencyException) { return Missing(); }
        return NoContent();
    }

    private ObjectResult Missing() => Problem(statusCode: StatusCodes.Status404NotFound,
        title: "Exhibition or artwork not found");
    private ObjectResult InactiveProfile() => Problem(statusCode: StatusCodes.Status403Forbidden,
        title: "Artist profile inactive", detail: "An inactive profile cannot modify artworks.");
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
