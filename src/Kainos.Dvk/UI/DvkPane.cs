/*  DvkPane.cs

The voice keyer's pane: REC / STOP / PLAY / PREVIEW for the selected slot, repeat CQ and its listening gap, then the
12 slots (F1 - F12). A click selects a slot, a double-click or its F-key sends it, Esc stops; a right click renames
it, sets its level, imports or exports a WAV, or clears it.

This file is part of the Kainos Voice Keyer module. Copyright (C) 2026 Justin Cron K7JUS. GPLv3 or later.
*/

using System;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Windows.Forms;
using Kainos.Modules;

namespace Kainos.Dvk.UI
{
    internal class DvkPane : Panel
    {
        private static readonly int[] Gaps = { 3, 5, 8, 10, 15, 20, 30, 45, 60 };

        private readonly IKainosHost _host;
        private readonly IKainosTheme _t;
        private readonly DvkEngine _e;
        private readonly KButton _rec, _stop, _play, _preview, _repeat, _gap, _pop;
        private readonly Label _status;
        private readonly LevelMeter _meter;
        private readonly SlotTile[] _tiles = new SlotTile[SlotStore.Count];
        private readonly Timer _timer;
        private bool _built;
        private int _selected = 1;
        private string _message = "";
        private DateTime _messageAt;

        public event Action PopToggled;

        public DvkPane(IKainosHost host, DvkEngine engine)
        {
            _host = host;
            _t = host.Theme;
            _e = engine;
            BackColor = _t.Bg;
            DoubleBuffered = true;

            ToolTip tip = new ToolTip();
            Func<string, string, KButton> btn = (text, tt) =>
            {
                KButton b = new KButton(_t, text);
                if (tt != null) tip.SetToolTip(b, tt);
                Controls.Add(b);
                return b;
            };
            _rec = btn("REC", "Record the selected slot from your mic (replaces what's in it). STOP or Esc ends it; the quiet before and after is cut off.");
            _stop = btn("STOP", "Stop recording, sending or the preview (Esc)");
            _play = btn("PLAY", "Send the selected slot: the radio is keyed, the recording goes out through your whole transmit chain, then it un-keys (or its F-key)");
            _preview = btn("PREVIEW", "Listen to the selected slot yourself, without sending it");
            _repeat = btn("REPEAT", "Repeat CQ: send the slot, listen for the gap, send it again, until STOP, Esc, or you transmit yourself to answer");
            _gap = btn("5 s", "The listening gap between repeats (click to change)");
            _pop = btn("Pop out", "Docked under the panadapter, or in its own window");

            _status = new Label { ForeColor = _t.Dim, BackColor = _t.Bg, AutoSize = false, AutoEllipsis = true, TextAlign = ContentAlignment.MiddleLeft, Font = _t.Font(_t.S(12), false) };
            Controls.Add(_status);
            _meter = new LevelMeter(_t) { Visible = false };
            tip.SetToolTip(_meter, "Your mic level while recording: aim for gold on the loudest words, never red");
            Controls.Add(_meter);

            for (int i = 0; i < SlotStore.Count; i++)
            {
                SlotTile tile = new SlotTile(_t, _e.Store.Slots[i]);
                int n = i + 1;
                tile.MouseDown += (s, ev) =>
                {
                    if (ev.Button == MouseButtons.Left) select(n);
                    else if (ev.Button == MouseButtons.Right) { select(n); slotMenu(n).Show(tile, ev.Location); }
                };
                tile.DoubleClick += (s, ev) => play(n);
                _tiles[i] = tile;
                Controls.Add(tile);
            }

            _rec.Click += (s, ev) => _e.Record(_selected);
            _stop.Click += (s, ev) => _e.StopNow();
            _play.Click += (s, ev) => play(_selected);
            _preview.Click += (s, ev) => _e.Preview(_selected);
            _repeat.Click += (s, ev) => { _e.Repeat = !_e.Repeat; _host.SetSetting("repeat", _e.Repeat ? "1" : "0"); refresh(); };
            _gap.Click += (s, ev) => gapMenu().Show(_gap, new Point(0, _gap.Height));
            _pop.Click += (s, ev) => PopToggled?.Invoke();

            _e.Status += m => _host.BeginInvoke(() => say(m));
            _e.Changed += () => _host.BeginInvoke(refresh);

            int sel;
            if (int.TryParse(_host.GetSetting("selected", "1"), out sel) && sel >= 1 && sel <= SlotStore.Count) _selected = sel;
            _e.Repeat = _host.GetSetting("repeat", "0") == "1";
            double gap;
            if (double.TryParse(_host.GetSetting("gap", "5"), NumberStyles.Float, CultureInfo.InvariantCulture, out gap)) _e.RepeatSeconds = gap;

            _timer = new Timer { Interval = 100 };
            _timer.Tick += (s, ev) => tick();
            _built = true;
            refresh();
            PerformLayout();
        }

        public bool Running { set { if (value) _timer.Start(); else _timer.Stop(); } }

        protected override void Dispose(bool disposing)
        {
            if (disposing) { _timer.Stop(); _timer.Dispose(); }
            base.Dispose(disposing);
        }

        // ---- keys (IKainosVoice.SetKeyHandler, UI thread): F1 - F12 send, Esc stops ----

        public bool Key(Keys k)
        {
            if (k >= Keys.F1 && k <= Keys.F12) { play(k - Keys.F1 + 1); return true; }
            if (k == Keys.Escape && _e.State != DvkState.Idle) { _e.StopNow(); return true; }
            return false;
        }

        private void play(int n)
        {
            select(n);
            _e.Play(n);
        }

        private void select(int n)
        {
            _selected = n;
            _host.SetSetting("selected", n.ToString(CultureInfo.InvariantCulture));
            refresh();
        }

        // ---- layout ----

        protected override void OnLayout(LayoutEventArgs levent)
        {
            base.OnLayout(levent);
            if (!_built) return;
            int pad = _t.S(4), bh = _t.S(24), x = pad, y = pad;
            Action<Control, int> place = (c, w) => { c.SetBounds(x, y, _t.S(w), bh); x += _t.S(w) + pad; };
            place(_rec, 52); place(_stop, 52); place(_play, 52); place(_preview, 70); x += pad;
            place(_repeat, 66); place(_gap, 44); x += pad;
            int right = Width - pad;
            _pop.SetBounds(right - _t.S(64), y, _t.S(64), bh); right -= _t.S(64) + pad;
            _meter.SetBounds(right - _t.S(120), y, _t.S(120), bh); right -= _t.S(120) + pad;
            _status.SetBounds(x + pad, y, Math.Max(0, right - x - pad), bh);
            y += bh + pad;

            // the slots: 6 a row, two rows (F1 - F6 above F7 - F12, as on a keyboard's function keys)
            int cols = 6, rows = 2;
            int tw = (Width - pad * (cols + 1)) / cols;
            int th = Math.Max(_t.S(30), Math.Min(_t.S(64), (Height - y - pad * (rows + 1)) / rows));
            for (int i = 0; i < _tiles.Length; i++)
                _tiles[i].SetBounds(pad + (i % cols) * (tw + pad), y + (i / cols) * (th + pad), tw, th);
        }

        // ---- what's showing ----

        private void say(string m)
        {
            _message = m ?? "";
            _messageAt = DateTime.UtcNow;
            tick();
        }

        private void refresh()
        {
            DvkState st = _e.State;
            int act = _e.Active;
            bool idle = st == DvkState.Idle;
            Slot sel = _e.Store.Slots[_selected - 1];
            _rec.Enabled = idle;
            _rec.On = st == DvkState.Recording;
            _stop.Enabled = !idle;
            _play.Enabled = idle && !sel.Empty;
            _play.On = st == DvkState.Playing || st == DvkState.Waiting;
            _preview.Enabled = idle && !sel.Empty;
            _preview.On = st == DvkState.Preview;
            _repeat.On = _e.Repeat;
            _gap.Text = _e.RepeatSeconds.ToString("0", CultureInfo.InvariantCulture) + " s";
            _meter.Visible = st == DvkState.Recording;
            for (int i = 0; i < _tiles.Length; i++)
            {
                SlotTile t = _tiles[i];
                t.Selected = i + 1 == _selected;
                t.Busy = !idle && i + 1 == act && st != DvkState.Waiting;
                t.BusyAs = st;
                t.Invalidate();
            }
            tick();
        }

        private void tick()
        {
            DvkState st = _e.State;
            int act = _e.Active;
            string ce = _e.Elapsed.ToString("0.0", CultureInfo.InvariantCulture);
            string text;
            Color colour = _t.Dim;
            switch (st)
            {
                case DvkState.Recording: text = "Recording F" + act + "   " + ce + " s"; colour = _t.Tx; break;
                case DvkState.Playing: text = "On the air: F" + act + "   " + ce + " s" + (_e.Repeat ? "   (repeat " + _e.Sends + ")" : ""); colour = _t.Tx; break;
                case DvkState.Waiting: text = "Listening: F" + act + " again in " + _e.Elapsed.ToString("0", CultureInfo.InvariantCulture) + " s   (transmit to answer, or Esc)"; colour = _t.Gold; break;
                case DvkState.Preview: text = "Preview F" + act + "   " + ce + " s"; colour = _t.Ice; break;
                default:
                    if (_message.Length > 0 && (DateTime.UtcNow - _messageAt).TotalSeconds < 8) { text = _message; colour = _t.Text; }
                    else text = "F1 - F12 send  ·  Esc stops  ·  select a slot, then REC to record it  ·  right-click a slot to name it";
                    break;
            }
            _status.Text = text;
            _status.ForeColor = colour;
            if (st == DvkState.Recording) { _meter.Db = _e.MicDb; _meter.Invalidate(); }
        }

        // ---- menus ----

        private ContextMenuStrip gapMenu()
        {
            ContextMenuStrip m = new ContextMenuStrip();
            foreach (int g in Gaps)
            {
                int v = g;
                ToolStripMenuItem it = new ToolStripMenuItem(v + " s") { Checked = Math.Abs(_e.RepeatSeconds - v) < 0.5 };
                it.Click += (s, e) => { _e.RepeatSeconds = v; _host.SetSetting("gap", v.ToString(CultureInfo.InvariantCulture)); refresh(); };
                m.Items.Add(it);
            }
            m.Closed += (s, e) => BeginInvoke(new Action(m.Dispose));
            return m;
        }

        private ContextMenuStrip slotMenu(int n)
        {
            Slot slot = _e.Store.Slots[n - 1];
            bool idle = _e.State == DvkState.Idle;
            ContextMenuStrip m = new ContextMenuStrip();
            Func<string, Action, ToolStripMenuItem> add = (text, a) =>
            {
                ToolStripMenuItem i = new ToolStripMenuItem(text);
                i.Click += (s, e) => a();
                m.Items.Add(i);
                return i;
            };
            add("Rename...", () => rename(slot));
            ToolStripMenuItem level = new ToolStripMenuItem("Level (" + slot.LevelDb.ToString("+0;-0;0", CultureInfo.InvariantCulture) + " dB)") { ToolTipText = "Sent and previewed this much louder or quieter than it was recorded" };
            foreach (int db in new[] { 6, 3, 0, -3, -6, -9, -12 })
            {
                int v = db;
                ToolStripMenuItem it = new ToolStripMenuItem(v.ToString("+0;-0;0", CultureInfo.InvariantCulture) + " dB") { Checked = Math.Abs(slot.LevelDb - v) < 0.1 };
                it.Click += (s, e) => { _e.Store.SetLevel(slot, v); refresh(); };
                level.DropDownItems.Add(it);
            }
            m.Items.Add(level);
            m.Items.Add(new ToolStripSeparator());
            add("Import a WAV...", () => import(slot)).Enabled = idle;
            add("Export as WAV...", () => export(slot)).Enabled = !slot.Empty;
            m.Items.Add(new ToolStripSeparator());
            add("Clear", () =>
            {
                if (MessageBox.Show(FindForm(), "Clear F" + slot.Number + " (" + slot.Label + ")?", "Voice Keyer", MessageBoxButtons.OKCancel) != DialogResult.OK) return;
                try { _e.Store.Clear(slot); _e.Store.Rename(slot, ""); } catch (Exception ex) { say("Couldn't clear it: " + ex.Message); }
                refresh();
            }).Enabled = idle && !slot.Empty;
            m.Closed += (s, e) => BeginInvoke(new Action(m.Dispose));
            return m;
        }

        private void rename(Slot slot)
        {
            using (Form f = new Form())
            {
                f.Text = "Voice Keyer: F" + slot.Number;
                f.FormBorderStyle = FormBorderStyle.FixedToolWindow;
                f.StartPosition = FormStartPosition.CenterParent;
                f.ShowInTaskbar = false;
                f.ClientSize = new Size(_t.S(320), _t.S(84));
                f.Font = _t.Font(_t.S(12), false);
                TextBox tb = new TextBox { Text = slot.Name, Location = new Point(_t.S(12), _t.S(12)), Width = _t.S(296), MaxLength = 24 };
                Button ok = new Button { Text = "OK", DialogResult = DialogResult.OK, Location = new Point(_t.S(140), _t.S(44)), Size = new Size(_t.S(80), _t.S(28)) };
                Button cancel = new Button { Text = "Cancel", DialogResult = DialogResult.Cancel, Location = new Point(_t.S(228), _t.S(44)), Size = new Size(_t.S(80), _t.S(28)) };
                f.Controls.AddRange(new Control[] { tb, ok, cancel });
                f.AcceptButton = ok;
                f.CancelButton = cancel;
                _t.Style(f);
                if (f.ShowDialog(FindForm()) == DialogResult.OK) { _e.Store.Rename(slot, tb.Text); refresh(); }
            }
        }

        private void import(Slot slot)
        {
            using (OpenFileDialog d = new OpenFileDialog { Title = "Import into F" + slot.Number, Filter = "WAV files (*.wav)|*.wav" })
            {
                if (d.ShowDialog(FindForm()) != DialogResult.OK) return;
                try
                {
                    string why = _e.Store.Import(slot, d.FileName);
                    say(why == null ? "F" + slot.Number + ": imported " + Path.GetFileName(d.FileName) : "Couldn't import it: " + why);
                }
                catch (Exception ex) { say("Couldn't import it: " + ex.Message); }
                refresh();
            }
        }

        private void export(Slot slot)
        {
            string name = (slot.Name.Length > 0 ? slot.Name : "F" + slot.Number);
            foreach (char c in Path.GetInvalidFileNameChars()) name = name.Replace(c, '-');
            using (SaveFileDialog d = new SaveFileDialog { Title = "Export F" + slot.Number, Filter = "WAV files (*.wav)|*.wav", FileName = name + ".wav" })
            {
                if (d.ShowDialog(FindForm()) != DialogResult.OK) return;
                try { _e.Store.Export(slot, d.FileName); say("F" + slot.Number + " saved as " + Path.GetFileName(d.FileName)); }
                catch (Exception ex) { say("Couldn't save it: " + ex.Message); }
            }
        }
    }
}
