namespace SnapdexCore.Indexing;

public sealed record ScannedImageFile(
    string Path,
    string Filename,
    long Size,
    DateTimeOffset ModifiedTimeUtc,
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
