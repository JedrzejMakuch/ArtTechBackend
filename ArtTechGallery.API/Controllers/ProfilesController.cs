using ArtTechGallery.Core.DTOs;
using ArtTechGallery.Core.Models;
using ArtTechGallery.Infrastructure.Data;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace ArtTechGallery.API.Controllers;

[ApiController]
[Route("api/[controller]")]
public sealed class ProfilesController : ControllerBase
{
    private readonly AppDbContext _dbContext;

    public ProfilesController(AppDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    [HttpGet("{profileCode}")]
    [ProducesResponseType<ArtistProfileDto>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<ArtistProfileDto>> GetByCode(
        string profileCode,
        CancellationToken cancellationToken)
    {
        ArtistProfileDto? profile = await _dbContext.ArtistProfiles
            .AsNoTracking()
            .VisibleToPublic()
            .Where(x => x.ProfileCode == profileCode)
            .Select(x => new ArtistProfileDto
            {
                Id = x.Id,
                ProfileCode = x.ProfileCode,
                DisplayName = x.DisplayName,
                Bio = x.Bio,
                ProfileImageUrl = x.ProfileImageUrl,
                Exhibitions = x.Exhibitions
                    .Where(exhibition => exhibition.Status == ExhibitionStatus.Published)
                    .OrderBy(exhibition => exhibition.SortOrder)
                    .ThenBy(exhibition => exhibition.Id)
                    .Select(exhibition => new ExhibitionSummaryDto
                    {
                        Id = exhibition.Id,
                        ExhibitionCode = exhibition.ExhibitionCode,
                        Title = exhibition.Title,
                        Description = exhibition.Description,
                        SortOrder = exhibition.SortOrder,
                        ArtworksCount = exhibition.Artworks.Count(
                            artwork => artwork.IsActive)
                    })
                    .ToList()
            })
            .SingleOrDefaultAsync(cancellationToken);

        if (profile is null)
        {
            return Problem(
                statusCode: StatusCodes.Status404NotFound,
                title: "Artist profile not found",
                detail: $"No active artist profile with code '{profileCode}' was found.");
        }

        return Ok(profile);
    }
}
