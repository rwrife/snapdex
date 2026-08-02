namespace SnapdexCore.Indexing;

public sealed record IndexedImageRecord(
    string Path,
    string Filename,
    long Size,
    DateTimeOffset ModifiedTimeUtc,
    DateTimeOffset IndexedAtUtc,
    string? CameraMake,
    string? CameraModel,
    string? LensModel,
    int? Iso,
    double? Aperture,
    double? ShutterSeconds,
    double? FocalLengthMm,
    DateTimeOffset? CapturedAtUtc,
    double? GpsLatitude,
    double? GpsLongitude);
