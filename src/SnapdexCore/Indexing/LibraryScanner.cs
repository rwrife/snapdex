using System.Collections.Immutable;

namespace SnapdexCore.Indexing;

public sealed class LibraryScanner
{
    private static readonly ImmutableHashSet<string> SupportedExtensions =
        ImmutableHashSet.Create(StringComparer.OrdinalIgnoreCase,
            ".jpg", ".jpeg", ".png", ".heic", ".heif", ".tif", ".tiff", ".webp",
            ".cr2", ".cr3", ".nef", ".arw", ".dng", ".rw2", ".orf", ".raf", ".srw");

    private readonly IImageMetadataReader _metadataReader;

    public LibraryScanner(IImageMetadataReader? metadataReader = null)
    {
        _metadataReader = metadataReader ?? new MetadataExtractorImageMetadataReader();
    }

    public IEnumerable<ScannedImageFile> Scan(string rootPath)
    {
        if (string.IsNullOrWhiteSpace(rootPath))
        {
            throw new ArgumentException("Root path is required.", nameof(rootPath));
        }

        if (!Directory.Exists(rootPath))
        {
            throw new DirectoryNotFoundException($"Directory does not exist: {rootPath}");
        }

        var stack = new Stack<string>();
        stack.Push(rootPath);

        while (stack.Count > 0)
        {
            var directory = stack.Pop();

            IEnumerable<string> childDirectories;
            try
            {
                childDirectories = Directory.EnumerateDirectories(directory);
            }
            catch (Exception ex) when (ex is UnauthorizedAccessException or IOException)
            {
                continue;
            }

            foreach (var child in childDirectories)
            {
                stack.Push(child);
            }

            IEnumerable<string> files;
            try
            {
                files = Directory.EnumerateFiles(directory);
            }
            catch (Exception ex) when (ex is UnauthorizedAccessException or IOException)
            {
                continue;
            }

            foreach (var filePath in files)
            {
                var extension = Path.GetExtension(filePath);
                if (!SupportedExtensions.Contains(extension))
                {
                    continue;
                }

                var fileInfo = new FileInfo(filePath);
                var metadata = _metadataReader.Read(filePath);

                yield return new ScannedImageFile(
                    Path.GetFullPath(filePath),
                    fileInfo.Name,
                    fileInfo.Length,
                    fileInfo.LastWriteTimeUtc,
                    metadata.CameraMake,
                    metadata.CameraModel,
                    metadata.LensModel,
                    metadata.Iso,
                    metadata.Aperture,
                    metadata.ShutterSeconds,
                    metadata.FocalLengthMm,
                    metadata.CapturedAtUtc,
                    metadata.GpsLatitude,
                    metadata.GpsLongitude);
            }
        }
    }
}
