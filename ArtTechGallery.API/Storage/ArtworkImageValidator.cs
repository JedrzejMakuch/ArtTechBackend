using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using SkiaSharp;

namespace ArtTechGallery.API.Storage;

public sealed record ValidatedArtworkImage(byte[] Content, string Extension, string ContentType);

public static class ArtworkImageValidator
{
    public const long MaximumBytes = 10 * 1024 * 1024;
    public const int MaximumPixelsPerDimension = 4096;

    public static async Task<ValidatedArtworkImage?> ValidateAsync(IFormFile file, ModelStateDictionary errors,
        CancellationToken cancellationToken)
    {
        if (file.Length <= 0) { errors.AddModelError("image", "Image must not be empty."); return null; }
        if (file.Length > MaximumBytes) { errors.AddModelError("image", "Image must not exceed 10 MiB."); return null; }
        byte[] bytes;
        await using (var input = file.OpenReadStream())
        using (var memory = new MemoryStream((int)file.Length))
        {
            await input.CopyToAsync(memory, cancellationToken);
            bytes = memory.ToArray();
        }
        using var data = SKData.CreateCopy(bytes);
        using var codec = SKCodec.Create(data);
        if (codec is null || codec.Info.Width <= 0 || codec.Info.Height <= 0
            || codec.Info.Width > MaximumPixelsPerDimension || codec.Info.Height > MaximumPixelsPerDimension)
        {
            errors.AddModelError("image", "Image is malformed or exceeds 4096 x 4096 pixels."); return null;
        }
        string extension, contentType;
        if (codec.EncodedFormat == SKEncodedImageFormat.Jpeg) { extension = "jpg"; contentType = "image/jpeg"; }
        else if (codec.EncodedFormat == SKEncodedImageFormat.Png)
        {
            if (codec.FrameCount > 1) { errors.AddModelError("image", "Animated PNG images are not supported."); return null; }
            extension = "png"; contentType = "image/png";
        }
        else { errors.AddModelError("image", "Only JPEG and PNG images are supported."); return null; }
        if (!string.Equals(file.ContentType, contentType, StringComparison.OrdinalIgnoreCase))
        {
            errors.AddModelError("image", $"The supplied content type must be {contentType}."); return null;
        }
        using var bitmap = new SKBitmap(codec.Info);
        if (codec.GetPixels(codec.Info, bitmap.GetPixels()) != SKCodecResult.Success)
        {
            errors.AddModelError("image", "Image data is incomplete or malformed."); return null;
        }
        return new(bytes, extension, contentType);
    }
}
