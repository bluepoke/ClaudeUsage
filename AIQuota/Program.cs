using AIQuota.Localization;

namespace AIQuota;

static class Program
{
    /// <summary>
    /// The main entry point for the application. Tray-only, no visible window.
    /// </summary>
    [STAThread]
    static void Main()
    {
        using var singleInstanceGuard = new Mutex(initiallyOwned: true, "AIQuota_SingleInstance", out var isNew);
        if (!isNew)
        {
            MessageBox.Show(
                Strings.InstanceAlreadyRunning,
                Strings.AppTitle,
                MessageBoxButtons.OK,
                MessageBoxIcon.Information);
            return;
        }

        ApplicationConfiguration.Initialize();
        Application.Run(new UsageTrayContext());
    }
}
