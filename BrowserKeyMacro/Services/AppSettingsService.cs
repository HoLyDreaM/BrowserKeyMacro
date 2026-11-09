using System.Globalization;
using System.Text;
using BrowserKeyMacro.Models;

namespace BrowserKeyMacro.Services;

public sealed class AppSettingsService
{
    private readonly string _filePath;

    public AppSettingsService(string? filePath = null)
    {
        _filePath = filePath ?? Path.Combine(AppContext.BaseDirectory, "settings.ini");
    }

    public string FilePath => _filePath;

    public AppSettings Load()
    {
        var settings = new AppSettings();
        if (!File.Exists(_filePath))
        {
            Save(settings);
            return settings;
        }

        try
        {
            string? section = null;
            foreach (var raw in File.ReadAllLines(_filePath, Encoding.UTF8))
            {
                var line = raw.Trim();
                if (line.Length == 0 || line.StartsWith(';') || line.StartsWith('#'))
                    continue;

                if (line.StartsWith('[') && line.EndsWith(']'))
                {
                    section = line[1..^1].Trim();
                    continue;
                }

                var eq = line.IndexOf('=');
                if (eq <= 0)
                    continue;

                var key = line[..eq].Trim();
                var value = line[(eq + 1)..].Trim();

                if (string.Equals(section, "General", StringComparison.OrdinalIgnoreCase))
                {
                    switch (key)
                    {
                        case "SelectedBrowserProcess":
                            settings.SelectedBrowserProcess = value;
                            break;
                        case "SelectedBrowserTitle":
                            settings.SelectedBrowserTitle = value;
                            break;
                        case "DelayMs":
                            if (int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var delay))
                                settings.DelayMs = Math.Clamp(delay, 10, 5000);
                            break;
                        case "BringBrowserToFront":
                            settings.BringBrowserToFront = ParseBool(value);
                            break;
                    }
                }
                else if (string.Equals(section, "Keys", StringComparison.OrdinalIgnoreCase))
                {
                    switch (key)
                    {
                        case "Trigger":
                            settings.Trigger = value;
                            break;
                        case "IntermediateKey":
                            settings.IntermediateKey = value;
                            break;
                        case "Key1":
                            settings.Key1 = value;
                            break;
                        case "Key2":
                            settings.Key2 = value;
                            break;
                        case "Key3":
                            settings.Key3 = value;
                            break;
                        case "Key4":
                            settings.Key4 = value;
                            break;
                    }
                }
            }
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException($"Ayar dosyası okunamadı: {ex.Message}", ex);
        }

        return settings;
    }

    public void Save(AppSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);

        var delay = Math.Clamp(settings.DelayMs, 10, 5000);
        var sb = new StringBuilder();
        sb.AppendLine("[General]");
        sb.AppendLine($"SelectedBrowserProcess={settings.SelectedBrowserProcess}");
        sb.AppendLine($"SelectedBrowserTitle={settings.SelectedBrowserTitle}");
        sb.AppendLine($"DelayMs={delay.ToString(CultureInfo.InvariantCulture)}");
        sb.AppendLine($"BringBrowserToFront={(settings.BringBrowserToFront ? "true" : "false")}");
        sb.AppendLine();
        sb.AppendLine("[Keys]");
        sb.AppendLine($"Trigger={settings.Trigger}");
        sb.AppendLine($"IntermediateKey={settings.IntermediateKey}");
        sb.AppendLine($"Key1={settings.Key1}");
        sb.AppendLine($"Key2={settings.Key2}");
        sb.AppendLine($"Key3={settings.Key3}");
        sb.AppendLine($"Key4={settings.Key4}");

        try
        {
            var dir = Path.GetDirectoryName(_filePath);
            if (!string.IsNullOrEmpty(dir))
                Directory.CreateDirectory(dir);

            File.WriteAllText(_filePath, sb.ToString(), new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException($"Ayar dosyası kaydedilemedi: {ex.Message}", ex);
        }
    }

    private static bool ParseBool(string value)
    {
        return value.Equals("true", StringComparison.OrdinalIgnoreCase)
            || value.Equals("1", StringComparison.OrdinalIgnoreCase)
            || value.Equals("yes", StringComparison.OrdinalIgnoreCase);
    }
}
