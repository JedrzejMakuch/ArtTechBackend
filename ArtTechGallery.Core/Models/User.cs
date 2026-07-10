using Microsoft.AspNetCore.Identity;

namespace ArtTechGallery.Core.Models;

public class User : IdentityUser<Guid>
{
    public ArtistProfile? ArtistProfile { get; set; }
    public bool IsActive { get; set; } = true;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
