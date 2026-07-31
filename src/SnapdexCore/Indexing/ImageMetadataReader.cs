using MetadataExtractor;
using MetadataExtractor.Formats.Exif;

namespace SnapdexCore.Indexing;

public interface IImageMetadataReader
{
    ImageMetadata Read(string filePath);
}

public sealed record ImageMetadata(
    string? CameraMake = null,
    string? CameraModel = null,
    string? LensModel = null,
    int? Iso = null,
    double? Aperture = null,
    double? ShutterSeconds = null,
    double? FocalLengthMm = null,
    DateTimeOffset? CapturedAtUtc = null,
    double? GpsLatitude = null,
    double? GpsLongitude = null);

public sealed class MetadataExtractorImageMetadataReader : IImageMetadataReader
{
    public ImageMetadata Read(string filePath)
    {
        if (string.IsNullOrWhiteSpace(filePath) || !File.Exists(filePath))
        {
            return new ImageMetadata();
        }

        try
        {
            var directories = ImageMetadataReader.ReadMetadata(filePath);
            var ifd0 = directories.OfType<ExifIfd0Directory>().FirstOrDefault();
            var subIfd = directories.OfType<ExifSubIfdDirectory>().FirstOrDefault();
            var gpsDirectory = directories.OfType<GpsDirectory>().FirstOrDefault();

            var cameraMake = Clean(ifd0?.GetString(ExifDirectoryBase.TagMake));
            var cameraModel = Clean(ifd0?.GetString(ExifDirectoryBase.TagModel));
            var lensModel = Clean(subIfd?.GetString(ExifDirectoryBase.TagLensModel));

            int? iso = null;
            if (subIfd is not null && subIfd.TryGetInt32(ExifDirectoryBase.TagIsoEquivalent, out var isoValue))
            {
                iso = isoValue;
            }

            var aperture = TryGetRationalAsDouble(subIfd, ExifDirectoryBase.TagFNumber);
            var shutter = TryGetRationalAsDouble(subIfd, ExifDirectoryBase.TagExposureTime);
            var focalLength = TryGetRationalAsDouble(subIfd, ExifDirectoryBase.TagFocalLength);

            DateTimeOffset? capturedAtUtc = null;
            if (subIfd is not null && subIfd.TryGetDateTime(ExifDirectoryBase.TagDateTimeOriginal, out var capturedAt))
            {
                capturedAtUtc = new DateTimeOffset(DateTime.SpecifyKind(capturedAt, DateTimeKind.Utc));
            }

            double? latitude = null;
            double? longitude = null;
            var geo = gpsDirectory?.GetGeoLocation();
            if (geo is not null && !geo.IsZero)
            {
                latitude = geo.Latitude;
                longitude = geo.Longitude;
            }

            return new ImageMetadata(
                cameraMake,
                cameraModel,
                lensModel,
                iso,
                aperture,
                shutter,
                focalLength,
                capturedAtUtc,
                latitude,
                longitude);
        }
        catch (ImageProcessingException)
        {
            return new ImageMetadata();
        }
        catch (IOException)
        {
            return new ImageMetadata();
        }
        catch (UnauthorizedAccessException)
        {
            return new ImageMetadata();
        }
    }

    private static string? Clean(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        return value.Trim();
    }

    private static double? TryGetRationalAsDouble(ExifSubIfdDirectory? directory, int tag)
    {
        if (directory is null)
        {
            return null;
        }

        return directory.TryGetRational(tag, out var rational)
            ? rational.ToDouble()
            : null;
    }
}
