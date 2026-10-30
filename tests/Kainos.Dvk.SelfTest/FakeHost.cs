/*  FakeHost.cs (self-test)

A stand-in for Kainos 1.0.12: the radio (MOX, mode), the transmit feed, and the voice interface (a mic fed from a
queue, the preview collected), all running at Speed times real time.

This file is part of the Kainos Voice Keyer module. Copyright (C) 2026 Justin Cron K7JUS. GPLv3 or later.
*/

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Windows.Forms;
using Kainos.Modules;

namespace Kainos.Dvk.SelfTest
{
    // Kainos before 1.0.12: no IKainosVoice
    internal class OldHost : IKainosHost, IKainosRadio, IKainosAudio
    {
        public readonly Dictionary<string, string> Settings = new Dictionary<string, string>();
        public readonly List<string> LogLines = new List<string>();
        public readonly List<FakeMenu> Menus = new List<FakeMenu>();
        public readonly List<FakePane> Panes = new List<FakePane>();
        public Form Window;
        public volatile bool Mox;
        public string ModeName = "USB";
        public double Speed = 1;

        public OldHost(string data) { DataFolder = data; Directory.CreateDirectory(data); }

        public int ApiVersion { get { return KainosModuleApi.Version; } }
        public virtual string KainosVersion { get { return "1.0.11"; } }
        public string ModuleId { get { return "dvk"; } }
        public string ModuleFolder { get { return DataFolder; } }
        public string DataFolder { get; private set; }
        public string Callsign { get { return "K7JUS"; } }
        public string Grid { get { return "CN87"; } }
        public IKainosRadio Radio { get { return this; } }
        public IKainosAudio Audio { get { return this; } }
        public IKainosTheme Theme { get { return FakeTheme.Instance; } }

        public IKainosMenuItem AddMenuItem(string text, string toolTip, Action click) { FakeMenu m = new FakeMenu { Text = text, Click = click }; Menus.Add(m); return m; }
        public IKainosPane CreatePane(string title, Control content) { FakePane p = new FakePane(this, content); Panes.Add(p); return p; }
        public void SetMarkers(int rx, KainosMarker[] markers) { }
        public string GetSetting(string key, string fallback) { lock (Settings) { string v; return Settings.TryGetValue(key, out v) ? v : fallback; } }
        public void SetSetting(string key, string value) { lock (Settings) Settings[key] = value; }
        public void Log(string message) { lock (LogLines) LogLines.Add(message); }
        public void BeginInvoke(Action action)
        {
            if (Window != null && Window.IsHandleCreated) Window.BeginInvoke(action);
            else if (Window == null) action();
        }

        public bool PowerOn { get { return true; } }
        public bool Transmitting { get { return Mox; } }
        public int TxReceiver { get { return 0; } }
        public double VfoHz(int rx) { return 14200000; }
        public void TuneVfo(int rx, double hz) { }
        public string Mode(int rx) { return ModeName; }
        public void SetMode(int rx, string mode) { ModeName = mode; }
        public int MoxOns;
        public void SetMox(bool on) { if (on && !Mox) MoxOns++; Mox = on; }

        public bool AcquireRx(int rx, Action released, out string problem) { problem = null; return true; }
        public void ReleaseRx() { }
        public int RxRate { get { return 48000; } }
        public int ReadRx(float[] dst, int max) { return 0; }

        // transmit: what's written is kept (TxAudio) and drains at Speed times real time
        public readonly List<float> TxAudio = new List<float>();
        private Stopwatch _txClock;
        private long _txWritten;
        private volatile bool _txOn;
        public int TxRate { get { return 48000; } }
        public void TxStart() { _txOn = true; _txClock = Stopwatch.StartNew(); _txWritten = 0; }
        public int TxWrite(float[] src, int n) { if (!_txOn) return 0; lock (TxAudio) for (int i = 0; i < n; i++) TxAudio.Add(src[i]); _txWritten += n; return n; }
        public int TxQueued { get { return _txOn ? (int)Math.Max(0, _txWritten - _txClock.Elapsed.TotalSeconds * Speed * TxRate) : 0; } }
        public void TxClear() { }
        public void TxStop() { _txOn = false; }
        public bool TxOn { get { return _txOn; } }
    }

    internal class FakeHost : OldHost, IKainosVoice, IKainosDock
    {
        public FakeHost(string data) : base(data) { }

        // the dock button (kept in Menus too, so the tests click it the same way)
        public string DockLabel, DockMenuText;
        public IKainosMenuItem AddDockButton(string label, string menuText, string toolTip, Action click)
        {
            DockLabel = label; DockMenuText = menuText;
            FakeMenu m = new FakeMenu { Text = menuText, Click = click };
            Menus.Add(m);
            return m;
        }
        public override string KainosVersion { get { return "1.0.12"; } }

        // the mic: what's queued (then silence), at Speed times real time; Dead: no samples at all
        public bool Dead;
        private readonly Queue<float> _mic = new Queue<float>();
        private Stopwatch _micClock;
        private long _micRead;
        public bool MicOn;
        public void Speak(float[] a) { lock (_mic) foreach (float s in a) _mic.Enqueue(s); }
        public void MicStart() { MicOn = true; _micClock = Stopwatch.StartNew(); _micRead = 0; }
        public void MicStop() { MicOn = false; }
        public int MicRate { get { return 48000; } }
        public int ReadMic(float[] dst, int max)
        {
            if (!MicOn || Dead) return 0;
            long due = (long)(_micClock.Elapsed.TotalSeconds * Speed * MicRate);
            int n = (int)Math.Min(Math.Min(max, dst.Length), due - _micRead);
            if (n <= 0) return 0;
            lock (_mic) for (int i = 0; i < n; i++) dst[i] = _mic.Count > 0 ? _mic.Dequeue() : 0f;
            _micRead += n;
            return n;
        }

        // the preview: collected, draining at Speed times real time
        public readonly List<float> PreviewAudio = new List<float>();
        private Stopwatch _pvClock;
        private long _pvWritten;
        public volatile bool PreviewOn;
        public int PreviewRate { get { return 48000; } }
        public void PreviewStart() { PreviewOn = true; _pvClock = Stopwatch.StartNew(); _pvWritten = 0; }
        public int PreviewWrite(float[] src, int n) { if (!PreviewOn) return 0; lock (PreviewAudio) for (int i = 0; i < n; i++) PreviewAudio.Add(src[i]); _pvWritten += n; return n; }
        public int PreviewQueued { get { return PreviewOn ? (int)Math.Max(0, _pvWritten - _pvClock.Elapsed.TotalSeconds * Speed * PreviewRate) : 0; } }
        public void PreviewClear() { }
        public void PreviewStop() { PreviewOn = false; }

        public Func<Keys, bool> Keys;
        public void SetKeyHandler(Func<Keys, bool> handler) { Keys = handler; }
    }

    // Kainos 1.0.15: the message bus (and CW, unused by the keyer)
    internal class KeyerHost : FakeHost, IKainosKeyer
    {
        public KeyerHost(string data) : base(data) { }
        public override string KainosVersion { get { return "1.0.15"; } }
        private readonly Dictionary<string, Action<string>> _subs = new Dictionary<string, Action<string>>();
        public readonly List<string> Said = new List<string>();
        public string SendCw(string text) { return "no CW here"; }
        public bool CwSending { get { return false; } }
        public void StopCw() { }
        public int Publish(string topic, string message)
        {
            if (topic == "dvk.status") { lock (Said) Said.Add(message); return 1; }
            Action<string> h;
            if (!_subs.TryGetValue(topic, out h)) return 0;
            BeginInvoke(() => h(message));
            return 1;
        }
        public void Subscribe(string topic, Action<string> handler) { if (handler == null) _subs.Remove(topic); else _subs[topic] = handler; }
    }

    internal class FakeTheme : IKainosTheme
    {
        public static readonly FakeTheme Instance = new FakeTheme();
        public Color Bg { get { return Color.FromArgb(0x05, 0x0b, 0x13); } }
        public Color Surface { get { return Color.FromArgb(0x0a, 0x15, 0x20); } }
        public Color Panel { get { return Color.FromArgb(0x0e, 0x1b, 0x29); } }
        public Color Raised { get { return Color.FromArgb(0x14, 0x25, 0x38); } }
        public Color Line { get { return Color.FromArgb(0x1f, 0x33, 0x46); } }
        public Color Text { get { return Color.FromArgb(0xe9, 0xee, 0xf3); } }
        public Color Dim { get { return Color.FromArgb(0x94, 0xa7, 0xb2); } }
        public Color Faint { get { return Color.FromArgb(0x5f, 0x75, 0x84); } }
        public Color Ice { get { return Color.FromArgb(0x7f, 0xb0, 0xcc); } }
        public Color Gold { get { return Color.FromArgb(0xd4, 0xad, 0x6a); } }
        public Color Violet { get { return Color.FromArgb(0xb5, 0xa8, 0xe0); } }
        public Color Tx { get { return Color.FromArgb(0xe2, 0x57, 0x4c); } }
        public Color Selected { get { return Color.FromArgb(0x1d, 0x2a, 0x33); } }
        public float Scale { get { return 1f; } }
        public int S(float designPx) { return (int)Math.Round(designPx); }
        public Font Font(float px, bool bold) { return new Font("Segoe UI", px, bold ? FontStyle.Bold : FontStyle.Regular, GraphicsUnit.Pixel); }
        public void DrawButton(Graphics g, RectangleF r, string text, bool on, bool enabled, bool hover, float fontPx)
        {
            using (Brush b = new SolidBrush(on ? Selected : Raised)) g.FillRectangle(b, r);
            using (Font f = Font(fontPx, true)) TextRenderer.DrawText(g, text, f, Rectangle.Round(r), !enabled ? Faint : on ? Gold : Dim);
        }
        public void Style(Control root) { root.BackColor = Bg; root.ForeColor = Text; }
    }

    internal class FakeMenu : IKainosMenuItem
    {
        public Action Click;
        public bool Checked { get; set; }
        public bool Enabled { get; set; } = true;
        public string Text { get; set; }
        public void Remove() { }
    }

    internal class FakePane : IKainosPane
    {
        private readonly OldHost _h;
        public readonly Control Content;
        public FakePane(OldHost h, Control content) { _h = h; Content = content; }
        public bool IsOpen { get; private set; }
        public bool Popped { get; set; }
        public void Open() { IsOpen = true; }
        public void Close() { IsOpen = false; }
        public event EventHandler Closed;
        public void UserCloses() { IsOpen = false; Closed?.Invoke(this, EventArgs.Empty); }
    }
}
