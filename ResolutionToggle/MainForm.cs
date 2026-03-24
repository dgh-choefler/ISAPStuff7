namespace ResolutionToggle;

internal sealed class MainForm : Form
{
    private readonly DisplayManager _display = new();

    private readonly Label _lblCurrent;
    private readonly Label _lblScaling;
    private readonly Label _lblRecommended;
    private readonly Label _lblStatus;
    private readonly Button _btnToggle;
    private readonly Button _btnRefresh;

    private const int TargetWidth = 1920;
    private const int TargetHeight = 1200;
    private const int TargetScaling = 100;

    public MainForm()
    {
        Text = "Resolution Toggle";
        FormBorderStyle = FormBorderStyle.FixedSingle;
        MaximizeBox = false;
        StartPosition = FormStartPosition.CenterScreen;
        ClientSize = new Size(460, 320);
        BackColor = Color.FromArgb(30, 30, 30);
        ForeColor = Color.White;
        Font = new Font("Segoe UI", 10F);

        var pnlHeader = new Panel
        {
            Dock = DockStyle.Top,
            Height = 50,
            BackColor = Color.FromArgb(45, 45, 48)
        };
        var lblTitle = new Label
        {
            Text = "Resolution & Scaling Toggle",
            Font = new Font("Segoe UI Semibold", 14F),
            ForeColor = Color.FromArgb(0, 150, 255),
            AutoSize = false,
            TextAlign = ContentAlignment.MiddleCenter,
            Dock = DockStyle.Fill
        };
        pnlHeader.Controls.Add(lblTitle);
        Controls.Add(pnlHeader);

        int y = 65;
        const int labelX = 20;

        _lblCurrent = CreateInfoLabel("Current: detecting...", labelX, y);
        y += 32;

        _lblScaling = CreateInfoLabel("Scaling: detecting...", labelX, y);
        y += 32;

        _lblRecommended = CreateInfoLabel("Recommended: detecting...", labelX, y);
        y += 48;

        _btnToggle = new Button
        {
            Text = "Toggle to Recommended",
            Location = new Point(labelX, y),
            Size = new Size(420, 44),
            FlatStyle = FlatStyle.Flat,
            BackColor = Color.FromArgb(0, 120, 215),
            ForeColor = Color.White,
            Font = new Font("Segoe UI Semibold", 11F),
            Cursor = Cursors.Hand
        };
        _btnToggle.FlatAppearance.BorderSize = 0;
        _btnToggle.Click += BtnToggle_Click;
        Controls.Add(_btnToggle);
        y += 56;

        _btnRefresh = new Button
        {
            Text = "Refresh",
            Location = new Point(labelX, y),
            Size = new Size(420, 36),
            FlatStyle = FlatStyle.Flat,
            BackColor = Color.FromArgb(55, 55, 58),
            ForeColor = Color.White,
            Font = new Font("Segoe UI", 10F),
            Cursor = Cursors.Hand
        };
        _btnRefresh.FlatAppearance.BorderSize = 0;
        _btnRefresh.Click += (_, _) => RefreshInfo();
        Controls.Add(_btnRefresh);
        y += 48;

        _lblStatus = new Label
        {
            Text = "",
            Location = new Point(labelX, y),
            Size = new Size(420, 40),
            ForeColor = Color.FromArgb(180, 180, 180),
            Font = new Font("Segoe UI", 9F)
        };
        Controls.Add(_lblStatus);

        RefreshInfo();
    }

    private Label CreateInfoLabel(string text, int x, int y)
    {
        var lbl = new Label
        {
            Text = text,
            Location = new Point(x, y),
            AutoSize = true,
            ForeColor = Color.FromArgb(220, 220, 220),
            Font = new Font("Segoe UI", 10F)
        };
        Controls.Add(lbl);
        return lbl;
    }

    private void RefreshInfo()
    {
        try
        {
            var current = _display.GetCurrentMode();
            _lblCurrent.Text = $"Current resolution:  {current.Width} x {current.Height}  @  {current.Frequency} Hz";

            int scaling = _display.GetCurrentScalingPercent();
            _lblScaling.Text = $"Current scaling:  {scaling}%";

            var recommended = _display.GetRecommendedMode();
            _lblRecommended.Text = $"Recommended:  {recommended.Width} x {recommended.Height}  @  {recommended.Frequency} Hz";

            UpdateToggleButton(current, scaling);
        }
        catch (Exception ex)
        {
            _lblStatus.Text = $"Error: {ex.Message}";
        }
    }

    private void UpdateToggleButton(DisplayManager.DisplayMode current, int scaling)
    {
        bool isAtTarget = current.Width == TargetWidth
                       && current.Height == TargetHeight
                       && scaling == TargetScaling;

        if (isAtTarget)
        {
            var rec = _display.GetRecommendedMode();
            _btnToggle.Text = $"Switch to Recommended  ({rec.Width}x{rec.Height} + auto scaling)";
            _btnToggle.BackColor = Color.FromArgb(16, 124, 65);
        }
        else
        {
            _btnToggle.Text = $"Switch to {TargetWidth}x{TargetHeight} @ {TargetScaling}% scaling";
            _btnToggle.BackColor = Color.FromArgb(0, 120, 215);
        }
    }

    private void BtnToggle_Click(object? sender, EventArgs e)
    {
        try
        {
            var current = _display.GetCurrentMode();
            int scaling = _display.GetCurrentScalingPercent();

            bool isAtTarget = current.Width == TargetWidth
                           && current.Height == TargetHeight
                           && scaling == TargetScaling;

            string result;
            if (isAtTarget)
            {
                var rec = _display.GetRecommendedMode();
                result = _display.SetResolution(rec.Width, rec.Height);
                ScalingHelper.SetScalingPercent(0);
                _lblStatus.Text = result + " Scaling reset to recommended (sign out to apply scaling).";
            }
            else
            {
                result = _display.SetResolution(TargetWidth, TargetHeight);
                ScalingHelper.SetScalingPercent(TargetScaling);
                _lblStatus.Text = result + $" Scaling set to {TargetScaling}%.";
            }

            RefreshInfo();
        }
        catch (Exception ex)
        {
            _lblStatus.Text = $"Error: {ex.Message}";
        }
    }
}
