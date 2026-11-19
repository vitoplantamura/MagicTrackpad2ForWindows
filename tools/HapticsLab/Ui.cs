using System.Drawing.Drawing2D;

namespace HapticsLab;

/// <summary>Colors and fonts. S0/S1/S2 have one color each everywhere in the app.</summary>
internal static class Theme
{
    public static readonly Color Back = Color.FromArgb(244, 246, 249);
    public static readonly Color Sidebar = Color.FromArgb(234, 238, 244);
    public static readonly Color Card = Color.White;
    public static readonly Color Border = Color.FromArgb(220, 225, 232);
    public static readonly Color Text = Color.FromArgb(31, 35, 40);
    public static readonly Color Muted = Color.FromArgb(96, 104, 115);
    public static readonly Color Accent = Color.FromArgb(47, 111, 235);
    public static readonly Color AccentHover = Color.FromArgb(37, 93, 207);
    public static readonly Color AccentSoft = Color.FromArgb(230, 239, 254);
    public static readonly Color Good = Color.FromArgb(31, 138, 76);
    public static readonly Color GoodSoft = Color.FromArgb(225, 244, 232);
    public static readonly Color Warn = Color.FromArgb(166, 87, 0);
    public static readonly Color WarnSoft = Color.FromArgb(255, 243, 222);
    public static readonly Color Bad = Color.FromArgb(198, 40, 40);
    public static readonly Color BadSoft = Color.FromArgb(253, 232, 232);

    public static readonly Color S0 = Color.FromArgb(76, 110, 245);   // strength / click force
    public static readonly Color S1 = Color.FromArgb(240, 140, 0);    // sharp
    public static readonly Color S2 = Color.FromArgb(16, 170, 125);   // deep

    public static readonly Font Body = new("Segoe UI", 9.75f);
    public static readonly Font Small = new("Segoe UI", 8.5f);
    public static readonly Font Strong = new("Segoe UI Semibold", 9.75f);
    public static readonly Font H1 = new("Segoe UI Semibold", 18f);
    public static readonly Font H2 = new("Segoe UI Semibold", 12f);
    public static readonly Font Mono = new("Consolas", 10f);
    public static readonly Font MonoSmall = new("Consolas", 8.5f);
}

/// <summary>Rounded rectangles and the pulse glyph used by every visual.</summary>
internal static class Draw
{
    // Bars are full at these values: the range that was pleasant on the pad (the hardware allows more).
    public const int S0Full = 0x30, S1Full = 0x10, S2Full = 0x10;

    public static GraphicsPath Round(RectangleF r, float radius)
    {
        var path = new GraphicsPath();
        float d = Math.Min(radius * 2, Math.Min(r.Width, r.Height));
        if (d <= 0.5f) { path.AddRectangle(r); return path; }
        path.AddArc(r.X, r.Y, d, d, 180, 90);
        path.AddArc(r.Right - d, r.Y, d, d, 270, 90);
        path.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
        path.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
        path.CloseFigure();
        return path;
    }

    public static void Fill(Graphics g, Color c, RectangleF r, float radius)
    {
        using var b = new SolidBrush(c);
        using var p = Round(r, radius);
        g.FillPath(b, p);
    }

    public static void Stroke(Graphics g, Color c, RectangleF r, float radius, float width = 1)
    {
        using var pen = new Pen(c, width);
        using var p = Round(r, radius);
        g.DrawPath(pen, p);
    }

    public static Color Mix(Color a, Color b, float t)
    {
        t = Math.Clamp(t, 0, 1);
        return Color.FromArgb((int)(a.R + (b.R - a.R) * t), (int)(a.G + (b.G - a.G) * t), (int)(a.B + (b.B - a.B) * t));
    }

    /// <summary>One pulse as three bars: S0 (strength), S1 (sharp), S2 (deep).</summary>
    public static void Glyph(Graphics g, RectangleF r, Pulse p, bool label, Color? track = null)
    {
        float labelH = label ? Theme.MonoSmall.Height + 2 : 0;
        var area = new RectangleF(r.X, r.Y, r.Width, Math.Max(4, r.Height - labelH));
        float gap = Math.Max(1.5f, area.Width * 0.08f);
        float w0 = (area.Width - 2 * gap) * 0.46f;
        float w12 = (area.Width - 2 * gap - w0) / 2;
        Bar(g, new RectangleF(area.X, area.Y, w0, area.Height), p.S0 / (float)S0Full, Theme.S0, track);
        Bar(g, new RectangleF(area.X + w0 + gap, area.Y, w12, area.Height), p.S1 / (float)S1Full, Theme.S1, track);
        Bar(g, new RectangleF(area.X + w0 + gap + w12 + gap, area.Y, w12, area.Height), p.S2 / (float)S2Full, Theme.S2, track);
        if (label)
        {
            var text = $"{p.S0:X2} {p.S1:X2} {p.S2:X2}";
            var size = TextRenderer.MeasureText(text, Theme.MonoSmall);
            var x = (int)(r.X + (r.Width - size.Width) / 2);
            TextRenderer.DrawText(g, text, Theme.MonoSmall, new Point(x, (int)(area.Bottom + 2)), Theme.Muted);
        }
    }

    private static void Bar(Graphics g, RectangleF slot, float fraction, Color c, Color? track)
    {
        Fill(g, track ?? Color.FromArgb(30, c), slot, 2);
        if (fraction <= 0) return;
        float h = Math.Max(2, slot.Height * Math.Min(fraction, 1));
        Fill(g, c, new RectangleF(slot.X, slot.Bottom - h, slot.Width, h), 2);
        if (fraction > 1)
        {
            // Beyond the pleasant range: a white notch at the top
            using var pen = new Pen(Color.White, 1.5f);
            g.DrawLine(pen, slot.X + 1, slot.Y + 3, slot.Right - 1, slot.Y + 3);
        }
    }

    /// <summary>A chain of pulses laid out on a time line, glyphs spaced by the gap between their starts.</summary>
    public static void Chain(Graphics g, RectangleF r, IReadOnlyList<Pulse> pulses, int gapMs, bool labels, bool timeLabels)
    {
        if (pulses.Count == 0) return;
        float labelH = labels ? Theme.MonoSmall.Height + 2 : 0;
        float barsH = r.Height - labelH;
        float glyphW = Math.Min(barsH * 0.62f, 34);
        // A labelled pulse needs room for its bytes; the space between pulses room for "30 ms"
        float slotW = labels ? Math.Max(glyphW, TextRenderer.MeasureText("00 00 00", Theme.MonoSmall).Width + 2) : glyphW;
        float space = Math.Max(10, gapMs * 0.7f);
        if (timeLabels) space = Math.Max(space, TextRenderer.MeasureText($"{gapMs} ms", Theme.Small).Width + 10);
        float x = r.X;
        for (int i = 0; i < pulses.Count; i++)
        {
            Glyph(g, new RectangleF(x + (slotW - glyphW) / 2, r.Y, glyphW, barsH), pulses[i], false);
            if (labels)
                TextRenderer.DrawText(g, $"{pulses[i].S0:X2} {pulses[i].S1:X2} {pulses[i].S2:X2}", Theme.MonoSmall,
                    new Point((int)x, (int)(r.Y + barsH + 2)), Theme.Muted);
            if (i < pulses.Count - 1)
            {
                float mid = r.Y + barsH / 2;
                using var pen = new Pen(Theme.Border, 1.2f) { DashStyle = DashStyle.Dot };
                g.DrawLine(pen, x + slotW + 3, mid, x + slotW + space - 3, mid);
                if (timeLabels)
                {
                    var t = $"{gapMs} ms";
                    var size = TextRenderer.MeasureText(t, Theme.Small);
                    TextRenderer.DrawText(g, t, Theme.Small,
                        new Point((int)(x + slotW + (space - size.Width) / 2), (int)(mid - size.Height - 1)), Theme.Muted);
                }
            }
            x += slotW + space;
        }
    }

    /// <summary>Width of an unlabelled chain drawn with <see cref="Chain"/>.</summary>
    public static float ChainWidth(int count, float height, int gapMs) =>
        count <= 0 ? 0 : count * Math.Min(height * 0.62f, 34) + (count - 1) * Math.Max(10, gapMs * 0.7f);
}

/// <summary>A vertical stack whose children fill the width; the building block of every page.</summary>
internal class Stack : TableLayoutPanel
{
    public Stack()
    {
        ColumnCount = 1;
        ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        AutoSize = true;
        AutoSizeMode = AutoSizeMode.GrowAndShrink;
        Dock = DockStyle.Top;
        Margin = Padding.Empty;
        Padding = Padding.Empty;
    }

    public T Add<T>(T control) where T : Control
    {
        if (control.Dock == DockStyle.None) control.Dock = DockStyle.Top;
        RowStyles.Add(new RowStyle(SizeType.AutoSize));
        Controls.Add(control, 0, RowCount);
        RowCount++;
        return control;
    }
}

/// <summary>A white rounded card.</summary>
internal sealed class Card : Stack
{
    public Color Fill { get; set; } = Theme.Card;
    public Color Edge { get; set; } = Theme.Border;

    public Card(Color? fill = null, Color? edge = null)
    {
        Fill = fill ?? Theme.Card;
        Edge = edge ?? Theme.Border;
        BackColor = Fill;
        Padding = new Padding(18, 14, 18, 16);
        Margin = new Padding(0, 0, 0, 14);
        DoubleBuffered = true;
        ResizeRedraw = true;
    }

    protected override void OnPaintBackground(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.Clear(Parent?.BackColor ?? Theme.Back);
        g.SmoothingMode = SmoothingMode.AntiAlias;
        var r = new RectangleF(0.5f, 0.5f, Width - 1.5f, Height - 1.5f);
        Draw.Fill(g, Fill, r, 10);
        Draw.Stroke(g, Edge, r, 10);
    }
}

/// <summary>A label that wraps to its width and sizes its height to the text.</summary>
internal sealed class WrapLabel : Label
{
    public WrapLabel(string text, Font? font = null, Color? color = null)
    {
        AutoSize = false;
        UseMnemonic = false;
        Font = font ?? Theme.Body;
        ForeColor = color ?? Theme.Text;
        Margin = new Padding(0, 2, 0, 6);
        Dock = DockStyle.Top;
        Text = text;
        Fit();
    }

    protected override void OnSizeChanged(EventArgs e) { base.OnSizeChanged(e); Fit(); }
    protected override void OnTextChanged(EventArgs e) { base.OnTextChanged(e); Fit(); }
    protected override void OnFontChanged(EventArgs e) { base.OnFontChanged(e); Fit(); }

    public override Size GetPreferredSize(Size proposedSize)
    {
        int w = proposedSize.Width is > 20 and < 5000 ? proposedSize.Width : Width;
        return new Size(w, Measure(w));
    }

    private int Measure(int width) =>
        TextRenderer.MeasureText(Text.Length == 0 ? " " : Text, Font, new Size(Math.Max(width, 40), int.MaxValue),
            TextFormatFlags.WordBreak | TextFormatFlags.TextBoxControl).Height + Padding.Vertical;

    private void Fit()
    {
        int h = Measure(Width);
        if (h != Height) Height = h;
    }
}

/// <summary>Exclusive choice chips ("Too weak", "Just right", ...). Clicking the selected chip clears it.</summary>
internal sealed class ChipGroup : FlowLayoutPanel
{
    private readonly List<CheckBox> _chips = new();
    private bool _updating;

    public event Action<string?>? SelectionChanged;

    public ChipGroup(IEnumerable<string> options, string? selected)
    {
        AutoSize = true;
        WrapContents = false;
        Margin = new Padding(0, 2, 0, 2);
        foreach (var option in options)
        {
            var chip = new CheckBox
            {
                Appearance = Appearance.Button, Text = option, AutoSize = false, FlatStyle = FlatStyle.Flat,
                Size = TextRenderer.MeasureText(option, Theme.Small) + new Size(22, 10),
                Margin = new Padding(0, 2, 6, 2), Cursor = Cursors.Hand,
                Font = Theme.Small, TextAlign = ContentAlignment.MiddleCenter, UseVisualStyleBackColor = false,
                Checked = option == selected,
            };
            chip.CheckedChanged += (_, _) => OnChip(chip);
            Style(chip);
            _chips.Add(chip);
            Controls.Add(chip);
        }
    }

    public string? Selected => _chips.FirstOrDefault(c => c.Checked)?.Text;

    private void OnChip(CheckBox chip)
    {
        if (_updating) return;
        _updating = true;
        if (chip.Checked)
            foreach (var other in _chips.Where(c => c != chip)) other.Checked = false;
        _updating = false;
        foreach (var c in _chips) Style(c);
        SelectionChanged?.Invoke(Selected);
    }

    private static void Style(CheckBox c)
    {
        c.BackColor = c.Checked ? Theme.AccentSoft : Color.White;
        c.ForeColor = c.Checked ? Theme.Accent : Theme.Text;
        c.FlatAppearance.BorderColor = c.Checked ? Theme.Accent : Theme.Border;
        c.FlatAppearance.CheckedBackColor = Theme.AccentSoft;
        c.FlatAppearance.MouseOverBackColor = Theme.AccentSoft;
    }
}

/// <summary>Small factory helpers for consistently styled controls.</summary>
internal static class Ui
{
    public static Button Button(string text, EventHandler click, bool primary = false)
    {
        var b = new Button
        {
            Text = text, AutoSize = true, FlatStyle = FlatStyle.Flat, Padding = new Padding(10, 3, 10, 3),
            Margin = new Padding(0, 4, 8, 4), Cursor = Cursors.Hand, Font = primary ? Theme.Strong : Theme.Body,
            UseVisualStyleBackColor = false, BackColor = primary ? Theme.Accent : Color.White,
            ForeColor = primary ? Color.White : Theme.Text,
        };
        b.FlatAppearance.BorderColor = primary ? Theme.Accent : Theme.Border;
        b.FlatAppearance.MouseOverBackColor = primary ? Theme.AccentHover : Theme.AccentSoft;
        b.Click += click;
        return b;
    }

    public static Label Text(string text, Font? font = null, Color? color = null) => new()
    {
        Text = text, AutoSize = true, Font = font ?? Theme.Body, ForeColor = color ?? Theme.Text,
        Margin = new Padding(0, 7, 6, 3), UseMnemonic = false,
    };

    public static Label Heading(string text) => new()
    {
        Text = text, AutoSize = true, Font = Theme.H2, ForeColor = Theme.Text, Margin = new Padding(0, 0, 0, 6), UseMnemonic = false,
    };

    public static FlowLayoutPanel Row(params Control[] controls)
    {
        var row = new FlowLayoutPanel { AutoSize = true, WrapContents = false, Margin = new Padding(0, 2, 0, 2) };
        row.Controls.AddRange(controls);
        return row;
    }

    /// <summary>Equal-width columns of equal height.</summary>
    public static TableLayoutPanel Columns(params Control[] controls)
    {
        var t = new TableLayoutPanel
        {
            ColumnCount = controls.Length, RowCount = 1, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink,
            Margin = Padding.Empty, Padding = Padding.Empty,
        };
        t.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        for (int i = 0; i < controls.Length; i++)
        {
            t.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f / controls.Length));
            controls[i].Dock = DockStyle.Fill;
            controls[i].Margin = new Padding(i == 0 ? 0 : 7, 0, i == controls.Length - 1 ? 0 : 7, 14);
            t.Controls.Add(controls[i], i, 0);
        }
        return t;
    }

    public static NumericUpDown Hex(int max) => new() { Hexadecimal = true, Minimum = 0, Maximum = max, Width = 64, Font = Theme.Mono, Margin = new Padding(0, 3, 6, 3) };

    public static NumericUpDown Dec(int min, int max) => new() { Minimum = min, Maximum = max, Width = 64, Font = Theme.Body, Margin = new Padding(0, 3, 6, 3) };

    public static ComboBox Combo(int width) => new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = width, Font = Theme.Body, Margin = new Padding(0, 3, 6, 3) };

    /// <summary>A labelled value row: label, input, unit, and an optional explanation in grey.</summary>
    public static Control Field(string label, Control input, string unit, string hint)
    {
        var t = new TableLayoutPanel { ColumnCount = 3, AutoSize = true, Margin = new Padding(0, 0, 0, 4) };
        t.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 330));
        t.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        t.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        t.Controls.Add(Text(label), 0, 0);
        t.Controls.Add(Row(input, Text(unit, null, Theme.Muted)), 1, 0);
        t.Controls.Add(Text(hint, Theme.Small, Theme.Muted), 2, 0);
        return t;
    }

    /// <summary>A coloured status pill drawn as a label.</summary>
    public static Label Pill(string text, Color fore, Color back) => new()
    {
        Text = text, AutoSize = true, Font = Theme.Small, ForeColor = fore, BackColor = back,
        Padding = new Padding(6, 2, 6, 2), Margin = new Padding(0, 6, 6, 2), UseMnemonic = false,
    };
}
