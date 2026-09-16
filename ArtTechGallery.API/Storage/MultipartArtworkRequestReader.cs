using System.ComponentModel.DataAnnotations;
using System.Globalization;
using ArtTechGallery.Core.DTOs;
using Microsoft.AspNetCore.Mvc.ModelBinding;

namespace ArtTechGallery.API.Storage;

public sealed record MultipartArtworkRequest(SaveArtworkRequest Metadata, ValidatedArtworkImage? Image);

public static class MultipartArtworkRequestReader
{
    private static readonly HashSet<string> Fields = new(StringComparer.OrdinalIgnoreCase)
        { "title", "description", "creationYear", "widthCm", "heightCm", "sortOrder" };

    public static async Task<MultipartArtworkRequest?> ReadAsync(HttpRequest request, bool imageRequired,
        ModelStateDictionary errors, CancellationToken cancellationToken)
    {
        IFormCollection form;
        try { form = await request.ReadFormAsync(cancellationToken); }
        catch (InvalidDataException) { errors.AddModelError("image", "The multipart request is malformed or too large."); return null; }

        foreach (var key in form.Keys)
        {
            if (!Fields.Contains(key)) errors.AddModelError(key, "Unexpected multipart field.");
            if (form[key].Count != 1) errors.AddModelError(key, "Multipart fields must be supplied exactly once.");
        }
        foreach (var required in new[] { "title", "creationYear", "widthCm", "heightCm", "sortOrder" })
            if (!form.ContainsKey(required)) errors.AddModelError(required, "The field is required.");

        var namedImages = form.Files.Where(x => string.Equals(x.Name, "image", StringComparison.OrdinalIgnoreCase)).ToArray();
        if (form.Files.Count != namedImages.Length) errors.AddModelError("image", "Unexpected file field.");
        if (namedImages.Length > 1) errors.AddModelError("image", "Only one image may be supplied.");
        if (imageRequired && namedImages.Length == 0) errors.AddModelError("image", "An image is required.");

        var metadata = new SaveArtworkRequest
        {
            Title = One(form, "title"), Description = form.ContainsKey("description") ? One(form, "description") : null,
            CreationYear = Integer(form, "creationYear", errors), WidthCm = Decimal(form, "widthCm", errors),
            HeightCm = Decimal(form, "heightCm", errors), SortOrder = Integer(form, "sortOrder", errors),
            ImageUrl = "https://upload.invalid/pending"
        };
        var validationResults = new List<ValidationResult>();
        Validator.TryValidateObject(metadata, new ValidationContext(metadata), validationResults, true);
        foreach (var result in validationResults)
            foreach (var member in result.MemberNames.DefaultIfEmpty(string.Empty)) errors.AddModelError(member, result.ErrorMessage!);

        ValidatedArtworkImage? image = null;
        if (errors.IsValid && namedImages.Length == 1)
            image = await ArtworkImageValidator.ValidateAsync(namedImages[0], errors, cancellationToken);
        return errors.IsValid ? new(metadata, image) : null;
    }

    private static string One(IFormCollection form, string key) => form.TryGetValue(key, out var value) ? value.ToString() : string.Empty;
    private static int Integer(IFormCollection form, string key, ModelStateDictionary errors)
    {
        if (int.TryParse(One(form, key), NumberStyles.Integer, CultureInfo.InvariantCulture, out var value)) return value;
        errors.AddModelError(key, "The value must be an integer."); return 0;
    }
    private static decimal Decimal(IFormCollection form, string key, ModelStateDictionary errors)
    {
        if (decimal.TryParse(One(form, key), NumberStyles.Number, CultureInfo.InvariantCulture, out var value)) return value;
        errors.AddModelError(key, "The value must be a decimal number using '.' as the separator."); return 0;
    }
}
