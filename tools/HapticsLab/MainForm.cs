using System.Diagnostics;
using System.Text;

namespace HapticsLab;

/// <summary>
/// HapticsLab: try, compare and pick the Magic Trackpad 2 haptic values for every part of the
/// Windows haptic touchpad support, then write them to common\MagicTrackpad2Haptics.h.
/// </summary>
public sealed class MainForm : Form
{
    // ------------------------------------------------------------------------------------------
    // State
    // ------------------------------------------------------------------------------------------
    private readonly string _sessionPath = Session.DefaultPath;
    private readonly string? _screenshotDir;
    private Session _session;
    private string? _headerPath;
    private Calibration _header = new();     // as loaded from the header file
    private Calibration _work = new();       // what you are editing
    private IRawTransport? _transport;
    private HidTouchpad? _hid;
    private bool _loading;

    private static readonly int[] Steps = { 25, 50, 75, 100 };
    private static readonly string[] StepHeaders = { "25 %", "50 %", "75 %", "100 %" };
    private static readonly string[] Levels = { "Light", "Medium", "Heavy" };

    // ------------------------------------------------------------------------------------------
    // What to feel for, per step. Ticks are kept in the session.
    // ------------------------------------------------------------------------------------------
    private static readonly Dictionary<string, string[]> Checklists = new()
    {
        ["clickForce"] = new[]
        {
            "Light clicks easily, but resting or dragging fingers don't click by accident",
            "Heavy needs a deliberate press, but doesn't tire your finger",
            "Light, Medium and Heavy feel like three clear steps",
            "Lifting your finger (the release) feels softer than the click",
        },
        ["clickFeedback"] = new[]
        {
            "At 25 % the click is quiet, but you still feel it",
            "At 100 % the click is strong, not harsh or rattly",
            "Each step is noticeably different from the one before",
            "The force you need to click stays the same at every step",
            "The release gets its own small tap only at the steps where you want one",
        },
        ["signals"] = new[]
        {
            "Hover is the faintest signal, but you still notice it",
            "Collide (window snap) is a soft bump, never annoying when repeated",
            "Align is a crisp tick that feels different from Collide, not only stronger",
            "Step is the firmest single pulse, like a notch in a dial",
            "Grow feels like it builds up",
            "Press and Release match a real Medium click and release",
        },
        ["signalStrength"] = new[]
        {
            "At 25 % every signal is still felt",
            "The four levels feel clearly different",
            "At 100 % Step and Grow are strong but pleasant",
            "Grow's pulses feel like one rising signal, not separate taps",
        },
        ["driver"] = new[]
        {
            "Every waveform fired through the driver feels like your pick",
            "Every row in the table says \"same\"",
            "\"Set like Windows\" changes the click as expected",
        },
    };

    private static readonly string[] SignalRatings = { "Too weak", "Just right", "Too strong", "Too similar to another" };

    // ------------------------------------------------------------------------------------------
    // Waveform descriptions: where Windows uses them and what to feel for
    // ------------------------------------------------------------------------------------------
    private static readonly Dictionary<string, (string Title, string Use, string Focus, string[] Alternatives, string Compare)> WaveformInfo = new()
    {
        ["PRESS"] = ("Press", "Optional waveform apps can play to imitate pressing a button. Windows itself presses with the pad's own click.",
            "Should feel the same as a real Medium click, so app feedback and real clicks match.", new[] { "17,06,06", "1E,08,08" }, "RELEASE"),
        ["RELEASE"] = ("Release", "The counterpart of Press: imitates letting go of a button.",
            "Should match the real release you feel when you lift after a Medium click: softer than Press.", new[] { "14,00,00", "10,00,00" }, "PRESS"),
        ["HOVER"] = ("Hover", "Light pulse when the pointer moves over something. Rarely used by Windows today.",
            "The faintest signal: you should still notice it clearly, but it must not feel like a click.", new[] { "0C,00,00", "0A,00,00", "10,00,00" }, "COLLIDE"),
        ["COLLIDE"] = ("Collide", "Soft pulse when something hits a boundary. Windows plays it when a window snaps to a screen edge or into a Snap layout, so you feel it often.",
            "Snap a window while trying it. Soft, rounded, a \"bump\"; clearly weaker than Step; never annoying when repeated.", new[] { "14,00,00", "18,00,00", "1A,02,04" }, "ALIGN"),
        ["ALIGN"] = ("Align", "Sharp pulse when an object snaps into alignment, e.g. guides in drawing or presentation apps.",
            "Crisp and short, a \"tick\". It should feel different from Collide, not only stronger.", new[] { "15,04,04", "15,14,00", "1E,08,08+1E,08,08" }, "COLLIDE"),
        ["STEP"] = ("Step", "Firm pulse for discrete steps. Windows plays it as the preview when you move the \"Haptic signals\" slider.",
            "A confident detent, like a notch in a dial: the strongest single pulse.", new[] { "1E,08,08", "22,0A,0D", "1E,0C,00" }, "COLLIDE"),
        ["GROW"] = ("Grow", "Dynamic pulse for motion or transitions: something growing, opening or expanding.",
            "Should feel like it builds up. Multi-pulse candidates rise from weak to strong.", new[] { "10,00,00+1E,08,08", "17,06,06", "0C,00,00+17,06,06+22,0A,0D" }, "STEP"),
    };

    private sealed class WaveformSection
    {
        public required string Key;
        public required NavItem Nav;
        public required Card Detail;
        public RadioButton[] Picks = Array.Empty<RadioButton>();
        public TextBox[] Candidates = Array.Empty<TextBox>();
        public PulseChainView[] Views = Array.Empty<PulseChainView>();
        public Label Current = new();
    }

    private sealed record PageEntry(string Key, string DefaultSubtitle, NavItem Nav, Panel Panel);

    private sealed record PlayItem(string Label, string Chain, int Intensity, Action? Flash);

    // ------------------------------------------------------------------------------------------
    // Controls that are read back
    // ------------------------------------------------------------------------------------------
    private readonly Panel _content = new() { Dock = DockStyle.Fill, BackColor = Theme.Back };
    private readonly Panel _sidebar = new() { Dock = DockStyle.Left, Width = 268, BackColor = Theme.Sidebar, Padding = new Padding(12, 18, 12, 12) };
    private readonly List<PageEntry> _pages = new();

    private readonly ToolStripStatusLabel _status = new() { Spring = true, TextAlign = ContentAlignment.MiddleLeft };
    private readonly ToolStripButton _replay = new("Replay (F5)") { Enabled = false };
    private readonly ToolStripButton _restoreButton = new("Click test active: restore my settings") { Visible = false, ForeColor = Theme.Bad, Font = Theme.Strong };
    private Func<Task>? _lastPlay;
    private int _playToken;
    private (int Level, int Intensity)? _activeTest;

    private readonly Label _sidebarConnection = new() { Dock = DockStyle.Bottom, Height = 44, Font = Theme.Small, ForeColor = Theme.Muted, TextAlign = ContentAlignment.MiddleLeft };
    private readonly List<(Label Dot, WrapLabel Text)> _connection = new();
    private readonly PulseChainView _legendChain = new() { Size = new Size(200, 74), Dock = DockStyle.None };

    private readonly NumericUpDown[] _pressS0 = new NumericUpDown[3];
    private readonly NumericUpDown[] _releaseS0 = new NumericUpDown[3];
    private readonly Label[] _forceResult = new Label[3];
    private readonly ComboBox _forceTestIntensity = Ui.Combo(70);
    private readonly ForceScale _forceScale = new();

    private readonly NumericUpDown _pressS12 = Ui.Hex(0x20);
    private readonly NumericUpDown _releaseS12 = Ui.Hex(0x20);
    private readonly NumericUpDown _releaseFrom = Ui.Dec(0, 100);
    private readonly ComboBox _feedbackTestLevel = Ui.Combo(100);
    private readonly ClickColumns _clickColumns = new() { Headers = StepHeaders };

    private readonly ComboBox _signalsIntensity = Ui.Combo(70);
    private readonly Dictionary<string, WaveformSection> _sections = new();

    private readonly NumericUpDown _s0MinPercent = Ui.Dec(0, 100);
    private readonly NumericUpDown _s0Floor = Ui.Hex(0xFF);
    private readonly NumericUpDown _gap = Ui.Dec(20, 200);
    private readonly StrengthMatrix _matrix = new() { Headers = StepHeaders };

    private readonly WrapLabel _driverValues = new("");
    private readonly ComboBox _driverIntensity = Ui.Combo(70);
    private readonly ComboBox _driverSetThreshold = Ui.Combo(100);
    private readonly NumericUpDown _driverSetIntensity = Ui.Dec(0, 100);
    private readonly Label[] _drvInstalled = new Label[Calibration.WaveformOrder.Length];
    private readonly Label[] _drvHeader = new Label[Calibration.WaveformOrder.Length];
    private readonly Label[] _drvStatus = new Label[Calibration.WaveformOrder.Length];

    private readonly WrapLabel _changes = new("", Theme.Mono);
    private readonly WrapLabel _summary = new("");
    private readonly TextBox _preview = new()
    {
        Multiline = true, ReadOnly = true, ScrollBars = ScrollBars.Both, WordWrap = false, Height = 330,
        Font = Theme.MonoSmall, BorderStyle = BorderStyle.FixedSingle, BackColor = Color.FromArgb(250, 251, 253),
    };

    private readonly System.Windows.Forms.Timer _autosave = new() { Interval = 1500 };

    // ==========================================================================================

    public MainForm(string? screenshotDir = null)
    {
        _screenshotDir = screenshotDir;
        Text = "HapticsLab - Magic Trackpad 2 haptics";
        Size = new Size(1300, 920);
        MinimumSize = new Size(1050, 640);
        StartPosition = FormStartPosition.CenterScreen;
        Font = Theme.Body;
        BackColor = Theme.Back;
        AutoScaleMode = AutoScaleMode.Dpi;
        KeyPreview = true;

        _session = Session.Load(_sessionPath);

        foreach (var s in Steps) { _forceTestIntensity.Items.Add(s); _signalsIntensity.Items.Add(s); _driverIntensity.Items.Add(s); }
        _forceTestIntensity.SelectedItem = 75; _signalsIntensity.SelectedItem = 100; _driverIntensity.SelectedItem = 100;
        _feedbackTestLevel.Items.AddRange(Levels); _feedbackTestLevel.SelectedIndex = 1;
        _driverSetThreshold.Items.AddRange(Levels); _driverSetThreshold.SelectedIndex = 1;
        _driverSetIntensity.Value = 50;

        var status = new StatusStrip { BackColor = Color.White, SizingGrip = false };
        status.Items.Add(_status);
        status.Items.Add(_replay);
        status.Items.Add(_restoreButton);
        _restoreButton.Click += (_, _) => StopClickTest();
        _replay.Click += (_, _) => { if (_lastPlay != null) _ = _lastPlay(); };

        Controls.Add(_content);
        Controls.Add(_sidebar);
        Controls.Add(status);

        _autosave.Tick += (_, _) => { _autosave.Stop(); SaveSession(); };
        FormClosing += OnClosing;
        Shown += (_, _) => OnStartup();
    }

    private void OnStartup()
    {
        // Outside a source checkout (a downloaded build), use the copy of the header built into the app:
        // everything works except writing to the header
        if (!LoadHeader(FindHeader()) && !LoadBuiltInHeader()) { Close(); return; }

        _work = _session.Working?.Waveforms.Count == Calibration.WaveformOrder.Length ? _session.Working : _header.Clone();

        SuspendLayout();
        BuildSidebar();
        BuildPages();
        ResumeLayout(true);
        LoadValuesIntoControls();
        DetectDevices();
        ShowPage(0);
        ShowWaveform("COLLIDE");

        if (_screenshotDir != null) { TakeScreenshots(); return; }

        if (_session.PendingRestore != null &&
            MessageBox.Show(this, "A click test was still active when HapticsLab last closed, so the drivers may still use test values.\n\nRestore your previous settings now?",
                Text, MessageBoxButtons.YesNo, MessageBoxIcon.Warning) == DialogResult.Yes)
        {
            StopClickTest();
        }
        UpdateRestoreButton();
    }

    private void OnClosing(object? sender, FormClosingEventArgs e)
    {
        if (_screenshotDir == null)
        {
            if (_session.PendingRestore != null) StopClickTest();
            SaveSession();
        }
        _hid?.Dispose();
    }

    protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
    {
        // F5 replays the last thing played; so does Space when no text, list or button has the focus
        bool space = keyData == Keys.Space && ActiveControl is not (TextBoxBase or NumericUpDown or ComboBox or ButtonBase or ListBox);
        if ((keyData == Keys.F5 || space) && _lastPlay != null)
        {
            _ = _lastPlay();
            return true;
        }
        return base.ProcessCmdKey(ref msg, keyData);
    }

    // ------------------------------------------------------------------------------------------
    // Header, session, devices
    // ------------------------------------------------------------------------------------------

    private static string? FindHeader()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null)
        {
            var candidate = Path.Combine(dir.FullName, "common", "MagicTrackpad2Haptics.h");
            if (File.Exists(candidate)) return candidate;
            dir = dir.Parent;
        }
        return null;
    }

    private bool LoadHeader(string? path)
    {
        if (path == null || !File.Exists(path)) return false;
        try
        {
            _header = Calibration.ParseHeader(File.ReadAllText(path));
            _headerPath = path;
            return true;
        }
        catch (Exception e)
        {
            MessageBox.Show(this, $"Cannot read {path}:\n{e.Message}", Text, MessageBoxButtons.OK, MessageBoxIcon.Error);
            return false;
        }
    }

    private bool LoadBuiltInHeader()
    {
        try
        {
            using var stream = typeof(MainForm).Assembly.GetManifestResourceStream("MagicTrackpad2Haptics.h")
                ?? throw new InvalidOperationException("The built-in copy is missing.");
            using var reader = new StreamReader(stream);
            _header = Calibration.ParseHeader(reader.ReadToEnd());
            _headerPath = null;
            return true;
        }
        catch (Exception e)
        {
            MessageBox.Show(this, "Cannot find common\\MagicTrackpad2Haptics.h next to HapticsLab or in a folder above it, " +
                "and the built-in copy can't be read:\n" + e.Message, Text, MessageBoxButtons.OK, MessageBoxIcon.Error);
            return false;
        }
    }

    private void SaveSession()
    {
        if (_screenshotDir != null) return;
        try
        {
            _session.Working = _work;
            foreach (var (key, s) in _sections)
            {
                _session.Candidates[key] = s.Candidates.Select(t => t.Text.Trim()).ToList();
                _session.Picked[key] = Array.FindIndex(s.Picks, r => r.Checked);
            }
            _session.Save(_sessionPath);
        }
        catch (Exception e) { SetStatus("Could not save the session: " + e.Message); }
    }

    private void Changed()
    {
        if (_loading) return;
        _autosave.Stop(); _autosave.Start();
        RefreshDerived();
    }

    private void DetectDevices()
    {
        _transport = UsbPipeTransport.IsAvailable() ? new UsbPipeTransport()
                   : BluetoothControlTransport.IsAvailable() ? new BluetoothControlTransport()
                   : null;
        _hid?.Dispose();
        _hid = HidTouchpad.Open();

        SetConnection(0, _transport != null, "Trying values (steps 1-4): " +
            (_transport?.Name ?? "no trackpad driver found. Connect the trackpad (USB or Bluetooth) with this project's driver, then press Refresh."));
        SetConnection(1, _hid?.HasHaptics == true, "Driver check (step 5): " + (_hid == null ? "touch pad collection not found"
            : _hid.HasHaptics ? "the installed driver supports Windows haptics" : "the installed driver has no haptic support"));
        SetConnection(2, true, _headerPath != null ? "Calibration file: " + _headerPath
            : "Calibration table: the copy built into HapticsLab (no source checkout found, so step 6 can't write to the header)");
        SetConnection(3, true, "Session (picks, ratings, notes, working values): " + _sessionPath);

        _sidebarConnection.Text = _transport == null ? "●  No trackpad found" : "●  Connected: " + _transport.Name;
        _sidebarConnection.ForeColor = _transport == null ? Theme.Warn : Theme.Good;
        RefreshDriverTab();
        UpdateNav();
        SetStatus(_transport == null ? "No trackpad driver found." : "Ready: " + _transport.Name);
    }

    private void SetConnection(int index, bool ok, string text)
    {
        if (index >= _connection.Count) return;
        _connection[index].Dot.ForeColor = ok ? Theme.Good : Theme.Warn;
        _connection[index].Text.Text = text;
    }

    private void SetStatus(string text) => _status.Text = text;

    // ------------------------------------------------------------------------------------------
    // Playing on the pad
    // ------------------------------------------------------------------------------------------

    private bool RequireTransport()
    {
        if (_transport != null) return true;
        DetectDevices();
        if (_transport != null) return true;
        SetStatus("No trackpad driver found: connect the pad and press Refresh on Start here.");
        return false;
    }

    /// <summary>Fires pulses (already scaled) with the working gap between their starts.</summary>
    private void FirePulses(IReadOnlyList<Pulse> pulses)
    {
        var sw = Stopwatch.StartNew();
        for (int i = 0; i < pulses.Count; i++)
        {
            if (i > 0)
            {
                var wait = i * _work.PulseGapMs - (int)sw.ElapsedMilliseconds;
                if (wait > 0) Thread.Sleep(wait);
            }
            _transport!.Fire(pulses[i]);
        }
    }

    private Task PlayChainAsync(string label, string chain, int intensity, int times = 1, Action? flash = null) =>
        PlaySequenceAsync(times == 1 ? label : $"{label} x{times}",
            Enumerable.Repeat(new PlayItem(label, chain, intensity, flash), times).ToList(), 650);

    /// <summary>Plays items one after another. A newer play stops an older one between items.</summary>
    private async Task PlaySequenceAsync(string label, IReadOnlyList<PlayItem> items, int pauseMs)
    {
        if (!RequireTransport()) return;
        var scaled = new List<List<Pulse>>();
        try
        {
            foreach (var item in items) scaled.Add(_work.PulsesForTrigger(Pulse.ParseChain(item.Chain), item.Intensity));
        }
        catch (FormatException e) { SetStatus($"{label}: {e.Message}"); return; }

        int token = ++_playToken;
        _lastPlay = () => PlaySequenceAsync(label, items, pauseMs);
        _replay.Enabled = true;
        _replay.Text = $"Replay: {label}  (F5)";
        try
        {
            for (int i = 0; i < items.Count; i++)
            {
                if (i > 0) await Task.Delay(pauseMs);
                if (token != _playToken) return;
                items[i].Flash?.Invoke();
                FirePulses(scaled[i]);
                SetStatus(items.Count == 1
                    ? $"Played {items[i].Label} at {items[i].Intensity} %: {Pulse.FormatChain(scaled[i])}"
                    : $"{label}: {items[i].Label} at {items[i].Intensity} % ({i + 1} of {items.Count}): {Pulse.FormatChain(scaled[i])}");
            }
        }
        catch (Exception e) { SetStatus($"{label}: {e.Message}"); }
    }

    /// <summary>Sets the pad's own click for testing (Override on) and keeps a restore point.</summary>
    private void StartClickTest(string label, Pulse click, Pulse release)
    {
        if (!RequireTransport()) return;
        try
        {
            if (_session.PendingRestore == null)
            {
                _session.PendingRestore = DriverSettings.Snapshot();
                SaveSession();
            }
            DriverSettings.ApplyTestClick(click.Packed, release.Packed);
            _transport!.ReloadSettings();
            SetStatus($"Testing {label}: click {click}, release {release}. Click the trackpad a few times.");
        }
        catch (Exception e) { SetStatus($"{label}: {e.Message}"); }
        UpdateRestoreButton();
    }

    private void StopClickTest()
    {
        _activeTest = null;
        RefreshActiveTest();
        if (_session.PendingRestore == null) return;
        try
        {
            DriverSettings.Restore(_session.PendingRestore);
            _session.PendingRestore = null;
            SaveSession();
            if (_transport != null || RequireTransport()) _transport?.ReloadSettings();
            SetStatus("Your click settings are restored.");
        }
        catch (Exception e) { SetStatus("Restore failed: " + e.Message); }
        UpdateRestoreButton();
    }

    private void UpdateRestoreButton() => _restoreButton.Visible = _session.PendingRestore != null;

    private void TryClick(int level, int intensity)
    {
        var (click, release) = _work.ClickFromWindows(level + 1, intensity);
        StartClickTest($"{Levels[level]} at {intensity} %", click, release);
        if (_session.PendingRestore == null) return;
        _activeTest = (level, intensity);
        RefreshActiveTest();
        _forceScale.Flash();
        _clickColumns.Flash();
    }

    private void RefreshActiveTest()
    {
        _forceScale.Active = _activeTest?.Level ?? -1;
        _clickColumns.Active = _activeTest is { } t && t.Level == _feedbackTestLevel.SelectedIndex ? Array.IndexOf(Steps, t.Intensity) : -1;
        _forceScale.Invalidate();
        _clickColumns.Invalidate();
    }

    // ------------------------------------------------------------------------------------------
    // Navigation
    // ------------------------------------------------------------------------------------------

    private Stack? _navStack;

    private void BuildSidebar()
    {
        _navStack = new Stack { BackColor = Theme.Sidebar };
        _navStack.Add(new Label { Text = "HapticsLab", Font = Theme.H1, AutoSize = true, ForeColor = Theme.Text, Margin = new Padding(8, 0, 0, 0) });
        _navStack.Add(new Label { Text = "Magic Trackpad 2 haptics", Font = Theme.Body, AutoSize = true, ForeColor = Theme.Muted, Margin = new Padding(10, 0, 0, 18) });
        _sidebar.Controls.Add(_navStack);
        _sidebar.Controls.Add(_sidebarConnection);
    }

    private Stack NewPage(string key, string badge, string navTitle, string navSubtitle, string title, string subtitle)
    {
        var panel = new Panel { Dock = DockStyle.Fill, AutoScroll = true, BackColor = Theme.Back, Padding = new Padding(30, 22, 30, 24), Visible = false };
        var stack = new Stack { BackColor = Theme.Back };
        panel.Controls.Add(stack);
        stack.Add(new Label { Text = title, Font = Theme.H1, AutoSize = true, ForeColor = Theme.Text, Margin = new Padding(0, 0, 0, 2) });
        stack.Add(new WrapLabel(subtitle, Theme.Body, Theme.Muted) { Margin = new Padding(0, 0, 0, 16) });
        _content.Controls.Add(panel);

        var nav = new NavItem { Badge = badge, Title = navTitle, Subtitle = navSubtitle };
        int index = _pages.Count;
        nav.Click += (_, _) => ShowPage(index);
        _navStack!.Add(nav);
        _pages.Add(new PageEntry(key, navSubtitle, nav, panel));
        return stack;
    }

    private void ShowPage(int index)
    {
        for (int i = 0; i < _pages.Count; i++)
        {
            _pages[i].Panel.Visible = i == index;
            _pages[i].Nav.Selected = i == index;
            _pages[i].Nav.Invalidate();
        }
        if (_pages[index].Key == "driver") RefreshDriverTab();
        _pages[index].Panel.Focus();
    }

    private void ShowPage(string key) => ShowPage(_pages.FindIndex(p => p.Key == key));

    private void UpdateNav()
    {
        foreach (var page in _pages)
        {
            var nav = page.Nav;
            nav.SubtitleColor = null;
            nav.Done = false;
            if (Checklists.TryGetValue(page.Key, out var items))
            {
                int done = Enumerable.Range(0, items.Length).Count(i => _session.Checks.TryGetValue($"{page.Key}.{i}", out var v) && v);
                nav.Done = done == items.Length;
                nav.Subtitle = done == 0 ? page.DefaultSubtitle : $"{done} of {items.Length} feel right";
                if (done > 0 && !nav.Done) nav.SubtitleColor = Theme.Accent;
            }
            else if (page.Key == "save")
            {
                int changes = _work.DiffTo(_header).Count;
                nav.Subtitle = changes == 0 ? "No changes to write" : $"{changes} change{(changes == 1 ? "" : "s")} to write";
                if (changes > 0) nav.SubtitleColor = Theme.Warn;
            }
            else if (page.Key == "start")
            {
                nav.Subtitle = _transport == null ? "No trackpad found" : "Connected: " + _transport.Name.Split(' ')[0];
                nav.SubtitleColor = _transport == null ? Theme.Warn : Theme.Good;
            }
            nav.Invalidate();
        }
    }

    // ------------------------------------------------------------------------------------------
    // Page building blocks
    // ------------------------------------------------------------------------------------------

    private void BuildPages()
    {
        StartPage();
        ClickForcePage();
        ClickFeedbackPage();
        SignalsPage();
        StrengthPage();
        DriverPage();
        SavePage();
    }

    /// <summary>Two cards side by side: where Windows uses the values and how to test, and what to feel for.</summary>
    private void Intro(Stack page, string key, string usedFor, string how)
    {
        var used = new Card();
        used.Add(Ui.Heading("Where Windows uses this"));
        used.Add(new WrapLabel(usedFor));
        used.Add(Ui.Heading("How to test")).Margin = new Padding(0, 10, 0, 6);
        used.Add(new WrapLabel(how));
        page.Add(Ui.Columns(used, Checklist(key)));
    }

    private Card Checklist(string key)
    {
        var card = new Card();
        card.Add(Ui.Heading("What to feel for"));
        card.Add(new WrapLabel("Tick a point once it feels right. What stays unticked is what is left to tune.", Theme.Small, Theme.Muted));
        var items = Checklists[key];
        for (int i = 0; i < items.Length; i++)
        {
            var id = $"{key}.{i}";
            var row = new TableLayoutPanel { ColumnCount = 2, AutoSize = true, Margin = new Padding(0, 1, 0, 1) };
            row.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            row.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            var box = new CheckBox { AutoSize = true, Checked = _session.Checks.TryGetValue(id, out var v) && v, Margin = new Padding(0, 4, 4, 0), Cursor = Cursors.Hand };
            var text = new WrapLabel(items[i]) { Margin = new Padding(0, 2, 0, 2), Cursor = Cursors.Hand, ForeColor = box.Checked ? Theme.Muted : Theme.Text };
            text.Click += (_, _) => box.Checked = !box.Checked;
            box.CheckedChanged += (_, _) =>
            {
                _session.Checks[id] = box.Checked;
                text.ForeColor = box.Checked ? Theme.Muted : Theme.Text;
                Changed();
            };
            row.Controls.Add(box, 0, 0);
            row.Controls.Add(text, 1, 0);
            card.Add(row);
        }
        return card;
    }

    private Card NotesCard(string key, string title = "Notes")
    {
        var card = new Card();
        card.Add(Ui.Heading(title));
        card.Add(new WrapLabel("In your own words. Kept between sessions, and part of the feedback report on step 6.", Theme.Small, Theme.Muted));
        var box = new TextBox
        {
            Multiline = true, Height = 76, ScrollBars = ScrollBars.Vertical, Font = Theme.Body, BorderStyle = BorderStyle.FixedSingle,
            Text = _session.Notes.TryGetValue(key, out var n) ? n : "",
        };
        box.TextChanged += (_, _) => { _session.Notes[key] = box.Text; Changed(); };
        card.Add(box);
        return card;
    }

    private ChipGroup Rating(string id, params string[] options)
    {
        var chips = new ChipGroup(options, _session.Ratings.TryGetValue(id, out var r) ? r : null);
        chips.SelectionChanged += s =>
        {
            if (s == null) _session.Ratings.Remove(id); else _session.Ratings[id] = s;
            Changed();
        };
        return chips;
    }

    private Control StopTestingRow() => Ui.Row(
        Ui.Button("Stop testing and restore my click", (_, _) => StopClickTest()),
        Ui.Text("Your own settings also come back when you close HapticsLab.", Theme.Small, Theme.Muted));

    // ------------------------------------------------------------------------------------------
    // Start here
    // ------------------------------------------------------------------------------------------

    private void StartPage()
    {
        var page = NewPage("start", "i", "Start here", "Connection and overview",
            "Tune how the trackpad feels",
            "The driver makes the Magic Trackpad 2 a Windows haptic touchpad. Every value that decides how it feels lives in one table in " +
            "common\\MagicTrackpad2Haptics.h. Here you try other values on the pad, rate them, and write your picks back to that table.");

        var connection = new Card();
        connection.Add(Ui.Heading("Connection"));
        for (int i = 0; i < 4; i++)
        {
            var row = new TableLayoutPanel { ColumnCount = 2, AutoSize = true, Margin = new Padding(0, 1, 0, 1) };
            row.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            row.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            var dot = new Label { Text = "●", AutoSize = true, ForeColor = Theme.Muted, Margin = new Padding(0, 3, 4, 0) };
            var text = new WrapLabel("") { Margin = new Padding(0, 2, 0, 2) };
            row.Controls.Add(dot, 0, 0);
            row.Controls.Add(text, 1, 0);
            connection.Add(row);
            _connection.Add((dot, text));
        }
        connection.Add(Ui.Row(
            Ui.Button("Refresh", (_, _) => DetectDevices()),
            Ui.Button("Open calibration file folder", (_, _) => { if (_headerPath != null) Process.Start("explorer.exe", "/select,\"" + _headerPath + "\""); })));

        var legend = new Card();
        legend.Add(Ui.Heading("Reading the pictures"));
        legend.Add(new WrapLabel("Every haptic on this pad is a pulse of three bytes, S0,S1,S2 in hex. Each picture shows them as three bars:", Theme.Body, Theme.Muted));
        legend.Add(new GlyphLegend());
        legend.Add(new WrapLabel("Pulses can be chained with +. Grow, for example, is a weak pulse followed by a firm one, started this far apart:", Theme.Body, Theme.Muted));
        legend.Add(_legendChain);
        page.Add(Ui.Columns(connection, legend));

        var steps = new Card();
        steps.Add(Ui.Heading("The steps, in the order the values are used"));
        steps.Add(new WrapLabel("Rest a finger lightly on the trackpad. On each step, read what to feel for first, then try values. " +
            "Tick what feels right, rate what doesn't, and write notes; everything is kept between sessions. When you are happy, write to the " +
            "header on step 6, rebuild and reinstall the driver, and confirm on step 5. Press F5 (or Space) to replay the last pulse.", Theme.Body, Theme.Muted));
        steps.Add(new WrapLabel("Testing a pull request? Go through steps 1 to 5, then use \"Copy feedback report\" on step 6 and paste it as a comment " +
            "on the pull request. You don't need to write to the header.", Theme.Body, Theme.Text));
        var grid = new TableLayoutPanel { ColumnCount = 2, AutoSize = true, Margin = new Padding(0, 6, 0, 0) };
        grid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
        grid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
        var tiles = new (string Key, string Badge, string Title, string Sub)[]
        {
            ("clickForce", "1", "Click force", "Settings: Click sensitivity (Light / Medium / Heavy)"),
            ("clickFeedback", "2", "Click feedback", "Settings: Haptic click slider"),
            ("signals", "3", "Haptic signals", "The waveforms Windows plays: snapping, previews"),
            ("signalStrength", "4", "Signal strength", "Settings: Haptic signals slider"),
            ("driver", "5", "Driver check", "What the installed driver really does"),
            ("save", "6", "Save to header", "Review, write, and share feedback"),
        };
        for (int i = 0; i < tiles.Length; i++)
        {
            var t = tiles[i];
            var tile = new NavItem { Badge = t.Badge, Title = t.Title, Subtitle = t.Sub, Dock = DockStyle.Fill, BackColor = Theme.Card };
            tile.Click += (_, _) => ShowPage(t.Key);
            grid.Controls.Add(tile, i % 2, i / 2);
        }
        steps.Add(grid);
        page.Add(steps);
    }

    // ------------------------------------------------------------------------------------------
    // 1 Click force
    // ------------------------------------------------------------------------------------------

    private void ClickForcePage()
    {
        var page = NewPage("clickForce", "1", "Click force", "Settings: Click sensitivity",
            "Click force", "How hard you press before the trackpad clicks, for Light, Medium and Heavy.");
        Intro(page, "clickForce",
            "Settings > Bluetooth & devices > Touchpad > Click sensitivity (Light / Medium / Heavy). The driver turns the level into S0 of the pad's " +
            "own click and of the release. S0 decides how hard you press, and also adds some strength to the click.",
            "Click a level on the scale (or Try), then click the trackpad several times. Try another level and compare. HapticsLab changes your " +
            "click temporarily; your own setting comes back when you stop testing or close the app.");

        var card = new Card();
        card.Add(Ui.Heading("The three levels"));
        _forceScale.LevelClicked += level => TryClick(level, (int)_forceTestIntensity.SelectedItem!);
        card.Add(_forceScale);

        var grid = new TableLayoutPanel { ColumnCount = 6, AutoSize = true, Margin = new Padding(0, 6, 0, 6) };
        for (int c = 0; c < 5; c++) grid.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        grid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        string[] heads = { "Level", "Press S0", "Release S0", "", "Bytes sent to the pad", "How does it feel?" };
        for (int c = 0; c < heads.Length; c++) grid.Controls.Add(Ui.Text(heads[c], Theme.Strong, Theme.Muted), c, 0);
        for (int i = 0; i < 3; i++)
        {
            int level = i;
            _pressS0[i] = Ui.Hex(0xFF); _releaseS0[i] = Ui.Hex(0xFF);
            _pressS0[i].ValueChanged += (_, _) => { _work.PressS0[level] = (byte)_pressS0[level].Value; Changed(); };
            _releaseS0[i].ValueChanged += (_, _) => { _work.ReleaseS0[level] = (byte)_releaseS0[level].Value; Changed(); };
            _forceResult[i] = Ui.Text("", Theme.MonoSmall, Theme.Muted);
            grid.Controls.Add(Ui.Text(Levels[i], Theme.Strong), 0, i + 1);
            grid.Controls.Add(_pressS0[i], 1, i + 1);
            grid.Controls.Add(_releaseS0[i], 2, i + 1);
            grid.Controls.Add(Ui.Button("Try " + Levels[i], (_, _) => TryClick(level, (int)_forceTestIntensity.SelectedItem!)), 3, i + 1);
            grid.Controls.Add(_forceResult[i], 4, i + 1);
            grid.Controls.Add(Rating($"clickForce.{i}", "Too light", "Just right", "Too heavy"), 5, i + 1);
        }
        card.Add(grid);
        _forceTestIntensity.SelectedIndexChanged += (_, _) => RefreshDerived();
        card.Add(Ui.Row(Ui.Text("Haptic click during the test:"), _forceTestIntensity, Ui.Text("%", null, Theme.Muted),
            Ui.Text("   (how loud the click is; tuned on step 2)", Theme.Small, Theme.Muted)));
        card.Add(StopTestingRow());
        page.Add(card);
        page.Add(NotesCard("clickForce"));
    }

    // ------------------------------------------------------------------------------------------
    // 2 Click feedback
    // ------------------------------------------------------------------------------------------

    private void ClickFeedbackPage()
    {
        var page = NewPage("clickFeedback", "2", "Click feedback", "Settings: Haptic click",
            "Click feedback", "How strong and loud each click feels at the four Haptic click steps, without changing how hard you press.");
        Intro(page, "clickFeedback",
            "Settings > Bluetooth & devices > Touchpad > Haptic click (a slider; Windows sends 25, 50, 75 or 100). The driver turns it into S1 = S2 " +
            "of the pad's own click: louder and stronger, while the force stays the same. From a chosen step on, the release gets its own small tap.",
            "Pick a click sensitivity, click a step in the picture to make it the trackpad's click, and click the pad several times. Go through the steps in order.");

        var values = new Card();
        values.Add(Ui.Heading("Values"));
        values.Add(Ui.Field("Click S1 = S2 at 100 %", _pressS12, "hex", "Scales linearly below 100 %; 0 is a quiet click."));
        values.Add(Ui.Field("Release S1 = S2 when strong", _releaseS12, "hex", "0 gives a release you feel but barely hear."));
        values.Add(Ui.Field("The release is strong from this step on", _releaseFrom, "%", "Below it, the release has S1 = S2 = 0."));
        _pressS12.ValueChanged += (_, _) => { _work.PressS12AtMax = (int)_pressS12.Value; Changed(); };
        _releaseS12.ValueChanged += (_, _) => { _work.ReleaseS12Strong = (int)_releaseS12.Value; Changed(); };
        _releaseFrom.ValueChanged += (_, _) => { _work.ReleaseStrongIntensity = (int)_releaseFrom.Value; Changed(); };
        page.Add(values);

        var tryCard = new Card();
        tryCard.Add(Ui.Heading("Try each step"));
        tryCard.Add(Ui.Row(Ui.Text("Click sensitivity during the test:"), _feedbackTestLevel,
            Ui.Text("   Click a step below to make it the trackpad's click.", Theme.Small, Theme.Muted)));
        _feedbackTestLevel.SelectedIndexChanged += (_, _) => { RefreshDerived(); RefreshActiveTest(); };
        _clickColumns.ColumnClicked += i => TryClick(_feedbackTestLevel.SelectedIndex, Steps[i]);
        tryCard.Add(_clickColumns);

        var ratings = new TableLayoutPanel { ColumnCount = 2, AutoSize = true, Margin = new Padding(0, 8, 0, 4) };
        ratings.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 70));
        ratings.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        for (int i = 0; i < Steps.Length; i++)
        {
            ratings.Controls.Add(Ui.Text(StepHeaders[i], Theme.Strong), 0, i);
            ratings.Controls.Add(Rating($"clickFeedback.{Steps[i]}", "Too quiet", "Just right", "Too loud", "Same as the step before"), 1, i);
        }
        tryCard.Add(ratings);
        tryCard.Add(StopTestingRow());
        page.Add(tryCard);
        page.Add(NotesCard("clickFeedback"));
    }

    // ------------------------------------------------------------------------------------------
    // 3 Haptic signals
    // ------------------------------------------------------------------------------------------

    private void SignalsPage()
    {
        var page = NewPage("signals", "3", "Haptic signals", "Snapping, slider previews",
            "Haptic signals", "The short signals Windows and apps play. Each one should be recognisable on its own, not just stronger or weaker.");
        var split = new TableLayoutPanel { ColumnCount = 2, AutoSize = true, Margin = Padding.Empty };
        split.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 290));
        split.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        var list = new Card { Margin = new Padding(0, 0, 14, 14) };
        list.Add(Ui.Heading("Signals"));
        list.Add(Ui.Row(Ui.Text("Play at"), _signalsIntensity, Ui.Text("%", null, Theme.Muted)));
        list.Add(Ui.Button("▶ Play all picks in order", (_, _) => _ = PlaySequenceAsync("All picks",
            Calibration.WaveformOrder.Select(k => new PlayItem(WaveformInfo[k].Title, Pulse.FormatChain(_work.Get(k).Pulses),
                (int)_signalsIntensity.SelectedItem!, () => _sections[k].Nav.Flash())).ToList(), 1000))).Margin = new Padding(0, 2, 0, 10);
        var details = new Stack { BackColor = Theme.Back };
        foreach (var key in Calibration.WaveformOrder)
        {
            var nav = new NavItem { Title = WaveformInfo[key].Title, Badge = WaveformInfo[key].Title[..1] };
            nav.Click += (_, _) => ShowWaveform(key);
            list.Add(nav);
            details.Add(WaveformDetail(key, nav));
        }
        list.Dock = DockStyle.Top;
        split.Controls.Add(list, 0, 0);
        details.Dock = DockStyle.Top;
        split.Controls.Add(details, 1, 0);
        page.Add(split);

        Intro(page, "signals",
            "Windows plays Collide when a window snaps and Step as the preview of the \"Haptic signals\" slider; apps can play the others. " +
            "The driver fires the pulses above, scaled by the Haptic signals level (step 4).",
            "Pick a signal on the left. Play its candidates (edit them freely: S0,S1,S2 in hex, chain pulses with +) and pick the one you prefer; " +
            "your pick becomes the working value. Use Compare to play two signals back to back.");
    }

    private Card WaveformDetail(string key, NavItem nav)
    {
        var info = WaveformInfo[key];
        var card = new Card();
        var section = new WaveformSection { Key = key, Nav = nav, Detail = card };
        card.Add(new Label { Text = info.Title, Font = Theme.H2, AutoSize = true, Margin = new Padding(0, 0, 0, 4) });
        card.Add(new WrapLabel("Used for: " + info.Use, Theme.Body, Theme.Muted));
        var focus = new Card(Theme.AccentSoft, Theme.AccentSoft) { Padding = new Padding(12, 8, 12, 8), Margin = new Padding(0, 4, 0, 10) };
        focus.Add(new WrapLabel("Feel for: " + info.Focus, Theme.Strong, Theme.Text) { Margin = Padding.Empty });
        card.Add(focus);

        const int rows = 4;
        var grid = new TableLayoutPanel { ColumnCount = 5, AutoSize = true, Margin = new Padding(0, 0, 0, 6) };
        for (int c = 0; c < 4; c++) grid.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        grid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        section.Picks = new RadioButton[rows];
        section.Candidates = new TextBox[rows];
        section.Views = new PulseChainView[rows];

        var saved = _session.Candidates.TryGetValue(key, out var list) ? list : null;
        var current = Pulse.FormatChain(_work.Get(key).Pulses);
        var defaults = new List<string> { current };
        defaults.AddRange(info.Alternatives.Where(a => a != current));

        for (int i = 0; i < rows; i++)
        {
            int index = i;
            var pick = new RadioButton { Text = i == 0 ? "Pick" : $"Pick {i + 1}", AutoSize = true, Margin = new Padding(0, 14, 8, 0), Cursor = Cursors.Hand };
            var text = new TextBox
            {
                Font = Theme.Mono, Width = 190, Margin = new Padding(0, 11, 8, 0), BorderStyle = BorderStyle.FixedSingle,
                Text = saved != null && i < saved.Count ? saved[i] : i < defaults.Count ? defaults[i] : "",
            };
            var view = new PulseChainView { Size = new Size(140, 48), Margin = new Padding(0, 0, 8, 0), Labels = false };
            view.SetChain(text.Text);
            var play = Ui.Button("▶ Play", (_, _) => _ = PlayChainAsync($"{info.Title} #{index + 1}", text.Text, (int)_signalsIntensity.SelectedItem!, 1, view.Flash));
            var play3 = Ui.Button("×3", (_, _) => _ = PlayChainAsync($"{info.Title} #{index + 1}", text.Text, (int)_signalsIntensity.SelectedItem!, 3, view.Flash));
            play.Margin = new Padding(0, 10, 6, 0);
            play3.Margin = new Padding(0, 10, 0, 0);
            pick.CheckedChanged += (_, _) => { if (pick.Checked) PickWaveform(section, index); };
            text.TextChanged += (_, _) => { view.SetChain(text.Text); if (pick.Checked) PickWaveform(section, index); };
            grid.Controls.Add(pick, 0, i);
            grid.Controls.Add(text, 1, i);
            grid.Controls.Add(view, 2, i);
            grid.Controls.Add(play, 3, i);
            grid.Controls.Add(play3, 4, i);
            section.Picks[i] = pick; section.Candidates[i] = text; section.Views[i] = view;
        }
        card.Add(grid);

        section.Current = Ui.Text("", Theme.Small, Theme.Muted);
        card.Add(section.Current);

        var compare = Ui.Combo(120);
        foreach (var other in Calibration.WaveformOrder.Where(k => k != key)) compare.Items.Add(WaveformInfo[other].Title);
        compare.SelectedItem = WaveformInfo[info.Compare].Title;
        card.Add(Ui.Row(Ui.Text("Compare your pick with"), compare,
            Ui.Button("▶ Play mine, then that one", (_, _) =>
            {
                var otherKey = Calibration.WaveformOrder.First(k => WaveformInfo[k].Title == (string)compare.SelectedItem!);
                int level = (int)_signalsIntensity.SelectedItem!;
                var mine = section.Views[Math.Max(0, Array.FindIndex(section.Picks, r => r.Checked))];
                _ = PlaySequenceAsync($"{info.Title} vs {WaveformInfo[otherKey].Title}", new List<PlayItem>
                {
                    new(info.Title, Pulse.FormatChain(_work.Get(key).Pulses), level, mine.Flash),
                    new(WaveformInfo[otherKey].Title, Pulse.FormatChain(_work.Get(otherKey).Pulses), level, _sections[otherKey].Nav.Flash),
                }, 800);
            })));

        card.Add(Ui.Text("How does your pick feel?", Theme.Strong)).Margin = new Padding(0, 10, 0, 2);
        card.Add(Rating("waveform." + key, SignalRatings));

        card.Add(Ui.Text("Notes", Theme.Strong)).Margin = new Padding(0, 10, 0, 2);
        var notes = new TextBox
        {
            Multiline = true, Height = 56, ScrollBars = ScrollBars.Vertical, BorderStyle = BorderStyle.FixedSingle,
            Text = _session.Notes.TryGetValue("waveform." + key, out var n) ? n : "",
        };
        notes.TextChanged += (_, _) => { _session.Notes["waveform." + key] = notes.Text; Changed(); };
        card.Add(notes);

        _sections[key] = section;
        // Select the saved pick, or the candidate equal to the working value
        _loading = true;
        int picked = _session.Picked.TryGetValue(key, out var p) && p >= 0 ? p
            : Math.Max(0, Array.FindIndex(section.Candidates, t => t.Text.Trim() == current));
        section.Picks[Math.Min(picked, rows - 1)].Checked = true;
        _loading = false;
        return card;
    }

    private void ShowWaveform(string key)
    {
        foreach (var (k, s) in _sections)
        {
            s.Detail.Visible = k == key;
            s.Nav.Selected = k == key;
            s.Nav.Invalidate();
        }
    }

    private void PickWaveform(WaveformSection section, int index)
    {
        if (_loading) return;
        try
        {
            var pulses = Pulse.ParseChain(section.Candidates[index].Text);
            var w = _work.Get(section.Key);
            w.Pulses = pulses;
            if (section.Key is not "PRESS" and not "RELEASE")
                w.DurationMs = Calibration.DurationFor(pulses.Count, _work.PulseGapMs);
            Changed();
        }
        catch (FormatException e) { SetStatus($"{section.Key}: {e.Message}"); }
    }

    // ------------------------------------------------------------------------------------------
    // 4 Signal strength
    // ------------------------------------------------------------------------------------------

    private void StrengthPage()
    {
        var page = NewPage("signalStrength", "4", "Signal strength", "Settings: Haptic signals",
            "Signal strength", "How every signal scales with the Haptic signals slider, from 25 % to 100 %.");
        Intro(page, "signalStrength",
            "Settings > Bluetooth & devices > Touchpad > Haptic signals (a slider; Windows sends 25, 50, 75 or 100). It scales every signal from " +
            "step 3: S1 and S2 linearly, S0 from the minimum below up to 100 %, never below the floor. Grow's pulses start the gap below apart.",
            "Click a cell to play that signal at that level; click a signal's name to play all four levels, rising. Then change the values and listen again.");

        var values = new Card();
        values.Add(Ui.Heading("Values"));
        values.Add(Ui.Field("S0 at the lowest level", _s0MinPercent, "%", "Percent of the signal's own S0. Higher: weak levels stay stronger."));
        values.Add(Ui.Field("S0 never below", _s0Floor, "hex", "The weakest pulse you still feel."));
        values.Add(Ui.Field("Gap between chained pulses", _gap, "ms", "Start to start. Shorter feels like one pulse, longer like separate taps."));
        _s0MinPercent.ValueChanged += (_, _) => { _work.PulseS0MinPercent = (int)_s0MinPercent.Value; Changed(); };
        _s0Floor.ValueChanged += (_, _) => { _work.PulseS0Floor = (int)_s0Floor.Value; Changed(); };
        _gap.ValueChanged += (_, _) =>
        {
            _work.PulseGapMs = (int)_gap.Value;
            foreach (var w in _work.Waveforms.Where(w => w.Key is not "PRESS" and not "RELEASE"))
                w.DurationMs = Calibration.DurationFor(w.Pulses.Count, _work.PulseGapMs);
            Changed();
        };
        page.Add(values);

        var card = new Card();
        card.Add(Ui.Heading("Every signal at every level"));
        card.Add(new WrapLabel("Hover a cell for its exact bytes.", Theme.Small, Theme.Muted));
        _matrix.CellClicked += (r, c) =>
        {
            var key = Calibration.WaveformOrder[r];
            _ = PlayChainAsync(WaveformInfo[key].Title, Pulse.FormatChain(_work.Get(key).Pulses), Steps[c], 1, () => _matrix.FlashCell(r, c));
        };
        _matrix.RowClicked += r =>
        {
            var key = Calibration.WaveformOrder[r];
            _ = PlaySequenceAsync($"{WaveformInfo[key].Title} rising", Steps.Select((s, c) =>
                new PlayItem(WaveformInfo[key].Title, Pulse.FormatChain(_work.Get(key).Pulses), s, () => _matrix.FlashCell(r, c))).ToList(), 900);
        };
        card.Add(_matrix);
        page.Add(card);
        page.Add(NotesCard("signalStrength"));
    }

    // ------------------------------------------------------------------------------------------
    // 5 Driver check
    // ------------------------------------------------------------------------------------------

    private void DriverPage()
    {
        var page = NewPage("driver", "5", "Driver check", "The installed driver",
            "Driver check", "What the installed driver does, through the same HID reports Windows uses. It uses the values compiled into the driver, not your working values.");
        Intro(page, "driver",
            "This is exactly what Windows does: it sets the click sensitivity and haptic click with feature reports, and fires signals with an output report. " +
            "After writing to the header on step 6, rebuild and reinstall the driver, then check here.",
            "Fire each signal and compare it with your pick on step 3. Set the click like Windows and click the pad. Windows sends its own values " +
            "again when you change them in Settings or reconnect the trackpad.");

        var windows = new Card();
        windows.Add(Ui.Heading("Click settings in the driver"));
        windows.Add(_driverValues);
        windows.Add(Ui.Row(Ui.Text("Set like Windows: click sensitivity"), _driverSetThreshold, Ui.Text("haptic click"), _driverSetIntensity, Ui.Text("%", null, Theme.Muted),
            Ui.Button("Set", (_, _) => DriverSet(), true), Ui.Button("Read again", (_, _) => RefreshDriverTab())));
        page.Add(windows);

        var card = new Card();
        card.Add(Ui.Heading("Signals in the installed driver"));
        card.Add(Ui.Row(Ui.Text("Fire at Haptic signals level:"), _driverIntensity, Ui.Text("%", null, Theme.Muted)));
        var grid = new TableLayoutPanel { ColumnCount = 5, AutoSize = true, Margin = new Padding(0, 6, 0, 6) };
        for (int c = 0; c < 4; c++) grid.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        grid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        string[] heads = { "", "Installed driver", "Header file", "", "" };
        for (int c = 0; c < heads.Length; c++) grid.Controls.Add(Ui.Text(heads[c], Theme.Strong, Theme.Muted), c, 0);
        for (int i = 0; i < Calibration.WaveformOrder.Length; i++)
        {
            int ordinal = i + 3;
            var title = WaveformInfo[Calibration.WaveformOrder[i]].Title;
            var fire = Ui.Button("▶ " + title, (_, _) => DriverFire(ordinal, title));
            fire.MinimumSize = new Size(126, 0);
            grid.Controls.Add(fire, 0, i + 1);
            grid.Controls.Add(_drvInstalled[i] = Ui.Text("", Theme.MonoSmall), 1, i + 1);
            grid.Controls.Add(_drvHeader[i] = Ui.Text("", Theme.MonoSmall), 2, i + 1);
            grid.Controls.Add(_drvStatus[i] = Ui.Pill("", Theme.Muted, Theme.Back), 3, i + 1);
            grid.Controls.Add(new Label { AutoSize = true }, 4, i + 1);
        }
        card.Add(grid);
        card.Add(new WrapLabel("The driver only reports each signal's ID and duration; the bytes themselves can't be read back, so feel them.", Theme.Small, Theme.Muted));
        page.Add(card);
    }

    private void RefreshDriverTab()
    {
        if (_drvInstalled[0] == null) return;
        if (_hid == null || !_hid.HasHaptics)
        {
            _driverValues.Text = _hid == null ? "Touch pad collection not found. Connect the trackpad and press Refresh on Start here."
                                              : "The installed driver has no haptic support.";
            for (int i = 0; i < _drvInstalled.Length; i++)
            {
                _drvInstalled[i].Text = "—";
                _drvHeader[i].Text = $"{Pulse.FormatChain(_header.Get(Calibration.WaveformOrder[i]).Pulses)}, {_header.Get(Calibration.WaveformOrder[i]).DurationMs} ms";
                SetPill(_drvStatus[i], "", Theme.Muted, Theme.Card);
            }
            return;
        }
        try
        {
            _driverValues.Text = $"The driver currently uses: click sensitivity {Levels[Math.Clamp(_hid.Threshold - 1, 0, 2)]} ({_hid.Threshold}), haptic click {_hid.Intensity} %.";
            var list = _hid.WaveformList(Calibration.WaveformOrder.Length);
            for (int i = 0; i < Calibration.WaveformOrder.Length; i++)
            {
                var key = Calibration.WaveformOrder[i];
                var h = _header.Get(key);
                var (id, ms) = list[i];
                bool same = ms == h.DurationMs && id == WaveformId(key);
                _drvInstalled[i].Text = $"0x{id:X4}, {ms} ms";
                _drvHeader[i].Text = $"{Pulse.FormatChain(h.Pulses)}, {h.DurationMs} ms";
                if (same) SetPill(_drvStatus[i], "same", Theme.Good, Theme.GoodSoft);
                else SetPill(_drvStatus[i], "different: rebuild and reinstall", Theme.Bad, Theme.BadSoft);
            }
        }
        catch (Exception e) { _driverValues.Text = "Reading the driver failed: " + e.Message; }
    }

    private static void SetPill(Label pill, string text, Color fore, Color back)
    {
        pill.Text = text;
        pill.ForeColor = fore;
        pill.BackColor = back;
        pill.Visible = text.Length > 0;
    }

    private static ushort WaveformId(string key) => key switch
    {
        "PRESS" => 0x1006, "RELEASE" => 0x1007, "HOVER" => 0x1008, "COLLIDE" => 0x1012,
        "ALIGN" => 0x1013, "STEP" => 0x1014, "GROW" => 0x1015, _ => 0,
    };

    private void DriverFire(int ordinal, string title)
    {
        if (_hid == null) { SetStatus("Touch pad collection not found."); return; }
        int intensity = (int)_driverIntensity.SelectedItem!;
        Task Fire()
        {
            try
            {
                _hid!.Trigger(ordinal, intensity);
                SetStatus($"The driver fired {title} at {intensity} %");
            }
            catch (Exception e) { SetStatus($"{title}: {e.Message}"); }
            return Task.CompletedTask;
        }
        _lastPlay = Fire;
        _replay.Enabled = true;
        _replay.Text = $"Replay: driver {title}  (F5)";
        _ = Fire();
    }

    private void DriverSet()
    {
        if (_hid == null) { SetStatus("Touch pad collection not found."); return; }
        try
        {
            _hid.SetThreshold(_driverSetThreshold.SelectedIndex + 1);
            _hid.SetIntensity((int)_driverSetIntensity.Value);
            SetStatus("Set like Windows. Click the pad to feel it.");
            RefreshDriverTab();
        }
        catch (Exception e) { SetStatus("Set failed: " + e.Message); }
    }

    // ------------------------------------------------------------------------------------------
    // 6 Save to header
    // ------------------------------------------------------------------------------------------

    private void SavePage()
    {
        var page = NewPage("save", "6", "Save to header", "Write your values",
            "Save to header", "Review your working values, write them to the calibration table, and share what you felt.");

        var changes = new Card();
        changes.Add(Ui.Heading("Changes compared with the header"));
        changes.Add(_changes);
        changes.Add(Ui.Row(
            Ui.Button("Write to header", (_, _) => WriteHeader(), true),
            Ui.Button("Reset working values to the header", (_, _) => ResetToHeader()),
            Ui.Button("Save session now", (_, _) => { SaveSession(); SetStatus("Session saved: " + _sessionPath); })));
        changes.Add(new WrapLabel("Writing replaces only the calibration table section of common\\MagicTrackpad2Haptics.h and keeps a .bak copy. " +
            "Then rebuild both drivers, reinstall, and confirm on step 5.", Theme.Small, Theme.Muted));
        page.Add(changes);

        var feedback = new Card();
        feedback.Add(Ui.Heading("Your feedback"));
        feedback.Add(_summary);
        feedback.Add(Ui.Row(
            Ui.Button("Copy feedback report", (_, _) => { Clipboard.SetText(BuildReport()); SetStatus("Feedback report copied: paste it as a comment on the pull request."); }, true),
            Ui.Button("Save report as file", (_, _) => SaveReport())));
        feedback.Add(new WrapLabel("The report is Markdown: your PC (Windows build, trackpad, driver version), changed values, ratings, ticks and notes. " +
            "Paste it as a comment on the pull request, or attach the file. Apart from your notes, nothing personal is in it.", Theme.Small, Theme.Muted));
        page.Add(feedback);

        var section = new Card();
        section.Add(Ui.Heading("Calibration table section that will be written"));
        section.Add(_preview);
        section.Add(Ui.Row(Ui.Button("Copy section", (_, _) => { Clipboard.SetText(_preview.Text); SetStatus("Section copied."); })));
        page.Add(section);
    }

    private void WriteHeader()
    {
        if (_headerPath == null)
        {
            SetStatus("HapticsLab is using its built-in table, so there is no header to write. Copy the feedback report instead.");
            return;
        }
        var diff = _work.DiffTo(_header);
        if (diff.Count == 0) { SetStatus("Nothing to write: the working values equal the header."); return; }
        if (MessageBox.Show(this, $"Write {diff.Count} change(s) to\n{_headerPath}?\n\nA backup is saved as .bak. Rebuild and reinstall the drivers afterwards.",
                Text, MessageBoxButtons.OKCancel, MessageBoxIcon.Question) != DialogResult.OK) return;
        try
        {
            var text = File.ReadAllText(_headerPath);
            File.WriteAllText(_headerPath + ".bak", text);
            File.WriteAllText(_headerPath, _work.ReplaceSection(text));
            LoadHeader(_headerPath);
            RefreshDerived();
            SetStatus("Written. Rebuild and reinstall the drivers, then check on step 5.");
        }
        catch (Exception e) { SetStatus("Write failed: " + e.Message); }
    }

    private void ResetToHeader()
    {
        if (MessageBox.Show(this, "Discard your working values and use the header's values again? Candidates, ratings and notes are kept.",
                Text, MessageBoxButtons.OKCancel, MessageBoxIcon.Question) != DialogResult.OK) return;
        _work = _header.Clone();
        LoadValuesIntoControls();
        Changed();
    }

    /// <summary>Ratings that were given, grouped by step, in reading order.</summary>
    private IEnumerable<(string Step, string What, string Rating)> RatingLines()
    {
        for (int i = 0; i < 3; i++)
            if (_session.Ratings.TryGetValue($"clickForce.{i}", out var r)) yield return ("Click force", Levels[i], r);
        foreach (var s in Steps)
            if (_session.Ratings.TryGetValue($"clickFeedback.{s}", out var r)) yield return ("Click feedback", $"{s} %", r);
        foreach (var key in Calibration.WaveformOrder)
            if (_session.Ratings.TryGetValue("waveform." + key, out var r)) yield return ("Haptic signals", WaveformInfo[key].Title, r);
    }

    /// <summary>What the installed driver reports through HID, in one line for the report.</summary>
    private string DriverSummary()
    {
        if (_hid == null) return "touch pad collection not found";
        if (!_hid.HasHaptics) return "none (the installed driver has no Windows haptics)";
        try
        {
            var list = _hid.WaveformList(Calibration.WaveformOrder.Length);
            var different = Calibration.WaveformOrder
                .Where((key, i) => list[i].DurationMs != _header.Get(key).DurationMs || list[i].Id != WaveformId(key))
                .Select(key => WaveformInfo[key].Title).ToList();
            return $"click sensitivity {_hid.Threshold}, haptic click {_hid.Intensity} %; signals " +
                   (different.Count == 0 ? "match the header" : "differ from the header: " + string.Join(", ", different));
        }
        catch (Exception e) { return "reading failed: " + e.Message; }
    }

    private void SaveReport()
    {
        using var dialog = new SaveFileDialog
        {
            FileName = $"HapticsLab-feedback-{DateTime.Now:yyyy-MM-dd}.md",
            Filter = "Markdown (*.md)|*.md|All files (*.*)|*.*",
        };
        if (dialog.ShowDialog(this) != DialogResult.OK) return;
        try
        {
            File.WriteAllText(dialog.FileName, BuildReport());
            SetStatus("Feedback report saved: " + dialog.FileName);
        }
        catch (Exception e) { SetStatus("Saving the report failed: " + e.Message); }
    }

    private string BuildReport()
    {
        var sb = new StringBuilder();
        sb.AppendLine("## HapticsLab feedback");
        sb.AppendLine();
        sb.AppendLine($"- Date: {DateTime.Now:yyyy-MM-dd}");
        sb.AppendLine($"- Windows: {SystemInfo.WindowsVersion()}");
        sb.AppendLine($"- Windows haptic touchpad settings: {SystemInfo.WindowsHapticSettings()}");
        var pads = SystemInfo.Trackpads();
        sb.AppendLine($"- Trackpad: {(pads.Count == 0 ? "none found" : string.Join("; ", pads))}");
        var drivers = SystemInfo.InstalledDrivers();
        sb.AppendLine($"- Driver: {(drivers.Count == 0 ? "not found" : string.Join("; ", drivers))}");
        sb.AppendLine($"- HapticsLab connection: {_transport?.Name ?? "none"}");
        sb.AppendLine($"- Driver haptics: {DriverSummary()}");
        sb.AppendLine();
        sb.AppendLine("### Changes from the header");
        var diff = _work.DiffTo(_header);
        if (diff.Count == 0) sb.AppendLine("None.");
        foreach (var d in diff) sb.AppendLine("- " + d);
        sb.AppendLine();
        sb.AppendLine("### Ratings");
        var ratings = RatingLines().ToList();
        if (ratings.Count == 0) sb.AppendLine("None given.");
        foreach (var (step, what, rating) in ratings) sb.AppendLine($"- {step}, {what}: {rating}");
        sb.AppendLine();
        sb.AppendLine("### What feels right");
        foreach (var (key, items) in Checklists)
        {
            sb.AppendLine($"**{_pages.First(p => p.Key == key).Nav.Title}**");
            for (int i = 0; i < items.Length; i++)
                sb.AppendLine($"- [{(_session.Checks.TryGetValue($"{key}.{i}", out var v) && v ? "x" : " ")}] {items[i]}");
            sb.AppendLine();
        }
        var notes = _session.Notes.Where(n => !string.IsNullOrWhiteSpace(n.Value)).ToList();
        if (notes.Count > 0)
        {
            sb.AppendLine("### Notes");
            foreach (var (key, text) in notes)
            {
                var name = key.StartsWith("waveform.") ? WaveformInfo.TryGetValue(key[9..], out var w) ? w.Title : key
                         : _pages.FirstOrDefault(p => p.Key == key)?.Nav.Title ?? key;
                sb.AppendLine($"**{name}:** {text.Trim().Replace("\r\n", " ").Replace("\n", " ")}");
                sb.AppendLine();
            }
        }
        sb.AppendLine("### Working calibration table");
        sb.AppendLine("```c");
        sb.Append(_work.GenerateSection("\n").Trim('\n'));
        sb.AppendLine();
        sb.AppendLine("```");
        return sb.ToString();
    }

    // ------------------------------------------------------------------------------------------
    // Keeping the controls and derived views in sync
    // ------------------------------------------------------------------------------------------

    private void LoadValuesIntoControls()
    {
        _loading = true;
        for (int i = 0; i < 3; i++) { _pressS0[i].Value = _work.PressS0[i]; _releaseS0[i].Value = _work.ReleaseS0[i]; }
        _pressS12.Value = _work.PressS12AtMax;
        _releaseS12.Value = _work.ReleaseS12Strong;
        _releaseFrom.Value = _work.ReleaseStrongIntensity;
        _s0MinPercent.Value = _work.PulseS0MinPercent;
        _s0Floor.Value = _work.PulseS0Floor;
        _gap.Value = Math.Clamp(_work.PulseGapMs, 20, 200);
        foreach (var (key, s) in _sections)
        {
            var current = Pulse.FormatChain(_work.Get(key).Pulses);
            int index = Array.FindIndex(s.Candidates, t => t.Text.Trim() == current);
            if (index < 0) { index = s.Candidates.Length - 1; s.Candidates[index].Text = current; }
            s.Picks[index].Checked = true;
        }
        _loading = false;
        RefreshDerived();
    }

    private void RefreshDerived()
    {
        int testIntensity = (int)(_forceTestIntensity.SelectedItem ?? 75);
        for (int i = 0; i < 3; i++)
        {
            if (_forceResult[i] == null) continue;
            var (c, r) = _work.ClickFromWindows(i + 1, testIntensity);
            _forceResult[i].Text = $"click {c}   release {r}";
        }
        _forceScale.Press = (byte[])_work.PressS0.Clone();
        _forceScale.Release = (byte[])_work.ReleaseS0.Clone();
        _forceScale.Invalidate();

        int level = Math.Max(0, _feedbackTestLevel.SelectedIndex);
        _clickColumns.Headers = Steps.Select(s => $"{Levels[level]} at {s} %").ToArray();
        _clickColumns.Values = Steps.Select(s => _work.ClickFromWindows(level + 1, s)).ToArray();
        _clickColumns.Invalidate();

        _matrix.GapMs = _work.PulseGapMs;
        _matrix.SetRows(Calibration.WaveformOrder.Select(key =>
            (WaveformInfo[key].Title, Steps.Select(s => _work.PulsesForTrigger(_work.Get(key).Pulses, s)).ToArray())));

        _legendChain.GapMs = _work.PulseGapMs;
        _legendChain.SetPulses(_work.Get("GROW").Pulses);

        foreach (var (key, s) in _sections)
        {
            var w = _work.Get(key);
            var chain = Pulse.FormatChain(w.Pulses);
            var header = Pulse.FormatChain(_header.Get(key).Pulses);
            s.Current.Text = $"Working value {chain} ({w.DurationMs} ms)" + (chain == header ? ", same as the header" : $", the header has {header}");
            foreach (var v in s.Views) { v.GapMs = _work.PulseGapMs; v.Invalidate(); }
            s.Nav.Preview = w.Pulses;
            s.Nav.Subtitle = _session.Ratings.TryGetValue("waveform." + key, out var rating) ? rating : chain;
            s.Nav.SubtitleColor = rating == null ? null : rating == "Just right" ? Theme.Good : Theme.Warn;
            s.Nav.Invalidate();
        }

        var diff = _work.DiffTo(_header);
        _changes.Text = diff.Count == 0 ? "No changes: the working values equal the header." : string.Join(Environment.NewLine, diff);
        _changes.ForeColor = diff.Count == 0 ? Theme.Muted : Theme.Text;

        var ratings = RatingLines().ToList();
        int ticked = Checklists.Sum(c => Enumerable.Range(0, c.Value.Length).Count(i => _session.Checks.TryGetValue($"{c.Key}.{i}", out var v) && v));
        int total = Checklists.Sum(c => c.Value.Length);
        _summary.Text = $"{ticked} of {total} points feel right. " +
            (ratings.Count == 0 ? "No ratings yet." : "Ratings: " + string.Join("; ", ratings.Select(r => $"{r.What} {r.Rating.ToLowerInvariant()}")) + ".");

        try { _preview.Text = _work.GenerateSection("\r\n"); } catch (Exception e) { _preview.Text = e.Message; }
        UpdateNav();
    }

    // ------------------------------------------------------------------------------------------
    // Screenshots (--screenshots <dir>): every page as a PNG, to check the layout without the pad
    // ------------------------------------------------------------------------------------------

    private async void TakeScreenshots()
    {
        Directory.CreateDirectory(_screenshotDir!);
        for (int i = 0; i < _pages.Count; i++)
        {
            ShowPage(i);
            await Task.Delay(400);
            using (var window = new Bitmap(Width, Height))
            {
                DrawToBitmap(window, new Rectangle(0, 0, Width, Height));
                window.Save(Path.Combine(_screenshotDir!, $"{i}-{_pages[i].Key}-window.png"));
            }
            var stack = _pages[i].Panel.Controls[0];
            using (var full = new Bitmap(stack.Width, stack.Height))
            {
                stack.DrawToBitmap(full, new Rectangle(0, 0, stack.Width, stack.Height));
                full.Save(Path.Combine(_screenshotDir!, $"{i}-{_pages[i].Key}-full.png"));
            }
        }
        File.WriteAllText(Path.Combine(_screenshotDir!, "feedback-report.md"), BuildReport());
        Close();
    }
}
