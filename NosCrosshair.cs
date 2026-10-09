// NosCrosshair — a click-through crosshair overlay with a big gallery of styles.
//
// Build (no SDK needed, the compiler ships with Windows):
//   C:\Windows\Microsoft.NET\Framework64\v4.0.30319\csc.exe /nologo /target:winexe /optimize
//     /reference:System.dll,System.Drawing.dll,System.Windows.Forms.dll
//     /win32icon:NosCrosshair.ico /out:NosCrosshair.exe NosCrosshair.cs
//
// The overlay is a separate topmost window drawn with per-pixel alpha
// (UpdateLayeredWindow), click-through and never activated, so it sits over games
// running windowed or borderless. Exclusive-fullscreen games draw over everything and
// will hide it — switch the game to borderless.

using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Globalization;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows.Forms;

// ---------------------------------------------------------------------------
// Palette — black and gold, same family as NosClicker
// ---------------------------------------------------------------------------
internal static class Pal
{
    public static readonly Color Ink = Color.FromArgb(11, 11, 13);
    public static readonly Color Panel = Color.FromArgb(20, 20, 24);
    public static readonly Color Field = Color.FromArgb(28, 28, 33);
    public static readonly Color Line = Color.FromArgb(46, 44, 38);
    public static readonly Color Accent = Color.FromArgb(240, 180, 41);
    public static readonly Color AccentLite = Color.FromArgb(255, 214, 102);
    public static readonly Color AccentDim = Color.FromArgb(122, 96, 32);
    public static readonly Color Text = Color.FromArgb(232, 230, 227);
    public static readonly Color Muted = Color.FromArgb(129, 123, 108);

    public static GraphicsPath Round(RectangleF r, float radius)
    {
        var p = new GraphicsPath();
        float d = radius * 2;
        p.AddArc(r.X, r.Y, d, d, 180, 90);
        p.AddArc(r.Right - d, r.Y, d, d, 270, 90);
        p.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
        p.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
        p.CloseFigure();
        return p;
    }
}

// ---------------------------------------------------------------------------
// Settings, stored in %APPDATA%\NosCrosshair\settings.ini
// ---------------------------------------------------------------------------
internal class Settings
{
    public string Style = "classic";
    public Color Color = Color.FromArgb(0, 255, 0);
    public Color OutlineColor = Color.Black;
    public int Size = 10, Thickness = 2, Gap = 4, Outline = 1, Opacity = 100, Rotation = 0;
    public int OffsetX = 0, OffsetY = 0, Monitor = 0;
    public bool CenterDot = false, Visible = true, Hotkeys = true;
    public string ImagePath = "";
    public string Category = "All";
    public int Backdrop = 0;

    public static string Dir
    {
        get { return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "NosCrosshair"); }
    }
    private static string File_ { get { return Path.Combine(Dir, "settings.ini"); } }

    public Settings Clone() { return (Settings)MemberwiseClone(); }

    public static Settings Load()
    {
        var s = new Settings();
        try
        {
            if (!File.Exists(File_)) return s;
            foreach (var raw in File.ReadAllLines(File_))
            {
                int eq = raw.IndexOf('=');
                if (eq < 0) continue;
                string k = raw.Substring(0, eq).Trim(), v = raw.Substring(eq + 1).Trim();
                int n;
                bool isNum = int.TryParse(v, NumberStyles.Integer, CultureInfo.InvariantCulture, out n);
                switch (k)
                {
                    case "style": s.Style = v; break;
                    case "color": if (isNum) s.Color = Color.FromArgb(255, Color.FromArgb(n)); break;
                    case "outlineColor": if (isNum) s.OutlineColor = Color.FromArgb(255, Color.FromArgb(n)); break;
                    case "size": if (isNum) s.Size = n; break;
                    case "thickness": if (isNum) s.Thickness = n; break;
                    case "gap": if (isNum) s.Gap = n; break;
                    case "outline": if (isNum) s.Outline = n; break;
                    case "opacity": if (isNum) s.Opacity = n; break;
                    case "rotation": if (isNum) s.Rotation = n; break;
                    case "offsetX": if (isNum) s.OffsetX = n; break;
                    case "offsetY": if (isNum) s.OffsetY = n; break;
                    case "monitor": if (isNum) s.Monitor = n; break;
                    case "centerDot": s.CenterDot = v == "1"; break;
                    case "visible": s.Visible = v == "1"; break;
                    case "hotkeys": s.Hotkeys = v == "1"; break;
                    case "image": s.ImagePath = v; break;
                    case "category": s.Category = v; break;
                    case "backdrop": if (isNum) s.Backdrop = n; break;
                }
            }
        }
        catch { }
        s.Sanitize();
        return s;
    }

    public void Sanitize()
    {
        Size = Clamp(Size, 1, 80); Thickness = Clamp(Thickness, 1, 12); Gap = Clamp(Gap, 0, 40);
        Outline = Clamp(Outline, 0, 6); Opacity = Clamp(Opacity, 10, 100);
        Rotation = ((Rotation % 360) + 360) % 360;
        OffsetX = Clamp(OffsetX, -400, 400); OffsetY = Clamp(OffsetY, -400, 400);
        if (Monitor < 0) Monitor = 0;
        if (Styles.Find(Style) == null) Style = "classic";
    }

    private static int Clamp(int v, int a, int b) { return Math.Max(a, Math.Min(b, v)); }

    public void Save()
    {
        try
        {
            Directory.CreateDirectory(Dir);
            var ci = CultureInfo.InvariantCulture;
            File.WriteAllLines(File_, new[]
            {
                "style=" + Style,
                "color=" + (Color.ToArgb() & 0xFFFFFF).ToString(ci),
                "outlineColor=" + (OutlineColor.ToArgb() & 0xFFFFFF).ToString(ci),
                "size=" + Size.ToString(ci), "thickness=" + Thickness.ToString(ci),
                "gap=" + Gap.ToString(ci), "outline=" + Outline.ToString(ci),
                "opacity=" + Opacity.ToString(ci), "rotation=" + Rotation.ToString(ci),
                "offsetX=" + OffsetX.ToString(ci), "offsetY=" + OffsetY.ToString(ci),
                "monitor=" + Monitor.ToString(ci),
                "centerDot=" + (CenterDot ? "1" : "0"), "visible=" + (Visible ? "1" : "0"),
                "hotkeys=" + (Hotkeys ? "1" : "0"),
                "image=" + ImagePath, "category=" + Category,
                "backdrop=" + Backdrop.ToString(ci),
            });
        }
        catch { }
    }
}

// ---------------------------------------------------------------------------
// Crosshair geometry.
//
// Every style is built from figures around (0,0). Open strokes (lines, arcs,
// chevrons) are widened into filled outlines with FLAT caps, so an outline wraps the
// ends of each arm by exactly the outline width. Closed strokes (rings, polygons) are
// drawn with a pen. Each figure is drawn separately, so overlapping figures never
// punch holes in each other the way one combined fill path would.
// ---------------------------------------------------------------------------
internal class Fig
{
    public GraphicsPath Path;
    public bool Fill;   // filled area; otherwise a closed outline stroked at Width
    public float Width;
}

internal class Shape
{
    public readonly List<Fig> Figs = new List<Fig>();
    public float S, T, G;
    public Image Picture;   // the "Your Image" style

    public Shape(float s, float t, float g) { S = s; T = t; G = g; }

    private static PointF Polar(float deg, float r)
    {
        double a = deg * Math.PI / 180.0;
        return new PointF((float)(Math.Cos(a) * r), (float)(Math.Sin(a) * r));
    }

    private void Widened(GraphicsPath p, float w)
    {
        using (var pen = new Pen(Color.Black, Math.Max(0.5f, w)))
        {
            pen.StartCap = LineCap.Flat; pen.EndCap = LineCap.Flat; pen.LineJoin = LineJoin.Miter;
            p.Widen(pen);
        }
        p.FillMode = FillMode.Winding;
        Figs.Add(new Fig { Path = p, Fill = true });
    }

    public void Line(float x1, float y1, float x2, float y2) { LineW(x1, y1, x2, y2, T); }

    public void LineW(float x1, float y1, float x2, float y2, float w)
    {
        if (Math.Abs(x1 - x2) < 0.01f && Math.Abs(y1 - y2) < 0.01f) return;
        var p = new GraphicsPath();
        p.AddLine(x1, y1, x2, y2);
        Widened(p, w);
    }

    // A straight arm pointing at `deg` (0 = right, 90 = down), from radius a to b.
    public void Arm(float deg, float a, float b) { ArmW(deg, a, b, T); }

    public void ArmW(float deg, float a, float b, float w)
    {
        var p1 = Polar(deg, a); var p2 = Polar(deg, b);
        LineW(p1.X, p1.Y, p2.X, p2.Y, w);
    }

    public void Arms(float a, float b, params float[] degs) { foreach (var d in degs) Arm(d, a, b); }

    public void Polyline(params PointF[] pts)
    {
        var p = new GraphicsPath();
        p.AddLines(pts);
        Widened(p, T);
    }

    public void Arc(float r, float start, float sweep)
    {
        var p = new GraphicsPath();
        p.AddArc(-r, -r, r * 2, r * 2, start, sweep);
        Widened(p, T);
    }

    public void Ring(float r) { Ring(0, 0, r); }

    public void Ring(float x, float y, float r)
    {
        var p = new GraphicsPath();
        p.AddEllipse(x - r, y - r, r * 2, r * 2);
        Figs.Add(new Fig { Path = p, Width = T });
    }

    public void Dot(float x, float y, float r)
    {
        var p = new GraphicsPath();
        p.AddEllipse(x - r, y - r, r * 2, r * 2);
        Figs.Add(new Fig { Path = p, Fill = true });
    }

    public void Block(float x, float y, float half)
    {
        var p = new GraphicsPath();
        p.AddRectangle(new RectangleF(x - half, y - half, half * 2, half * 2));
        Figs.Add(new Fig { Path = p, Fill = true });
    }

    public void Box(float half)
    {
        var p = new GraphicsPath();
        p.AddRectangle(new RectangleF(-half, -half, half * 2, half * 2));
        Figs.Add(new Fig { Path = p, Width = T });
    }

    public void Poly(params PointF[] pts)
    {
        var p = new GraphicsPath();
        p.AddPolygon(pts);
        Figs.Add(new Fig { Path = p, Width = T });
    }

    public void FillPoly(params PointF[] pts)
    {
        var p = new GraphicsPath();
        p.AddPolygon(pts);
        Figs.Add(new Fig { Path = p, Fill = true });
    }

    // Regular polygon / star corner points, starting at `startDeg`.
    public static PointF[] Ngon(int n, float r, float startDeg)
    {
        var pts = new PointF[n];
        for (int i = 0; i < n; i++) pts[i] = Polar(startDeg + 360f * i / n, r);
        return pts;
    }

    public static PointF[] StarPts(int points, float outer, float inner, float startDeg)
    {
        var pts = new PointF[points * 2];
        for (int i = 0; i < points * 2; i++)
            pts[i] = Polar(startDeg + 180f * i / points, i % 2 == 0 ? outer : inner);
        return pts;
    }

    public static PointF P(float x, float y) { return new PointF(x, y); }

    public float DotR { get { return Math.Max(1f, T * 0.5f + 0.5f); } }

    // "Unit" for drawn symbols: they are designed on a ±10 grid and scale with size+gap.
    public float U { get { return (G + S) / 10f; } }

    public static PointF At(float deg, float r) { return Polar(deg, r); }
    public static PointF At(float cx, float cy, float deg, float r) { var p = Polar(deg, r); return new PointF(cx + p.X, cy + p.Y); }

    public void Seg(PointF a, PointF b) { Line(a.X, a.Y, b.X, b.Y); }

    public void ArcAt(float cx, float cy, float r, float start, float sweep)
    {
        var p = new GraphicsPath();
        p.AddArc(cx - r, cy - r, r * 2, r * 2, start, sweep);
        Widened(p, T);
    }

    public void Oval(float cx, float cy, float rx, float ry, float rot, bool fill)
    {
        var p = new GraphicsPath();
        p.AddEllipse(cx - rx, cy - ry, rx * 2, ry * 2);
        if (rot != 0) using (var m = new Matrix()) { m.RotateAt(rot, new PointF(cx, cy)); p.Transform(m); }
        Figs.Add(new Fig { Path = p, Fill = fill, Width = T });
    }

    // Filled ring sector between radii r1 and r2.
    public void Sector(float r1, float r2, float start, float sweep)
    {
        var p = new GraphicsPath();
        p.AddArc(-r2, -r2, r2 * 2, r2 * 2, start, sweep);
        p.AddArc(-r1, -r1, r1 * 2, r1 * 2, start + sweep, -sweep);
        p.CloseFigure();
        Figs.Add(new Fig { Path = p, Fill = true });
    }

    public void Closed(GraphicsPath p) { Figs.Add(new Fig { Path = p, Width = T }); }
    public void Filled(GraphicsPath p) { Figs.Add(new Fig { Path = p, Fill = true }); }

    // A ">" shaped chevron whose tip sits at (cx,cy) and points toward `deg`.
    public void Chev(float cx, float cy, float deg, float len, float spread)
    {
        var a = Polar(deg + 180 - spread, len); var b = Polar(deg + 180 + spread, len);
        Polyline(P(cx + a.X, cy + a.Y), P(cx, cy), P(cx + b.X, cy + b.Y));
    }

    // Solid arrowhead with its tip at (cx,cy) pointing toward `deg`.
    public void Tri(float cx, float cy, float deg, float len, float half)
    {
        var back = Polar(deg + 180, len); var side = Polar(deg + 90, half);
        FillPoly(P(cx, cy), P(cx + back.X + side.X, cy + back.Y + side.Y), P(cx + back.X - side.X, cy + back.Y - side.Y));
    }

    // Filled arbitrary polygon from (x,y) pairs scaled by `k`.
    public static PointF[] Pts(float k, params float[] xy)
    {
        var pts = new PointF[xy.Length / 2];
        for (int i = 0; i < pts.Length; i++) pts[i] = new PointF(xy[i * 2] * k, xy[i * 2 + 1] * k);
        return pts;
    }

    public static GraphicsPath HeartPath(float s, bool flip)
    {
        var p = new GraphicsPath();
        p.AddBezier(0, -4 * s, -2 * s, -9 * s, -10 * s, -7 * s, -9 * s, -1 * s);
        p.AddBezier(-9 * s, -1 * s, -8 * s, 4 * s, -3 * s, 6 * s, 0, 9 * s);
        p.AddBezier(0, 9 * s, 3 * s, 6 * s, 8 * s, 4 * s, 9 * s, -1 * s);
        p.AddBezier(9 * s, -1 * s, 10 * s, -7 * s, 2 * s, -9 * s, 0, -4 * s);
        p.CloseFigure();
        if (flip) using (var m = new Matrix(1, 0, 0, -1, 0, 0)) p.Transform(m);
        return p;
    }
}

internal class Style
{
    public string Id, Name, Cat;
    public Action<Shape> Build;
    public Style(string id, string name, string cat, Action<Shape> b) { Id = id; Name = name; Cat = cat; Build = b; }
}

internal static class Styles
{
    public static readonly string[] Categories = { "All", "Classic", "Dots", "Circles", "Shapes", "Geometric", "Scopes", "Fun", "Symbols" };

    public static readonly List<Style> All = new List<Style>();

    private static void Add(string id, string name, string cat, Action<Shape> b) { All.Add(new Style(id, name, cat, b)); }

    public static Style Find(string id)
    {
        foreach (var s in All) if (s.Id == id) return s;
        return null;
    }

    static Styles()
    {
        // ---- Classic ------------------------------------------------------
        Add("classic", "Classic", "Classic", x => x.Arms(x.G, x.G + x.S, 0, 90, 180, 270));
        Add("plus", "Solid Plus", "Classic", x => { float r = x.G + x.S; x.Line(-r, 0, r, 0); x.Line(0, -r, 0, r); });
        Add("micro", "Micro Cross", "Classic", x => x.Arms(x.G, x.G + x.S * 0.5f, 0, 90, 180, 270));
        Add("long", "Long Lines", "Classic", x => x.Arms(x.G, x.G + x.S * 3, 0, 90, 180, 270));
        Add("tcross", "T-Cross", "Classic", x => x.Arms(x.G, x.G + x.S, 0, 90, 180));
        Add("invt", "Inverted T", "Classic", x => x.Arms(x.G, x.G + x.S, 0, 180, 270));
        Add("tall", "Tall Cross", "Classic", x => { x.Arms(x.G, x.G + x.S, 0, 180); x.Arms(x.G, x.G + x.S * 1.7f, 90, 270); });
        Add("wide", "Wide Cross", "Classic", x => { x.Arms(x.G, x.G + x.S * 1.7f, 0, 180); x.Arms(x.G, x.G + x.S, 90, 270); });
        Add("xcross", "X Cross", "Classic", x => x.Arms(x.G, x.G + x.S, 45, 135, 225, 315));
        Add("solidx", "Solid X", "Classic", x => { float r = x.G + x.S; x.Arms(0, r, 45, 135, 225, 315); });
        Add("burst", "Starburst", "Classic", x => { x.Arms(x.G, x.G + x.S, 0, 90, 180, 270); x.Arms(x.G, x.G + x.S * 0.6f, 45, 135, 225, 315); });
        Add("rails", "Twin Rails", "Classic", x =>
        {
            float o = x.T, a = x.G, b = x.G + x.S;
            x.Line(a, -o, b, -o); x.Line(a, o, b, o); x.Line(-a, -o, -b, -o); x.Line(-a, o, -b, o);
            x.Line(-o, a, -o, b); x.Line(o, a, o, b); x.Line(-o, -a, -o, -b); x.Line(o, -a, o, -b);
        });
        Add("hline", "Horizon", "Classic", x => x.Arms(x.G, x.G + x.S * 1.5f, 0, 180));
        Add("vline", "Pillar", "Classic", x => x.Arms(x.G, x.G + x.S * 1.5f, 90, 270));
        Add("tri", "Tri-Arms", "Classic", x => x.Arms(x.G, x.G + x.S, 90, 210, 330));
        Add("yarms", "Y-Arms", "Classic", x => x.Arms(x.G, x.G + x.S, 270, 30, 150));
        Add("aster", "Asterisk", "Classic", x => x.Arms(x.G, x.G + x.S, 30, 90, 150, 210, 270, 330));
        Add("crossbox", "Cross + Box", "Classic", x => { x.Arms(x.G, x.G + x.S * 1.2f, 0, 90, 180, 270); x.Box(x.G + x.S * 0.55f); });
        Add("dotcross", "Dotted Cross", "Classic", x =>
        {
            float r = x.DotR;
            foreach (float d in new float[] { 0, 90, 180, 270 })
                for (int i = 0; i < 3; i++)
                {
                    double a = d * Math.PI / 180, rr = x.G + x.S * i / 2.0;
                    x.Dot((float)(Math.Cos(a) * rr), (float)(Math.Sin(a) * rr), r);
                }
        });

        // ---- Dots ---------------------------------------------------------
        Add("dot", "Dot", "Dots", x => x.Dot(0, 0, x.S * 0.25f + x.T * 0.5f));
        Add("microdot", "Micro Dot", "Dots", x => x.Dot(0, 0, Math.Max(1f, x.T * 0.6f)));
        Add("pixel", "Pixel Square", "Dots", x => x.Block(0, 0, x.S * 0.15f + x.T * 0.5f));
        Add("hollow", "Hollow Dot", "Dots", x => x.Ring(x.S * 0.3f + x.T * 0.5f));
        Add("dotring", "Dot + Ring", "Dots", x => { x.Dot(0, 0, x.DotR); x.Ring(x.G + x.S * 0.6f); });
        Add("trio", "Dot Trio", "Dots", x =>
        {
            foreach (var p in Shape.Ngon(3, x.G + x.S * 0.4f, 270)) x.Dot(p.X, p.Y, x.DotR);
        });
        Add("four", "Four Dots", "Dots", x =>
        {
            foreach (var p in Shape.Ngon(4, x.G + x.S * 0.5f, 0)) x.Dot(p.X, p.Y, x.DotR);
        });
        Add("five", "Dot Plus", "Dots", x =>
        {
            x.Dot(0, 0, x.DotR);
            foreach (var p in Shape.Ngon(4, x.G + x.S * 0.5f, 0)) x.Dot(p.X, p.Y, x.DotR);
        });
        Add("ringdots", "Dot Halo", "Dots", x =>
        {
            x.Dot(0, 0, x.DotR);
            foreach (var p in Shape.Ngon(8, x.G + x.S * 0.8f, 0)) x.Dot(p.X, p.Y, x.DotR * 0.8f);
        });
        Add("bigdot", "Big Dot", "Dots", x => x.Dot(0, 0, x.S * 0.5f + x.T));

        // ---- Circles ------------------------------------------------------
        Add("circle", "Circle", "Circles", x => x.Ring(x.G + x.S));
        Add("circlecross", "Circle Cross", "Circles", x =>
        {
            x.Ring(x.G + x.S * 0.6f);
            x.Arms(x.G + x.S * 0.25f, x.G + x.S * 1.3f, 0, 90, 180, 270);
        });
        Add("ringcross", "Ringed Cross", "Circles", x => { x.Ring(x.G + x.S); x.Arms(x.G, x.G + x.S * 0.7f, 0, 90, 180, 270); });
        Add("crossedcircle", "Crossed Circle", "Circles", x =>
        {
            float r = x.G + x.S; x.Ring(r); x.Line(-r, 0, r, 0); x.Line(0, -r, 0, r);
        });
        Add("target", "Target", "Circles", x =>
        {
            x.Dot(0, 0, x.DotR); x.Ring(x.G + x.S * 0.5f); x.Ring(x.G * 2 + x.S * 1.1f);
        });
        Add("double", "Double Ring", "Circles", x => { x.Ring(x.G + x.S * 0.6f); x.Ring(x.G + x.S * 1.2f); });
        Add("broken", "Broken Ring", "Circles", x =>
        {
            foreach (float d in new float[] { 45, 135, 225, 315 }) x.Arc(x.G + x.S, d - 30, 60);
        });
        Add("split", "Split Ring", "Circles", x =>
        {
            x.Dot(0, 0, x.DotR);
            foreach (float d in new float[] { 0, 90, 180, 270 }) x.Arc(x.G + x.S, d - 30, 60);
        });
        Add("horseshoe", "Horseshoe", "Circles", x => { x.Dot(0, 0, x.DotR); x.Arc(x.G + x.S, 150, 240); });
        Add("holo", "Holo Sight", "Circles", x =>
        {
            float r = x.G + x.S * 1.5f;
            x.Dot(0, 0, x.DotR); x.Ring(r);
            x.Arms(r, r + x.S * 0.5f, 0, 90, 180, 270);
        });
        Add("reddot", "Red Dot Ring", "Circles", x => { x.Dot(0, 0, x.DotR); x.Ring(x.G + x.S * 2); });
        Add("sun", "Sun", "Circles", x =>
        {
            float r = x.G + x.S * 0.5f;
            x.Ring(r);
            x.Arms(r + x.T * 1.5f, r + x.S * 0.7f, 0, 45, 90, 135, 180, 225, 270, 315);
        });
        Add("cqb", "CQB Ring", "Circles", x =>
        {
            float r = x.G + x.S * 2.2f;
            x.Dot(0, 0, x.DotR); x.Ring(r);
            x.Arms(r - x.S * 0.6f, r, 0, 90, 180, 270);
        });

        // ---- Shapes -------------------------------------------------------
        Add("square", "Square", "Shapes", x => x.Box(x.G + x.S * 0.6f));
        Add("squaredot", "Square + Dot", "Shapes", x => { x.Box(x.G + x.S * 0.6f); x.Dot(0, 0, x.DotR); });
        Add("diamond", "Diamond", "Shapes", x => x.Poly(Shape.Ngon(4, x.G + x.S * 0.8f, 0)));
        Add("diamonddot", "Diamond + Dot", "Shapes", x => { x.Poly(Shape.Ngon(4, x.G + x.S * 0.8f, 0)); x.Dot(0, 0, x.DotR); });
        Add("triangle", "Triangle", "Shapes", x => x.Poly(Shape.Ngon(3, x.G + x.S * 0.8f, 270)));
        Add("tridown", "Down Triangle", "Shapes", x => x.Poly(Shape.Ngon(3, x.G + x.S * 0.8f, 90)));
        Add("hex", "Hexagon", "Shapes", x => x.Poly(Shape.Ngon(6, x.G + x.S * 0.75f, 0)));
        Add("octo", "Octagon + Dot", "Shapes", x => { x.Poly(Shape.Ngon(8, x.G + x.S * 0.8f, 22.5f)); x.Dot(0, 0, x.DotR); });
        Add("chevron", "Chevron", "Shapes", x =>
        {
            float y = x.G * 0.5f;
            x.Polyline(Shape.P(-x.S, y + x.S * 0.8f), Shape.P(0, y), Shape.P(x.S, y + x.S * 0.8f));
        });
        Add("vchev", "V Chevron", "Shapes", x =>
        {
            float y = -x.G * 0.5f;
            x.Polyline(Shape.P(-x.S, y - x.S * 0.8f), Shape.P(0, y), Shape.P(x.S, y - x.S * 0.8f));
        });
        Add("brackets", "Brackets", "Shapes", x =>
        {
            float o = x.G + x.S * 0.5f, h = x.S * 0.6f, w = x.S * 0.35f;
            x.Polyline(Shape.P(-o + w, -h), Shape.P(-o, -h), Shape.P(-o, h), Shape.P(-o + w, h));
            x.Polyline(Shape.P(o - w, -h), Shape.P(o, -h), Shape.P(o, h), Shape.P(o - w, h));
        });
        Add("angles", "Angle Brackets", "Shapes", x =>
        {
            float o = x.G + x.S * 0.4f, h = x.S * 0.6f, w = x.S * 0.45f;
            x.Polyline(Shape.P(-o + w, -h), Shape.P(-o, 0), Shape.P(-o + w, h));
            x.Polyline(Shape.P(o - w, -h), Shape.P(o, 0), Shape.P(o - w, h));
        });
        Add("corners", "Corners", "Shapes", x =>
        {
            float o = x.G + x.S * 0.6f, l = x.S * 0.45f;
            foreach (var sx in new[] { -1f, 1f })
                foreach (var sy in new[] { -1f, 1f })
                    x.Polyline(Shape.P(sx * (o - l), sy * o), Shape.P(sx * o, sy * o), Shape.P(sx * o, sy * (o - l)));
        });
        Add("boxx", "Box X", "Shapes", x => { x.Box(x.G + x.S * 0.6f); x.Arms(x.G * 0.5f, x.G + x.S * 0.6f, 45, 135, 225, 315); });
        Add("arrow", "Arrow Tip", "Shapes", x =>
        {
            float y = x.G * 0.25f;
            x.FillPoly(Shape.P(0, y), Shape.P(-x.S * 0.5f, y + x.S * 0.9f), Shape.P(x.S * 0.5f, y + x.S * 0.9f));
        });
        Add("pointer", "Pointer", "Shapes", x =>
        {
            float y = -x.G * 0.25f;
            x.FillPoly(Shape.P(0, y), Shape.P(-x.S * 0.5f, y - x.S * 0.9f), Shape.P(x.S * 0.5f, y - x.S * 0.9f));
        });
        Add("block", "Block", "Shapes", x => x.Block(0, 0, x.S * 0.35f + 1));

        // ---- Scopes -------------------------------------------------------
        Add("sniper", "Sniper", "Scopes", x => { x.Arms(x.G, x.G + x.S * 4, 0, 90, 180, 270); x.Ring(x.G + x.S * 3); });
        Add("sniperdot", "Sniper Dot", "Scopes", x =>
        {
            x.Dot(0, 0, x.DotR); x.Arms(x.G + x.S * 1.2f, x.G + x.S * 4, 0, 90, 180, 270); x.Ring(x.G + x.S * 3);
        });
        Add("mildot", "Mil-Dot", "Scopes", x =>
        {
            float r = x.S * 4 + x.G;
            x.LineW(-r, 0, r, 0, Math.Max(1, x.T * 0.5f)); x.LineW(0, -r, 0, r, Math.Max(1, x.T * 0.5f));
            float step = Math.Max(3, r / 5);
            for (float d = step; d < r - 1; d += step)
            {
                x.Dot(d, 0, x.DotR); x.Dot(-d, 0, x.DotR); x.Dot(0, d, x.DotR); x.Dot(0, -d, x.DotR);
            }
        });
        Add("duplex", "Duplex", "Scopes", x =>
        {
            float mid = x.G + x.S * 1.8f, end = x.G + x.S * 4;
            x.Arms(x.G, mid, 0, 90, 180, 270);
            foreach (float d in new float[] { 0, 90, 180, 270 }) x.ArmW(d, mid, end, x.T * 2.5f);
        });
        Add("post", "Post Reticle", "Scopes", x =>
        {
            x.ArmW(90, x.G, x.G + x.S * 4, x.T * 2.2f);
            x.Arms(x.G, x.G + x.S * 4, 0, 180);
        });
        Add("acog", "Chevron Scope", "Scopes", x =>
        {
            x.Polyline(Shape.P(-x.S * 0.6f, x.S * 0.6f), Shape.P(0, 0), Shape.P(x.S * 0.6f, x.S * 0.6f));
            float top = x.S * 0.6f + x.G;
            x.Line(0, top, 0, top + x.S * 2);
            x.Line(-x.S * 0.4f, top + x.S, x.S * 0.4f, top + x.S);
            x.Line(-x.S * 0.25f, top + x.S * 2, x.S * 0.25f, top + x.S * 2);
        });
        Add("ladder", "Drop Ladder", "Scopes", x =>
        {
            x.Arms(x.G, x.G + x.S, 0, 180, 270);
            x.Arm(90, x.G, x.G + x.S * 4);
            for (int i = 1; i <= 3; i++)
            {
                float y = x.G + x.S * i, w = x.S * (0.9f - i * 0.2f);
                x.Line(-w, y, w, y);
            }
        });
        Add("range", "Rangefinder", "Scopes", x =>
        {
            float end = x.G + x.S * 3;
            x.Arms(x.G, end, 0, 90, 180, 270);
            float tick = Math.Max(2, x.S * 0.25f), step = Math.Max(3, x.S * 0.75f);
            for (float d = x.G + step; d <= end + 0.1f; d += step)
            {
                x.Line(d, -tick, d, tick); x.Line(-d, -tick, -d, tick);
                x.Line(-tick, d, tick, d); x.Line(-tick, -d, tick, -d);
            }
        });
        Add("scopering", "Tactical Ring", "Scopes", x =>
        {
            float r = x.G + x.S * 2.5f;
            x.Ring(r); x.Dot(0, 0, x.DotR);
            x.Arms(x.G, x.G + x.S, 0, 90, 180, 270);
            x.Arms(r - x.S * 0.4f, r + x.S * 0.4f, 45, 135, 225, 315);
        });

        // ---- Fun ----------------------------------------------------------
        Add("star", "Star", "Fun", x => x.Poly(Shape.StarPts(5, x.G + x.S, (x.G + x.S) * 0.45f, 270)));
        Add("shuriken", "Shuriken", "Fun", x => x.FillPoly(Shape.StarPts(4, x.G + x.S, (x.G + x.S) * 0.3f, 270)));
        Add("heart", "Heart", "Fun", x =>
        {
            float s = (x.G + x.S) / 10f;
            var p = new GraphicsPath();
            p.AddBezier(0, -4 * s, -2 * s, -9 * s, -10 * s, -7 * s, -9 * s, -1 * s);
            p.AddBezier(-9 * s, -1 * s, -8 * s, 4 * s, -3 * s, 6 * s, 0, 9 * s);
            p.AddBezier(0, 9 * s, 3 * s, 6 * s, 8 * s, 4 * s, 9 * s, -1 * s);
            p.AddBezier(9 * s, -1 * s, 10 * s, -7 * s, 2 * s, -9 * s, 0, -4 * s);
            p.CloseFigure();
            x.Figs.Add(new Fig { Path = p, Width = x.T });
        });
        Add("smile", "Smiley", "Fun", x =>
        {
            float r = x.G + x.S;
            x.Ring(r);
            x.Dot(-r * 0.35f, -r * 0.25f, x.DotR); x.Dot(r * 0.35f, -r * 0.25f, x.DotR);
            var p = new GraphicsPath();
            float a = r * 0.55f;
            p.AddArc(-a, -a, a * 2, a * 2, 25, 130);
            using (var pen = new Pen(Color.Black, x.T)) p.Widen(pen);
            p.FillMode = FillMode.Winding;
            x.Figs.Add(new Fig { Path = p, Fill = true });
        });
        Add("peace", "Peace", "Fun", x =>
        {
            float r = x.G + x.S;
            x.Ring(r); x.Line(0, -r, 0, r); x.Arm(45, 0, r); x.Arm(135, 0, r);
        });
        Add("flower", "Flower", "Fun", x =>
        {
            x.Dot(0, 0, x.DotR);
            foreach (var p in Shape.Ngon(6, x.G + x.S * 0.6f, 270)) x.Ring(p.X, p.Y, x.S * 0.35f);
        });
        Add("bolt", "Lightning", "Fun", x =>
        {
            float s = (x.G + x.S) / 10f;
            x.FillPoly(Shape.P(2 * s, -10 * s), Shape.P(-5 * s, 1 * s), Shape.P(-0.5f * s, 1 * s),
                       Shape.P(-2 * s, 10 * s), Shape.P(5 * s, -1.5f * s), Shape.P(0.5f * s, -1.5f * s));
        });
        Add("crown", "Crown", "Fun", x =>
        {
            float s = (x.G + x.S) / 10f;
            x.Poly(Shape.P(-8 * s, 5 * s), Shape.P(-9 * s, -5 * s), Shape.P(-4 * s, 0), Shape.P(0, -7 * s),
                   Shape.P(4 * s, 0), Shape.P(9 * s, -5 * s), Shape.P(8 * s, 5 * s));
        });
        Add("spiral", "Spiral", "Fun", x =>
        {
            var pts = new List<PointF>();
            float max = x.G + x.S * 1.2f;
            for (int i = 0; i <= 120; i++)
            {
                double t = i / 120.0, a = t * Math.PI * 5;
                pts.Add(new PointF((float)(Math.Cos(a) * max * t), (float)(Math.Sin(a) * max * t)));
            }
            x.Polyline(pts.ToArray());
        });
        Add("eye", "Eye", "Fun", x =>
        {
            float w = x.G + x.S * 1.3f, h = w * 0.55f;
            var p = new GraphicsPath();
            p.AddBezier(-w, 0, -w * 0.4f, -h * 1.4f, w * 0.4f, -h * 1.4f, w, 0);
            p.AddBezier(w, 0, w * 0.4f, h * 1.4f, -w * 0.4f, h * 1.4f, -w, 0);
            p.CloseFigure();
            x.Figs.Add(new Fig { Path = p, Width = x.T });
            x.Dot(0, 0, h * 0.5f);
        });
        Add("atom", "Atom", "Fun", x =>
        {
            float r = x.G + x.S * 1.2f;
            x.Dot(0, 0, x.DotR * 1.3f);
            foreach (float d in new float[] { 0, 60, 120 })
            {
                var p = new GraphicsPath();
                p.AddEllipse(-r, -r * 0.35f, r * 2, r * 0.7f);
                using (var m = new Matrix()) { m.Rotate(d); p.Transform(m); }
                x.Figs.Add(new Fig { Path = p, Width = x.T });
            }
        });

        AddMore();

        Add("custom", "Your Image", "Fun", x => { });
    }

    private static readonly string[] PolyNames =
        { "", "", "", "Triangle", "Tilted Square", "Pentagon", "Hexagon", "Heptagon", "Octagon", "Nonagon", "Decagon", "Hendecagon", "Dodecagon" };

    // The second wave of styles. Families are generated in loops; note every loop copies
    // its counter into a local first, because C# 5 lambdas capture the variable, not the value.
    private static void AddMore()
    {
        // ---- Classic, more ------------------------------------------------
        Add("crossdot", "Cross + Dot", "Classic", x => { x.Arms(x.G, x.G + x.S, 0, 90, 180, 270); x.Dot(0, 0, x.DotR); });
        Add("tight", "Tight Cross", "Classic", x => x.Arms(x.G * 0.5f, x.G * 0.5f + x.S * 0.6f, 0, 90, 180, 270));
        Add("compact", "Compact Plus", "Classic", x => { float r = x.S * 0.6f; x.Line(-r, 0, r, 0); x.Line(0, -r, 0, r); });
        Add("hairline", "Hairline", "Classic", x =>
        {
            float w = Math.Max(1, x.T * 0.5f);
            foreach (float d in new float[] { 0, 90, 180, 270 }) x.ArmW(d, x.G, x.G + x.S * 2.5f, w);
        });
        Add("bold", "Bold Cross", "Classic", x => { foreach (float d in new float[] { 0, 90, 180, 270 }) x.ArmW(d, x.G, x.G + x.S * 0.8f, x.T * 2); });
        Add("taper", "Tapered", "Classic", x =>
        {
            foreach (float d in new float[] { 0, 90, 180, 270 })
            {
                var a = Shape.At(d, x.G); var b = Shape.At(d, x.G + x.S);
                var n1 = Shape.At(d + 90, x.T * 0.3f); var n2 = Shape.At(d + 90, x.T * 1.3f);
                x.FillPoly(Shape.P(a.X + n1.X, a.Y + n1.Y), Shape.P(b.X + n2.X, b.Y + n2.Y), Shape.P(b.X - n2.X, b.Y - n2.Y), Shape.P(a.X - n1.X, a.Y - n1.Y));
            }
        });
        Add("spikes", "Spikes", "Classic", x =>
        {
            foreach (float d in new float[] { 0, 90, 180, 270 }) x.Tri(Shape.At(d, x.G).X, Shape.At(d, x.G).Y, d + 180, x.S, x.T * 1.3f);
        });
        Add("tends", "T-Ends", "Classic", x =>
        {
            x.Arms(x.G, x.G + x.S, 0, 90, 180, 270);
            float e = x.G + x.S, c = x.S * 0.3f;
            x.Line(e, -c, e, c); x.Line(-e, -c, -e, c); x.Line(-c, e, c, e); x.Line(-c, -e, c, -e);
        });
        Add("arrowarms", "Arrow Arms", "Classic", x =>
        {
            x.Arms(x.G + x.S * 0.3f, x.G + x.S * 1.2f, 0, 90, 180, 270);
            foreach (float d in new float[] { 0, 90, 180, 270 }) { var p = Shape.At(d, x.G); x.Tri(p.X, p.Y, d + 180, x.S * 0.45f, x.S * 0.3f + x.T * 0.5f); }
        });
        Add("barbed", "Barbed", "Classic", x =>
        {
            x.Arms(x.G, x.G + x.S * 1.3f, 0, 90, 180, 270);
            foreach (float d in new float[] { 0, 90, 180, 270 }) { var p = Shape.At(d, x.G + x.S * 0.6f); x.Chev(p.X, p.Y, d + 180, x.S * 0.35f, 45); }
        });
        Add("splitarms", "Split Arms", "Classic", x =>
        {
            x.Arms(x.G, x.G + x.S * 0.4f, 0, 90, 180, 270); x.Arms(x.G + x.S * 0.65f, x.G + x.S * 1.1f, 0, 90, 180, 270);
        });
        Add("dashed", "Dashed Lines", "Classic", x =>
        {
            for (int i = 0; i < 4; i++) x.Arms(x.G + x.S * 0.6f * i, x.G + x.S * 0.6f * i + x.S * 0.35f, 0, 90, 180, 270);
        });
        Add("longdrop", "Long Drop", "Classic", x => { x.Arms(x.G, x.G + x.S, 0, 180, 270); x.Arm(90, x.G, x.G + x.S * 1.8f); });
        Add("crossticks", "Plus + Ticks", "Classic", x =>
        {
            x.Arms(x.G, x.G + x.S, 0, 90, 180, 270);
            foreach (float d in new float[] { 45, 135, 225, 315 }) { var p = Shape.At(d, x.G + x.S * 0.6f); x.Dot(p.X, p.Y, x.DotR); }
        });
        Add("horizondot", "Horizon Dot", "Classic", x => { x.Arms(x.G, x.G + x.S * 1.5f, 0, 180); x.Dot(0, 0, x.DotR); });
        Add("pillardot", "Pillar Dot", "Classic", x => { x.Arms(x.G, x.G + x.S * 1.5f, 90, 270); x.Dot(0, 0, x.DotR); });
        Add("openbox", "Open Box", "Classic", x =>
        {
            float h = x.G + x.S * 0.7f, l = h * 0.55f;
            x.Line(-l, -h, l, -h); x.Line(-l, h, l, h); x.Line(-h, -l, -h, l); x.Line(h, -l, h, l);
        });
        Add("lshape", "L-Shape", "Classic", x => x.Arms(x.G, x.G + x.S, 90, 180));
        Add("gammaflip", "Corner Ray", "Classic", x => x.Arms(x.G, x.G + x.S, 0, 270));
        Add("xdot", "X + Dot", "Classic", x => { x.Arms(x.G, x.G + x.S, 45, 135, 225, 315); x.Dot(0, 0, x.DotR); });
        Add("xticks", "X Long", "Classic", x => x.Arms(x.G, x.G + x.S * 2.5f, 45, 135, 225, 315));
        Add("tdot", "T-Dot", "Classic", x => { x.Arms(x.G, x.G + x.S, 0, 90, 180); x.Dot(0, 0, x.DotR); });
        Add("crossring2", "Cross in Ring", "Classic", x => { x.Arms(x.G, x.G + x.S * 0.6f, 0, 90, 180, 270); x.Ring(x.G + x.S * 0.8f); x.Dot(0, 0, x.DotR); });
        Add("bigsmall", "Big + Small", "Classic", x => { x.Arms(x.G, x.G + x.S * 0.5f, 0, 90, 180, 270); x.Arms(x.G + x.S, x.G + x.S * 2f, 0, 90, 180, 270); });
        Add("railsx", "Twin X", "Classic", x =>
        {
            float o = x.T;
            foreach (float d in new float[] { 45, 135, 225, 315 })
            {
                var n = Shape.At(d + 90, o); var a = Shape.At(d, x.G); var b = Shape.At(d, x.G + x.S);
                x.Line(a.X + n.X, a.Y + n.Y, b.X + n.X, b.Y + n.Y); x.Line(a.X - n.X, a.Y - n.Y, b.X - n.X, b.Y - n.Y);
            }
        });

        // Spokes: n evenly spaced arms, gapped and solid.
        foreach (int n0 in new[] { 3, 5, 6, 7, 8, 9, 10, 12 })
        {
            int n = n0;
            Add("spoke" + n, n + "-Spoke", "Geometric", x => { for (int i = 0; i < n; i++) x.Arm(270 + 360f * i / n, x.G, x.G + x.S); });
        }
        foreach (int n0 in new[] { 3, 5, 6, 8, 12 })
        {
            int n = n0;
            Add("wheel" + n, n + "-Spoke Solid", "Geometric", x => { for (int i = 0; i < n; i++) x.Arm(270 + 360f * i / n, 0, x.G + x.S); });
        }

        // ---- Dots, more ---------------------------------------------------
        foreach (int n0 in new[] { 3, 4, 5, 6, 8, 10, 12 })
        {
            int n = n0;
            Add("dring" + n, n + "-Dot Ring", "Dots", x => { foreach (var p in Shape.Ngon(n, x.G + x.S * 0.7f, 270)) x.Dot(p.X, p.Y, x.DotR); });
            Add("dringc" + n, n + "-Dot Ring + Center", "Dots", x =>
            {
                x.Dot(0, 0, x.DotR);
                foreach (var p in Shape.Ngon(n, x.G + x.S * 0.7f, 270)) x.Dot(p.X, p.Y, x.DotR);
            });
        }
        Add("dotsquare", "Dot Square", "Dots", x => { foreach (var p in Shape.Ngon(4, x.G + x.S * 0.6f, 45)) x.Dot(p.X, p.Y, x.DotR); });
        Add("dotgrid", "Dot Grid", "Dots", x =>
        {
            float s = x.G + x.S * 0.4f;
            for (int i = -1; i <= 1; i++) for (int j = -1; j <= 1; j++) x.Dot(i * s, j * s, x.DotR);
        });
        Add("dotline", "Dot Line", "Dots", x => { float s = x.G + x.S * 0.3f; for (int i = -2; i <= 2; i++) x.Dot(i * s, 0, x.DotR); });
        Add("dotcol", "Dot Column", "Dots", x => { float s = x.G + x.S * 0.3f; for (int i = -2; i <= 2; i++) x.Dot(0, i * s, x.DotR); });
        Add("dotv", "Dot V", "Dots", x =>
        {
            x.Dot(0, 0, x.DotR);
            for (int i = 1; i <= 3; i++) { float s = (x.G + x.S * 0.3f) * i; x.Dot(-s * 0.8f, s, x.DotR); x.Dot(s * 0.8f, s, x.DotR); }
        });
        Add("dotx", "Dot X", "Dots", x =>
        {
            x.Dot(0, 0, x.DotR);
            foreach (float d in new float[] { 45, 135, 225, 315 }) for (int i = 1; i <= 2; i++) { var p = Shape.At(d, (x.G + x.S * 0.4f) * i); x.Dot(p.X, p.Y, x.DotR); }
        });
        Add("blockring", "Pixel Ring", "Dots", x => { foreach (var p in Shape.Ngon(8, x.G + x.S * 0.7f, 0)) x.Block(p.X, p.Y, x.DotR); });
        Add("fatfour", "Fat Four", "Dots", x => { foreach (var p in Shape.Ngon(4, x.G + x.S * 0.6f, 0)) x.Dot(p.X, p.Y, x.T * 1.2f + 1); });
        Add("dotchev", "Dot Arrow", "Dots", x => { x.Dot(0, 0, x.DotR); x.Chev(0, x.G + x.S * 0.3f, 270, x.S * 0.6f, 45); });
        Add("bubbles", "Bubbles", "Dots", x => { x.Dot(0, 0, x.DotR); foreach (var p in Shape.Ngon(6, x.G + x.S * 0.7f, 270)) x.Ring(p.X, p.Y, x.S * 0.18f + 1); });
        Add("pixelplus", "Pixel Plus", "Dots", x =>
        {
            float h = x.T * 0.5f + 0.5f, s = h * 2 + 1;
            x.Block(0, 0, h); x.Block(s, 0, h); x.Block(-s, 0, h); x.Block(0, s, h); x.Block(0, -s, h);
        });
        Add("dotduo", "Dot Duo", "Dots", x => { float s = x.G + x.S * 0.3f; x.Dot(-s, 0, x.DotR); x.Dot(s, 0, x.DotR); });
        Add("dotdrop", "Dot Drop", "Dots", x => { x.Dot(0, 0, x.DotR); for (int i = 1; i <= 3; i++) x.Dot(0, (x.G + x.S * 0.4f) * i, x.DotR * (1 - i * 0.15f)); });
        Add("dottri", "Down Trio", "Dots", x => { foreach (var p in Shape.Ngon(3, x.G + x.S * 0.4f, 90)) x.Dot(p.X, p.Y, x.DotR); });
        Add("dottedring", "Dotted Ring", "Dots", x => { foreach (var p in Shape.Ngon(24, x.G + x.S, 0)) x.Dot(p.X, p.Y, Math.Max(0.8f, x.T * 0.45f)); });
        Add("bigdotring", "Big Dot + Ring", "Dots", x => { x.Dot(0, 0, x.S * 0.3f + x.T * 0.5f); x.Ring(x.G + x.S); });

        // ---- Circles, more ------------------------------------------------
        for (int k0 = 2; k0 <= 5; k0++)
        {
            int k = k0;
            Add("rings" + k, k + " Rings", "Circles", x => { x.Dot(0, 0, x.DotR); for (int i = 1; i <= k; i++) x.Ring(x.G + x.S * 0.55f * i); });
        }
        foreach (int n0 in new[] { 2, 3, 5, 6, 8, 12 })
        {
            int n = n0;
            Add("arcs" + n, n + "-Arc Ring", "Circles", x =>
            {
                float seg = 360f / n;
                for (int i = 0; i < n; i++) x.Arc(x.G + x.S, 270 + seg * i - seg * 0.3f, seg * 0.6f);
            });
        }
        for (int n0 = 3; n0 <= 8; n0++)
        {
            int n = n0;
            Add("rtick" + n, "Ring " + n + " Ticks", "Circles", x =>
            {
                float r = x.G + x.S;
                x.Ring(r);
                for (int i = 0; i < n; i++) x.Arm(270 + 360f * i / n, r, r + x.S * 0.5f);
            });
            Add("rtin" + n, "Ring " + n + " Inner", "Circles", x =>
            {
                float r = x.G + x.S;
                x.Ring(r); x.Dot(0, 0, x.DotR);
                for (int i = 0; i < n; i++) x.Arm(270 + 360f * i / n, r - x.S * 0.5f, r);
            });
        }
        Add("arch", "Arch", "Circles", x => { x.Dot(0, 0, x.DotR); x.Arc(x.G + x.S, 200, 140); });
        Add("cup", "Cup", "Circles", x => { x.Dot(0, 0, x.DotR); x.Arc(x.G + x.S, 20, 140); });
        Add("parens", "Parentheses", "Circles", x => { x.Arc(x.G + x.S, -40, 80); x.Arc(x.G + x.S, 140, 80); });
        Add("parensdot", "Parens + Dot", "Circles", x => { x.Dot(0, 0, x.DotR); x.Arc(x.G + x.S, -40, 80); x.Arc(x.G + x.S, 140, 80); });
        Add("ringsq", "Ring + Square", "Circles", x => { x.Ring(x.G + x.S); x.Box((x.G + x.S) * 0.55f); });
        Add("ringx", "Ring X", "Circles", x => { float r = x.G + x.S; x.Ring(r); x.Arms(r * 0.4f, r * 1.4f, 45, 135, 225, 315); });
        Add("quarters", "Quartered", "Circles", x =>
        {
            float r = x.G + x.S;
            foreach (float d in new float[] { 0, 90, 180, 270 }) x.Arc(r, d + 15, 60);
            x.Arms(r * 0.5f, r * 1.3f, 0, 90, 180, 270);
        });
        Add("radar", "Radar", "Circles", x =>
        {
            float r = x.G + x.S * 1.2f;
            x.Ring(r); x.Ring(r * 0.5f); x.Arm(315, 0, r); x.Dot(0, 0, x.DotR);
        });
        Add("oval", "Oval Sight", "Circles", x => { x.Oval(0, 0, x.G + x.S * 1.3f, (x.G + x.S) * 0.6f, 0, false); x.Dot(0, 0, x.DotR); });
        Add("talloval", "Tall Oval", "Circles", x => { x.Oval(0, 0, (x.G + x.S) * 0.6f, x.G + x.S * 1.3f, 0, false); x.Dot(0, 0, x.DotR); });
        Add("wheel", "Wheel", "Circles", x => { float r = x.G + x.S; x.Ring(r); x.Arms(x.G, r, 0, 45, 90, 135, 180, 225, 270, 315); });
        Add("lifebuoy", "Lifebuoy", "Circles", x =>
        {
            float r = x.G + x.S;
            x.Ring(r); x.Ring(r * 0.55f);
            x.Arms(r * 0.55f, r, 45, 135, 225, 315);
        });
        Add("dashedring", "Dashed Ring", "Circles", x => { for (int i = 0; i < 16; i++) x.Arc(x.G + x.S, i * 22.5f, 12); });
        Add("ringpost", "Ring Posts", "Circles", x =>
        {
            float r = x.G + x.S * 0.8f;
            x.Ring(r); x.Dot(0, 0, x.DotR);
            foreach (float d in new float[] { 0, 90, 180, 270 }) x.ArmW(d, r + 2, r + x.S, x.T * 1.8f);
        });
        Add("halfhalf", "Half Rings", "Circles", x => { x.Arc(x.G + x.S * 0.5f, 180, 180); x.Arc(x.G + x.S, 0, 180); });
        Add("ringcrossout", "Ring Cross Out", "Circles", x => { float r = x.G + x.S * 0.6f; x.Ring(r); x.Arms(r, r + x.S, 0, 90, 180, 270); });
        Add("eclipse", "Eclipse", "Circles", x => { float r = x.G + x.S; x.Ring(r); x.Sector(r * 0.6f, r, 200, 140); });
        Add("orbit", "Orbit", "Circles", x => { float r = x.G + x.S; x.Ring(r); x.Dot(0, 0, x.DotR); var p = Shape.At(315, r); x.Dot(p.X, p.Y, x.DotR * 1.6f); });

        // ---- Geometric ----------------------------------------------------
        for (int n0 = 3; n0 <= 12; n0++)
        {
            int n = n0;
            Add("poly" + n, PolyNames[n], "Geometric", x => x.Poly(Shape.Ngon(n, x.G + x.S * 0.8f, 270)));
            Add("polyd" + n, PolyNames[n] + " Dot", "Geometric", x => { x.Poly(Shape.Ngon(n, x.G + x.S * 0.8f, 270)); x.Dot(0, 0, x.DotR); });
        }
        for (int n0 = 3; n0 <= 8; n0++)
        {
            int n = n0;
            Add("solidpoly" + n, "Solid " + PolyNames[n], "Geometric", x => x.FillPoly(Shape.Ngon(n, x.S * 0.4f + x.T, 270)));
        }
        for (int n0 = 4; n0 <= 10; n0++)
        {
            int n = n0;
            Add("starn" + n, n + "-Point Star", "Geometric", x => x.Poly(Shape.StarPts(n, x.G + x.S, (x.G + x.S) * 0.5f, 270)));
            Add("solidstar" + n, "Solid " + n + "-Star", "Geometric", x => x.FillPoly(Shape.StarPts(n, x.S * 0.6f + x.T, (x.S * 0.6f + x.T) * 0.45f, 270)));
        }
        Add("tricross", "Triangle Cross", "Geometric", x => { x.Poly(Shape.Ngon(3, x.G + x.S * 0.9f, 270)); x.Arms(x.G * 0.5f, x.G + x.S * 0.4f, 90, 210, 330); });
        Add("hexcross", "Hex Cross", "Geometric", x => { x.Poly(Shape.Ngon(6, x.G + x.S * 0.8f, 0)); x.Arms(x.G, x.G + x.S * 1.3f, 0, 90, 180, 270); });
        Add("pentacross", "Pentagon Cross", "Geometric", x => { x.Poly(Shape.Ngon(5, x.G + x.S * 0.8f, 270)); x.Arms(x.G * 0.5f, x.G + x.S * 0.4f, 0, 90, 180, 270); });
        Add("diacross", "Diamond Cross", "Geometric", x => { x.Poly(Shape.Ngon(4, x.G + x.S * 0.8f, 0)); x.Arms(x.G, x.G + x.S * 1.3f, 0, 90, 180, 270); });
        Add("octacross", "Octagon Cross", "Geometric", x => { x.Poly(Shape.Ngon(8, x.G + x.S * 0.8f, 22.5f)); x.Arms(x.G, x.G + x.S * 0.5f, 0, 90, 180, 270); });
        Add("eightbox", "Star Box", "Geometric", x => { float h = x.G + x.S * 0.6f; x.Box(h); x.Poly(Shape.Ngon(4, h * 1.41f, 0)); });
        Add("hexagram", "Hexagram", "Geometric", x => { x.Poly(Shape.Ngon(3, x.G + x.S, 270)); x.Poly(Shape.Ngon(3, x.G + x.S, 90)); });
        Add("cube", "Cube", "Geometric", x =>
        {
            float r = x.G + x.S;
            x.Poly(Shape.Ngon(6, r, 270));
            foreach (float d in new float[] { 30, 150, 270 }) x.Arm(d, 0, r);
        });
        Add("pyramid", "Pyramid", "Geometric", x =>
        {
            float r = x.G + x.S;
            var t = Shape.Ngon(3, r, 270);
            x.Poly(t); x.Line(t[0].X, t[0].Y, 0, t[1].Y);
        });
        Add("bowtie", "Bowtie", "Geometric", x => { x.Tri(-x.G, 0, 0, x.S, x.S * 0.6f); x.Tri(x.G, 0, 180, x.S, x.S * 0.6f); });
        Add("hourglass", "Hourglass", "Geometric", x => { x.Tri(0, -x.G, 90, x.S, x.S * 0.6f); x.Tri(0, x.G, 270, x.S, x.S * 0.6f); });
        Add("pinch", "Pinch", "Geometric", x => { foreach (float d in new float[] { 0, 90, 180, 270 }) { var p = Shape.At(d, x.G); x.Tri(p.X, p.Y, d + 180, x.S * 0.8f, x.S * 0.35f); } });
        Add("pinch3", "Tri Pinch", "Geometric", x => { foreach (float d in new float[] { 90, 210, 330 }) { var p = Shape.At(d, x.G); x.Tri(p.X, p.Y, d + 180, x.S * 0.8f, x.S * 0.35f); } });
        Add("pinchx", "X Pinch", "Geometric", x => { foreach (float d in new float[] { 45, 135, 225, 315 }) { var p = Shape.At(d, x.G); x.Tri(p.X, p.Y, d + 180, x.S * 0.8f, x.S * 0.35f); } });
        Add("hash", "Hash", "Geometric", x =>
        {
            float a = x.G, b = x.G + x.S;
            x.Line(-a, -b, -a, b); x.Line(a, -b, a, b); x.Line(-b, -a, b, -a); x.Line(-b, a, b, a);
        });
        Add("window", "Window", "Geometric", x => { float h = x.G + x.S; x.Box(h); x.Arms(x.G, h, 0, 90, 180, 270); });
        Add("kite", "Kite", "Geometric", x => { float r = x.G + x.S; x.Poly(Shape.P(0, -r * 0.7f), Shape.P(r * 0.55f, 0), Shape.P(0, r * 1.2f), Shape.P(-r * 0.55f, 0)); });
        Add("sqspiral", "Square Spiral", "Geometric", x =>
        {
            var pts = new List<PointF>();
            float step = Math.Max(2, (x.G + x.S) / 5f), px = 0, py = 0;
            pts.Add(new PointF(0, 0));
            for (int i = 1; i <= 9; i++)
            {
                float len = step * ((i + 1) / 2);
                switch (i % 4) { case 1: px += len; break; case 2: py += len; break; case 3: px -= len; break; case 0: py -= len; break; }
                pts.Add(new PointF(px, py));
            }
            x.Polyline(pts.ToArray());
        });
        Add("nested", "Nested Squares", "Geometric", x => { x.Box(x.G + x.S * 0.3f); x.Box(x.G + x.S); });
        Add("nesteddia", "Nested Diamonds", "Geometric", x => { x.Poly(Shape.Ngon(4, x.G + x.S * 0.4f, 0)); x.Poly(Shape.Ngon(4, x.G + x.S * 1.2f, 0)); });
        Add("tritri", "Triforce Lines", "Geometric", x =>
        {
            var t = Shape.Ngon(3, x.G + x.S, 270);
            x.Poly(t);
            x.Poly(Shape.P((t[0].X + t[1].X) / 2, (t[0].Y + t[1].Y) / 2), Shape.P((t[1].X + t[2].X) / 2, (t[1].Y + t[2].Y) / 2), Shape.P((t[2].X + t[0].X) / 2, (t[2].Y + t[0].Y) / 2));
        });
        Add("rhombi", "Rhombi", "Geometric", x =>
        {
            float r = x.G + x.S;
            foreach (float d in new float[] { 0, 90, 180, 270 })
            {
                var c = Shape.At(d, x.G + r * 0.35f); var a = Shape.At(d, r * 0.35f); var s = Shape.At(d + 90, r * 0.18f);
                x.Poly(Shape.P(c.X - a.X, c.Y - a.Y), Shape.P(c.X + s.X, c.Y + s.Y), Shape.P(c.X + a.X, c.Y + a.Y), Shape.P(c.X - s.X, c.Y - s.Y));
            }
        });

        // ---- Shapes, more (chevrons and arrows) ---------------------------
        Add("chevin", "Chevrons In", "Shapes", x => { foreach (float d in new float[] { 0, 90, 180, 270 }) { var p = Shape.At(d, x.G); x.Chev(p.X, p.Y, d + 180, x.S * 0.7f, 45); } });
        Add("chevout", "Chevrons Out", "Shapes", x => { foreach (float d in new float[] { 0, 90, 180, 270 }) { var p = Shape.At(d, x.G + x.S * 0.6f); x.Chev(p.X, p.Y, d, x.S * 0.6f, 45); } });
        Add("chev3in", "Tri Chevrons", "Shapes", x => { foreach (float d in new float[] { 90, 210, 330 }) { var p = Shape.At(d, x.G); x.Chev(p.X, p.Y, d + 180, x.S * 0.7f, 45); } });
        Add("chevxin", "X Chevrons", "Shapes", x => { foreach (float d in new float[] { 45, 135, 225, 315 }) { var p = Shape.At(d, x.G); x.Chev(p.X, p.Y, d + 180, x.S * 0.7f, 45); } });
        Add("pincers", "Pincers", "Shapes", x => { x.Chev(-x.G, 0, 0, x.S * 0.8f, 45); x.Chev(x.G, 0, 180, x.S * 0.8f, 45); });
        Add("chev2", "Double Chevron", "Shapes", x => { x.Chev(0, x.G * 0.5f, 270, x.S, 50); x.Chev(0, x.G * 0.5f + x.S * 0.5f, 270, x.S, 50); });
        Add("chev3", "Triple Chevron", "Shapes", x => { for (int i = 0; i < 3; i++) x.Chev(0, x.G * 0.5f + x.S * 0.45f * i, 270, x.S * 0.9f, 50); });
        Add("chevdown2", "Double V", "Shapes", x => { x.Chev(0, -x.G * 0.5f, 90, x.S, 50); x.Chev(0, -x.G * 0.5f - x.S * 0.5f, 90, x.S, 50); });
        Add("chevdot", "Chevron Dot", "Shapes", x => { x.Dot(0, 0, x.DotR); x.Chev(0, x.G, 270, x.S, 50); });
        Add("arrows4", "Four Arrows", "Shapes", x =>
        {
            foreach (float d in new float[] { 0, 90, 180, 270 })
            {
                var p = Shape.At(d, x.G); x.Tri(p.X, p.Y, d + 180, x.S * 0.5f, x.S * 0.3f + x.T * 0.5f);
                x.Arm(d, x.G + x.S * 0.45f, x.G + x.S * 1.2f);
            }
        });
        Add("arrowsout", "Burst Arrows", "Shapes", x =>
        {
            foreach (float d in new float[] { 0, 90, 180, 270 })
            {
                var p = Shape.At(d, x.G + x.S * 1.2f); x.Tri(p.X, p.Y, d, x.S * 0.5f, x.S * 0.3f + x.T * 0.5f);
                x.Arm(d, x.G, x.G + x.S * 0.75f);
            }
        });
        Add("uparrow", "Up Arrow", "Shapes", x => { x.Tri(0, x.G * 0.5f, 270, x.S * 0.6f, x.S * 0.4f); x.Line(0, x.G * 0.5f + x.S * 0.55f, 0, x.G + x.S * 1.6f); });
        Add("sidearrows", "Side Arrows", "Shapes", x => { x.Tri(-x.G, 0, 0, x.S * 0.7f, x.S * 0.4f); x.Tri(x.G, 0, 180, x.S * 0.7f, x.S * 0.4f); });
        Add("vertarrows", "Top + Bottom Arrows", "Shapes", x => { x.Tri(0, -x.G, 90, x.S * 0.7f, x.S * 0.4f); x.Tri(0, x.G, 270, x.S * 0.7f, x.S * 0.4f); });
        Add("bracketsv", "Top Brackets", "Shapes", x =>
        {
            float o = x.G + x.S * 0.5f, h = x.S * 0.6f, w = x.S * 0.35f;
            x.Polyline(Shape.P(-h, -o + w), Shape.P(-h, -o), Shape.P(h, -o), Shape.P(h, -o + w));
            x.Polyline(Shape.P(-h, o - w), Shape.P(-h, o), Shape.P(h, o), Shape.P(h, o - w));
        });
        Add("bracketsdot", "Brackets + Dot", "Shapes", x =>
        {
            float o = x.G + x.S * 0.5f, h = x.S * 0.6f, w = x.S * 0.35f;
            x.Dot(0, 0, x.DotR);
            x.Polyline(Shape.P(-o + w, -h), Shape.P(-o, -h), Shape.P(-o, h), Shape.P(-o + w, h));
            x.Polyline(Shape.P(o - w, -h), Shape.P(o, -h), Shape.P(o, h), Shape.P(o - w, h));
        });
        Add("cornersdot", "Corners + Dot", "Shapes", x =>
        {
            float o = x.G + x.S * 0.6f, l = x.S * 0.45f;
            x.Dot(0, 0, x.DotR);
            foreach (var sx in new[] { -1f, 1f }) foreach (var sy in new[] { -1f, 1f })
                x.Polyline(Shape.P(sx * (o - l), sy * o), Shape.P(sx * o, sy * o), Shape.P(sx * o, sy * (o - l)));
        });
        Add("cornerscross", "Corners + Cross", "Shapes", x =>
        {
            float o = x.G + x.S * 0.8f, l = x.S * 0.45f;
            x.Arms(x.G, x.G + x.S * 0.5f, 0, 90, 180, 270);
            foreach (var sx in new[] { -1f, 1f }) foreach (var sy in new[] { -1f, 1f })
                x.Polyline(Shape.P(sx * (o - l), sy * o), Shape.P(sx * o, sy * o), Shape.P(sx * o, sy * (o - l)));
        });
        Add("anglesdot", "Angles + Dot", "Shapes", x =>
        {
            float o = x.G + x.S * 0.4f, h = x.S * 0.6f, w = x.S * 0.45f;
            x.Dot(0, 0, x.DotR);
            x.Polyline(Shape.P(-o + w, -h), Shape.P(-o, 0), Shape.P(-o + w, h));
            x.Polyline(Shape.P(o - w, -h), Shape.P(o, 0), Shape.P(o - w, h));
        });
        Add("trisdot", "Triangle Dot", "Shapes", x => { x.Poly(Shape.Ngon(3, x.G + x.S * 0.8f, 270)); x.Dot(0, 0, x.DotR); });
        Add("block2", "Hollow Block", "Shapes", x => { x.Box(x.S * 0.3f + 1); });
        Add("arrowtipring", "Arrow + Ring", "Shapes", x => { float y = x.G * 0.25f; x.Ring(x.G + x.S * 1.2f); x.FillPoly(Shape.P(0, y), Shape.P(-x.S * 0.45f, y + x.S * 0.8f), Shape.P(x.S * 0.45f, y + x.S * 0.8f)); });
        Add("ibeam", "I-Beam", "Shapes", x =>
        {
            float a = x.G, b = x.G + x.S, c = x.S * 0.4f;
            x.Line(0, a, 0, b); x.Line(0, -a, 0, -b); x.Line(-c, b, c, b); x.Line(-c, -b, c, -b);
        });
        Add("hbeam", "H-Beam", "Shapes", x =>
        {
            float a = x.G, b = x.G + x.S, c = x.S * 0.4f;
            x.Line(a, 0, b, 0); x.Line(-a, 0, -b, 0); x.Line(b, -c, b, c); x.Line(-b, -c, -b, c);
        });

        // ---- Scopes, more -------------------------------------------------
        Add("german4", "German #4", "Scopes", x =>
        {
            float inner = x.G + x.S * 1.5f, end = x.G + x.S * 4;
            x.Arms(x.G, inner, 0, 90, 180);
            x.Arm(270, x.G, end);
            foreach (float d in new float[] { 0, 90, 180 }) x.ArmW(d, inner, end, x.T * 2.5f);
        });
        Add("fineduplex", "Fine Duplex", "Scopes", x =>
        {
            float mid = x.G + x.S * 2.4f, end = x.G + x.S * 4;
            float thin = Math.Max(1, x.T * 0.5f);
            foreach (float d in new float[] { 0, 90, 180, 270 }) { x.ArmW(d, x.G, mid, thin); x.ArmW(d, mid, end, x.T * 1.6f); }
        });
        Add("duplexdot", "Duplex Dot", "Scopes", x =>
        {
            float mid = x.G + x.S * 1.8f, end = x.G + x.S * 4;
            x.Dot(0, 0, x.DotR);
            foreach (float d in new float[] { 0, 90, 180, 270 }) x.ArmW(d, mid, end, x.T * 2.5f);
        });
        Add("xmas", "Xmas Tree", "Scopes", x =>
        {
            x.Arms(x.G, x.G + x.S, 0, 180, 270);
            x.Arm(90, x.G, x.G + x.S * 4);
            for (int i = 1; i <= 4; i++)
            {
                float y = x.G + x.S * 0.8f * i, w = x.S * (0.3f + 0.25f * i);
                x.Line(-w, y, w, y);
                x.Dot(-w - x.DotR * 2, y, x.DotR); x.Dot(w + x.DotR * 2, y, x.DotR);
            }
        });
        Add("kobra", "Kobra", "Scopes", x =>
        {
            x.Chev(0, 0, 270, x.S * 0.8f, 40);
            float y = x.S * 0.6f;
            x.Line(-x.S * 2, y, -x.S * 0.8f, y); x.Line(x.S * 0.8f, y, x.S * 2, y);
            x.Line(0, x.S * 0.9f, 0, x.S * 2.2f);
        });
        Add("pso", "PSO", "Scopes", x =>
        {
            x.Chev(0, 0, 270, x.S * 0.7f, 45);
            for (int i = 1; i <= 3; i++) x.Chev(0, x.S * 0.9f * i, 270, x.S * 0.45f, 45);
            x.Line(-x.S * 3, 0, -x.S * 1.2f, 0);
            for (int i = 0; i < 4; i++) { float px = -x.S * 1.2f - x.S * 0.6f * i; x.Line(px, 0, px, -x.S * (i % 2 == 0 ? 0.5f : 0.3f)); }
        });
        Add("holo2", "2-Dot Holo", "Scopes", x =>
        {
            float r = x.G + x.S * 1.5f;
            x.Ring(r); x.Dot(0, 0, x.DotR); x.Dot(0, r * 0.55f, x.DotR);
        });
        Add("bdc", "BDC Dots", "Scopes", x =>
        {
            x.Arms(x.G, x.G + x.S * 2.5f, 0, 180, 270);
            x.Line(0, x.G, 0, x.G + x.S * 0.6f);
            float y = x.G + x.S * 0.6f;
            for (int i = 1; i <= 4; i++) { y += x.S * 0.4f * i; x.Dot(0, y, x.DotR); }
        });
        Add("circleposts", "Circle Posts", "Scopes", x =>
        {
            float r = x.G + x.S * 2;
            x.Ring(r); x.Dot(0, 0, x.DotR);
            foreach (float d in new float[] { 0, 90, 180, 270 }) x.ArmW(d, r, r + x.S * 1.5f, x.T * 2);
        });
        Add("horseshoebdc", "Horseshoe BDC", "Scopes", x =>
        {
            float r = x.G + x.S;
            x.Dot(0, 0, x.DotR); x.Arc(r, 150, 240);
            for (int i = 1; i <= 3; i++) { float y = r + x.S * 0.5f * i, w = x.S * (0.5f - i * 0.1f); x.Line(-w, y, w, y); }
        });
        Add("mildotring", "Mil Ring", "Scopes", x =>
        {
            float r = x.S * 3 + x.G;
            x.LineW(-r, 0, r, 0, Math.Max(1, x.T * 0.5f)); x.LineW(0, -r, 0, r, Math.Max(1, x.T * 0.5f));
            float step = Math.Max(3, r / 4);
            for (float d = step; d < r - 1; d += step) { x.Dot(d, 0, x.DotR); x.Dot(-d, 0, x.DotR); x.Dot(0, d, x.DotR); x.Dot(0, -d, x.DotR); }
            x.Ring(r);
        });
        Add("tripost", "Triangle Post", "Scopes", x =>
        {
            x.FillPoly(Shape.P(0, 0), Shape.P(-x.S * 0.35f, x.S * 1.2f), Shape.P(x.S * 0.35f, x.S * 1.2f));
            x.ArmW(90, x.S * 1.2f, x.S * 3.5f, x.S * 0.7f);
            x.Arms(x.G, x.G + x.S * 3, 0, 180);
        });
        Add("diagsniper", "Diagonal Sniper", "Scopes", x => { x.Arms(x.G, x.G + x.S * 4, 45, 135, 225, 315); x.Ring(x.G + x.S * 3); x.Dot(0, 0, x.DotR); });
        Add("crossbars", "Crossbars", "Scopes", x =>
        {
            float end = x.G + x.S * 3.5f, mid = x.G + x.S * 1.8f, c = x.S * 0.4f;
            x.Arms(x.G, end, 0, 90, 180, 270);
            x.Line(mid, -c, mid, c); x.Line(-mid, -c, -mid, c); x.Line(-c, mid, c, mid); x.Line(-c, -mid, c, -mid);
        });
        Add("rangearcs", "Range Arcs", "Scopes", x =>
        {
            x.Dot(0, 0, x.DotR);
            x.Arms(x.G, x.G + x.S, 0, 180, 270);
            for (int i = 1; i <= 3; i++) x.Arc(x.G + x.S * 0.8f * i, 60, 60);
        });
        Add("acogdot", "Chevron Dot Scope", "Scopes", x =>
        {
            x.Dot(0, 0, x.DotR);
            x.Polyline(Shape.P(-x.S * 0.6f, x.G + x.S * 0.6f), Shape.P(0, x.G), Shape.P(x.S * 0.6f, x.G + x.S * 0.6f));
            x.Line(0, x.G + x.S * 0.9f, 0, x.G + x.S * 2.4f);
        });
        Add("tactx", "Tactical X", "Scopes", x =>
        {
            float r = x.G + x.S * 2.5f;
            x.Ring(r); x.Dot(0, 0, x.DotR);
            x.Arms(r - x.S * 0.8f, r, 45, 135, 225, 315);
            x.Arms(x.G, x.G + x.S * 0.6f, 0, 90, 180, 270);
        });
        Add("thinsniper", "Thin Sniper", "Scopes", x =>
        {
            float w = Math.Max(1, x.T * 0.5f), r = x.G + x.S * 4;
            x.LineW(-r, 0, r, 0, w); x.LineW(0, -r, 0, r, w);
        });
        Add("scopesq", "Box Scope", "Scopes", x => { x.Arms(x.G, x.G + x.S * 4, 0, 90, 180, 270); x.Box(x.G + x.S * 2.5f); });

        // ---- Fun, more ----------------------------------------------------
        Add("moon", "Moon", "Fun", x =>
        {
            float r = x.G + x.S, d = r * 0.8f;
            float b = (float)(Math.Atan2(Math.Sqrt(r * r - d * d / 4), d / 2) * 180 / Math.PI);
            var p = new GraphicsPath();
            p.AddArc(-r, -r, r * 2, r * 2, b, 360 - 2 * b);
            p.AddArc(d - r, -r, r * 2, r * 2, 180 + b, -2 * b);
            p.CloseFigure();
            x.Closed(p); x.Dot(0, 0, x.DotR);
        });
        Add("chomper", "Chomper", "Fun", x =>
        {
            float r = x.G + x.S;
            var p = new GraphicsPath(); p.AddPie(-r, -r, r * 2, r * 2, 35, 290); x.Closed(p);
            x.Dot(r * 0.1f, -r * 0.5f, x.DotR);
        });
        Add("ghost", "Ghost", "Fun", x =>
        {
            float r = x.G + x.S;
            var p = new GraphicsPath();
            p.AddArc(-r, -r, r * 2, r * 2, 180, 180);
            p.AddLine(r, 0, r, r);
            float w = r / 2;
            p.AddLines(new[] { Shape.P(r, r), Shape.P(r - w * 0.5f, r * 0.7f), Shape.P(r - w, r), Shape.P(r - w * 1.5f, r * 0.7f), Shape.P(0, r),
                               Shape.P(-w * 0.5f, r * 0.7f), Shape.P(-w, r), Shape.P(-w * 1.5f, r * 0.7f), Shape.P(-r, r) });
            p.CloseFigure();
            x.Closed(p);
            x.Dot(-r * 0.35f, -r * 0.2f, x.DotR * 1.3f); x.Dot(r * 0.35f, -r * 0.2f, x.DotR * 1.3f);
        });
        Add("paw", "Paw", "Fun", x =>
        {
            float u = x.U;
            x.Oval(0, 4 * u, 4.5f * u, 3.5f * u, 0, true);
            x.Oval(-6 * u, -1 * u, 1.6f * u, 2.2f * u, -20, true); x.Oval(6 * u, -1 * u, 1.6f * u, 2.2f * u, 20, true);
            x.Oval(-2.3f * u, -5 * u, 1.6f * u, 2.2f * u, -8, true); x.Oval(2.3f * u, -5 * u, 1.6f * u, 2.2f * u, 8, true);
        });
        Add("cat", "Cat Face", "Fun", x =>
        {
            float r = x.G + x.S;
            x.Ring(r);
            x.Polyline(Shape.P(-r * 0.82f, -r * 0.55f), Shape.P(-r * 0.8f, -r * 1.3f), Shape.P(-r * 0.3f, -r * 0.95f));
            x.Polyline(Shape.P(r * 0.82f, -r * 0.55f), Shape.P(r * 0.8f, -r * 1.3f), Shape.P(r * 0.3f, -r * 0.95f));
            x.Dot(-r * 0.38f, -r * 0.15f, x.DotR); x.Dot(r * 0.38f, -r * 0.15f, x.DotR);
            x.Dot(0, r * 0.25f, x.DotR * 0.8f);
        });
        Add("alien", "Alien", "Fun", x =>
        {
            float u = x.U;
            x.Oval(0, 0, 7.5f * u, 10 * u, 0, false);
            x.Oval(-3.2f * u, -1 * u, 1.6f * u, 3 * u, 35, true); x.Oval(3.2f * u, -1 * u, 1.6f * u, 3 * u, -35, true);
        });
        Add("ufo", "UFO", "Fun", x =>
        {
            float u = x.U;
            x.Oval(0, 2 * u, 10 * u, 3.2f * u, 0, false);
            x.ArcAt(0, 0, 4.5f * u, 180, 180);
            for (int i = -1; i <= 1; i++) x.Dot(i * 4.5f * u, 7.5f * u, x.DotR);
        });
        Add("planet", "Ringed Planet", "Fun", x => { float r = x.G + x.S; x.Ring(r * 0.6f); x.Oval(0, 0, r * 1.25f, r * 0.3f, -20, false); });
        Add("fish", "Fish", "Fun", x =>
        {
            float u = x.U;
            x.Oval(-1.5f * u, 0, 7 * u, 4.2f * u, 0, false);
            x.Poly(Shape.P(5.2f * u, 0), Shape.P(10 * u, -4 * u), Shape.P(10 * u, 4 * u));
            x.Dot(-5 * u, -1 * u, x.DotR);
        });
        Add("butterfly", "Butterfly", "Fun", x =>
        {
            float u = x.U;
            x.Oval(-4.2f * u, -3.2f * u, 4 * u, 3 * u, -30, false); x.Oval(4.2f * u, -3.2f * u, 4 * u, 3 * u, 30, false);
            x.Oval(-3.2f * u, 3.8f * u, 3 * u, 2.2f * u, 30, false); x.Oval(3.2f * u, 3.8f * u, 3 * u, 2.2f * u, -30, false);
            x.Line(0, -5 * u, 0, 7 * u);
        });
        Add("leaf", "Leaf", "Fun", x =>
        {
            float r = x.G + x.S;
            var p = new GraphicsPath();
            p.AddBezier(-r, r, -r, -r * 0.2f, -r * 0.2f, -r, r, -r);
            p.AddBezier(r, -r, r, r * 0.2f, r * 0.2f, r, -r, r);
            p.CloseFigure();
            x.Closed(p); x.Line(-r * 0.6f, r * 0.6f, r * 0.6f, -r * 0.6f);
        });
        Add("daisy", "Daisy", "Fun", x =>
        {
            float r = x.G + x.S;
            x.Dot(0, 0, x.DotR * 1.3f);
            for (int i = 0; i < 8; i++) { var c = Shape.At(i * 45, r * 0.6f); x.Oval(c.X, c.Y, r * 0.38f, r * 0.17f, i * 45, false); }
        });
        Add("clover", "Clover", "Fun", x =>
        {
            float r = (x.G + x.S) * 0.42f;
            x.Dot(0, 0, x.DotR);
            foreach (float d in new float[] { 45, 135, 225, 315 }) { var c = Shape.At(d, r * 1.25f); x.Ring(c.X, c.Y, r); }
        });
        Add("snowflake", "Snowflake", "Fun", x =>
        {
            float r = x.G + x.S;
            for (int i = 0; i < 6; i++)
            {
                float d = 270 + i * 60;
                x.Arm(d, x.G * 0.5f, r);
                var p = Shape.At(d, r * 0.62f); x.Chev(p.X, p.Y, d, r * 0.3f, 45);
            }
        });
        Add("sparkle", "Sparkle", "Fun", x =>
        {
            float r = x.G + x.S, k = r * 0.12f;
            var p = new GraphicsPath();
            p.AddBezier(0, -r, k, -k, k, -k, r, 0);
            p.AddBezier(r, 0, k, k, k, k, 0, r);
            p.AddBezier(0, r, -k, k, -k, k, -r, 0);
            p.AddBezier(-r, 0, -k, -k, -k, -k, 0, -r);
            p.CloseFigure();
            x.Filled(p);
        });
        Add("sparkles", "Twin Sparkle", "Fun", x =>
        {
            float r = x.G + x.S * 0.8f, k = r * 0.12f;
            foreach (var c in new[] { Shape.P(0, 0), Shape.P(r * 0.9f, -r * 0.9f) })
            {
                float rr = c.X == 0 ? r : r * 0.45f, kk = c.X == 0 ? k : k * 0.45f;
                var p = new GraphicsPath();
                p.AddBezier(c.X, c.Y - rr, c.X + kk, c.Y - kk, c.X + kk, c.Y - kk, c.X + rr, c.Y);
                p.AddBezier(c.X + rr, c.Y, c.X + kk, c.Y + kk, c.X + kk, c.Y + kk, c.X, c.Y + rr);
                p.AddBezier(c.X, c.Y + rr, c.X - kk, c.Y + kk, c.X - kk, c.Y + kk, c.X - rr, c.Y);
                p.AddBezier(c.X - rr, c.Y, c.X - kk, c.Y - kk, c.X - kk, c.Y - kk, c.X, c.Y - rr);
                p.CloseFigure();
                x.Filled(p);
            }
        });
        Add("gem", "Gem", "Fun", x =>
        {
            float r = x.G + x.S;
            x.Poly(Shape.P(-r * 0.5f, -r * 0.6f), Shape.P(r * 0.5f, -r * 0.6f), Shape.P(r, -r * 0.1f), Shape.P(0, r * 0.9f), Shape.P(-r, -r * 0.1f));
            x.Line(-r, -r * 0.1f, r, -r * 0.1f);
        });
        Add("shield", "Shield", "Fun", x =>
        {
            float r = x.G + x.S;
            var p = new GraphicsPath();
            p.AddLine(-r * 0.8f, -r * 0.9f, r * 0.8f, -r * 0.9f);
            p.AddLine(r * 0.8f, -r * 0.9f, r * 0.8f, 0);
            p.AddBezier(r * 0.8f, 0, r * 0.8f, r * 0.5f, r * 0.3f, r * 0.8f, 0, r);
            p.AddBezier(0, r, -r * 0.3f, r * 0.8f, -r * 0.8f, r * 0.5f, -r * 0.8f, 0);
            p.CloseFigure();
            x.Closed(p); x.Dot(0, 0, x.DotR);
        });
        Add("note", "Music Note", "Fun", x =>
        {
            float u = x.U;
            x.Oval(-3 * u, 6 * u, 3 * u, 2.2f * u, -20, true);
            x.Line(-0.2f * u, 5.5f * u, -0.2f * u, -8 * u);
            x.Polyline(Shape.P(-0.2f * u, -8 * u), Shape.P(4.5f * u, -5 * u), Shape.P(4.5f * u, -1.5f * u));
        });
        Add("heartsolid", "Solid Heart", "Fun", x => x.Filled(Shape.HeartPath((x.S * 0.6f + x.T) / 10f, false)));
        Add("heartdot", "Heart + Dot", "Fun", x => { x.Closed(Shape.HeartPath((x.G + x.S) / 10f, false)); x.Dot(0, 0, x.DotR); });
        Add("sunburst", "Sunburst", "Fun", x => { x.Dot(0, 0, x.DotR * 1.5f); for (int i = 0; i < 12; i++) x.Arm(i * 30, x.G, x.G + x.S * (i % 2 == 0 ? 1f : 0.6f)); });
        Add("gear", "Gear", "Fun", x =>
        {
            float r = x.G + x.S;
            var pts = new PointF[32];
            for (int i = 0; i < 32; i++) pts[i] = Shape.At(i * 360f / 32 + 5.6f, (i / 2) % 2 == 0 ? r : r * 0.78f);
            x.Poly(pts); x.Ring(r * 0.35f);
        });
        Add("trefoil", "Trefoil", "Fun", x =>
        {
            float r = x.G + x.S;
            x.Dot(0, 0, x.DotR * 1.4f);
            foreach (float d in new float[] { 270, 30, 150 }) x.Sector(r * 0.3f, r, d - 30, 60);
        });
        Add("fan", "Fan", "Fun", x => { float r = x.G + x.S; foreach (float d in new float[] { 0, 90, 180, 270 }) x.Sector(r * 0.3f, r, d + 20, 50); });
        Add("bullseye", "Solid Bullseye", "Fun", x => { float r = x.G + x.S; x.Dot(0, 0, r * 0.3f); x.Ring(r * 0.65f); x.Ring(r); });
        Add("dice", "Dice", "Fun", x =>
        {
            float h = x.G + x.S, o = h * 0.5f;
            x.Box(h); x.Dot(0, 0, x.DotR);
            x.Dot(-o, -o, x.DotR); x.Dot(o, -o, x.DotR); x.Dot(-o, o, x.DotR); x.Dot(o, o, x.DotR);
        });
        Add("checker", "Checker", "Fun", x => { float h = x.S * 0.3f + 1; x.Block(-h, -h, h); x.Block(h, h, h); });
        Add("crown2", "Crown + Dot", "Fun", x =>
        {
            float s = (x.G + x.S) / 10f;
            x.Poly(Shape.P(-8 * s, 5 * s), Shape.P(-9 * s, -5 * s), Shape.P(-4 * s, 0), Shape.P(0, -7 * s),
                   Shape.P(4 * s, 0), Shape.P(9 * s, -5 * s), Shape.P(8 * s, 5 * s));
            x.Dot(0, 2 * s, x.DotR);
        });
        Add("pumpkin", "Pumpkin", "Fun", x =>
        {
            float u = x.U;
            x.Oval(0, 1 * u, 9 * u, 7 * u, 0, false); x.Oval(0, 1 * u, 3.5f * u, 7 * u, 0, false);
            x.Line(0, -6 * u, 1.5f * u, -9 * u);
        });
        Add("rocket", "Rocket", "Fun", x =>
        {
            float u = x.U;
            var p = new GraphicsPath();
            p.AddBezier(0, -10 * u, 4 * u, -6 * u, 3.5f * u, 0, 3 * u, 5 * u);
            p.AddLine(3 * u, 5 * u, -3 * u, 5 * u);
            p.AddBezier(-3 * u, 5 * u, -3.5f * u, 0, -4 * u, -6 * u, 0, -10 * u);
            p.CloseFigure();
            x.Closed(p);
            x.Ring(0, -3 * u, 1.5f * u);
            x.Polyline(Shape.P(-3.2f * u, 1 * u), Shape.P(-6 * u, 6 * u), Shape.P(-3 * u, 5 * u));
            x.Polyline(Shape.P(3.2f * u, 1 * u), Shape.P(6 * u, 6 * u), Shape.P(3 * u, 5 * u));
        });
        Add("cloud", "Cloud", "Fun", x =>
        {
            float u = x.U;
            var p = new GraphicsPath();
            p.AddArc(-9 * u, -2 * u, 6 * u, 6 * u, 90, 180);
            p.AddArc(-5 * u, -7 * u, 9 * u, 9 * u, 200, 140);
            p.AddArc(2 * u, -4 * u, 7 * u, 7 * u, 250, 200);
            p.CloseFigure();
            x.Closed(p);
        });
        Add("dna", "Helix", "Fun", x =>
        {
            float r = x.G + x.S, a = r * 0.45f;
            var l = new List<PointF>(); var rr = new List<PointF>();
            for (int i = 0; i <= 40; i++)
            {
                float y = -r + 2 * r * i / 40f; double t = i / 40.0 * Math.PI * 3;
                l.Add(Shape.P((float)(Math.Sin(t) * a), y)); rr.Add(Shape.P((float)(-Math.Sin(t) * a), y));
            }
            x.Polyline(l.ToArray()); x.Polyline(rr.ToArray());
        });

        // ---- Symbols ------------------------------------------------------
        Add("check", "Check", "Symbols", x => { float r = x.G + x.S; x.Polyline(Shape.P(-r * 0.6f, 0), Shape.P(-r * 0.15f, r * 0.45f), Shape.P(r * 0.7f, -r * 0.5f)); });
        Add("medic", "Medic", "Symbols", x =>
        {
            float a = x.S * 0.2f + x.T, b = x.S * 0.6f + x.T;
            x.FillPoly(Shape.P(-a, -b), Shape.P(a, -b), Shape.P(a, -a), Shape.P(b, -a), Shape.P(b, a), Shape.P(a, a),
                       Shape.P(a, b), Shape.P(-a, b), Shape.P(-a, a), Shape.P(-b, a), Shape.P(-b, -a), Shape.P(-a, -a));
        });
        Add("medicoutline", "Medic Outline", "Symbols", x =>
        {
            float a = (x.G + x.S) * 0.3f, b = x.G + x.S;
            x.Poly(Shape.P(-a, -b), Shape.P(a, -b), Shape.P(a, -a), Shape.P(b, -a), Shape.P(b, a), Shape.P(a, a),
                   Shape.P(a, b), Shape.P(-a, b), Shape.P(-a, a), Shape.P(-b, a), Shape.P(-b, -a), Shape.P(-a, -a));
        });
        Add("percent", "Percent", "Symbols", x =>
        {
            float r = x.G + x.S;
            x.Line(-r * 0.7f, r * 0.8f, r * 0.7f, -r * 0.8f);
            x.Ring(-r * 0.5f, -r * 0.5f, r * 0.25f); x.Ring(r * 0.5f, r * 0.5f, r * 0.25f);
        });
        Add("equals", "Equals", "Symbols", x => { float a = x.G, b = x.G + x.S; x.Line(-b, -a, b, -a); x.Line(-b, a, b, a); });
        Add("slash", "Slash", "Symbols", x => x.Arms(x.G, x.G + x.S, 135, 315));
        Add("backslash", "Backslash", "Symbols", x => x.Arms(x.G, x.G + x.S, 45, 225));
        Add("warning", "Warning", "Symbols", x =>
        {
            float r = x.G + x.S * 1.2f;
            x.Poly(Shape.Ngon(3, r, 270));
            x.Line(0, -r * 0.45f, 0, r * 0.05f); x.Dot(0, r * 0.28f, x.DotR);
        });
        Add("info", "Info", "Symbols", x => { float r = x.G + x.S; x.Ring(r); x.Dot(0, -r * 0.45f, x.DotR); x.Line(0, -r * 0.15f, 0, r * 0.55f); });
        Add("pause", "Pause", "Symbols", x => { float a = x.G, b = x.S * 0.8f; x.Line(-a, -b, -a, b); x.Line(a, -b, a, b); });
        Add("play", "Play", "Symbols", x => x.Tri(x.G + x.S * 0.6f, 0, 0, x.S * 1.2f, x.S * 0.7f));
        Add("compass", "Compass", "Symbols", x =>
        {
            float r = x.G + x.S;
            x.Ring(r);
            x.FillPoly(Shape.P(0, -r * 0.9f), Shape.P(r * 0.15f, -r * 0.15f), Shape.P(r * 0.5f, 0), Shape.P(r * 0.15f, r * 0.15f),
                       Shape.P(0, r * 0.9f), Shape.P(-r * 0.15f, r * 0.15f), Shape.P(-r * 0.5f, 0), Shape.P(-r * 0.15f, -r * 0.15f));
        });
        Add("nav", "Nav Arrow", "Symbols", x => { float r = x.G + x.S; x.Poly(Shape.P(0, -r), Shape.P(r * 0.65f, r * 0.75f), Shape.P(0, r * 0.35f), Shape.P(-r * 0.65f, r * 0.75f)); });
        Add("cursor", "Cursor", "Symbols", x => x.Poly(Shape.Pts(x.U, 0, 0, 0, 15, 4, 11.5f, 7, 18, 9.5f, 17, 6.5f, 10.5f, 11, 10.5f)));
        Add("cursorsolid", "Solid Cursor", "Symbols", x => x.FillPoly(Shape.Pts(x.U * 0.7f, 0, 0, 0, 15, 4, 11.5f, 7, 18, 9.5f, 17, 6.5f, 10.5f, 11, 10.5f)));
        Add("pin", "Map Pin", "Symbols", x =>
        {
            float r = (x.G + x.S) * 0.55f;
            var p = new GraphicsPath();
            p.AddArc(-r, -r * 3.2f, r * 2, r * 2, 140, 260);
            p.AddLine(Shape.At(0, -r * 2.2f, 40, r), Shape.P(0, 0));
            p.CloseFigure();
            x.Closed(p); x.Ring(0, -r * 2.2f, r * 0.4f);
        });
        Add("power", "Power", "Symbols", x => { float r = x.G + x.S; x.Arc(r, 300, 300); x.Line(0, -r * 1.15f, 0, -r * 0.25f); });
        Add("spade", "Spade", "Symbols", x =>
        {
            float s = (x.G + x.S) / 11f;
            var p = Shape.HeartPath(s, true);
            x.Closed(p);
            x.Poly(Shape.P(0, 4 * s), Shape.P(-3 * s, 11 * s), Shape.P(3 * s, 11 * s));
        });
        Add("club", "Club", "Symbols", x =>
        {
            float r = (x.G + x.S) * 0.33f;
            x.Ring(0, -r * 1.2f, r); x.Ring(-r * 1.1f, r * 0.4f, r); x.Ring(r * 1.1f, r * 0.4f, r);
            x.Poly(Shape.P(0, r * 0.4f), Shape.P(-r * 0.8f, r * 2.6f), Shape.P(r * 0.8f, r * 2.6f));
        });
        Add("suitdiamond", "Card Diamond", "Symbols", x => { float r = x.G + x.S; x.Poly(Shape.P(0, -r), Shape.P(r * 0.65f, 0), Shape.P(0, r), Shape.P(-r * 0.65f, 0)); });
        Add("infinity", "Infinity", "Symbols", x =>
        {
            float a = x.G + x.S * 1.2f;
            var pts = new PointF[72];
            for (int i = 0; i < 72; i++)
            {
                double t = i * Math.PI * 2 / 72, d = 1 + Math.Sin(t) * Math.Sin(t);
                pts[i] = Shape.P((float)(a * Math.Cos(t) / d), (float)(a * Math.Sin(t) * Math.Cos(t) / d));
            }
            x.Poly(pts);
        });
        Add("wave", "Wave", "Symbols", x =>
        {
            float r = x.G + x.S * 1.3f;
            var pts = new PointF[41];
            for (int i = 0; i <= 40; i++) { float px = -r + 2 * r * i / 40f; pts[i] = Shape.P(px, (float)(Math.Sin(px / r * Math.PI * 2) * r * 0.25f)); }
            x.Polyline(pts);
        });
        Add("zigzag", "Zigzag", "Symbols", x =>
        {
            float r = x.G + x.S * 1.3f, h = r * 0.25f;
            var pts = new PointF[9];
            for (int i = 0; i <= 8; i++) pts[i] = Shape.P(-r + 2 * r * i / 8f, i % 2 == 0 ? h : -h);
            x.Polyline(pts);
        });
        Add("signal", "Signal", "Symbols", x =>
        {
            float r = x.G + x.S, cy = r * 0.5f;
            x.Dot(0, cy, x.DotR * 1.3f);
            for (int i = 1; i <= 3; i++) x.ArcAt(0, cy, r * 0.45f * i, 225, 90);
        });
        Add("plusminus", "Plus-Minus", "Symbols", x =>
        {
            float r = x.G + x.S;
            x.Line(-r * 0.6f, -r * 0.3f, r * 0.6f, -r * 0.3f); x.Line(0, -r * 0.9f, 0, r * 0.3f); x.Line(-r * 0.6f, r * 0.7f, r * 0.6f, r * 0.7f);
        });
        Add("divide", "Divide", "Symbols", x => { float r = x.G + x.S; x.Line(-r, 0, r, 0); x.Dot(0, -r * 0.6f, x.DotR * 1.3f); x.Dot(0, r * 0.6f, x.DotR * 1.3f); });
        Add("refresh", "Refresh", "Symbols", x =>
        {
            float r = x.G + x.S;
            x.Arc(r, 300, 290);
            var tip = Shape.At(300, r); x.Tri(tip.X, tip.Y, 30, x.S * 0.6f, x.S * 0.35f);
        });
        Add("lock", "Lock", "Symbols", x =>
        {
            float u = x.U;
            x.Poly(Shape.P(-6 * u, -1 * u), Shape.P(6 * u, -1 * u), Shape.P(6 * u, 9 * u), Shape.P(-6 * u, 9 * u));
            x.ArcAt(0, -1 * u, 4 * u, 180, 180); x.Dot(0, 4 * u, x.DotR);
        });
        Add("hexnut", "Hex Nut", "Symbols", x => { float r = x.G + x.S; x.Poly(Shape.Ngon(6, r, 0)); x.Ring(r * 0.45f); });
        Add("stopsign", "Stop Sign", "Symbols", x => { float r = x.G + x.S; x.Poly(Shape.Ngon(8, r, 22.5f)); x.Line(-r * 0.5f, 0, r * 0.5f, 0); });
        Add("noentry", "No Entry", "Symbols", x => { float r = x.G + x.S; x.Ring(r); x.Arms(0, r, 135, 315); });
        Add("target3", "Crosshair Badge", "Symbols", x =>
        {
            float r = x.G + x.S;
            x.Ring(r); x.Ring(r * 0.45f);
            x.Arms(r * 0.25f, r * 1.25f, 0, 90, 180, 270);
        });
        Add("biobox", "Brackets Square", "Symbols", x =>
        {
            float h = x.G + x.S, l = h * 0.5f;
            x.Polyline(Shape.P(-l, -h), Shape.P(-h, -h), Shape.P(-h, h), Shape.P(-l, h));
            x.Polyline(Shape.P(l, -h), Shape.P(h, -h), Shape.P(h, h), Shape.P(l, h));
            x.Dot(0, 0, x.DotR);
        });
    }
}

// ---------------------------------------------------------------------------
// Rendering. Always drawn fully opaque; the opacity slider is applied to the whole
// finished image, otherwise the outline shows through a translucent crosshair.
// ---------------------------------------------------------------------------
internal static class Render
{
    public static Image CustomImage;
    private static string loadedPath;

    public static void LoadImage(string path)
    {
        if (path == loadedPath) return;
        loadedPath = path;
        if (CustomImage != null) { CustomImage.Dispose(); CustomImage = null; }
        try
        {
            if (!string.IsNullOrEmpty(path) && File.Exists(path))
                using (var raw = Image.FromFile(path)) CustomImage = new Bitmap(raw);
        }
        catch { CustomImage = null; }
    }

    public static Shape Build(Style st, Settings c, float scale)
    {
        var sh = new Shape(c.Size * scale, Math.Max(0.75f, c.Thickness * scale), c.Gap * scale);
        if (st.Id == "custom") { sh.Picture = CustomImage; return sh; }
        st.Build(sh);
        if (c.CenterDot) sh.Dot(0, 0, sh.DotR);
        if (c.Rotation != 0)
            using (var m = new Matrix())
            {
                m.Rotate(c.Rotation);
                foreach (var f in sh.Figs) f.Path.Transform(m);
            }
        return sh;
    }

    public static SizeF PictureSize(Shape sh)
    {
        if (sh.Picture == null) return new SizeF(sh.S * 4, sh.S * 4);
        float h = sh.S * 4, w = h * sh.Picture.Width / Math.Max(1, sh.Picture.Height);
        return new SizeF(w, h);
    }

    // Radius from the centre that the drawn crosshair can reach.
    public static float Extent(Shape sh, float outline)
    {
        if (sh.Figs.Count == 0)
        {
            var ps = PictureSize(sh);
            return (float)Math.Sqrt(ps.Width * ps.Width + ps.Height * ps.Height) / 2 + 2;
        }
        float e = 0;
        foreach (var f in sh.Figs)
        {
            var b = f.Path.GetBounds();
            float pad = (f.Fill ? 0 : f.Width / 2) + outline;
            e = Math.Max(e, Math.Max(Math.Max(-b.Left, b.Right), Math.Max(-b.Top, b.Bottom)) + pad);
        }
        return e + 2;
    }

    public static void Draw(Graphics g, Shape sh, Settings c, float cx, float cy, float outline)
    {
        var state = g.Save();
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.PixelOffsetMode = PixelOffsetMode.Half;
        g.InterpolationMode = InterpolationMode.HighQualityBicubic;

        // Odd widths are centred on a pixel centre so straight arms land crisp.
        bool odd = ((int)Math.Round(sh.T)) % 2 == 1 && c.Rotation % 90 == 0;
        g.TranslateTransform(cx + (odd ? 0.5f : 0), cy + (odd ? 0.5f : 0));

        if (sh.Figs.Count == 0)
        {
            var ps = PictureSize(sh);
            g.RotateTransform(c.Rotation);
            if (sh.Picture != null)
                g.DrawImage(sh.Picture, -ps.Width / 2, -ps.Height / 2, ps.Width, ps.Height);
            else
                using (var f = new Font("Segoe UI", Math.Max(6, sh.S * 0.8f), FontStyle.Bold))
                using (var b = new SolidBrush(c.Color))
                using (var fmt = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center })
                    g.DrawString("IMG", f, b, 0, 0, fmt);
            g.Restore(state);
            return;
        }

        if (outline > 0)
            using (var ob = new SolidBrush(c.OutlineColor))
                foreach (var f in sh.Figs)
                {
                    if (f.Fill)
                    {
                        g.FillPath(ob, f.Path);
                        using (var op = new Pen(c.OutlineColor, outline * 2) { LineJoin = LineJoin.Round })
                            g.DrawPath(op, f.Path);
                    }
                    else
                        using (var op = new Pen(c.OutlineColor, f.Width + outline * 2) { LineJoin = LineJoin.Miter })
                            g.DrawPath(op, f.Path);
                }

        using (var mb = new SolidBrush(c.Color))
            foreach (var f in sh.Figs)
            {
                if (f.Fill) g.FillPath(mb, f.Path);
                else using (var mp = new Pen(c.Color, f.Width) { LineJoin = LineJoin.Miter }) g.DrawPath(mp, f.Path);
            }
        g.Restore(state);
    }

    // A full-size, square, transparent bitmap holding the crosshair centred.
    public static Bitmap Bitmap(Settings c, out int half)
    {
        var st = Styles.Find(c.Style) ?? Styles.All[0];
        var sh = Build(st, c, 1f);
        float ol = st.Id == "custom" ? 0 : c.Outline;
        half = (int)Math.Ceiling(Extent(sh, ol)) + 1;
        var bmp = new Bitmap(half * 2, half * 2, PixelFormat.Format32bppArgb);
        using (var g = Graphics.FromImage(bmp))
        {
            g.Clear(Color.Transparent);
            Draw(g, sh, c, half, half, ol);
        }
        return bmp;
    }

    // Scaled to fit a box — used by the gallery tiles and the app icon.
    public static void DrawFitted(Graphics g, Style st, Settings c, RectangleF box)
    {
        var sh = Build(st, c, 1f);
        float ol = st.Id == "custom" ? 0 : c.Outline;
        float e = Extent(sh, ol), room = Math.Min(box.Width, box.Height) / 2f;
        if (e > room)
        {
            float k = room / e;
            sh = Build(st, c, k);
            ol *= k;
        }
        Draw(g, sh, c, (float)Math.Floor(box.X + box.Width / 2), (float)Math.Floor(box.Y + box.Height / 2), ol);
    }
}

// ---------------------------------------------------------------------------
// The overlay: click-through, never activated, per-pixel alpha
// ---------------------------------------------------------------------------
internal class Overlay : Form
{
    [StructLayout(LayoutKind.Sequential)] struct PT { public int x, y; public PT(int a, int b) { x = a; y = b; } }
    [StructLayout(LayoutKind.Sequential)] struct SZ { public int cx, cy; public SZ(int a, int b) { cx = a; cy = b; } }
    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    struct BLEND { public byte Op, Flags, Alpha, Format; }

    [DllImport("user32.dll")] static extern bool UpdateLayeredWindow(IntPtr hwnd, IntPtr hdcDst, ref PT pptDst, ref SZ psize, IntPtr hdcSrc, ref PT pprSrc, int crKey, ref BLEND pblend, int dwFlags);
    [DllImport("user32.dll")] static extern IntPtr GetDC(IntPtr h);
    [DllImport("user32.dll")] static extern int ReleaseDC(IntPtr h, IntPtr dc);
    [DllImport("gdi32.dll")] static extern IntPtr CreateCompatibleDC(IntPtr dc);
    [DllImport("gdi32.dll")] static extern bool DeleteDC(IntPtr dc);
    [DllImport("gdi32.dll")] static extern IntPtr SelectObject(IntPtr dc, IntPtr obj);
    [DllImport("gdi32.dll")] static extern bool DeleteObject(IntPtr obj);
    [DllImport("user32.dll")] static extern bool SetWindowPos(IntPtr h, IntPtr after, int x, int y, int cx, int cy, uint flags);

    const int WS_EX_LAYERED = 0x80000, WS_EX_TRANSPARENT = 0x20, WS_EX_TOOLWINDOW = 0x80,
              WS_EX_NOACTIVATE = 0x8000000, WS_EX_TOPMOST = 0x8;
    static readonly IntPtr HWND_TOPMOST = new IntPtr(-1);
    const uint SWP_NOMOVE = 2, SWP_NOSIZE = 1, SWP_NOACTIVATE = 0x10, SWP_SHOWWINDOW = 0x40;

    private readonly System.Windows.Forms.Timer keeper = new System.Windows.Forms.Timer { Interval = 750 };

    public Overlay()
    {
        FormBorderStyle = FormBorderStyle.None;
        ShowInTaskbar = false;
        StartPosition = FormStartPosition.Manual;
        TopMost = true;
        Text = "NosCrosshair Overlay";
        // Games that go borderless-fullscreen keep grabbing the top of the z-order;
        // re-asserting topmost is cheap and keeps the crosshair above them.
        keeper.Tick += (s, e) =>
        {
            if (Visible) SetWindowPos(Handle, HWND_TOPMOST, 0, 0, 0, 0, SWP_NOMOVE | SWP_NOSIZE | SWP_NOACTIVATE);
        };
        keeper.Start();
    }

    protected override bool ShowWithoutActivation { get { return true; } }

    protected override CreateParams CreateParams
    {
        get
        {
            var cp = base.CreateParams;
            cp.ExStyle |= WS_EX_LAYERED | WS_EX_TRANSPARENT | WS_EX_TOOLWINDOW | WS_EX_NOACTIVATE | WS_EX_TOPMOST;
            return cp;
        }
    }

    public void Apply(Settings c)
    {
        var screens = Screen.AllScreens;
        var scr = screens[Math.Min(c.Monitor, screens.Length - 1)].Bounds;
        int half;
        using (var bmp = Render.Bitmap(c, out half))
        {
            int cx = scr.X + scr.Width / 2 + c.OffsetX, cy = scr.Y + scr.Height / 2 + c.OffsetY;
            Push(bmp, cx - half, cy - half, (byte)Math.Round(c.Opacity * 2.55));
        }
        if (c.Visible && !Visible) Show();
        if (!c.Visible && Visible) Hide();
    }

    private void Push(Bitmap bmp, int x, int y, byte alpha)
    {
        IntPtr screenDc = GetDC(IntPtr.Zero), memDc = CreateCompatibleDC(screenDc), hBmp = IntPtr.Zero, old = IntPtr.Zero;
        try
        {
            hBmp = bmp.GetHbitmap(Color.FromArgb(0));
            old = SelectObject(memDc, hBmp);
            var size = new SZ(bmp.Width, bmp.Height);
            var src = new PT(0, 0);
            var dst = new PT(x, y);
            var blend = new BLEND { Op = 0, Flags = 0, Alpha = alpha, Format = 1 /* AC_SRC_ALPHA */ };
            UpdateLayeredWindow(Handle, screenDc, ref dst, ref size, memDc, ref src, 0, ref blend, 2 /* ULW_ALPHA */);
        }
        finally
        {
            ReleaseDC(IntPtr.Zero, screenDc);
            if (hBmp != IntPtr.Zero) { SelectObject(memDc, old); DeleteObject(hBmp); }
            DeleteDC(memDc);
        }
    }
}

// ---------------------------------------------------------------------------
// Controls
// ---------------------------------------------------------------------------
internal class Slider : Control
{
    private int min = 0, max = 100, val = 10;
    private bool dragging;
    private const int PAD = 9;
    public event EventHandler ValueChanged;

    public Slider()
    {
        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint | ControlStyles.OptimizedDoubleBuffer |
                 ControlStyles.ResizeRedraw | ControlStyles.Selectable, true);
        Height = 26; Cursor = Cursors.Hand; BackColor = Pal.Panel;
    }

    public int Minimum { get { return min; } set { min = value; val = Clampd(val); Invalidate(); } }
    public int Maximum { get { return max; } set { max = value; val = Clampd(val); Invalidate(); } }
    public int Value
    {
        get { return val; }
        set
        {
            int v = Clampd(value);
            if (v == val) return;
            val = v; Invalidate();
            if (ValueChanged != null) ValueChanged(this, EventArgs.Empty);
        }
    }
    public void SetSilent(int v) { val = Clampd(v); Invalidate(); }
    private int Clampd(int v) { return Math.Max(min, Math.Min(max, v)); }
    private int TrackWidth { get { return Math.Max(1, Width - PAD * 2); } }

    protected override void OnMouseDown(MouseEventArgs e) { base.OnMouseDown(e); Focus(); dragging = true; Capture = true; FromX(e.X); }
    protected override void OnMouseMove(MouseEventArgs e) { base.OnMouseMove(e); if (dragging) FromX(e.X); }
    protected override void OnMouseUp(MouseEventArgs e) { base.OnMouseUp(e); dragging = false; Capture = false; }
    protected override bool IsInputKey(Keys k) { return k == Keys.Left || k == Keys.Right || base.IsInputKey(k); }
    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if (e.KeyCode == Keys.Left) Value = val - 1; else if (e.KeyCode == Keys.Right) Value = val + 1;
    }
    protected override void OnMouseWheel(MouseEventArgs e) { base.OnMouseWheel(e); Value = val + (e.Delta > 0 ? 1 : -1); }
    protected override void OnGotFocus(EventArgs e) { base.OnGotFocus(e); Invalidate(); }
    protected override void OnLostFocus(EventArgs e) { base.OnLostFocus(e); Invalidate(); }

    private void FromX(int x)
    {
        double r = Math.Max(0, Math.Min(1, (double)(x - PAD) / TrackWidth));
        Value = min + (int)Math.Round(r * (max - min));
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        int cy = Height / 2;
        using (var b = new SolidBrush(Pal.AccentDim)) g.FillRectangle(b, PAD, cy - 2, TrackWidth, 4);
        double frac = (double)(val - min) / Math.Max(1, max - min);
        int kx = PAD + (int)Math.Round(frac * TrackWidth);
        using (var b = new SolidBrush(Pal.Accent)) g.FillRectangle(b, PAD, cy - 2, kx - PAD, 4);
        const int R = 7;
        using (var b = new SolidBrush(Focused ? Pal.AccentLite : Pal.Accent)) g.FillEllipse(b, kx - R, cy - R, R * 2, R * 2);
        using (var p = new Pen(Pal.Ink, 2f)) g.DrawEllipse(p, kx - R, cy - R, R * 2, R * 2);
    }
}

internal class Pill : Button
{
    private bool hot;
    public bool Active;

    public Pill(string text)
    {
        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint | ControlStyles.OptimizedDoubleBuffer |
                 ControlStyles.ResizeRedraw, true);
        FlatStyle = FlatStyle.Flat; FlatAppearance.BorderSize = 0;
        BackColor = Pal.Panel; Cursor = Cursors.Hand; Text = text;
        Font = new Font("Segoe UI", 9f, FontStyle.Bold);
    }

    protected override void OnMouseEnter(EventArgs e) { hot = true; Invalidate(); base.OnMouseEnter(e); }
    protected override void OnMouseLeave(EventArgs e) { hot = false; Invalidate(); base.OnMouseLeave(e); }
    protected override bool ShowFocusCues { get { return false; } }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        using (var bg = new SolidBrush(Parent != null ? Parent.BackColor : Pal.Panel)) g.FillRectangle(bg, ClientRectangle);
        var r = new RectangleF(1, 1, Width - 3, Height - 3);
        using (var path = Pal.Round(r, Math.Min(9, r.Height / 2)))
        {
            if (Active)
                using (var b = new LinearGradientBrush(r, Pal.AccentLite, Pal.Accent, 90f)) g.FillPath(b, path);
            else
            {
                using (var b = new SolidBrush(hot ? Pal.Field : Pal.Panel)) g.FillPath(b, path);
                using (var p = new Pen(hot ? Pal.Accent : Pal.AccentDim, 1.5f)) g.DrawPath(p, path);
            }
        }
        using (var fmt = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center })
        using (var fg = new SolidBrush(Active ? Pal.Ink : Pal.Accent))
            g.DrawString(Text, Font, fg, r, fmt);
    }
}

internal class Swatch : Control
{
    public Color Value { get; set; }
    public bool Selected, IsCustom;
    private bool hot;

    public Swatch(Color c)
    {
        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint | ControlStyles.OptimizedDoubleBuffer, true);
        Value = c; Size = new Size(28, 28); Cursor = Cursors.Hand; BackColor = Pal.Panel;
    }
    protected override void OnMouseEnter(EventArgs e) { hot = true; Invalidate(); base.OnMouseEnter(e); }
    protected override void OnMouseLeave(EventArgs e) { hot = false; Invalidate(); base.OnMouseLeave(e); }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.Clear(Pal.Panel);
        var r = new RectangleF(4, 4, Width - 9, Height - 9);
        if (IsCustom)
        {
            // a little hue wheel, so it reads as "pick any colour"
            for (int i = 0; i < 12; i++)
                using (var b = new SolidBrush(Hsv(i * 30, 1, 1)))
                    g.FillPie(b, r.X, r.Y, r.Width, r.Height, i * 30 - 90, 31);
            using (var b = new SolidBrush(Value)) g.FillEllipse(b, r.X + r.Width * 0.28f, r.Y + r.Height * 0.28f, r.Width * 0.44f, r.Height * 0.44f);
        }
        else
        {
            using (var b = new SolidBrush(Value)) g.FillEllipse(b, r);
            // keeps a black swatch (the default outline colour) visible on the dark panel
            using (var p = new Pen(Color.FromArgb(90, 255, 255, 255), 1f)) g.DrawEllipse(p, r);
        }
        if (Selected || hot)
            using (var p = new Pen(Selected ? Pal.AccentLite : Pal.AccentDim, 2f))
                g.DrawEllipse(p, 1.5f, 1.5f, Width - 4, Height - 4);
    }

    public static Color Hsv(double h, double s, double v)
    {
        h = ((h % 360) + 360) % 360;
        double c = v * s, x = c * (1 - Math.Abs((h / 60) % 2 - 1)), m = v - c, r = 0, gg = 0, b = 0;
        if (h < 60) { r = c; gg = x; } else if (h < 120) { r = x; gg = c; } else if (h < 180) { gg = c; b = x; }
        else if (h < 240) { gg = x; b = c; } else if (h < 300) { r = x; b = c; } else { r = c; b = x; }
        return Color.FromArgb((int)((r + m) * 255), (int)((gg + m) * 255), (int)((b + m) * 255));
    }
}

// The crosshair gallery: a grid of live previews with its own wheel scrolling, so
// there is no light-grey native scrollbar on the dark UI.
internal class Gallery : Control
{
    public List<Style> Items = new List<Style>();
    public Settings Look;
    public string SelectedId;
    public event Action<Style> Picked;

    private int scroll, hot = -1;
    private const int TW = 80, TH = 90, GAP = 6;
    private readonly Dictionary<string, Bitmap> cache = new Dictionary<string, Bitmap>();
    private string cacheKey = "";

    public Gallery()
    {
        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint | ControlStyles.OptimizedDoubleBuffer |
                 ControlStyles.ResizeRedraw | ControlStyles.Selectable, true);
        BackColor = Pal.Ink; Cursor = Cursors.Hand;
    }

    private int Cols { get { return Math.Max(1, (Width - GAP - 10) / (TW + GAP)); } }
    private int ContentHeight { get { return (Items.Count + Cols - 1) / Cols * (TH + GAP) + GAP; } }
    private int MaxScroll { get { return Math.Max(0, ContentHeight - Height); } }

    public void SetItems(List<Style> items) { Items = items; scroll = 0; hot = -1; Invalidate(); }

    public void Refresh(Settings look)
    {
        Look = look;
        // Tiles only depend on the colours, thickness, outline and dot, so dragging
        // the offset or opacity sliders doesn't rebuild 80 previews.
        string key = look.Color.ToArgb() + "|" + look.OutlineColor.ToArgb() + "|" + look.Thickness + "|" +
                     look.Outline + "|" + look.CenterDot + "|" + look.ImagePath;
        if (key != cacheKey)
        {
            foreach (var b in cache.Values) b.Dispose();
            cache.Clear();
            cacheKey = key;
        }
        Invalidate();
    }

    public void EnsureVisible(string id)
    {
        int i = Items.FindIndex(s => s.Id == id);
        if (i < 0) return;
        int top = GAP + i / Cols * (TH + GAP);
        if (top < scroll) scroll = top - GAP;
        else if (top + TH > scroll + Height) scroll = top + TH + GAP - Height;
        scroll = Math.Max(0, Math.Min(MaxScroll, scroll));
        Invalidate();
    }

    private Bitmap Thumb(Style st)
    {
        Bitmap b;
        if (cache.TryGetValue(st.Id, out b)) return b;
        var c = Look.Clone();
        c.Size = 10; c.Gap = 4; c.Rotation = 0;
        c.Thickness = Math.Min(c.Thickness, 3);
        c.Outline = Math.Min(c.Outline, 1);
        b = new Bitmap(TW - 8, TH - 26, PixelFormat.Format32bppArgb);
        using (var g = Graphics.FromImage(b)) Render.DrawFitted(g, st, c, new RectangleF(3, 3, b.Width - 6, b.Height - 6));
        cache[st.Id] = b;
        return b;
    }

    private Rectangle TileRect(int i)
    {
        int cols = Cols;
        int used = cols * (TW + GAP) - GAP, left = Math.Max(GAP, (Width - 10 - used) / 2);
        return new Rectangle(left + (i % cols) * (TW + GAP), GAP + (i / cols) * (TH + GAP) - scroll, TW, TH);
    }

    private int HitTest(Point p)
    {
        for (int i = 0; i < Items.Count; i++) if (TileRect(i).Contains(p)) return i;
        return -1;
    }

    protected override void OnMouseWheel(MouseEventArgs e)
    {
        base.OnMouseWheel(e);
        scroll = Math.Max(0, Math.Min(MaxScroll, scroll - e.Delta / 120 * (TH + GAP) / 2));
        hot = HitTest(e.Location);
        Invalidate();
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        if (dragBar) { DragBar(e.Y); return; }
        int h = HitTest(e.Location);
        if (h != hot) { hot = h; Invalidate(); }
    }

    protected override void OnMouseLeave(EventArgs e) { base.OnMouseLeave(e); hot = -1; Invalidate(); }

    private bool dragBar;
    private int dragGrab;

    private Rectangle BarRect()
    {
        if (MaxScroll == 0) return Rectangle.Empty;
        int h = Math.Max(30, Height * Height / ContentHeight);
        int y = (int)((long)(Height - h) * scroll / MaxScroll);
        return new Rectangle(Width - 8, y, 5, h);
    }

    private void DragBar(int y)
    {
        var bar = BarRect();
        int travel = Height - bar.Height;
        if (travel <= 0) return;
        scroll = Math.Max(0, Math.Min(MaxScroll, (int)((long)(y - dragGrab) * MaxScroll / travel)));
        Invalidate();
    }

    protected override void OnMouseDown(MouseEventArgs e)
    {
        base.OnMouseDown(e);
        Focus();
        var bar = BarRect();
        if (bar != Rectangle.Empty && e.X >= Width - 12)
        {
            dragBar = true; Capture = true;
            dragGrab = bar.Contains(e.X - 0, e.Y) || (e.Y >= bar.Y && e.Y <= bar.Bottom) ? e.Y - bar.Y : bar.Height / 2;
            DragBar(e.Y);
            return;
        }
        int i = HitTest(e.Location);
        if (i >= 0 && Picked != null) Picked(Items[i]);
    }

    protected override void OnMouseUp(MouseEventArgs e) { base.OnMouseUp(e); dragBar = false; Capture = false; }

    protected override void OnResize(EventArgs e) { base.OnResize(e); scroll = Math.Min(scroll, MaxScroll); }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.Clear(Pal.Ink);
        if (Look == null) return;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        using (var nameFont = new Font("Segoe UI", 8f))
        using (var fmt = new StringFormat { Alignment = StringAlignment.Center, Trimming = StringTrimming.EllipsisCharacter, FormatFlags = StringFormatFlags.NoWrap })
            for (int i = 0; i < Items.Count; i++)
            {
                var r = TileRect(i);
                if (r.Bottom < 0 || r.Top > Height) continue;
                var st = Items[i];
                bool sel = st.Id == SelectedId;
                using (var path = Pal.Round(new RectangleF(r.X + 0.5f, r.Y + 0.5f, r.Width - 1, r.Height - 1), 8))
                {
                    using (var b = new SolidBrush(sel ? Color.FromArgb(40, 34, 18) : i == hot ? Pal.Field : Pal.Panel)) g.FillPath(b, path);
                    using (var p = new Pen(sel ? Pal.Accent : i == hot ? Pal.AccentDim : Pal.Line, sel ? 2f : 1f)) g.DrawPath(p, path);
                }
                g.DrawImageUnscaled(Thumb(st), r.X + 4, r.Y + 4);
                using (var tb = new SolidBrush(sel ? Pal.AccentLite : Pal.Text))
                    g.DrawString(st.Name, nameFont, tb, new RectangleF(r.X + 2, r.Bottom - 20, r.Width - 4, 16), fmt);
            }
        var bar = BarRect();
        if (bar != Rectangle.Empty)
            using (var b = new SolidBrush(dragBar ? Pal.Accent : Pal.AccentDim))
            using (var p = Pal.Round(bar, 2.5f)) g.FillPath(b, p);
    }
}

// Live preview on a few backdrops, at the real on-screen pixel size.
internal class Preview : Control
{
    public Settings Look;
    public int Backdrop;
    public static readonly string[] Names = { "Night", "Sky", "Snow", "Grid", "Desert" };
    public event EventHandler BackdropChanged;

    public Preview()
    {
        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
        Cursor = Cursors.Hand;
    }

    protected override void OnMouseDown(MouseEventArgs e)
    {
        base.OnMouseDown(e);
        Backdrop = (Backdrop + 1) % Names.Length;
        if (BackdropChanged != null) BackdropChanged(this, EventArgs.Empty);
        Invalidate();
    }

    private void Scene(Graphics g, Rectangle r)
    {
        switch (Backdrop)
        {
            case 1:
                using (var b = new LinearGradientBrush(r, Color.FromArgb(96, 165, 250), Color.FromArgb(191, 219, 254), 90f)) g.FillRectangle(b, r);
                using (var b = new SolidBrush(Color.FromArgb(74, 124, 60))) g.FillRectangle(b, r.X, r.Y + r.Height * 2 / 3, r.Width, r.Height / 3 + 1);
                using (var b = new SolidBrush(Color.FromArgb(120, 113, 108))) g.FillRectangle(b, r.X + r.Width * 0.62f, r.Y + r.Height * 0.3f, r.Width * 0.14f, r.Height * 0.37f + 1);
                break;
            case 2:
                using (var b = new LinearGradientBrush(r, Color.FromArgb(241, 245, 249), Color.FromArgb(203, 213, 225), 90f)) g.FillRectangle(b, r);
                break;
            case 3:
                g.Clear(Color.FromArgb(38, 38, 42));
                using (var p = new Pen(Color.FromArgb(60, 60, 66)))
                {
                    for (int x = r.X + (r.Width / 2) % 20; x < r.Right; x += 20) g.DrawLine(p, x, r.Y, x, r.Bottom);
                    for (int y = r.Y + (r.Height / 2) % 20; y < r.Bottom; y += 20) g.DrawLine(p, r.X, y, r.Right, y);
                }
                break;
            case 4:
                using (var b = new LinearGradientBrush(r, Color.FromArgb(251, 191, 114), Color.FromArgb(180, 120, 60), 90f)) g.FillRectangle(b, r);
                break;
            default:
                using (var b = new LinearGradientBrush(r, Color.FromArgb(15, 23, 42), Color.FromArgb(30, 27, 45), 90f)) g.FillRectangle(b, r);
                break;
        }
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        var r = ClientRectangle;
        using (var clip = Pal.Round(new RectangleF(0, 0, Width, Height), 10))
        {
            g.Clear(Pal.Panel);
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.SetClip(clip);
            Scene(g, r);
            if (Look != null)
            {
                int half;
                using (var bmp = Render.Bitmap(Look, out half))
                using (var ia = new ImageAttributes())
                {
                    var cm = new ColorMatrix { Matrix33 = Look.Opacity / 100f };
                    ia.SetColorMatrix(cm);
                    g.DrawImage(bmp, new Rectangle(Width / 2 - half, Height / 2 - half, bmp.Width, bmp.Height),
                                0, 0, bmp.Width, bmp.Height, GraphicsUnit.Pixel, ia);
                }
            }
            g.ResetClip();
            using (var f = new Font("Segoe UI", 8f))
            using (var b = new SolidBrush(Color.FromArgb(170, 0, 0, 0)))
            {
                string label = "Preview · " + Names[Backdrop] + " · click to change backdrop";
                var sz = g.MeasureString(label, f);
                g.FillRectangle(b, 6, Height - sz.Height - 8, sz.Width + 6, sz.Height + 2);
                g.DrawString(label, f, Brushes.White, 9, Height - sz.Height - 7);
            }
            using (var p = new Pen(Pal.Line, 1.5f)) g.DrawPath(p, clip);
        }
    }
}

// ---------------------------------------------------------------------------
// Main window
// ---------------------------------------------------------------------------
public class NosCrosshair : Form
{
    [DllImport("user32.dll")] static extern bool RegisterHotKey(IntPtr h, int id, uint mods, uint vk);
    [DllImport("user32.dll")] static extern bool UnregisterHotKey(IntPtr h, int id);
    [DllImport("user32.dll")] static extern bool SetProcessDPIAware();
    [DllImport("user32.dll")] static extern bool PostMessage(IntPtr h, int msg, IntPtr w, IntPtr l);
    [DllImport("user32.dll")] static extern int RegisterWindowMessage(string s);
    [DllImport("user32.dll")] static extern bool AllowSetForegroundWindow(int pid);
    [DllImport("dwmapi.dll")] static extern int DwmSetWindowAttribute(IntPtr h, int attr, ref int val, int size);

    const int WM_HOTKEY = 0x312, HWND_BROADCAST = 0xFFFF;
    static readonly int WM_SHOWME = RegisterWindowMessage("NosCrosshair.ShowMe");
    const int HK_TOGGLE = 1, HK_NEXT = 2, HK_PREV = 3;

    private readonly Settings cfg;
    private readonly Overlay overlay = new Overlay();
    private readonly Gallery gallery = new Gallery();
    private readonly Preview preview = new Preview();
    private readonly List<Swatch> swatches = new List<Swatch>();
    private Swatch customSwatch, outlineSwatch;
    private readonly List<Pill> catPills = new List<Pill>();
    private Pill showPill, dotPill, monitorPill, hotkeyPill;
    private Label styleName, hotkeyNote;
    private readonly Dictionary<string, Slider> sliders = new Dictionary<string, Slider>();
    private readonly Dictionary<string, Label> values = new Dictionary<string, Label>();
    private readonly NotifyIcon tray = new NotifyIcon();
    private readonly System.Windows.Forms.Timer saveTimer = new System.Windows.Forms.Timer { Interval = 800 };
    private readonly ToolTip tips = new ToolTip { InitialDelay = 400, AutoPopDelay = 8000 };
    private bool quitting, trayHintShown, hotkeysOk;
    private readonly Random rng = new Random();

    private static readonly Color[] Presets =
    {
        Color.FromArgb(0, 255, 0), Color.FromArgb(0, 255, 255), Color.FromArgb(255, 40, 40), Color.FromArgb(255, 235, 0),
        Color.FromArgb(255, 0, 255), Color.FromArgb(255, 255, 255), Color.FromArgb(255, 140, 0), Color.FromArgb(255, 105, 180),
        Color.FromArgb(160, 255, 40), Color.FromArgb(40, 140, 255),
    };

    public NosCrosshair()
    {
        cfg = Settings.Load();
        Render.LoadImage(cfg.ImagePath);

        Text = "NosCrosshair";
        BackColor = Pal.Ink;
        ForeColor = Pal.Text;
        FormBorderStyle = FormBorderStyle.FixedSingle;
        MaximizeBox = false;
        StartPosition = FormStartPosition.CenterScreen;
        ClientSize = new Size(1100, 730);
        Font = new Font("Segoe UI", 9f);
        Icon = MakeIcon(32);

        BuildUi();

        tray.Icon = MakeIcon(16);
        tray.Text = "NosCrosshair";
        tray.Visible = true;
        var menu = new ContextMenuStrip();
        menu.Items.Add("Open NosCrosshair", null, (s, e) => ShowPanel());
        menu.Items.Add("Show / hide crosshair  (F8)", null, (s, e) => ToggleVisible());
        menu.Items.Add("Next crosshair  (F9)", null, (s, e) => Cycle(1));
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add("Exit", null, (s, e) => Quit());
        tray.ContextMenuStrip = menu;
        tray.DoubleClick += (s, e) => ShowPanel();

        saveTimer.Tick += (s, e) => { saveTimer.Stop(); cfg.Save(); };
        Microsoft.Win32.SystemEvents.DisplaySettingsChanged += (s, e) => BeginInvoke((Action)ApplyAll);
    }

    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        int on = 1;
        try { DwmSetWindowAttribute(Handle, 20, ref on, 4); } catch { }   // dark title bar
        RegisterKeys();
    }

    protected override void OnShown(EventArgs e)
    {
        base.OnShown(e);
        ApplyAll();
        gallery.EnsureVisible(cfg.Style);
    }

    // ---------------------------------------------------------------- layout
    private Label MakeLabel(string text, int x, int y, int w, Color c, float size = 9f, FontStyle fs = FontStyle.Regular, Control parent = null)
    {
        var l = new Label
        {
            Text = text, Location = new Point(x, y), AutoSize = false, Size = new Size(w, (int)(size * 2.2f)),
            ForeColor = c, BackColor = Color.Transparent, Font = new Font("Segoe UI", size, fs)
        };
        (parent ?? this).Controls.Add(l);
        return l;
    }

    private void BuildUi()
    {
        MakeLabel("NOSCROSSHAIR", 20, 14, 300, Pal.Accent, 18f, FontStyle.Bold);
        MakeLabel(Styles.All.Count - 1 + " crosshairs · click-through overlay · works over any windowed / borderless game",
                  22, 50, 600, Pal.Muted, 9f);

        showPill = new Pill("") { Location = new Point(900, 18), Size = new Size(180, 40), Font = new Font("Segoe UI", 11f, FontStyle.Bold) };
        showPill.Click += (s, e) => ToggleVisible();
        tips.SetToolTip(showPill, "Turn the on-screen crosshair on or off (F8 anywhere)");
        Controls.Add(showPill);

        // ---- left: categories + gallery
        int x = 20;
        foreach (var cat in Styles.Categories)
        {
            var p = new Pill(cat) { Location = new Point(x, 80), Size = new Size(TextRenderer.MeasureText(cat, new Font("Segoe UI", 9f, FontStyle.Bold)).Width + 22, 28) };
            string c = cat;
            p.Click += (s, e) => SetCategory(c);
            catPills.Add(p);
            Controls.Add(p);
            x += p.Width + 6;
        }

        var galleryFrame = new Panel { Location = new Point(20, 116), Size = new Size(630, 562), BackColor = Pal.Line, Padding = new Padding(1) };
        gallery.Dock = DockStyle.Fill;
        galleryFrame.Controls.Add(gallery);
        Controls.Add(galleryFrame);
        gallery.Picked += st =>
        {
            if (st.Id == "custom" && Render.CustomImage == null && !PickImage()) return;
            cfg.Style = st.Id;
            Changed();
        };

        hotkeyNote = MakeLabel("", 20, 688, 630, Pal.Muted, 8.5f);

        // ---- right: preview + tuning
        var side = new Panel { Location = new Point(670, 80), Size = new Size(410, 632), BackColor = Pal.Panel };
        Controls.Add(side);

        preview.Location = new Point(12, 12);
        preview.Size = new Size(386, 170);
        preview.Backdrop = Math.Max(0, Math.Min(Preview.Names.Length - 1, cfg.Backdrop));
        preview.BackdropChanged += (s, e) => { cfg.Backdrop = preview.Backdrop; QueueSave(); };
        side.Controls.Add(preview);

        styleName = MakeLabel("", 12, 188, 386, Pal.AccentLite, 11f, FontStyle.Bold, side);

        MakeLabel("COLOR", 12, 216, 80, Pal.Muted, 8f, FontStyle.Bold, side);
        int sx = 10;
        foreach (var c in Presets)
        {
            var sw = new Swatch(c) { Location = new Point(sx, 234) };
            sw.Click += (s, e) => { cfg.Color = sw.Value; Changed(); };
            swatches.Add(sw);
            side.Controls.Add(sw);
            sx += 31;
        }
        customSwatch = new Swatch(cfg.Color) { Location = new Point(sx + 4, 234), IsCustom = true };
        tips.SetToolTip(customSwatch, "Pick any colour");
        customSwatch.Click += (s, e) =>
        {
            var c = PickColor(cfg.Color);
            if (c.HasValue) { cfg.Color = c.Value; Changed(); }
        };
        side.Controls.Add(customSwatch);

        int y = 272;
        AddSlider(side, "size", "Size", 1, 80, ref y, "How long the arms are / how big the shape is");
        AddSlider(side, "thickness", "Thickness", 1, 12, ref y, "Line width in pixels");
        AddSlider(side, "gap", "Gap", 0, 40, ref y, "Empty space in the middle");
        AddSlider(side, "outline", "Outline", 0, 6, ref y, "Dark border so it stays visible on bright scenes (0 = off)");
        outlineSwatch = new Swatch(cfg.OutlineColor) { Location = new Point(372, y - 33) };
        tips.SetToolTip(outlineSwatch, "Outline colour");
        outlineSwatch.Click += (s, e) =>
        {
            var c = PickColor(cfg.OutlineColor);
            if (c.HasValue) { cfg.OutlineColor = c.Value; Changed(); }
        };
        side.Controls.Add(outlineSwatch);
        AddSlider(side, "opacity", "Opacity", 10, 100, ref y, "How see-through the crosshair is");
        AddSlider(side, "rotation", "Rotation", 0, 359, ref y, "Spin the crosshair (degrees)");
        AddSlider(side, "offsetX", "Offset X", -400, 400, ref y, "Nudge left/right from the screen centre (pixels)");
        AddSlider(side, "offsetY", "Offset Y", -400, 400, ref y, "Nudge up/down from the screen centre (pixels)");

        y += 4;
        dotPill = SidePill(side, "Center dot", 12, y, 118, "Add a dot in the middle of any crosshair",
                           () => { cfg.CenterDot = !cfg.CenterDot; Changed(); });
        monitorPill = SidePill(side, "", 136, y, 132, "Which monitor the crosshair is centred on",
                               () => { cfg.Monitor = (cfg.Monitor + 1) % Screen.AllScreens.Length; Changed(); });
        hotkeyPill = SidePill(side, "Hotkeys", 274, y, 124, "F8 show/hide · F7 / F9 previous / next crosshair",
                              () => { cfg.Hotkeys = !cfg.Hotkeys; RegisterKeys(); Changed(); });
        y += 38;
        SidePill(side, "Randomize", 12, y, 118, "Surprise me", Randomize);
        SidePill(side, "Load image…", 136, y, 132, "Use your own PNG as the crosshair", () =>
        {
            if (PickImage()) { cfg.Style = "custom"; Changed(); }
        });
        SidePill(side, "Reset", 274, y, 124, "Back to a green classic cross", () =>
        {
            var d = new Settings { Visible = cfg.Visible, Monitor = cfg.Monitor, Hotkeys = cfg.Hotkeys, ImagePath = cfg.ImagePath, Category = cfg.Category, Backdrop = cfg.Backdrop };
            CopyInto(d);
            Changed();
        });

        SetCategory(cfg.Category, false);
    }

    private void CopyInto(Settings d)
    {
        cfg.Style = d.Style; cfg.Color = d.Color; cfg.OutlineColor = d.OutlineColor;
        cfg.Size = d.Size; cfg.Thickness = d.Thickness; cfg.Gap = d.Gap; cfg.Outline = d.Outline;
        cfg.Opacity = d.Opacity; cfg.Rotation = d.Rotation; cfg.OffsetX = d.OffsetX; cfg.OffsetY = d.OffsetY;
        cfg.CenterDot = d.CenterDot;
    }

    private Pill SidePill(Control parent, string text, int x, int y, int w, string tip, Action click)
    {
        var p = new Pill(text) { Location = new Point(x, y), Size = new Size(w, 32) };
        p.Click += (s, e) => click();
        tips.SetToolTip(p, tip);
        parent.Controls.Add(p);
        return p;
    }

    private void AddSlider(Control parent, string key, string label, int min, int max, ref int y, string tip)
    {
        var l = MakeLabel(label, 12, y + 3, 80, Pal.Text, 9f, FontStyle.Regular, parent);
        var s = new Slider { Location = new Point(92, y), Width = 220, Minimum = min, Maximum = max };
        s.SetSilent(Get(key));
        s.ValueChanged += (o, e) => { Set(key, s.Value); Changed(); };
        var v = MakeLabel("", 314, y + 3, 54, Pal.Accent, 9f, FontStyle.Bold, parent);
        v.TextAlign = ContentAlignment.TopRight;
        v.Cursor = Cursors.Hand;
        tips.SetToolTip(v, "Double-click to reset");
        v.DoubleClick += (o, e) => { var d = new Settings(); s.Value = GetFrom(d, key); };
        tips.SetToolTip(s, tip); tips.SetToolTip(l, tip);
        parent.Controls.Add(s);
        sliders[key] = s; values[key] = v;
        y += 33;
    }

    private int Get(string k) { return GetFrom(cfg, k); }

    private static int GetFrom(Settings c, string k)
    {
        switch (k)
        {
            case "size": return c.Size; case "thickness": return c.Thickness; case "gap": return c.Gap;
            case "outline": return c.Outline; case "opacity": return c.Opacity; case "rotation": return c.Rotation;
            case "offsetX": return c.OffsetX; case "offsetY": return c.OffsetY;
        }
        return 0;
    }

    private void Set(string k, int v)
    {
        switch (k)
        {
            case "size": cfg.Size = v; break; case "thickness": cfg.Thickness = v; break; case "gap": cfg.Gap = v; break;
            case "outline": cfg.Outline = v; break; case "opacity": cfg.Opacity = v; break; case "rotation": cfg.Rotation = v; break;
            case "offsetX": cfg.OffsetX = v; break; case "offsetY": cfg.OffsetY = v; break;
        }
    }

    // ---------------------------------------------------------------- behaviour
    private void SetCategory(string cat, bool save = true)
    {
        if (Array.IndexOf(Styles.Categories, cat) < 0) cat = "All";
        cfg.Category = cat;
        foreach (var p in catPills) { p.Active = p.Text == cat; p.Invalidate(); }
        gallery.SetItems(cat == "All" ? new List<Style>(Styles.All) : Styles.All.FindAll(s => s.Cat == cat));
        if (save) QueueSave();
    }

    private void Changed()
    {
        cfg.Sanitize();
        ApplyAll();
        QueueSave();
    }

    private void QueueSave() { saveTimer.Stop(); saveTimer.Start(); }

    private void ApplyAll()
    {
        var st = Styles.Find(cfg.Style);
        foreach (var kv in sliders) kv.Value.SetSilent(Get(kv.Key));
        foreach (var kv in values)
        {
            int v = Get(kv.Key);
            kv.Value.Text = kv.Key == "opacity" ? v + "%" : kv.Key == "rotation" ? v + "°" : v + (kv.Key.StartsWith("offset") ? "" : " px");
        }
        bool matched = false;
        foreach (var sw in swatches)
        {
            bool on = sw.Value.ToArgb() == cfg.Color.ToArgb();
            matched |= on;
            if (sw.Selected != on) { sw.Selected = on; sw.Invalidate(); }
        }
        customSwatch.Selected = !matched;
        customSwatch.Value = cfg.Color;
        customSwatch.Invalidate();
        outlineSwatch.Value = cfg.OutlineColor;
        outlineSwatch.Invalidate();

        showPill.Active = cfg.Visible;
        showPill.Text = cfg.Visible ? "CROSSHAIR ON" : "CROSSHAIR OFF";
        showPill.Invalidate();
        dotPill.Active = cfg.CenterDot; dotPill.Invalidate();
        hotkeyPill.Active = cfg.Hotkeys; hotkeyPill.Invalidate();
        int screens = Screen.AllScreens.Length;
        monitorPill.Text = "Monitor " + (Math.Min(cfg.Monitor, screens - 1) + 1) + " of " + screens;
        monitorPill.Invalidate();

        styleName.Text = st.Name + "  ·  " + st.Cat;
        hotkeyNote.Text = !cfg.Hotkeys ? "Hotkeys off. Closing this window keeps the crosshair running in the tray."
            : hotkeysOk ? "F8 show / hide  ·  F7 / F9 previous / next crosshair  ·  closing this window keeps it running in the tray"
            : "Some hotkeys are taken by another app. Closing this window keeps the crosshair running in the tray.";

        gallery.SelectedId = cfg.Style;
        gallery.Refresh(cfg);
        preview.Look = cfg;
        preview.Invalidate();
        if (!NoOverlay) overlay.Apply(cfg);
        tray.Text = "NosCrosshair — " + st.Name + (cfg.Visible ? "" : " (hidden)");
    }

    private void ToggleVisible() { cfg.Visible = !cfg.Visible; Changed(); }

    private void Cycle(int dir)
    {
        var list = Styles.All.FindAll(s => s.Id != "custom" || Render.CustomImage != null);
        int i = list.FindIndex(s => s.Id == cfg.Style);
        i = ((i + dir) % list.Count + list.Count) % list.Count;
        cfg.Style = list[i].Id;
        if (!cfg.Visible) cfg.Visible = true;
        Changed();
        gallery.EnsureVisible(cfg.Style);
    }

    private void Randomize()
    {
        var list = Styles.All.FindAll(s => s.Id != "custom");
        cfg.Style = list[rng.Next(list.Count)].Id;
        cfg.Color = Swatch.Hsv(rng.Next(360), 0.85 + rng.NextDouble() * 0.15, 1);
        cfg.Size = rng.Next(5, 16); cfg.Thickness = rng.Next(1, 4); cfg.Gap = rng.Next(0, 8);
        cfg.Outline = rng.Next(0, 2); cfg.Rotation = 0; cfg.CenterDot = rng.Next(4) == 0;
        Changed();
        gallery.EnsureVisible(cfg.Style);
    }

    private Color? PickColor(Color start)
    {
        using (var d = new ColorDialog { Color = start, FullOpen = true, AnyColor = true })
            return d.ShowDialog(this) == DialogResult.OK ? Color.FromArgb(255, d.Color) : (Color?)null;
    }

    private bool PickImage()
    {
        using (var d = new OpenFileDialog { Title = "Choose a crosshair image", Filter = "Images|*.png;*.gif;*.bmp;*.jpg;*.jpeg|All files|*.*" })
        {
            if (d.ShowDialog(this) != DialogResult.OK) return false;
            Render.LoadImage(d.FileName);
            if (Render.CustomImage == null)
            {
                MessageBox.Show(this, "That file couldn't be opened as an image.", "NosCrosshair");
                return false;
            }
            cfg.ImagePath = d.FileName;
            return true;
        }
    }

    // ---------------------------------------------------------------- hotkeys / tray
    private void RegisterKeys()
    {
        UnregisterHotKey(Handle, HK_TOGGLE); UnregisterHotKey(Handle, HK_NEXT); UnregisterHotKey(Handle, HK_PREV);
        hotkeysOk = true;
        if (!cfg.Hotkeys) return;
        hotkeysOk &= RegisterHotKey(Handle, HK_TOGGLE, 0x4000 /* MOD_NOREPEAT */, (uint)Keys.F8);
        hotkeysOk &= RegisterHotKey(Handle, HK_NEXT, 0x4000, (uint)Keys.F9);
        hotkeysOk &= RegisterHotKey(Handle, HK_PREV, 0x4000, (uint)Keys.F7);
    }

    protected override void WndProc(ref Message m)
    {
        if (m.Msg == WM_HOTKEY)
        {
            int id = m.WParam.ToInt32();
            if (id == HK_TOGGLE) ToggleVisible();
            else if (id == HK_NEXT) Cycle(1);
            else if (id == HK_PREV) Cycle(-1);
            return;
        }
        if (m.Msg == WM_SHOWME) { ShowPanel(); return; }
        base.WndProc(ref m);
    }

    private void ShowPanel()
    {
        Show();
        if (WindowState == FormWindowState.Minimized) WindowState = FormWindowState.Normal;
        Activate();
    }

    protected override void OnFormClosing(FormClosingEventArgs e)
    {
        if (!quitting && e.CloseReason == CloseReason.UserClosing)
        {
            // The crosshair is the point of the app — closing the panel shouldn't kill it.
            e.Cancel = true;
            Hide();
            if (!trayHintShown)
            {
                trayHintShown = true;
                tray.ShowBalloonTip(3000, "NosCrosshair is still running",
                    "Your crosshair stays on screen. Right-click the tray icon to exit.", ToolTipIcon.Info);
            }
            return;
        }
        base.OnFormClosing(e);
    }

    private void Quit()
    {
        quitting = true;
        cfg.Save();
        tray.Visible = false;
        overlay.Close();
        Close();
    }

    protected override void OnFormClosed(FormClosedEventArgs e)
    {
        cfg.Save();
        tray.Visible = false;
        tray.Dispose();
        base.OnFormClosed(e);
    }

    private static Icon MakeIcon(int size)
    {
        using (var bmp = new Bitmap(size, size, PixelFormat.Format32bppArgb))
        {
            using (var g = Graphics.FromImage(bmp))
            {
                g.SmoothingMode = SmoothingMode.AntiAlias;
                g.Clear(Color.Transparent);
                using (var b = new SolidBrush(Pal.Ink)) g.FillEllipse(b, 0, 0, size - 1, size - 1);
                var c = new Settings { Style = "holo", Color = Pal.Accent, Thickness = size >= 32 ? 2 : 1, Outline = 0, Size = 10, Gap = 3 };
                Render.DrawFitted(g, Styles.Find("holo"), c, new RectangleF(1, 1, size - 2, size - 2));
            }
            return Icon.FromHandle(bmp.GetHicon());
        }
    }

    // ---------------------------------------------------------------- entry
    // Writes the .ico the build embeds: PNG-compressed entries, which Windows reads fine.
    private static void WriteIco(string path)
    {
        int[] sizes = { 16, 24, 32, 48, 64, 256 };
        var pngs = new List<byte[]>();
        foreach (int s in sizes)
            using (var bmp = new Bitmap(s, s, PixelFormat.Format32bppArgb))
            using (var ms = new MemoryStream())
            {
                using (var g = Graphics.FromImage(bmp))
                {
                    g.SmoothingMode = SmoothingMode.AntiAlias;
                    g.Clear(Color.Transparent);
                    using (var b = new SolidBrush(Pal.Ink)) g.FillEllipse(b, 0, 0, s - 1, s - 1);
                    using (var p = new Pen(Pal.AccentDim, Math.Max(1, s / 32f))) g.DrawEllipse(p, s / 64f, s / 64f, s - 1 - s / 32f, s - 1 - s / 32f);
                    var c = new Settings { Color = Pal.Accent, Thickness = Math.Max(1, s / 16), Outline = 0, Size = 10, Gap = 3 };
                    Render.DrawFitted(g, Styles.Find("holo"), c, new RectangleF(s * 0.1f, s * 0.1f, s * 0.8f, s * 0.8f));
                }
                bmp.Save(ms, ImageFormat.Png);
                pngs.Add(ms.ToArray());
            }
        using (var w = new BinaryWriter(File.Create(path)))
        {
            w.Write((short)0); w.Write((short)1); w.Write((short)sizes.Length);
            int offset = 6 + 16 * sizes.Length;
            for (int i = 0; i < sizes.Length; i++)
            {
                w.Write((byte)(sizes[i] >= 256 ? 0 : sizes[i])); w.Write((byte)(sizes[i] >= 256 ? 0 : sizes[i]));
                w.Write((byte)0); w.Write((byte)0); w.Write((short)1); w.Write((short)32);
                w.Write(pngs[i].Length); w.Write(offset);
                offset += pngs[i].Length;
            }
            foreach (var p in pngs) w.Write(p);
        }
    }

    // Renders the panel straight to a PNG without showing anything — for checking the
    // layout while a game is covering the screen.
    public static bool NoOverlay;

    private static void Snapshot(string path)
    {
        NoOverlay = true;
        try { SetProcessDPIAware(); } catch { }
        Application.EnableVisualStyles();
        using (var f = new NosCrosshair())
        {
            f.tray.Visible = false;
            f.ShowInTaskbar = false;
            f.StartPosition = FormStartPosition.Manual;
            f.Location = new Point(-5000, -5000);
            f.Show();
            Application.DoEvents();
            f.ApplyAll();
            Application.DoEvents();
            using (var bmp = new Bitmap(f.Width, f.Height))
            {
                f.DrawToBitmap(bmp, new Rectangle(Point.Empty, bmp.Size));
                bmp.Save(path, ImageFormat.Png);
            }
        }
    }

    // Every style at real size on one image, for checking new styles at a glance.
    private static void Sheet(string path)
    {
        const int cols = 16, cw = 96, ch = 104;
        int rows = (Styles.All.Count + cols - 1) / cols;
        var c = new Settings { Color = Color.FromArgb(0, 255, 0), Outline = 1, Size = 10, Gap = 4, Thickness = 2 };
        using (var bmp = new Bitmap(cols * cw, rows * ch))
        using (var g = Graphics.FromImage(bmp))
        using (var f = new Font("Segoe UI", 7f))
        using (var fmt = new StringFormat { Alignment = StringAlignment.Center })
        using (var line = new Pen(Pal.Line))
        {
            g.Clear(Pal.Ink);
            for (int i = 0; i < Styles.All.Count; i++)
            {
                var st = Styles.All[i];
                var box = new Rectangle(i % cols * cw, i / cols * ch, cw, ch);
                g.DrawRectangle(line, box);
                Render.DrawFitted(g, st, c, new RectangleF(box.X + 4, box.Y + 4, cw - 8, ch - 22));
                g.DrawString(st.Name, f, Brushes.Gainsboro, new RectangleF(box.X, box.Bottom - 16, cw, 14), fmt);
            }
            bmp.Save(path, ImageFormat.Png);
        }
    }

    [STAThread]
    public static void Main(string[] args)
    {
        if (args.Length == 2 && args[0] == "--write-icon") { WriteIco(args[1]); return; }
        if (args.Length == 2 && args[0] == "--snapshot") { Snapshot(args[1]); return; }
        if (args.Length == 2 && args[0] == "--sheet") { Sheet(args[1]); return; }
        bool first;
        using (var only = new Mutex(true, "NosCrosshair.SingleInstance", out first))
        {
            if (!first)
            {
                AllowSetForegroundWindow(-1);
                PostMessage((IntPtr)HWND_BROADCAST, WM_SHOWME, IntPtr.Zero, IntPtr.Zero);
                return;
            }
            // Without this, Windows scaling (125%, 150%) would stretch the overlay
            // and put the crosshair a few pixels off true centre.
            try { SetProcessDPIAware(); } catch { }
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            Application.Run(new NosCrosshair());
            GC.KeepAlive(only);
        }
    }
}
