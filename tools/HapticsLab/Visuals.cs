using System.Drawing.Drawing2D;

namespace HapticsLab;

/// <summary>Base for the painted visuals: double buffered, with a short highlight that fades after a play.</summary>
internal abstract class Visual : Control
{
    private readonly System.Windows.Forms.Timer _fade = new() { Interval = 16 };
    protected float FlashLevel;

    protected Visual()
    {
        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint |
                 ControlStyles.ResizeRedraw | ControlStyles.SupportsTransparentBackColor, true);
        Margin = new Padding(0, 4, 0, 4);
        Font = Theme.Body;
        _fade.Tick += (_, _) =>
        {
            FlashLevel -= 0.04f;
            if (FlashLevel <= 0) { FlashLevel = 0; _fade.Stop(); }
            Invalidate();
        };
    }

    public void Flash()
    {
        FlashLevel = 1;
        _fade.Start();
        Invalidate();
    }

    protected Color FlashColor(Color baseColor) => Draw.Mix(baseColor, Color.FromArgb(196, 219, 255), FlashLevel);

    protected static Graphics Prepare(PaintEventArgs e, Color back)
    {
        var g = e.Graphics;
        g.Clear(back);
        g.SmoothingMode = SmoothingMode.AntiAlias;
        return g;
    }

    protected Color ParentBack => Parent?.BackColor ?? Theme.Card;

    protected override void Dispose(bool disposing)
    {
        if (disposing) _fade.Dispose();
        base.Dispose(disposing);
    }
}

/// <summary>A pulse chain (one candidate) as bars on a time line. Shows parse errors in red.</summary>
internal sealed class PulseChainView : Visual
{
    private IReadOnlyList<Pulse> _pulses = Array.Empty<Pulse>();
    private string? _error;

    public int GapMs { get; set; } = 30;
    public bool Labels { get; set; } = true;

    public PulseChainView() { Size = new Size(190, 46); }

    public IReadOnlyList<Pulse> Pulses => _pulses;

    public void SetPulses(IReadOnlyList<Pulse> pulses) { _pulses = pulses; _error = null; Invalidate(); }

    public void SetChain(string text)
    {
        if (string.IsNullOrWhiteSpace(text)) { SetPulses(Array.Empty<Pulse>()); return; }
        try { SetPulses(Pulse.ParseChain(text)); }
        catch (FormatException e) { _pulses = Array.Empty<Pulse>(); _error = e.Message; Invalidate(); }
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = Prepare(e, ParentBack);
        var r = new RectangleF(1, 1, Width - 2, Height - 2);
        if (FlashLevel > 0) Draw.Fill(g, FlashColor(ParentBack), r, 6);
        if (_error != null && _pulses.Count == 0)
        {
            TextRenderer.DrawText(g, _error, Theme.Small, Rectangle.Round(r), Theme.Bad,
                TextFormatFlags.WordBreak | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);
            return;
        }
        Draw.Chain(g, new RectangleF(6, 4, Width - 12, Height - 8), _pulses, GapMs, Labels, Labels);
    }
}

/// <summary>Legend for the bar colors.</summary>
internal sealed class GlyphLegend : Visual
{
    public GlyphLegend() { Size = new Size(400, 92); }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = Prepare(e, ParentBack);
        Draw.Chain(g, new RectangleF(4, 4, 70, Height - 8), new[] { new Pulse(0x1E, 0x08, 0x08) }, 30, true, false);
        int x = 86, y = 6;
        void Line(Color c, string name, string text)
        {
            Draw.Fill(g, c, new RectangleF(x, y + 4, 12, 12), 3);
            TextRenderer.DrawText(g, name, Theme.Strong, new Point(x + 18, y), Theme.Text);
            TextRenderer.DrawText(g, text, Theme.Body, new Rectangle(x + 46, y, Width - x - 46, 22), Theme.Muted, TextFormatFlags.EndEllipsis);
            y += 26;
        }
        Line(Theme.S0, "S0", "strength; for the click also the force");
        Line(Theme.S1, "S1", "louder and sharper");
        Line(Theme.S2, "S2", "louder and deeper");
    }
}

/// <summary>Click force: where the three press levels (and their releases) sit on the S0 scale.</summary>
internal sealed class ForceScale : Visual
{
    private const int Lo = 0x08, Hi = 0x28;
    private static readonly string[] Names = { "Light", "Medium", "Heavy" };
    private int _hover = -1;

    public byte[] Press { get; set; } = new byte[3];
    public byte[] Release { get; set; } = new byte[3];
    public int Active { get; set; } = -1;
    public event Action<int>? LevelClicked;

    public ForceScale() { Height = 128; Cursor = Cursors.Hand; }

    private float X(int s0) => 40 + (Width - 80) * Math.Clamp((s0 - Lo) / (float)(Hi - Lo), 0, 1);
    private float TrackY => 62;

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = Prepare(e, ParentBack);
        var track = new RectangleF(40, TrackY - 5, Width - 80, 10);
        using (var brush = new LinearGradientBrush(track, Color.FromArgb(205, 214, 252), Theme.S0, LinearGradientMode.Horizontal))
        using (var path = Draw.Round(track, 5))
            g.FillPath(brush, path);

        TextRenderer.DrawText(g, "easy press", Theme.Small, new Point(36, (int)TrackY + 22), Theme.Muted);
        var right = "firm press";
        TextRenderer.DrawText(g, right, Theme.Small, new Point(Width - 40 - TextRenderer.MeasureText(right, Theme.Small).Width, (int)TrackY + 22), Theme.Muted);

        for (int i = 0; i < 3; i++)
        {
            // Release: small triangle under the track
            float rx = X(Release[i]);
            using (var pen = new Pen(Theme.Muted, 1.3f))
                g.DrawPolygon(pen, new[] { new PointF(rx, TrackY + 9), new PointF(rx - 5, TrackY + 18), new PointF(rx + 5, TrackY + 18) });
        }
        for (int i = 0; i < 3; i++)
        {
            float px = X(Press[i]);
            bool on = i == Active, hover = i == _hover;
            if (on) Draw.Fill(g, Draw.Mix(Theme.AccentSoft, Color.FromArgb(196, 219, 255), FlashLevel), new RectangleF(px - 16, TrackY - 16, 32, 32), 16);
            Draw.Fill(g, Color.White, new RectangleF(px - 10, TrackY - 10, 20, 20), 10);
            Draw.Fill(g, on ? Theme.Accent : Theme.S0, new RectangleF(px - 7, TrackY - 7, 14, 14), 7);
            if (hover && !on) Draw.Stroke(g, Theme.Accent, new RectangleF(px - 11, TrackY - 11, 22, 22), 11, 1.5f);

            var label = $"{Names[i]}  {Press[i]:X2}";
            var size = TextRenderer.MeasureText(label, Theme.Strong);
            int ly = 10 + (i % 2 == 1 && Math.Abs(X(Press[i]) - X(Press[i - 1])) < size.Width ? 18 : 0);
            TextRenderer.DrawText(g, label, Theme.Strong, new Point((int)(px - size.Width / 2), ly), on ? Theme.Accent : Theme.Text);
        }
        TextRenderer.DrawText(g, "● press (click)    △ release (lifting)    click a level to try it", Theme.Small,
            new Point(40, (int)TrackY + 44), Theme.Muted);
    }

    private int HitTest(Point p)
    {
        for (int i = 0; i < 3; i++)
            if (Math.Abs(p.X - X(Press[i])) < 14 && Math.Abs(p.Y - TrackY) < 30) return i;
        return -1;
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        int h = HitTest(e.Location);
        if (h != _hover) { _hover = h; Invalidate(); }
        base.OnMouseMove(e);
    }

    protected override void OnMouseLeave(EventArgs e) { _hover = -1; Invalidate(); base.OnMouseLeave(e); }

    protected override void OnMouseClick(MouseEventArgs e)
    {
        int h = HitTest(e.Location);
        if (h >= 0) LevelClicked?.Invoke(h);
        base.OnMouseClick(e);
    }
}

/// <summary>Click feedback: the click and release pulses at each Haptic click step, side by side.</summary>
internal sealed class ClickColumns : Visual
{
    private int _hover = -1;

    public string[] Headers { get; set; } = Array.Empty<string>();
    public (Pulse Click, Pulse Release)[] Values { get; set; } = Array.Empty<(Pulse, Pulse)>();
    public int Active { get; set; } = -1;
    public event Action<int>? ColumnClicked;

    public ClickColumns() { Height = 178; Cursor = Cursors.Hand; }

    private RectangleF Column(int i)
    {
        float w = (Width - 2) / (float)Math.Max(1, Values.Length);
        return new RectangleF(1 + i * w + 4, 1, w - 8, Height - 3);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = Prepare(e, ParentBack);
        for (int i = 0; i < Values.Length; i++)
        {
            var c = Column(i);
            bool on = i == Active;
            var back = on ? FlashColor(Theme.AccentSoft) : i == _hover ? Color.FromArgb(246, 249, 255) : Color.FromArgb(249, 250, 252);
            Draw.Fill(g, back, c, 8);
            Draw.Stroke(g, on ? Theme.Accent : Theme.Border, c, 8, on ? 1.5f : 1);
            var head = i < Headers.Length ? Headers[i] : "";
            var hs = TextRenderer.MeasureText(head, Theme.Strong);
            TextRenderer.DrawText(g, head, Theme.Strong, new Point((int)(c.X + (c.Width - hs.Width) / 2), (int)c.Y + 8), on ? Theme.Accent : Theme.Text);

            float slot = TextRenderer.MeasureText("00 00 00", Theme.MonoSmall).Width + 2, top = c.Y + 34, h = c.Height - 60;
            float x1 = c.X + c.Width / 2 - slot - 6, x2 = c.X + c.Width / 2 + 6;
            Draw.Chain(g, new RectangleF(x1, top, slot, h), new[] { Values[i].Click }, 30, true, false);
            Draw.Chain(g, new RectangleF(x2, top, slot, h), new[] { Values[i].Release }, 30, true, false);
            void Caption(string t, float x)
            {
                var s = TextRenderer.MeasureText(t, Theme.Small);
                TextRenderer.DrawText(g, t, Theme.Small, new Point((int)(x + (slot - s.Width) / 2), (int)(top + h + 4)), Theme.Muted);
            }
            Caption("click", x1);
            Caption("release", x2);
        }
    }

    private int HitTest(Point p)
    {
        for (int i = 0; i < Values.Length; i++) if (Column(i).Contains(p)) return i;
        return -1;
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        int h = HitTest(e.Location);
        if (h != _hover) { _hover = h; Invalidate(); }
        base.OnMouseMove(e);
    }

    protected override void OnMouseLeave(EventArgs e) { _hover = -1; Invalidate(); base.OnMouseLeave(e); }

    protected override void OnMouseClick(MouseEventArgs e)
    {
        int h = HitTest(e.Location);
        if (h >= 0) ColumnClicked?.Invoke(h);
        base.OnMouseClick(e);
    }
}

/// <summary>Signal strength: every waveform at every Haptic signals step. Click a cell to play it.</summary>
internal sealed class StrengthMatrix : Visual
{
    private const int HeaderH = 30, RowH = 54, TitleW = 130;
    private readonly ToolTip _tip = new();
    private (int R, int C) _hover = (-1, -1), _flash = (-1, -1);
    private string _tipText = "";

    public string[] Headers { get; set; } = Array.Empty<string>();
    public List<(string Title, List<Pulse>[] Cells)> Rows { get; } = new();
    public int GapMs { get; set; } = 30;
    public event Action<int, int>? CellClicked;
    public event Action<int>? RowClicked;

    public StrengthMatrix() { Cursor = Cursors.Hand; }

    public void SetRows(IEnumerable<(string Title, List<Pulse>[] Cells)> rows)
    {
        Rows.Clear();
        Rows.AddRange(rows);
        Height = HeaderH + Rows.Count * RowH + 2;
        Invalidate();
    }

    public void FlashCell(int r, int c) { _flash = (r, c); Flash(); }

    private float ColW => (Width - TitleW - 2) / (float)Math.Max(1, Headers.Length);

    private RectangleF Cell(int r, int c) => new(TitleW + c * ColW + 3, HeaderH + r * RowH + 3, ColW - 6, RowH - 6);

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = Prepare(e, ParentBack);
        for (int c = 0; c < Headers.Length; c++)
        {
            var size = TextRenderer.MeasureText(Headers[c], Theme.Strong);
            TextRenderer.DrawText(g, Headers[c], Theme.Strong, new Point((int)(TitleW + c * ColW + (ColW - size.Width) / 2), 6), Theme.Muted);
        }
        for (int r = 0; r < Rows.Count; r++)
        {
            bool rowHover = _hover.R == r && _hover.C < 0;
            var title = Rows[r].Title;
            TextRenderer.DrawText(g, title, Theme.Strong, new Point(4, HeaderH + r * RowH + 10), rowHover ? Theme.Accent : Theme.Text);
            TextRenderer.DrawText(g, rowHover ? "▶ play rising" : "", Theme.Small, new Point(4, HeaderH + r * RowH + 29), Theme.Accent);
            for (int c = 0; c < Headers.Length && c < Rows[r].Cells.Length; c++)
            {
                var cell = Cell(r, c);
                bool hover = _hover == (r, c) || rowHover;
                var back = _flash == (r, c) && FlashLevel > 0 ? FlashColor(Theme.AccentSoft)
                         : hover ? Theme.AccentSoft : Color.FromArgb(249, 250, 252);
                Draw.Fill(g, back, cell, 6);
                var pulses = Rows[r].Cells[c];
                float h = cell.Height - 10;
                float w = Draw.ChainWidth(pulses.Count, h, GapMs);
                Draw.Chain(g, new RectangleF(cell.X + (cell.Width - w) / 2, cell.Y + 5, w, h), pulses, GapMs, false, false);
            }
        }
    }

    private (int R, int C) HitTest(Point p)
    {
        int r = (p.Y - HeaderH) / RowH;
        if (p.Y < HeaderH || r < 0 || r >= Rows.Count) return (-1, -1);
        if (p.X < TitleW) return (r, -1);
        int c = (int)((p.X - TitleW) / ColW);
        return c >= 0 && c < Headers.Length ? (r, c) : (-1, -1);
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        var h = HitTest(e.Location);
        if (h != _hover)
        {
            _hover = h;
            Invalidate();
            var text = h.R >= 0 && h.C >= 0 ? $"{Rows[h.R].Title} at {Headers[h.C]}: {Pulse.FormatChain(Rows[h.R].Cells[h.C])}"
                     : h.R >= 0 ? $"Play {Rows[h.R].Title} at every step, rising" : "";
            if (text != _tipText) { _tipText = text; _tip.SetToolTip(this, text); }
        }
        base.OnMouseMove(e);
    }

    protected override void OnMouseLeave(EventArgs e) { _hover = (-1, -1); Invalidate(); base.OnMouseLeave(e); }

    protected override void OnMouseClick(MouseEventArgs e)
    {
        var h = HitTest(e.Location);
        if (h.R >= 0 && h.C >= 0) CellClicked?.Invoke(h.R, h.C);
        else if (h.R >= 0) RowClicked?.Invoke(h.R);
        base.OnMouseClick(e);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing) _tip.Dispose();
        base.Dispose(disposing);
    }
}

/// <summary>A navigation entry: a numbered circle, title, subtitle, optional progress and pulse preview.</summary>
internal sealed class NavItem : Visual
{
    private bool _hover;

    public string Badge { get; set; } = "";
    public string Title { get; set; } = "";
    public string Subtitle { get; set; } = "";
    public bool Selected { get; set; }
    public bool Done { get; set; }
    public Color? SubtitleColor { get; set; }
    public IReadOnlyList<Pulse>? Preview { get; set; }

    public NavItem() { Height = 54; Cursor = Cursors.Hand; Margin = new Padding(0, 1, 0, 1); }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = Prepare(e, ParentBack);
        var r = new RectangleF(2, 2, Width - 4, Height - 4);
        if (Selected) Draw.Fill(g, Color.White, r, 8);
        else if (_hover) Draw.Fill(g, Color.FromArgb(246, 248, 251), r, 8);
        if (FlashLevel > 0) Draw.Fill(g, FlashColor(Selected ? Color.White : ParentBack), r, 8);
        if (Selected) Draw.Fill(g, Theme.Accent, new RectangleF(2, 14, 3, Height - 28), 2);

        var circle = new RectangleF(14, Height / 2f - 13, 26, 26);
        if (Done) Draw.Fill(g, Theme.Good, circle, 13);
        else if (Selected) Draw.Fill(g, Theme.Accent, circle, 13);
        else { Draw.Fill(g, Color.White, circle, 13); Draw.Stroke(g, Theme.Border, circle, 13, 1.2f); }
        var badge = Done ? "✓" : Badge;
        var bs = TextRenderer.MeasureText(badge, Theme.Strong);
        TextRenderer.DrawText(g, badge, Theme.Strong, new Point((int)(circle.X + (circle.Width - bs.Width) / 2 + 1), (int)(circle.Y + (circle.Height - bs.Height) / 2)),
            Done || Selected ? Color.White : Theme.Muted);

        float previewW = Preview == null ? 0 : Draw.ChainWidth(Preview.Count, Height - 22, 20) + 10;
        int textW = (int)(Width - 52 - previewW - 8);
        TextRenderer.DrawText(g, Title, Theme.Strong, new Rectangle(50, 9, textW, 20), Selected ? Theme.Text : Theme.Text, TextFormatFlags.EndEllipsis);
        TextRenderer.DrawText(g, Subtitle, Theme.Small, new Rectangle(50, 29, textW, 18), SubtitleColor ?? Theme.Muted, TextFormatFlags.EndEllipsis);
        if (Preview != null)
            Draw.Chain(g, new RectangleF(Width - previewW - 6, 11, previewW, Height - 22), Preview, 20, false, false);
    }

    protected override void OnMouseEnter(EventArgs e) { _hover = true; Invalidate(); base.OnMouseEnter(e); }
    protected override void OnMouseLeave(EventArgs e) { _hover = false; Invalidate(); base.OnMouseLeave(e); }
}
