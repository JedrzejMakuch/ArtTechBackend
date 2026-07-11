using ArtTechGallery.Core.DTOs;
using ArtTechGallery.Infrastructure.Data;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace ArtTechGallery.API.Controllers;

[ApiController]
[Route("api/[controller]")]
public sealed class ExhibitionsController : ControllerBase
{
    private readonly AppDbContext _dbContext;

    public ExhibitionsController(AppDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    [HttpGet("{exhibitionCode}")]
    [ProducesResponseType<ExhibitionDto>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<ExhibitionDto>> GetByCode(
        string exhibitionCode,
        CancellationToken cancellationToken)
    {
        ExhibitionDto? exhibition = await _dbContext.Exhibitions
            .AsNoTracking()
            .Where(x =>
                x.ExhibitionCode == exhibitionCode &&
                x.IsActive &&
                x.ArtistProfile.IsActive)
            .Select(x => new ExhibitionDto
            {
                Id = x.Id,
                ExhibitionCode = x.ExhibitionCode,
                Title = x.Title,
                Description = x.Description,
                ArtistDisplayName = x.ArtistProfile.DisplayName,
                ArtistProfileCode = x.ArtistProfile.ProfileCode,
                Artworks = x.Artworks
                    .Where(artwork => artwork.IsActive)
                    .OrderBy(artwork => artwork.SortOrder)
                    .Select(artwork => new ArtworkDto
                    {
                        Id = artwork.Id,
                        Title = artwork.Title,
                        Description = artwork.Description,
                        CreationYear = artwork.CreationYear,
                        WidthCm = artwork.WidthCm,
                        HeightCm = artwork.HeightCm,
                        ImageUrl = artwork.ImageUrl,
                        SortOrder = artwork.SortOrder
                    })
                    .ToList()
            })
            .SingleOrDefaultAsync(cancellationToken);

        if (exhibition is null)
        {
            return NotFound();
        }

        return Ok(exhibition);
    }
}