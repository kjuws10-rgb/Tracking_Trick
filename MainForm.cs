namespace TrackingTrick;

public sealed class MainForm : Form
{
    private readonly NumericUpDown idleSeconds = Number(60, 1, 86400);
    private readonly NumericUpDown minInterval = Number(5, 1, 3600);
    private readonly NumericUpDown maxInterval = Number(15, 1, 3600);
    private readonly NumericUpDown activeMinutes = Number(10, 1, 1440);
    private readonly CheckBox immediate = new() { Text = "활성화 시 바로 시작", AutoSize = true };
    private readonly CheckBox enabled = new() { Text = "자동 클릭 활성화", AutoSize = true, Checked = true };
    private readonly Label status = new() { AutoSize = true, ForeColor = Color.DimGray };
    private readonly NotifyIcon tray = null!;
    // Cursor polling does not need sub-frame precision; 250 ms keeps CPU use negligible.
    private readonly System.Windows.Forms.Timer timer = new() { Interval = 250 };
    private readonly Random random = new();
    private NativeMouse.POINT previous;
    private DateTime lastMovement;
    private DateTime activeUntil;
    private DateTime nextClick;
    private bool active;
    private bool armed = true;
    private bool exiting;
    private string lastStatus = string.Empty;
    private string lastTrayText = string.Empty;

    public MainForm()
    {
        Text = "Tracking Trick";
        ClientSize = new Size(380, 315);
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        StartPosition = FormStartPosition.CenterScreen;
        Icon = SystemIcons.Application;

        var table = new TableLayoutPanel { Dock = DockStyle.Fill, Padding = new Padding(18), ColumnCount = 2, RowCount = 7 };
        table.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 62));
        table.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 38));
        AddRow(table, 0, "유휴 시간 (초)", idleSeconds);
        AddRow(table, 1, "클릭 간격 최소 (초)", minInterval);
        AddRow(table, 2, "클릭 간격 최대 (초)", maxInterval);
        AddRow(table, 3, "클릭 지속 시간 (분)", activeMinutes);
        table.Controls.Add(immediate, 0, 4); table.SetColumnSpan(immediate, 2);
        table.Controls.Add(enabled, 0, 5); table.SetColumnSpan(enabled, 2);
        table.Controls.Add(status, 0, 6); table.SetColumnSpan(status, 2);
        Controls.Add(table);

        var menu = new ContextMenuStrip();
        menu.Items.Add("Open", null, (_, _) => ShowWindow());
        menu.Items.Add("활성화 / 비활성화", null, (_, _) => enabled.Checked = !enabled.Checked);
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add("종료", null, (_, _) => { exiting = true; tray.Visible = false; Close(); });
        tray = new NotifyIcon { Icon = SystemIcons.Application, Text = "Tracking Trick", ContextMenuStrip = menu, Visible = true };
        tray.DoubleClick += (_, _) => ShowWindow();

        enabled.CheckedChanged += (_, _) => ResetState();
        immediate.CheckedChanged += (_, _) => ResetState();
        minInterval.ValueChanged += (_, _) => ValidateIntervals();
        maxInterval.ValueChanged += (_, _) => ValidateIntervals();
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
        if (enabled.Checked && immediate.Checked) StartActive();
        else UpdateStatus();
    }

    private void Tick()
    {
        NativeMouse.GetCursorPos(out var current);
        if (current.X != previous.X || current.Y != previous.Y)
        {
            previous = current;
            // User activity always cancels a current click run and arms a fresh idle period.
            active = false;
            armed = true;
            lastMovement = DateTime.Now;
        }

        if (!enabled.Checked) { UpdateStatus(); return; }

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
            NativeMouse.LeftClick();
            ScheduleNextClick(now);
        }
        UpdateStatus();
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

    private void UpdateStatus()
    {
        var nextStatus = !enabled.Checked ? "상태: 비활성화됨" : active
            ? $"상태: 클릭 중 · {Math.Max(0, (int)(activeUntil - DateTime.Now).TotalSeconds)}초 남음"
            : armed
                ? $"상태: 유휴 대기 · {(int)Math.Max(0, ((double)idleSeconds.Value - (DateTime.Now - lastMovement).TotalSeconds))}초"
                : "상태: 중지됨 · 마우스를 움직이면 다시 대기합니다";
        var nextTrayText = !enabled.Checked ? "Tracking Trick - 비활성화" : active ? "Tracking Trick - 클릭 중" : armed ? "Tracking Trick - 대기 중" : "Tracking Trick - 중지됨";

        // Avoid redundant UI updates on every timer tick.
        if (nextStatus != lastStatus) { status.Text = nextStatus; lastStatus = nextStatus; }
        if (nextTrayText != lastTrayText) { tray.Text = nextTrayText; lastTrayText = nextTrayText; }
    }

    private void HideToTray() { Hide(); ShowInTaskbar = false; }
    private void ShowWindow() { ShowInTaskbar = true; Show(); WindowState = FormWindowState.Normal; Activate(); }
    private void OnFormClosing(object? sender, FormClosingEventArgs e)
    {
        if (!exiting && e.CloseReason == CloseReason.UserClosing) { e.Cancel = true; HideToTray(); }
        else { timer.Stop(); tray.Dispose(); }
    }
}
