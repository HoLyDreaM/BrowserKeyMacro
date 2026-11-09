namespace BrowserKeyMacro.Services;

/// <summary>
/// Publish sonrası varlık yolları — exe yanında icon.png; form ikonu exe gömülü ikondan.
/// Assets\ alt klasörü runtime'da gerekmez.
/// </summary>
public static class AppAssets
{
    public static string LogoPngPath =>
        Path.Combine(AppContext.BaseDirectory, "icon.png");

    public static Icon? TryLoadAppIcon()
    {
        try
        {
            var exe = Environment.ProcessPath;
            if (string.IsNullOrWhiteSpace(exe) || !File.Exists(exe))
                exe = Application.ExecutablePath;
            if (string.IsNullOrWhiteSpace(exe) || !File.Exists(exe))
                return null;
            return Icon.ExtractAssociatedIcon(exe);
        }
        catch
        {
            return null;
        }
    }

    public static Image? TryLoadLogoImage()
    {
        try
        {
            var path = LogoPngPath;
            if (!File.Exists(path))
                return null;
            using var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            return Image.FromStream(fs);
        }
        catch
        {
            return null;
        }
    }
}
