using ClaudeUsageTray.Auth;

namespace ClaudeUsageTray;

public sealed class UsageTrayContext : ApplicationContext
{
    private static readonly TimeSpan PollInterval = TimeSpan.FromSeconds(60);
    private const int WarnThresholdPercent = 90;

    private readonly OAuthClient _oauth = new();
    private readonly UsageApiClient _usageApi;

    private readonly NotifyIcon _notifyIcon;
    private readonly System.Windows.Forms.Timer _timer;
    private readonly ToolStripMenuItem _sessionItem;
    private readonly ToolStripMenuItem _weeklyItem;
    private readonly ToolStripMenuItem _statusItem;
    private readonly ToolStripMenuItem _loginItem;
    private readonly ToolStripMenuItem _logoutItem;
    private readonly ToolStripMenuItem _startupItem;

    private bool _sessionWarningShown;
    private bool _weeklyWarningShown;
    private bool _refreshInProgress;

    public UsageTrayContext()
    {
        _usageApi = new UsageApiClient(_oauth);

        _sessionItem = new ToolStripMenuItem("Sitzung (5h): –") { Enabled = false };
        _weeklyItem = new ToolStripMenuItem("Woche (7 Tage): –") { Enabled = false };
        _statusItem = new ToolStripMenuItem("Nicht angemeldet") { Enabled = false };
        _loginItem = new ToolStripMenuItem("Bei claude.ai anmelden…", null, OnLoginClicked);
        _logoutItem = new ToolStripMenuItem("Abmelden", null, OnLogoutClicked) { Visible = false };
        _startupItem = new ToolStripMenuItem("Bei Windows-Start ausführen", null, OnToggleStartup)
        {
            CheckOnClick = false,
            Checked = StartupManager.IsEnabled(),
        };

        var menu = new ContextMenuStrip();
        menu.Items.Add(_sessionItem);
        menu.Items.Add(_weeklyItem);
        menu.Items.Add(_statusItem);
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add("Jetzt aktualisieren", null, async (_, _) => await RefreshAsync());
        menu.Items.Add(_loginItem);
        menu.Items.Add(_logoutItem);
        menu.Items.Add(_startupItem);
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add("Beenden", null, (_, _) => ExitThread());

        _notifyIcon = new NotifyIcon
        {
            Icon = TrayIconFactory.CreateUnavailableIcon(),
            Text = "Claude Nutzung – nicht angemeldet",
            ContextMenuStrip = menu,
            Visible = true,
        };
        _notifyIcon.DoubleClick += async (_, _) => await RefreshAsync();

        _timer = new System.Windows.Forms.Timer { Interval = (int)PollInterval.TotalMilliseconds };
        _timer.Tick += async (_, _) => await RefreshAsync();
        _timer.Start();

        UpdateLoginMenuState();
        _ = RefreshAsync();
    }

    private async void OnLoginClicked(object? sender, EventArgs e)
    {
        _loginItem.Enabled = false;
        try
        {
            _notifyIcon.Text = "Claude Nutzung – Anmeldung läuft (Browser öffnet sich)…";
            await _oauth.LoginAsync(CancellationToken.None);
            UpdateLoginMenuState();
            await RefreshAsync();
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Anmeldung fehlgeschlagen:\n{ex.Message}", "Claude Nutzung",
                MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
        finally
        {
            _loginItem.Enabled = true;
        }
    }

    private async void OnLogoutClicked(object? sender, EventArgs e)
    {
        _oauth.Logout();
        UpdateLoginMenuState();
        await RefreshAsync();
    }

    private void OnToggleStartup(object? sender, EventArgs e)
    {
        var enable = !_startupItem.Checked;
        StartupManager.SetEnabled(enable);
        _startupItem.Checked = StartupManager.IsEnabled();
    }

    private void UpdateLoginMenuState()
    {
        var loggedIn = _oauth.IsLoggedIn;
        _loginItem.Visible = !loggedIn;
        _logoutItem.Visible = loggedIn;
    }

    private async Task RefreshAsync()
    {
        if (_refreshInProgress)
            return;
        _refreshInProgress = true;
        try
        {
            var result = await _usageApi.FetchAsync(CancellationToken.None);
            ApplyResult(result);
        }
        finally
        {
            _refreshInProgress = false;
        }
    }

    private void ApplyResult(UsageFetchResult result)
    {
        switch (result.Status)
        {
            case UsageFetchStatus.NotLoggedIn:
                SetIcon(TrayIconFactory.CreateUnavailableIcon());
                _notifyIcon.Text = "Claude Nutzung – nicht angemeldet";
                _sessionItem.Text = "Sitzung (5h): –";
                _weeklyItem.Text = "Woche (7 Tage): –";
                _statusItem.Text = "Rechtsklick > Anmelden";
                UpdateLoginMenuState();
                return;

            case UsageFetchStatus.AuthExpired:
                _oauth.Logout();
                SetIcon(TrayIconFactory.CreateUnavailableIcon());
                _notifyIcon.Text = "Claude Nutzung – Anmeldung abgelaufen";
                _statusItem.Text = "Bitte erneut anmelden";
                UpdateLoginMenuState();
                return;

            case UsageFetchStatus.NetworkError:
                _statusItem.Text = $"Fehler beim Abrufen ({result.Error})";
                return;
        }

        var snapshot = result.Snapshot!;
        SetIcon(TrayIconFactory.CreateUsageIcon(snapshot.SessionPercent, snapshot.WeeklyPercent));

        _notifyIcon.Text = Truncate(
            $"Claude Nutzung\nSitzung (5h): {snapshot.SessionPercent}%\nWoche (7 Tage): {snapshot.WeeklyPercent}%",
            127);

        _sessionItem.Text = $"Sitzung (5h): {snapshot.SessionPercent}%{FormatReset(snapshot.SessionResetsAt)}";
        _weeklyItem.Text = $"Woche (7 Tage): {snapshot.WeeklyPercent}%{FormatReset(snapshot.WeeklyResetsAt)}";
        _statusItem.Text = $"Stand: {snapshot.FetchedAt:HH:mm:ss}";

        MaybeWarn(snapshot);
    }

    private static string FormatReset(DateTimeOffset? resetsAt)
    {
        if (resetsAt is not { } r)
            return "";

        var remaining = r - DateTimeOffset.Now;
        var countdown = remaining <= TimeSpan.Zero
            ? "in Kürze"
            : remaining.TotalHours >= 1
                ? $"in {(int)remaining.TotalHours}h {remaining.Minutes}min"
                : $"in {Math.Max(remaining.Minutes, 1)}min";

        return $" (bis {r.ToLocalTime():HH:mm}, {countdown})";
    }

    private void SetIcon(Icon icon)
    {
        var old = _notifyIcon.Icon;
        _notifyIcon.Icon = icon;
        old?.Dispose();
    }

    private void MaybeWarn(UsageSnapshot snapshot)
    {
        if (snapshot.SessionPercent >= WarnThresholdPercent && !_sessionWarningShown)
        {
            _notifyIcon.ShowBalloonTip(8000, "Claude Nutzung",
                $"Sitzungslimit (5h) bei {snapshot.SessionPercent}%.", ToolTipIcon.Warning);
            _sessionWarningShown = true;
        }
        else if (snapshot.SessionPercent < WarnThresholdPercent)
        {
            _sessionWarningShown = false;
        }

        if (snapshot.WeeklyPercent >= WarnThresholdPercent && !_weeklyWarningShown)
        {
            _notifyIcon.ShowBalloonTip(8000, "Claude Nutzung",
                $"Wochenlimit bei {snapshot.WeeklyPercent}%.", ToolTipIcon.Warning);
            _weeklyWarningShown = true;
        }
        else if (snapshot.WeeklyPercent < WarnThresholdPercent)
        {
            _weeklyWarningShown = false;
        }
    }

    private static string Truncate(string value, int maxLength) =>
        value.Length <= maxLength ? value : value[..maxLength];

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _timer.Dispose();
            _notifyIcon.Visible = false;
            _notifyIcon.Icon?.Dispose();
            _notifyIcon.Dispose();
        }
        base.Dispose(disposing);
    }
}
