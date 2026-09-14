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
[Route("api/artist/profile")]
[Authorize(Policy = ActiveUserRequirement.PolicyName)]
public sealed class ArtistProfileManagementController(
    AppDbContext dbContext,
    UserManager<User> userManager,
    ProfileCodeGenerator codeGenerator) : ControllerBase
{
    private const int MaximumCodeAttempts = 5;

    // ActiveUser has already validated that the principal contains an existing active user ID.
    private Guid CurrentUserId => Guid.Parse(userManager.GetUserId(User)!);

    [HttpGet]
    public async Task<ActionResult<OwnArtistProfileDto>> Get(CancellationToken cancellationToken)
    {
        var profile = await dbContext.ArtistProfiles.AsNoTracking()
            .SingleOrDefaultAsync(x => x.UserId == CurrentUserId, cancellationToken);
        return profile is null ? MissingProfile() : Ok(ToDto(profile));
    }

    [HttpPost]
    public async Task<ActionResult<OwnArtistProfileDto>> Create(
        SaveArtistProfileRequest request, CancellationToken cancellationToken)
    {
        Guid userId = CurrentUserId;
        if (await dbContext.ArtistProfiles.AnyAsync(x => x.UserId == userId, cancellationToken))
            return ExistingProfile();

        var profile = new ArtistProfile
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            DisplayName = request.DisplayName,
            Bio = request.Bio ?? string.Empty,
            ProfileImageUrl = string.Empty,
            IsActive = true,
            CreatedAt = DateTime.UtcNow
        };

        for (int attempt = 0; attempt < MaximumCodeAttempts; attempt++)
        {
            profile.ProfileCode = codeGenerator.Generate();
            dbContext.ArtistProfiles.Add(profile);
            try
            {
                await dbContext.SaveChangesAsync(cancellationToken);
                // Return the persisted timestamp precision, consistent with subsequent GET/PUT responses.
                await dbContext.Entry(profile).ReloadAsync(cancellationToken);
                return CreatedAtAction(nameof(Get), ToDto(profile));
            }
            catch (DbUpdateException exception) when (exception.InnerException is PostgresException
                { SqlState: PostgresErrorCodes.UniqueViolation, ConstraintName: "IX_ArtistProfiles_UserId" })
            {
                dbContext.Entry(profile).State = EntityState.Detached;
                return ExistingProfile();
            }
            catch (DbUpdateException exception) when (exception.InnerException is PostgresException
                { SqlState: PostgresErrorCodes.UniqueViolation, ConstraintName: "IX_ArtistProfiles_ProfileCode" })
            {
                dbContext.Entry(profile).State = EntityState.Detached;
                // Another request for this user may have won while its code collided too.
                if (await dbContext.ArtistProfiles.AnyAsync(x => x.UserId == userId, cancellationToken))
                    return ExistingProfile();
            }
        }

        return Problem(statusCode: StatusCodes.Status409Conflict, title: "Profile code unavailable",
            detail: "Could not allocate a unique profile code. Please retry the request.");
    }

    [HttpPut]
    public async Task<ActionResult<OwnArtistProfileDto>> Update(
        SaveArtistProfileRequest request, CancellationToken cancellationToken)
    {
        var profile = await dbContext.ArtistProfiles
            .SingleOrDefaultAsync(x => x.UserId == CurrentUserId, cancellationToken);
        if (profile is null) return MissingProfile();
        if (!profile.IsActive)
            return Problem(statusCode: StatusCodes.Status403Forbidden, title: "Artist profile inactive",
                detail: "An inactive profile cannot be edited through this endpoint.");

        profile.DisplayName = request.DisplayName;
        profile.Bio = request.Bio ?? string.Empty;
        await dbContext.SaveChangesAsync(cancellationToken);
        return Ok(ToDto(profile));
    }

    private ObjectResult MissingProfile() => Problem(statusCode: StatusCodes.Status404NotFound,
        title: "Artist profile not found", detail: "Create your artist profile first.");

    private ObjectResult ExistingProfile() => Problem(statusCode: StatusCodes.Status409Conflict,
        title: "Artist profile already exists", detail: "Each user can create only one artist profile.");

    private static OwnArtistProfileDto ToDto(ArtistProfile profile) => new()
    {
        Id = profile.Id,
        ProfileCode = profile.ProfileCode,
        DisplayName = profile.DisplayName,
        Bio = profile.Bio,
        ProfileImageUrl = profile.ProfileImageUrl,
        IsActive = profile.IsActive,
        CreatedAt = profile.CreatedAt
    };
}
