using System.Security.Cryptography;

namespace ArtTechGallery.API.Authorization;

// A concrete generator also allows deterministic collision tests without changing production randomness.
public class ProfileCodeGenerator
{
    public virtual string Generate() => Convert.ToHexString(RandomNumberGenerator.GetBytes(8)).ToLowerInvariant();
}
