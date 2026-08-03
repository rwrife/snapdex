using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace Snapdex.App;

internal sealed class ThumbnailCache
{
    private readonly string _cacheDirectory;
    private readonly object _lock = new();

    public ThumbnailCache(string cacheDirectory)
    {
        _cacheDirectory = cacheDirectory;
        Directory.CreateDirectory(_cacheDirectory);
    }

    public string? GetOrCreate(string imagePath, DateTimeOffset modifiedTimeUtc, int maxEdge = 256)
    {
        if (string.IsNullOrWhiteSpace(imagePath) || !File.Exists(imagePath))
        {
            return null;
        }

        var fullPath = Path.GetFullPath(imagePath);
        var key = CreateCacheKey(fullPath, modifiedTimeUtc);
        var thumbnailPath = Path.Combine(_cacheDirectory, $"{key}.jpg");

        if (File.Exists(thumbnailPath))
        {
            return thumbnailPath;
        }

        lock (_lock)
        {
            if (File.Exists(thumbnailPath))
            {
                return thumbnailPath;
            }

            try
            {
                GenerateThumbnail(fullPath, thumbnailPath, maxEdge);
                return thumbnailPath;
            }
            catch
            {
                return null;
            }
        }
    }

    private static string CreateCacheKey(string fullPath, DateTimeOffset modifiedTimeUtc)
    {
        var payload = $"{fullPath}|{modifiedTimeUtc.UtcTicks}";
        var bytes = Encoding.UTF8.GetBytes(payload);
        var hash = SHA256.HashData(bytes);
        return Convert.ToHexString(hash).ToLowerInvariant();
    }

    private static void GenerateThumbnail(string sourcePath, string destinationPath, int maxEdge)
    {
        using var source = File.OpenRead(sourcePath);
        var frame = BitmapFrame.Create(
            source,
            BitmapCreateOptions.PreservePixelFormat,
            BitmapCacheOption.OnLoad);

        var maxDimension = Math.Max(frame.PixelWidth, frame.PixelHeight);
        var scale = maxDimension > maxEdge
            ? maxEdge / (double)maxDimension
            : 1d;

        BitmapSource output = frame;
        if (scale < 1d)
        {
            var transformed = new TransformedBitmap(frame, new ScaleTransform(scale, scale));
            transformed.Freeze();
            output = transformed;
        }

        var destinationDirectory = Path.GetDirectoryName(destinationPath);
        if (!string.IsNullOrWhiteSpace(destinationDirectory))
        {
            Directory.CreateDirectory(destinationDirectory);
        }

        using var destination = File.Open(destinationPath, FileMode.Create, FileAccess.Write, FileShare.None);
        var encoder = new JpegBitmapEncoder
        {
            QualityLevel = 85
        };
        encoder.Frames.Add(BitmapFrame.Create(output));
        encoder.Save(destination);
    }
}
