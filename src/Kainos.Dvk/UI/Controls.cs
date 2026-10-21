/*  Controls.cs

The voice keyer's controls, drawn in the Kainos colours: a button, a slot tile and the mic level meter.

This file is part of the Kainos Voice Keyer module. Copyright (C) 2026 Justin Cron K7JUS. GPLv3 or later.
*/

using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Globalization;
using System.Windows.Forms;
using Kainos.Modules;

namespace Kainos.Dvk.UI
{
    internal class KButton : Control
    {
        private readonly IKainosTheme _t;
        private bool _on, _hover;
        public float FontPx = 11;

        public KButton(IKainosTheme t, string text)
        {
            _t = t;
            Text = text;
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true);
            BackColor = t.Bg;
            Cursor = Cursors.Hand;
        }

        public bool On { get { return _on; } set { if (_on != value) { _on = value; Invalidate(); } } }

        protected override void OnPaint(PaintEventArgs e)
        {
            e.Graphics.Clear(BackColor);
            _t.DrawButton(e.Graphics, new RectangleF(0.5f, 0.5f, Width - 1.5f, Height - 1.5f), Text, _on, Enabled, _hover, _t.S(FontPx));
        }

        protected override void OnMouseEnter(EventArgs e) { _hover = true; Invalidate(); base.OnMouseEnter(e); }
        protected override void OnMouseLeave(EventArgs e) { _hover = false; Invalidate(); base.OnMouseLeave(e); }
        protected override void OnTextChanged(EventArgs e) { Invalidate(); base.OnTextChanged(e); }
        protected override void OnEnabledChanged(EventArgs e) { Invalidate(); base.OnEnabledChanged(e); }
    }

    // One slot: its key, name and length; gold outline when selected, red while it's on the air or recording, ice
    // while previewed
    internal class SlotTile : Control
    {
        private readonly IKainosTheme _t;
        public readonly Slot Slot;
        public bool Selected, Busy;
        public DvkState BusyAs;
        private bool _hover;

        public SlotTile(IKainosTheme t, Slot s)
        {
            _t = t;
            Slot = s;
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true);
            BackColor = t.Bg;
            Cursor = Cursors.Hand;
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.Clear(BackColor);
            g.SmoothingMode = SmoothingMode.AntiAlias;
            RectangleF r = new RectangleF(1, 1, Width - 3, Height - 3);
            Color fill = Busy ? (BusyAs == DvkState.Preview ? Blend(_t.Raised, _t.Ice, 0.25f) : Blend(_t.Raised, _t.Tx, 0.45f)) : _hover ? Blend(_t.Raised, _t.Ice, 0.08f) : _t.Raised;
            Color edge = Busy ? (BusyAs == DvkState.Preview ? _t.Ice : _t.Tx) : Selected ? _t.Gold : _t.Line;
            using (GraphicsPath p = Rounded(r, _t.S(5)))
            {
                using (Brush b = new SolidBrush(fill)) g.FillPath(b, p);
                using (Pen pen = new Pen(edge, Selected || Busy ? 2f : 1f)) g.DrawPath(pen, p);
            }
            int pad = _t.S(6);
            using (Font key = _t.Font(_t.S(11), true))
            using (Font name = _t.Font(_t.S(12), false))
            using (Font small = _t.Font(_t.S(10), false))
            {
                string k = "F" + Slot.Number;
                TextRenderer.DrawText(g, k, key, new Point(pad, pad), Selected ? _t.Gold : _t.Dim, TextFormatFlags.NoPadding);
                string len = Slot.Empty ? "" : Slot.Seconds.ToString("0.0", CultureInfo.InvariantCulture) + " s";
                Size ls = TextRenderer.MeasureText(len, small);
                TextRenderer.DrawText(g, len, small, new Point(Width - pad - ls.Width, pad + _t.S(1)), _t.Faint, TextFormatFlags.NoPadding);
                Rectangle nr = new Rectangle(pad, pad + _t.S(14), Width - 2 * pad, Math.Max(_t.S(14), Height - 2 * pad - _t.S(14)));
                TextRenderer.DrawText(g, Slot.Label, name, nr, Slot.Empty ? _t.Faint : _t.Text, TextFormatFlags.EndEllipsis | TextFormatFlags.NoPadding | TextFormatFlags.VerticalCenter);
            }
        }

        protected override void OnMouseEnter(EventArgs e) { _hover = true; Invalidate(); base.OnMouseEnter(e); }
        protected override void OnMouseLeave(EventArgs e) { _hover = false; Invalidate(); base.OnMouseLeave(e); }

        internal static Color Blend(Color a, Color b, float k)
        {
            return Color.FromArgb((int)(a.R + (b.R - a.R) * k), (int)(a.G + (b.G - a.G) * k), (int)(a.B + (b.B - a.B) * k));
        }

        internal static GraphicsPath Rounded(RectangleF r, float rad)
        {
            GraphicsPath p = new GraphicsPath();
            float d = rad * 2;
            p.AddArc(r.X, r.Y, d, d, 180, 90);
            p.AddArc(r.Right - d, r.Y, d, d, 270, 90);
            p.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
            p.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
            p.CloseFigure();
            return p;
        }
    }

    // The mic level while recording: -60 to 0 dB, green to -12, gold to -3, red above
    internal class LevelMeter : Control
    {
        private readonly IKainosTheme _t;
        public float Db = -120;

        public LevelMeter(IKainosTheme t)
        {
            _t = t;
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true);
            BackColor = t.Bg;
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.Clear(BackColor);
            Rectangle r = new Rectangle(0, Height / 2 - _t.S(4), Width - 1, _t.S(8));
            using (Brush b = new SolidBrush(_t.Raised)) g.FillRectangle(b, r);
            float k = Math.Max(0, Math.Min(1, (Db + 60) / 60f));
            Color c = Db > -3 ? _t.Tx : Db > -12 ? _t.Gold : Color.FromArgb(0x5f, 0xb8, 0x7a);
            using (Brush b = new SolidBrush(c)) g.FillRectangle(b, r.X, r.Y, r.Width * k, r.Height);
            using (Pen p = new Pen(_t.Line)) g.DrawRectangle(p, r);
        }
    }
}
