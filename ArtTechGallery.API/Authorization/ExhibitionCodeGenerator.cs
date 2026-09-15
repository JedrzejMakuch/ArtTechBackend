using System.Security.Cryptography;

namespace ArtTechGallery.API.Authorization;

public class ExhibitionCodeGenerator
{
    public virtual string Generate() => Convert.ToHexString(RandomNumberGenerator.GetBytes(8)).ToLowerInvariant();
}
