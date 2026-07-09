namespace ClaudeUsageTray;

static class Program
{
    /// <summary>
    /// The main entry point for the application. Tray-only, no visible window.
    /// </summary>
    [STAThread]
    static void Main()
    {
        using var singleInstanceGuard = new Mutex(initiallyOwned: true, "ClaudeUsageTray_SingleInstance", out var isNew);
        if (!isNew)
        {
            MessageBox.Show(
                "Claude Nutzung läuft bereits (siehe Systemtray).",
                "Claude Nutzung",
                MessageBoxButtons.OK,
                MessageBoxIcon.Information);
            return;
        }

        ApplicationConfiguration.Initialize();
        Application.Run(new UsageTrayContext());
    }
}
