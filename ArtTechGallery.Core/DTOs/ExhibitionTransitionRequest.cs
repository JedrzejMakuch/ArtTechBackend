using System.Text.Json.Serialization;

namespace ArtTechGallery.Core.DTOs;

// Transition commands accept an empty JSON object, never writable state or ownership fields.
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed class ExhibitionTransitionRequest { }
