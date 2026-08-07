using System.Reflection;
using Microsoft.Win32;

namespace TrackingTrick;

public sealed class MainForm : Form
{
    private readonly NumericUpDown idleSeconds = Number(60, 1, 86400);
    private readonly NumericUpDown minInterval = Number(5, 1, 3600);
    private readonly NumericUpDown maxInterval = Number(15, 1, 3600);
    private readonly NumericUpDown activeMinutes = Number(10, 1, 1440);
    private readonly CheckBox immediate = new() { Text = "활성화 시 바로 시작", AutoSize = true };
    private readonly Label status = new() { AutoSize = true, ForeColor = Color.DimGray };
    private readonly NotifyIcon tray = null!;
    private readonly Icon applicationIcon;
    private readonly ToolStripMenuItem automationMenuItem;
    private readonly ToolStripMenuItem startupMenuItem;
    // Cursor polling does not need sub-frame precision; 250 ms keeps CPU use negligible.
    private readonly System.Windows.Forms.Timer timer = new() { Interval = 250 };
    private readonly Random random = new();
    private NativeMouse.POINT previous;
    private DateTime lastMovement;
    private DateTime activeUntil;
    private DateTime nextClick;
    private bool active;
    private bool armed = true;
    private bool automationEnabled = true;
    private bool exiting;
    private string lastStatus = string.Empty;
    private string lastTrayText = string.Empty;

    public MainForm()
    {
        var saved = AppSettings.Load();
        idleSeconds.Value = Clamp(saved.IdleSeconds, idleSeconds.Minimum, idleSeconds.Maximum);
        minInterval.Value = Clamp(saved.MinIntervalSeconds, minInterval.Minimum, minInterval.Maximum);
        maxInterval.Value = Clamp(Math.Max(saved.MaxIntervalSeconds, saved.MinIntervalSeconds), maxInterval.Minimum, maxInterval.Maximum);
        activeMinutes.Value = Clamp(saved.ActiveMinutes, activeMinutes.Minimum, activeMinutes.Maximum);
        immediate.Checked = saved.StartImmediately;
        automationEnabled = saved.AutomationEnabled;
        applicationIcon = new Icon(Path.Combine(AppContext.BaseDirectory, "Assets", "tracking-trick.ico"));

        Text = "Tracking Trick";
        ClientSize = new Size(380, 315);
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        StartPosition = FormStartPosition.CenterScreen;
        Icon = applicationIcon;

        var table = new TableLayoutPanel { Dock = DockStyle.Fill, Padding = new Padding(18), ColumnCount = 2, RowCount = 6 };
        table.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 62));
        table.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 38));
        AddRow(table, 0, "유휴 시간 (초)", idleSeconds);
        AddRow(table, 1, "클릭 간격 최소 (초)", minInterval);
        AddRow(table, 2, "클릭 간격 최대 (초)", maxInterval);
        AddRow(table, 3, "클릭 지속 시간 (분)", activeMinutes);
        table.Controls.Add(immediate, 0, 4); table.SetColumnSpan(immediate, 2);
        table.Controls.Add(status, 0, 5); table.SetColumnSpan(status, 2);
        Controls.Add(table);

        var toolTip = new ToolTip { AutoPopDelay = 9000, InitialDelay = 350, ReshowDelay = 100 };
        toolTip.SetToolTip(idleSeconds, "마우스 또는 키보드가 이 시간 동안 사용되지 않으면 자동 클릭 주기를 시작합니다.");
        toolTip.SetToolTip(minInterval, "각 클릭 사이의 무작위 대기 시간의 최솟값입니다.");
        toolTip.SetToolTip(maxInterval, "각 클릭 사이의 무작위 대기 시간의 최댓값입니다.");
        toolTip.SetToolTip(activeMinutes, "한 번 시작된 자동 클릭 주기가 유지되는 총 시간입니다.");
        toolTip.SetToolTip(immediate, "자동 클릭을 활성화할 때 유휴 시간을 기다리지 않고 바로 주기를 시작합니다.");
        toolTip.SetToolTip(status, "클릭 중에는 마우스 이동 또는 키보드 입력으로 즉시 중단할 수 있습니다.");

        var menu = new ContextMenuStrip();
        menu.Items.Add("Open", null, (_, _) => ShowWindow());
        automationMenuItem = new ToolStripMenuItem("자동 클릭 활성화") { CheckOnClick = true, Checked = automationEnabled };
        startupMenuItem = new ToolStripMenuItem("Windows 시작 시 실행") { CheckOnClick = true, Checked = IsStartupEnabled() };
        automationMenuItem.ToolTipText = "프로그램을 종료하지 않고 자동 클릭 기능 전체를 켜거나 끕니다.";
        startupMenuItem.ToolTipText = "Windows에 로그인하면 Tracking Trick을 자동으로 실행합니다.";
        automationMenuItem.CheckedChanged += (_, _) => { automationEnabled = automationMenuItem.Checked; ResetState(); SaveSettings(); };
        startupMenuItem.CheckedChanged += (_, _) => SetStartupEnabled(startupMenuItem.Checked);
        menu.Items.Add(automationMenuItem);
        menu.Items.Add(startupMenuItem);
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add("종료", null, (_, _) => { exiting = true; tray.Visible = false; Close(); });
        tray = new NotifyIcon { Icon = applicationIcon, Text = "Tracking Trick", ContextMenuStrip = menu, Visible = true };
        tray.DoubleClick += (_, _) => ShowWindow();

        immediate.CheckedChanged += (_, _) => { ResetState(); SaveSettings(); };
        minInterval.ValueChanged += (_, _) => { ValidateIntervals(); SaveSettings(); };
        maxInterval.ValueChanged += (_, _) => { ValidateIntervals(); SaveSettings(); };
        idleSeconds.ValueChanged += (_, _) => SaveSettings();
        activeMinutes.ValueChanged += (_, _) => SaveSettings();
        FormClosing += OnFormClosing;
        Resize += (_, _) => { if (WindowState == FormWindowState.Minimized) Hide(); };

        NativeMouse.GetCursorPos(out previous);
        lastMovement = DateTime.Now;
        ResetState();
        timer.Tick += (_, _) => Tick();
        timer.Start();
    }

    protected override void OnShown(EventArgs e)
    {
        base.OnShown(e);
        // A window handle exists here. Calling BeginInvoke from the constructor does not guarantee that.
        HideToTray();
    }

    private static NumericUpDown Number(decimal value, decimal min, decimal max) => new()
    {
        Minimum = min, Maximum = max, Value = value, Dock = DockStyle.Fill, TextAlign = HorizontalAlignment.Right
    };

    private static decimal Clamp(int value, decimal min, decimal max) => Math.Min(max, Math.Max(min, value));

    private static void AddRow(TableLayoutPanel table, int row, string label, Control control)
    {
        table.Controls.Add(new Label { Text = label, AutoSize = true, Anchor = AnchorStyles.Left }, 0, row);
        table.Controls.Add(control, 1, row);
    }

    private void ValidateIntervals()
    {
        if (minInterval.Value > maxInterval.Value) maxInterval.Value = minInterval.Value;
    }

    private void ResetState()
    {
        active = false;
        armed = true;
        lastMovement = DateTime.Now;
        if (automationEnabled && immediate.Checked) StartActive();
        else UpdateStatus();
    }

    private void Tick()
    {
        NativeMouse.GetCursorPos(out var current);
        var mouseMoved = current.X != previous.X || current.Y != previous.Y;
        if (mouseMoved) previous = current;

        if (mouseMoved || KeyboardActivityDetected())
        {
            ResetForUserActivity();
        }

        if (!automationEnabled) { UpdateStatus(); return; }

        var now = DateTime.Now;
        if (!active && armed && now - lastMovement >= TimeSpan.FromSeconds((double)idleSeconds.Value)) StartActive();
        if (active && now >= activeUntil)
        {
            active = false;
            // A completed run must not start again until the user moves the mouse.
            armed = false;
        }
        if (active && now >= nextClick)
        {
            MoveAndClickAtRandomPosition();
            ScheduleNextClick(now);
        }
        UpdateStatus();
    }

    private static bool KeyboardActivityDetected()
    {
        // The high bit means the key is currently down; the low bit records a press since the last call.
        // This observes globally pressed keys without intercepting or blocking input.
        for (var virtualKey = 8; virtualKey <= 254; virtualKey++)
        {
            if ((NativeMouse.GetAsyncKeyState(virtualKey) & 0x8001) != 0) return true;
        }
        return false;
    }

    private void ResetForUserActivity()
    {
        // Any physical mouse movement or keyboard use cancels the current run and starts a fresh idle period.
        active = false;
        armed = true;
        lastMovement = DateTime.Now;
    }

    private void StartActive()
    {
        active = true;
        var now = DateTime.Now;
        activeUntil = now.AddMinutes((double)activeMinutes.Value);
        ScheduleNextClick(now);
        UpdateStatus();
    }

    private void ScheduleNextClick(DateTime from)
    {
        var min = (int)minInterval.Value;
        var max = (int)maxInterval.Value;
        nextClick = from.AddSeconds(random.Next(min, max + 1));
    }

    private void MoveAndClickAtRandomPosition()
    {
        // Keep random clicks inside the active monitor's usable area, avoiding its taskbar.
        NativeMouse.GetCursorPos(out var cursor);
        var workArea = Screen.FromPoint(new Point(cursor.X, cursor.Y)).WorkingArea;
        const int padding = 12;
        var minX = workArea.Left + padding;
        var maxX = Math.Max(minX, workArea.Right - padding - 1);
        var minY = workArea.Top + padding;
        var maxY = Math.Max(minY, workArea.Bottom - padding - 1);
        var x = random.Next(minX, maxX + 1);
        var y = random.Next(minY, maxY + 1);

        NativeMouse.SetCursorPos(x, y);
        // Record the application's own pointer move so the next poll detects only user movement.
        previous = new NativeMouse.POINT { X = x, Y = y };
        NativeMouse.LeftClick();
    }

    private void UpdateStatus()
    {
        var nextStatus = !automationEnabled ? "상태: 비활성화됨" : active
            ? $"상태: 클릭 중 · {Math.Max(0, (int)(activeUntil - DateTime.Now).TotalSeconds)}초 남음"
            : armed
                ? $"상태: 유휴 대기 · {(int)Math.Max(0, ((double)idleSeconds.Value - (DateTime.Now - lastMovement).TotalSeconds))}초"
                : "상태: 중지됨 · 마우스를 움직이면 다시 대기합니다";
        var nextTrayText = !automationEnabled ? "Tracking Trick - 비활성화" : active ? "Tracking Trick - 클릭 중" : armed ? "Tracking Trick - 대기 중" : "Tracking Trick - 중지됨";

        // Avoid redundant UI updates on every timer tick.
        if (nextStatus != lastStatus) { status.Text = nextStatus; lastStatus = nextStatus; }
        if (nextTrayText != lastTrayText) { tray.Text = nextTrayText; lastTrayText = nextTrayText; }
    }

    private void HideToTray() { Hide(); ShowInTaskbar = false; }
    private void ShowWindow() { ShowInTaskbar = true; Show(); WindowState = FormWindowState.Normal; Activate(); }
    private void OnFormClosing(object? sender, FormClosingEventArgs e)
    {
        if (!exiting && e.CloseReason == CloseReason.UserClosing) { e.Cancel = true; HideToTray(); }
        else { timer.Stop(); tray.Dispose(); applicationIcon.Dispose(); }
    }

    private void SaveSettings()
    {
        new AppSettings
        {
            IdleSeconds = (int)idleSeconds.Value,
            MinIntervalSeconds = (int)minInterval.Value,
            MaxIntervalSeconds = (int)maxInterval.Value,
            ActiveMinutes = (int)activeMinutes.Value,
            StartImmediately = immediate.Checked,
            AutomationEnabled = automationEnabled
        }.Save();
    }

    private static bool IsStartupEnabled()
    {
        using var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Run");
        return key?.GetValue("Tracking Trick") is string;
    }

    private static void SetStartupEnabled(bool enabled)
    {
        try
        {
            using var key = Registry.CurrentUser.CreateSubKey(@"Software\Microsoft\Windows\CurrentVersion\Run");
            if (enabled) key.SetValue("Tracking Trick", StartupCommand());
            else key.DeleteValue("Tracking Trick", false);
        }
        catch (UnauthorizedAccessException)
        {
            MessageBox.Show("Windows 시작 프로그램 설정을 변경할 권한이 없습니다.", "Tracking Trick", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
    }

    private static string StartupCommand()
    {
        var processPath = Environment.ProcessPath ?? Application.ExecutablePath;
        var entryAssembly = Assembly.GetEntryAssembly()?.Location;
        return processPath.EndsWith("dotnet.exe", StringComparison.OrdinalIgnoreCase) && !string.IsNullOrEmpty(entryAssembly)
            ? $"\"{processPath}\" \"{entryAssembly}\""
            : $"\"{processPath}\"";
    }
}
