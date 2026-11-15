namespace WendYourWay.Launcher;

/// <summary>
/// The small window while Wend Your Way starts, rebuilds or stops (#142, T138, AC7; user: "I want there
/// to be progress indicators of some sort when starting up so that I know something is happening"):
/// the step it's on, and a bar that keeps moving; or why it can't, and a button to close.
/// </summary>
internal sealed class StartingWindow : Form
{
    private readonly Label _step = new() { AutoSize = false, Dock = DockStyle.Top, Height = 64, Font = new Font("Segoe UI", 10.5f), Padding = new Padding(4, 8, 4, 0) };
    private readonly ProgressBar _bar = new() { Style = ProgressBarStyle.Marquee, MarqueeAnimationSpeed = 25, Dock = DockStyle.Top, Height = 14 };
    private readonly Button _close = new() { Text = "Close", Dock = DockStyle.Bottom, Height = 32, Visible = false };

    public StartingWindow(Icon icon)
    {
        Text = "Wend Your Way";
        Icon = icon;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = true;
        StartPosition = FormStartPosition.CenterScreen;
        ClientSize = new Size(420, 120);
        Padding = new Padding(16, 12, 16, 12);
        BackColor = Color.FromArgb(0xf4, 0xf0, 0xe6);
        _close.Click += (_, _) => Close();
        Controls.Add(_close);
        Controls.Add(_bar);
        Controls.Add(_step);
    }

    /// <summary>The step it's on, the bar moving.</summary>
    public void Step(string what)
    {
        _step.Text = what;
        _bar.Visible = true;
        _close.Visible = false;
    }

    /// <summary>Why it can't go on: the bar stopped, a button to close.</summary>
    public void Stopped(string why)
    {
        _step.Text = why;
        _bar.Visible = false;
        _close.Visible = true;
        ClientSize = new Size(420, 150);
    }
}
