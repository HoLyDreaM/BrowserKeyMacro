namespace BrowserKeyMacro;

static class Program
{
    [STAThread]
    static void Main()
    {
        ApplicationConfiguration.Initialize();
        Application.SetUnhandledExceptionMode(UnhandledExceptionMode.CatchException);
        Application.ThreadException += (_, e) =>
        {
            MessageBox.Show(
                $"Beklenmeyen bir hata oluştu:\n{e.Exception.Message}",
                "Browser Key Macro",
                MessageBoxButtons.OK,
                MessageBoxIcon.Error);
        };
        AppDomain.CurrentDomain.UnhandledException += (_, e) =>
        {
            if (e.ExceptionObject is Exception ex)
            {
                MessageBox.Show(
                    $"Kritik hata:\n{ex.Message}",
                    "Browser Key Macro",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Error);
            }
        };

        Application.Run(new MainForm());
    }
}
