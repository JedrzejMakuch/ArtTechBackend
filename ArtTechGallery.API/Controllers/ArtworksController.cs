using ArtTechGallery.Core.DTOs;
using ArtTechGallery.Infrastructure.Data;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace ArtTechGallery.API.Controllers;

[ApiController]
[Route("api/[controller]")]
public sealed class ArtworksController : ControllerBase
{
    private readonly AppDbContext _dbContext;

    public ArtworksController(AppDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    [HttpGet("{id:guid}")]
    [ProducesResponseType<ArtworkDetailsDto>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<ArtworkDetailsDto>> GetById(
        Guid id,
        CancellationToken cancellationToken)
    {
        ArtworkDetailsDto? artwork = await _dbContext.Artworks
            .AsNoTracking()
            .Where(x =>
                x.Id == id &&
                x.IsActive &&
                x.Exhibition.IsActive &&
                x.Exhibition.ArtistProfile.IsActive &&
                x.Exhibition.ArtistProfile.User.IsActive)
            .Select(x => new ArtworkDetailsDto
            {
                Id = x.Id,
                Title = x.Title,
                Description = x.Description,
                CreationYear = x.CreationYear,
                WidthCm = x.WidthCm,
                HeightCm = x.HeightCm,
                ImageUrl = x.ImageUrl,
                ExhibitionCode = x.Exhibition.ExhibitionCode,
                ExhibitionTitle = x.Exhibition.Title,
                ArtistProfileCode =
                    x.Exhibition.ArtistProfile.ProfileCode,
                ArtistDisplayName =
                    x.Exhibition.ArtistProfile.DisplayName
            })
            .SingleOrDefaultAsync(cancellationToken);

        if (artwork is null)
        {
            return NotFound();
        }

        return Ok(artwork);
    }
}