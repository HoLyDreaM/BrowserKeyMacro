using System.Text;

namespace BrowserKeyMacro.Services;

/// <summary>
/// Uygulama klasörüne satır satır debug log yazar (macro-debug.log).
/// </summary>
public static class MacroDebugLog
{
    private static readonly object Gate = new();
    private static readonly string FilePath =
        Path.Combine(AppContext.BaseDirectory, "macro-debug.log");

    public static void Write(string message)
    {
        try
        {
            var line = $"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}  {message}{Environment.NewLine}";
            lock (Gate)
            {
                File.AppendAllText(FilePath, line, Encoding.UTF8);
            }
        }
        catch
        {
            // Log yazılamazsa makroyu bozma
        }
    }
}
