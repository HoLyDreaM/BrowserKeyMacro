namespace BrowserKeyMacro.Models;

public sealed class AppSettings
{
    public string SelectedBrowserProcess { get; set; } = string.Empty;
    public string SelectedBrowserTitle { get; set; } = string.Empty;
    public int DelayMs { get; set; } = 300;
    public bool BringBrowserToFront { get; set; } = true;
    public string Trigger { get; set; } = "Q";
    public string IntermediateKey { get; set; } = string.Empty;
    public string Key1 { get; set; } = "1";
    public string Key2 { get; set; } = "2";
    public string Key3 { get; set; } = "3";
    public string Key4 { get; set; } = "4";
}
