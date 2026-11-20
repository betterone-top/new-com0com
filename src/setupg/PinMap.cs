using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;

namespace Com0com.Setupg;

/// <summary>
/// Pin map of the original setupg (setupg/pinmap.cpp), drawn and operated the same way:
/// a grid of 17 columns x 11 lines, port A pins in the left column, port B in the right one,
/// ON in the middle of the last line. Drag from a pin to a pin of the other kind to wire them
/// (one green pin to one red pin, a red pin to several green pins); double click a green pin to
/// add/remove an inverter. Changed wires are blue. The pin names and "ON" are drawn around the grid
/// where the original had labels.
/// </summary>
internal sealed class PinMap : Control
{
    private sealed class PinSource(string source, bool invert)
    {
        public string Source = source;
        public bool Invert = invert;
        public PinSource Copy() => new(Source, Invert);
        public bool IsEqual(PinSource o) => Source == o.Source && Invert == o.Invert;
    }

    private sealed class Pin(int c, int l, bool isFixed, int ic)
    {
        public readonly int C = c, L = l, IC = ic;
        public readonly bool IsFixed = isFixed;
        public bool IsNoise;
        public PinSource? Source;
        public bool IsSource => Source == null;
        public void Link(PinSource s) => Source = s.Copy();
    }

    private const int NumLines = 11, MaxDiff = 7, NumMiddleSrcs = 1;
    private const int UL = 0, DL = NumLines - 1;
    private const int NumColumns = (MaxDiff + 1) * 2 + NumMiddleSrcs;
    private const int LC = 0, RC = NumColumns - 1, MC = NumColumns / 2;

    private static readonly string[] Names = ["RX", "TX", "DTR", "DSR", "DCD", "RTS", "CTS", "RI", "OUT1", "OUT2", "OPEN"];
    private static readonly string[] Destinations = ["DSR", "DCD", "CTS", "RI"];

    // Geometry (the original form scaled): pin names | picture | pin names, "ON" below.
    private readonly double _labelW, _picW, _picH, _onH, _pinSize, _invertSize, _font;

    private readonly Dictionary<string, Pin> _pins = [];
    private readonly Dictionary<string, PinSource> _pinsOrg = [];

    private string _selected = "";
    private Point _mb, _me;
    private bool _mouseInside;

    private static readonly IBrush Black = Brushes.Black;
    private static readonly IBrush Red = new SolidColorBrush(Color.FromRgb(0xFF, 0x00, 0x00));
    private static readonly IBrush Green = new SolidColorBrush(Color.FromRgb(0x00, 0x80, 0x00));
    private static readonly IBrush Blue = new SolidColorBrush(Color.FromRgb(0x00, 0x00, 0xFF));
    private static readonly IBrush Highlight = new SolidColorBrush(Color.FromRgb(0xF0, 0x8C, 0x00));

    public event EventHandler? Changed;

    public PinMap(double scale, double fontSize)
    {
        _labelW = 38 * scale;
        _picW = 188 * scale;
        _picH = 179 * scale;
        _onH = 16 * scale;
        _pinSize = 9 * scale;
        _invertSize = 14 * scale;
        _font = fontSize;
        Width = _labelW * 2 + _picW;
        Height = _picH + _onH;
        ClipToBounds = true;
        Cursor = new Cursor(StandardCursorType.Hand);
        ToolTip.SetTip(this, "One green pin can be wired to one red pin.\n" +
                             "One red pin can be wired to multiple green pins.\n" +
                             "Double click a green pin to add/remove inverter.");

        foreach (var (side, col) in new[] { ("A", LC), ("B", RC) })
        {
            int line = UL, ic = 0;
            _pins[$"RX({side})"] = new Pin(col, line++, true, 0);
            _pins[$"TX({side})"] = new Pin(col, line++, true, 0);
            _pins[$"DTR({side})"] = new Pin(col, line++, false, ic++);
            _pins[$"DSR({side})"] = new Pin(col, line++, false, 0);
            _pins[$"DCD({side})"] = new Pin(col, line++, false, 0);
            _pins[$"RTS({side})"] = new Pin(col, line++, false, ic++);
            _pins[$"CTS({side})"] = new Pin(col, line++, false, 0);
            _pins[$"RI({side})"] = new Pin(col, line++, false, 0);
            _pins[$"OUT1({side})"] = new Pin(col, line++, false, ic++);
            _pins[$"OUT2({side})"] = new Pin(col, line++, false, ic++);
            _pins[$"OPEN({side})"] = new Pin(col, line++, false, ic++);
        }
        _pins["ON"] = new Pin(MC, DL, false, 0);
        _pins["RX(A)"].Link(new PinSource("TX(B)", false));
        _pins["RX(B)"].Link(new PinSource("TX(A)", false));
        Init(null, null);
    }

    // ------------------------------------------------------------------ model

    /// <summary>
    /// Loads the wiring of a pair from its parameters (lower-case keys, upper-case values as in
    /// "setupc --detail-prms list": dsr=RDTR, ri=!ON, emunoise=0, ...). Null clears the map.
    /// </summary>
    public void Init(IReadOnlyDictionary<string, string>? a, IReadOnlyDictionary<string, string>? b)
    {
        MouseInit();
        var pair = new[] { a, b };
        for (int i = 0; i < 2; i++)
        {
            string tx = i == 0 ? "TX(A)" : "TX(B)";
            _pins[tx].IsNoise = pair[i] != null && pair[i]!.TryGetValue("emunoise", out var noise) &&
                                noise.IndexOfAny("123456789".ToCharArray()) >= 0;
        }

        foreach (var dst in Destinations)
        {
            for (int i = 0; i < 2; i++)
            {
                string key = $"{dst}({(i == 0 ? "A" : "B")})";
                if (pair[i] == null || !pair[i]!.TryGetValue(dst.ToLowerInvariant(), out var src))
                {
                    Link(key, "", false);
                    continue;
                }
                bool invert = false;
                if (src.StartsWith('!'))
                {
                    invert = true;
                    src = src[1..];
                }
                string suffix = "";
                if (src.StartsWith('L'))
                {
                    suffix = i == 0 ? "(A)" : "(B)";
                    src = src[1..];
                }
                else if (src.StartsWith('R'))
                {
                    suffix = i == 1 ? "(A)" : "(B)";
                    src = src[1..];
                }
                Link(key, src + suffix, invert);
            }
        }
        InvalidateVisual();
    }

    private void Link(string pinName, string source, bool invert)
    {
        _pinsOrg[pinName] = new PinSource(source, invert);
        _pins[pinName].Link(_pinsOrg[pinName]);
    }

    /// <summary>Changed wires as setupc parameters per port (index 0 = A): "dsr" → "!RDTR".</summary>
    public Dictionary<string, string>[] GetChanges()
    {
        var changes = new[] { new Dictionary<string, string>(), new Dictionary<string, string>() };
        foreach (var (name, pin) in _pins)
        {
            if (pin.IsFixed || pin.IsSource) continue;
            if (_pinsOrg.TryGetValue(name, out var org) && pin.Source!.IsEqual(org)) continue;

            var fields = name.Split('(');
            if (fields.Length != 2) continue;
            int iDst = fields[1].StartsWith('A') ? 0 : fields[1].StartsWith('B') ? 1 : -1;
            if (iDst < 0) continue;

            string src = pin.Source!.Invert ? "!" : "";
            var srcFields = pin.Source.Source.Split('(');
            if (srcFields.Length > 1)
            {
                if (srcFields[1].StartsWith('A')) src += iDst == 0 ? "l" : "r";
                else if (srcFields[1].StartsWith('B')) src += iDst == 1 ? "l" : "r";
            }
            src += srcFields[0];
            changes[iDst][fields[0].ToLowerInvariant()] = src.ToUpperInvariant();
        }
        return changes;
    }

    // ------------------------------------------------------------------ geometry

    private int StepC => (int)(_picW / NumColumns);
    private int StepL => (int)(_picH / NumLines);
    private double XCorr => _labelW + (_picW - NumColumns * StepC) / 2;
    private double YCorr => (_picH - NumLines * StepL) / 2;
    private Point Pos(Pin p) => new(XCorr + StepC / 2.0 + p.C * StepC, YCorr + StepL / 2.0 + p.L * StepL);

    /// <summary>Any pin under the mouse (also RX/TX), for highlighting its wires.</summary>
    private string? PinUnder(Point m)
    {
        foreach (var (name, pin) in _pins)
        {
            var c = Pos(pin);
            if (Math.Abs(m.X - c.X) < StepC && Math.Abs(m.Y - c.Y) < StepL / 2.0) return name;
        }
        // the pin names around the picture belong to their pin as well
        for (int i = 0; i < Names.Length; i++)
        {
            double y = YCorr + StepL / 2.0 + i * StepL;
            if (Math.Abs(m.Y - y) >= StepL / 2.0) continue;
            if (m.X < _labelW) return $"{Names[i]}(A)";
            if (m.X > _labelW + _picW) return $"{Names[i]}(B)";
        }
        return null;
    }

    /// <summary>The pin under the mouse that can be selected/linked now (as in the original paint loop).</summary>
    private string? PinAt(Point m)
    {
        foreach (var (name, pin) in _pins)
        {
            if (pin.IsFixed) continue;
            if (_pins.TryGetValue(_selected, out var sel) && sel.IsSource == pin.IsSource) continue;
            var c = Pos(pin);
            if (m.X > c.X - StepC && m.X < c.X + StepC && m.Y > c.Y - StepL / 2.0 && m.Y < c.Y + StepL / 2.0)
                return name;
        }
        return null;
    }

    // ------------------------------------------------------------------ mouse

    private void MouseInit()
    {
        _mb = _me = default;
        _selected = "";
    }

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);
        if (!IsEnabled) return;
        _me = e.GetPosition(this);
        if (e.ClickCount == 2)
        {
            // double click: add/remove the inverter of a green pin
            MouseInit();
            if (PinAt(_me) is { } name && _pins[name].Source is { } s)
            {
                s.Invert = !s.Invert;
                Changed?.Invoke(this, EventArgs.Empty);
            }
        }
        else if (PinAt(_me) is { } name)
        {
            _selected = name;
            _mb = Pos(_pins[name]);
        }
        e.Pointer.Capture(this);
        InvalidateVisual();
    }

    protected override void OnPointerMoved(PointerEventArgs e)
    {
        base.OnPointerMoved(e);
        _me = e.GetPosition(this);
        _mouseInside = true;
        InvalidateVisual();
    }

    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        base.OnPointerReleased(e);
        _me = e.GetPosition(this);
        if (_pins.ContainsKey(_selected) && PinAt(_me) is { } name)
        {
            string src, dst;
            if (_pins[name].IsSource) { src = name; dst = _selected; }
            else { src = _selected; dst = name; }
            if (_pinsOrg[dst].Source == src)
                _pins[dst].Link(_pinsOrg[dst]);
            else
                _pins[dst].Link(new PinSource(src, false));
            Changed?.Invoke(this, EventArgs.Empty);
        }
        _selected = "";
        e.Pointer.Capture(null);
        InvalidateVisual();
    }

    protected override void OnPointerExited(PointerEventArgs e)
    {
        base.OnPointerExited(e);
        _mouseInside = false;
        if (string.IsNullOrEmpty(_selected)) MouseInit();
        InvalidateVisual();
    }

    // ------------------------------------------------------------------ drawing

    private FormattedText Text(string s) =>
        new(s, CultureInfo.CurrentUICulture, FlowDirection.LeftToRight, new Typeface(FontFamily.Default), _font, Black);

    public override void Render(DrawingContext g)
    {
        double lw = Math.Max(1, _pinSize / 9);
        var pOrg = new Pen(Black, lw);
        var pNew = new Pen(Blue, lw);
        double left = _labelW, right = _labelW + _picW, bottom = _picH;
        double half = Math.Floor(_pinSize / 2);

        // pin names around the picture, "ON" below it
        for (int i = 0; i < Names.Length; i++)
        {
            double y = YCorr + StepL / 2.0 + i * StepL;
            var t = Text(Names[i]);
            g.DrawText(t, new Point(left - 3 - t.Width, y - t.Height / 2));
            g.DrawText(t, new Point(right + 3, y - t.Height / 2));
        }
        var on = Text("ON");
        g.DrawText(on, new Point(left + _picW / 2 - on.Width / 2, bottom));

        // pins and their stubs to the picture edge
        string? hover = _mouseInside ? PinAt(_me) : null;
        // Not in the original: the wires of the pin under the mouse (or being dragged) are highlighted.
        string? focus = _pins.ContainsKey(_selected) ? _selected : _mouseInside ? PinUnder(_me) : null;
        var hlPen = new Pen(Highlight, lw * 2.4);
        var related = new HashSet<string>();
        if (focus != null)
        {
            related.Add(focus);
            foreach (var (n, p) in _pins)
            {
                if (p.Source == null) continue;
                if (n == focus) related.Add(p.Source.Source);
                if (p.Source.Source == focus) related.Add(n);
            }
        }
        foreach (var (name, pin) in _pins)
        {
            var c = Pos(pin);
            double size = _pinSize + (name == hover ? 4 : 0);
            IBrush b = pin.IsFixed ? Black : pin.IsSource ? Red : Green;
            g.DrawEllipse(b, null, c, size / 2, size / 2);
            if (related.Contains(name)) g.DrawEllipse(null, new Pen(Highlight, lw * 1.6), c, size / 2 + 2.5, size / 2 + 2.5);

            if (pin.C == LC)
            {
                if (pin.IsSource) Arrow(g, pOrg, new Point(left, c.Y), new Point(c.X - half, c.Y));
                else Arrow(g, pOrg, new Point(c.X - half, c.Y), new Point(left, c.Y));
            }
            else if (pin.C == RC)
            {
                if (pin.IsSource) Arrow(g, pOrg, new Point(right, c.Y), new Point(c.X + half, c.Y));
                else Arrow(g, pOrg, new Point(c.X + half, c.Y), new Point(right, c.Y));
            }
            else if (pin.L == DL)
            {
                if (pin.IsSource) Arrow(g, pOrg, new Point(c.X, bottom), new Point(c.X, c.Y + half));
                else Arrow(g, pOrg, new Point(c.X, c.Y + half), new Point(c.X, bottom));
            }
        }

        // wires (highlighted ones are drawn last, on top)
        var highlighted = new List<Point[]>();
        foreach (var (name, pin) in _pins)
        {
            if (pin.Source == null || !_pins.TryGetValue(pin.Source.Source, out var pinS)) continue;
            var s = Pos(pinS);
            var d = Pos(pin);
            var line = new Point[4];

            if (pinS.C == LC) line[0] = new Point(s.X + half, s.Y);
            else if (pinS.C == RC) line[0] = new Point(s.X - half, s.Y);
            else if (pinS.L == UL) line[0] = new Point(s.X, s.Y + half);
            else if (pinS.L == DL) line[0] = new Point(s.X, s.Y - half);
            else line[0] = s;

            var pCur = pin.IsFixed || pin.Source.IsEqual(_pinsOrg[name]) ? pOrg : pNew;
            double inv = _invertSize / 2;
            if (pin.C == LC)
            {
                line[3] = new Point(d.X + half, d.Y);
                if (pin.Source.Invert)
                {
                    line[3] = new Point(line[3].X + inv, d.Y);
                    Polygon(g, pCur, new Point(line[3].X, d.Y - inv), new Point(line[3].X, d.Y + inv), new Point(line[3].X - inv, d.Y));
                }
            }
            else if (pin.C == RC)
            {
                line[3] = new Point(d.X - half, d.Y);
                if (pin.Source.Invert)
                {
                    line[3] = new Point(line[3].X - inv, d.Y);
                    Polygon(g, pCur, new Point(line[3].X, d.Y - inv), new Point(line[3].X, d.Y + inv), new Point(line[3].X + inv, d.Y));
                }
            }
            else
            {
                line[3] = d;
            }

            double x1, y1 = line[0].Y, x2, y2 = line[3].Y;
            double diffX = Math.Abs(pin.L - pinS.L) * StepC;
            double centre = XCorr + NumColumns * StepC / 2.0;
            if (pinS.C != pin.C)
            {
                if (pinS.C == LC) { x1 = centre - StepC / 2.0; x2 = x1 + diffX; }
                else if (pinS.C == RC) { x1 = centre + StepC / 2.0; x2 = x1 - diffX; }
                else { x1 = line[0].X; y1 = y2; x2 = line[3].X; }
            }
            else
            {
                if (pinS.C == LC) { x1 = line[0].X + (StepC + pinS.IC * StepC); x2 = x1; }
                else if (pinS.C == RC) { x1 = s.X - (StepC + pinS.IC * StepC); x2 = x1; }
                else { x1 = line[0].X; x2 = line[3].X; }
            }
            line[1] = new Point(x1, y1);
            line[2] = new Point(x2, y2);

            if (focus != null && (name == focus || pin.Source.Source == focus))
                highlighted.Add(line);
            Lines(g, pCur, line);
            Arrow(g, pCur, line[2], line[3], drawLine: false);
            if (pinS.IsNoise)
                Lines(g, new Pen(pCur.Brush, lw * 3, new DashStyle([0.5, 1.5], 0)), line);
        }

        foreach (var line in highlighted)
        {
            Lines(g, hlPen, line);
            Arrow(g, hlPen, line[2], line[3], drawLine: false);
        }

        // rubber band while dragging from a pin
        if (_pins.ContainsKey(_selected))
            g.DrawLine(pOrg, _mb, _me);
    }

    private static void Lines(DrawingContext g, Pen pen, Point[] pts)
    {
        var geo = new StreamGeometry();
        using (var c = geo.Open())
        {
            c.BeginFigure(pts[0], false);
            for (int i = 1; i < pts.Length; i++) c.LineTo(pts[i]);
            c.EndFigure(false);
        }
        g.DrawGeometry(null, pen, geo);
    }

    private static void Polygon(DrawingContext g, Pen pen, params Point[] pts)
    {
        var geo = new StreamGeometry();
        using (var c = geo.Open())
        {
            c.BeginFigure(pts[0], true);
            for (int i = 1; i < pts.Length; i++) c.LineTo(pts[i]);
            c.EndFigure(true);
        }
        g.DrawGeometry(null, pen, geo);
    }

    /// <summary>Line with an arrow head at <paramref name="to"/> (GDI+ ArrowAnchor end cap).</summary>
    private static void Arrow(DrawingContext g, Pen pen, Point from, Point to, bool drawLine = true)
    {
        if (drawLine) g.DrawLine(pen, from, to);
        var v = to - from;
        double len = Math.Sqrt(v.X * v.X + v.Y * v.Y);
        if (len < 0.1) return;
        double ux = v.X / len, uy = v.Y / len;
        double l = Math.Max(4, pen.Thickness * 4), w = l / 2;
        var back = new Point(to.X - ux * l, to.Y - uy * l);
        var geo = new StreamGeometry();
        using (var c = geo.Open())
        {
            c.BeginFigure(to, true);
            c.LineTo(new Point(back.X - uy * w, back.Y + ux * w));
            c.LineTo(new Point(back.X + uy * w, back.Y - ux * w));
            c.EndFigure(true);
        }
        g.DrawGeometry(pen.Brush, null, geo);
    }
}
