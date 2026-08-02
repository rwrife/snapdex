using SnapdexCore.Indexing;

namespace Snapdex.App;

internal sealed class SearchResultRow
{
    public required IndexedImageRecord Record { get; init; }

    public required string DisplayPath { get; init; }

    public string Filename => Record.Filename;

    public string? ThumbnailPath { get; init; }

    public string CaptureDateText => Record.CapturedAtUtc?.ToLocalTime().ToString("yyyy-MM-dd HH:mm") ?? "—";

    public string CameraText => JoinNonEmpty(" ", Record.CameraMake, Record.CameraModel);

    public string LensText => string.IsNullOrWhiteSpace(Record.LensModel) ? "—" : Record.LensModel;

    public string IsoText => Record.Iso?.ToString() ?? "—";

    public string IsoDisplayText => Record.Iso is null ? "—" : $"ISO {Record.Iso}";

    public string ApertureText => Record.Aperture is null ? "—" : $"f/{Record.Aperture:0.0#}";

    public string ShutterText => Record.ShutterSeconds is null ? "—" : FormatShutter(Record.ShutterSeconds.Value);

    public string FocalLengthText => Record.FocalLengthMm is null ? "—" : $"{Record.FocalLengthMm:0.#} mm";

    private static string JoinNonEmpty(string separator, params string?[] parts)
    {
        var values = parts.Where(p => !string.IsNullOrWhiteSpace(p)).ToArray();
        return values.Length == 0 ? "—" : string.Join(separator, values!);
    }

    private static string FormatShutter(double seconds)
    {
        if (seconds <= 0)
        {
            return "—";
        }

        if (seconds >= 1)
        {
            return $"{seconds:0.###}s";
        }

        var reciprocal = Math.Round(1d / seconds);
        if (reciprocal > 0)
        {
            return $"1/{reciprocal:0}";
        }

        return $"{seconds:0.###}s";
    }
}
