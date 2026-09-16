using ArtTechGallery.Core.Storage;

namespace ArtTechGallery.Infrastructure.Storage;

public sealed class LocalArtworkStorage : IArtworkStorage
{
    private readonly string root;

    public LocalArtworkStorage(string rootPath)
    {
        root = Path.GetFullPath(rootPath);
        if (!Directory.Exists(root)) return;
        foreach (var temporary in Directory.EnumerateFiles(root, "*.tmp-*", SearchOption.AllDirectories))
        {
            try { File.Delete(temporary); }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }
    }

    public async Task WriteAsync(ArtworkImageReference reference, Stream content, CancellationToken cancellationToken)
    {
        var destination = Resolve(reference);
        Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
        var temporary = destination + ".tmp-" + Guid.NewGuid().ToString("N");
        try
        {
            await using (var output = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None,
                81920, FileOptions.Asynchronous | FileOptions.WriteThrough))
            {
                await content.CopyToAsync(output, cancellationToken);
                await output.FlushAsync(cancellationToken);
            }
            File.Move(temporary, destination, false);
        }
        finally
        {
            if (File.Exists(temporary)) File.Delete(temporary);
        }
    }

    public Task<Stream?> OpenReadAsync(ArtworkImageReference reference, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var path = Resolve(reference);
        Stream? result = File.Exists(path)
            ? new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 81920, FileOptions.Asynchronous)
            : null;
        return Task.FromResult(result);
    }

    public Task DeleteIfExistsAsync(ArtworkImageReference reference, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var path = Resolve(reference);
        if (File.Exists(path)) File.Delete(path);
        var directory = Path.GetDirectoryName(path)!;
        if (Directory.Exists(directory) && !Directory.EnumerateFileSystemEntries(directory).Any()) Directory.Delete(directory);
        return Task.CompletedTask;
    }

    private string Resolve(ArtworkImageReference reference)
    {
        var path = Path.GetFullPath(Path.Combine(root, reference.Key.Replace('/', Path.DirectorySeparatorChar)));
        var prefix = root.TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        if (!path.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Invalid managed artwork image reference.");
        return path;
    }
}
