using System;
using System.IO;

namespace Snapdex.App;

internal static class AppPaths
{
    public static string AppDataRoot
        => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "snapdex");

    public static string DatabasePath => Path.Combine(AppDataRoot, "snapdex.db");

    public static string ThumbnailCacheDirectory => Path.Combine(AppDataRoot, "thumb-cache");

    public static string LocalAiSettingsPath => Path.Combine(AppDataRoot, "local-ai-settings.json");
}
