/*  Program.cs (self-test)

The voice keyer against a stand-in Kainos: WAV files, recording (trimmed; no mic), sending (keyed and un-keyed, the
audio at its level), stopping (Stop, Esc, the radio un-keyed elsewhere), voice modes only, repeat CQ (and giving up
when you transmit), preview, the F-keys, an older Kainos, and the pane drawn (a PNG to look at).

This file is part of the Kainos Voice Keyer module. Copyright (C) 2026 Justin Cron K7JUS. GPLv3 or later.
*/

using System;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
using System.Threading;
using System.Windows.Forms;
using Kainos.Dvk.UI;

namespace Kainos.Dvk.SelfTest
{
    internal static class Program
    {
        private static int _fail;
        private const double Speed = 10;

        private static void check(bool ok, string what)
        {
            Console.WriteLine((ok ? "PASS  " : "FAIL  ") + what);
            if (!ok) _fail++;
        }

        private static bool waitFor(Func<bool> cond, double seconds)
        {
            Stopwatch sw = Stopwatch.StartNew();
            while (sw.Elapsed.TotalSeconds < seconds) { if (cond()) return true; Thread.Sleep(5); }
            return cond();
        }

        // a stand-in for a few words: quiet, then tone bursts (300, 600 and 900 Hz at 0.5), then quiet
        private static float[] words(double quiet, double talk)
        {
            int r = 48000, q = (int)(quiet * r), t = (int)(talk * r);
            float[] a = new float[2 * q + t];
            for (int i = 0; i < t; i++)
            {
                double x = i / (double)r, env = (Math.Sin(2 * Math.PI * 3 * x) > -0.3 ? 1 : 0.1);
                a[q + i] = (float)(0.5 * env * (0.5 * Math.Sin(2 * Math.PI * 300 * x) + 0.3 * Math.Sin(2 * Math.PI * 600 * x) + 0.2 * Math.Sin(2 * Math.PI * 900 * x)));
            }
            return a;
        }

        private static string folder(string name)
        {
            string d = Path.Combine(Path.GetTempPath(), "kainos-dvk-selftest", name);
            if (Directory.Exists(d)) Directory.Delete(d, true);
            Directory.CreateDirectory(d);
            return d;
        }

        [STAThread]
        private static int Main(string[] args)
        {
            string work = folder("work");

            // 1. WAV: 16-bit mono back as written; 24-bit stereo 44.1 kHz read and mixed to mono, resampled to 48 kHz
            {
                float[] a = new float[4800];
                for (int i = 0; i < a.Length; i++) a[i] = (float)(0.7 * Math.Sin(2 * Math.PI * 440 * i / 48000.0));
                string p = Path.Combine(work, "a.wav");
                Wav.Write(p, a, 48000);
                int rate; string why;
                float[] b = Wav.Read(p, out rate, out why);
                double err = b == null ? 1 : a.Zip(b, (x, y) => Math.Abs(x - y)).Max();
                check(b != null && rate == 48000 && b.Length == a.Length && err < 1.0 / 16000, "WAV 16-bit mono written and read back (worst error " + err.ToString("0.00000") + ")");

                string p2 = Path.Combine(work, "b.wav");
                int n = 44100;
                using (BinaryWriter w = new BinaryWriter(File.Create(p2)))
                {
                    int data = n * 2 * 3;
                    w.Write(System.Text.Encoding.ASCII.GetBytes("RIFF")); w.Write(36 + data); w.Write(System.Text.Encoding.ASCII.GetBytes("WAVE"));
                    w.Write(System.Text.Encoding.ASCII.GetBytes("fmt ")); w.Write(16); w.Write((short)1); w.Write((short)2);
                    w.Write(44100); w.Write(44100 * 6); w.Write((short)6); w.Write((short)24);
                    w.Write(System.Text.Encoding.ASCII.GetBytes("data")); w.Write(data);
                    for (int i = 0; i < n; i++)
                    {
                        int v = (int)(0.5 * Math.Sin(2 * Math.PI * 500 * i / 44100.0) * 8388607);
                        for (int c = 0; c < 2; c++) { w.Write((byte)(v & 0xff)); w.Write((byte)((v >> 8) & 0xff)); w.Write((byte)((v >> 16) & 0xff)); }
                    }
                }
                FakeHost h = new FakeHost(folder("import"));
                SlotStore st = new SlotStore(h);
                string iw = st.Import(st.Slots[2], p2);
                Slot s3 = st.Slots[2];
                check(iw == null && s3.Rate == 48000 && Math.Abs(s3.Seconds - 1.0) < 0.01 && Math.Abs(s3.Audio.Max() - 0.5) < 0.03 && s3.Name == "b",
                    "WAV 24-bit stereo 44.1 kHz imported: " + (iw ?? s3.Seconds.ToString("0.000") + " s at " + s3.Rate + ", peak " + s3.Audio.Max().ToString("0.00") + ", named \"" + s3.Name + "\""));
                st.Load();
                check(!st.Slots[2].Empty && st.Slots[2].Name == "b", "slots read back from their files and settings");
            }

            // 2. recording: the quiet before and after cut off (0.1 s kept), saved
            FakeHost host = new FakeHost(folder("engine")) { Speed = Speed };
            DvkEngine e = new DvkEngine(host, host);
            string last = "";
            e.Status += m => last = m;
            e.Start();
            {
                host.Speak(words(1.0, 2.0));
                e.Record(1);
                waitFor(() => e.State == DvkState.Recording, 2);
                Thread.Sleep((int)(4500 / Speed));
                e.StopNow();
                waitFor(() => e.State == DvkState.Idle, 2);
                Slot s1 = e.Store.Slots[0];
                check(!s1.Empty && Math.Abs(s1.Seconds - 2.2) < 0.15 && File.Exists(e.Store.FileOf(1)),
                    "recorded F1: 2 s of words with 1 s of quiet each side comes out " + s1.Seconds.ToString("0.00") + " s (" + last + ")");
            }
            {
                host.Dead = true;
                e.Record(2);
                waitFor(() => e.State == DvkState.Idle && last.StartsWith("No mic"), 5);
                check(e.Store.Slots[1].Empty && last.StartsWith("No mic"), "no mic audio: says so (" + last + ")");
                host.Dead = false;
            }

            // 3. sending: keyed, the recording at its level after 0.15 s, un-keyed at the end
            {
                Slot s1 = e.Store.Slots[0];
                e.Store.SetLevel(s1, -6);
                lock (host.TxAudio) host.TxAudio.Clear();
                bool sawMox = false;
                e.Play(1);
                waitFor(() => { sawMox |= host.Mox; return e.State == DvkState.Playing; }, 2);
                waitFor(() => { sawMox |= host.Mox; return e.State == DvkState.Idle; }, 5);
                float[] tx; lock (host.TxAudio) tx = host.TxAudio.ToArray();
                int lead = (int)(0.15 * 48000);
                double ratio = tx.Skip(lead).Select(Math.Abs).Max() / s1.Audio.Select(Math.Abs).Max();
                check(sawMox && !host.Mox && !host.TxOn && tx.Length == lead + s1.Audio.Length && tx.Take(lead).All(v => v == 0) && Math.Abs(ratio - 0.501) < 0.01,
                    "sent F1: keyed, " + tx.Length + " samples (0.15 s lead + the recording) at " + (20 * Math.Log10(ratio)).ToString("0.0") + " dB, un-keyed");
                e.Store.SetLevel(s1, 0);
            }

            // 4. stopping: Stop, the radio un-keyed elsewhere, and only in voice modes
            {
                host.Speed = 1;
                e.Play(1);
                waitFor(() => e.State == DvkState.Playing && host.Mox, 2);
                Thread.Sleep(300);
                e.StopNow();
                bool stopped = waitFor(() => e.State == DvkState.Idle && !host.Mox, 1);
                check(stopped && !host.TxOn, "Stop while sending: un-keyed at once");

                e.Play(1);
                waitFor(() => e.State == DvkState.Playing && host.Mox, 2);
                Thread.Sleep(300);
                host.Mox = false;                   // the MOX button, or a foot switch
                stopped = waitFor(() => e.State == DvkState.Idle, 1);
                check(stopped && !host.TxOn && last.Contains("un-keyed"), "un-keyed elsewhere while sending: stops (" + last + ")");

                host.ModeName = "DIGU";
                int ons = host.MoxOns;
                e.Play(1);
                Thread.Sleep(200);
                check(e.State == DvkState.Idle && host.MoxOns == ons && last.Contains("voice modes"), "DIGU: not sent (" + last + ")");
                host.ModeName = "LSB";
                host.Speed = Speed;
            }

            // 5. repeat CQ: sent again after the gap; you transmitting in the gap stops it
            {
                e.Repeat = true;
                e.RepeatSeconds = 1;
                e.Play(1);
                bool again = waitFor(() => e.Sends >= 2 && e.State == DvkState.Playing, 6);
                check(again, "repeat: sent again after the 1 s gap (" + e.Sends + " sends)");
                waitFor(() => e.State == DvkState.Waiting, 4);
                Thread.Sleep(200);
                host.Mox = true;                    // you answer someone
                bool gaveUp = waitFor(() => e.State == DvkState.Idle, 1);
                check(gaveUp && last.Contains("repeat stopped") && host.TxOn == false, "repeat: you transmitting in the gap stops it (" + last + ")");
                host.Mox = false;
                e.Repeat = false;
            }

            // 6. preview: heard, not sent
            {
                lock (host.PreviewAudio) host.PreviewAudio.Clear();
                int ons = host.MoxOns;
                e.Preview(1);
                waitFor(() => e.State == DvkState.Preview, 2);
                waitFor(() => e.State == DvkState.Idle, 5);
                int got; lock (host.PreviewAudio) got = host.PreviewAudio.Count;
                check(got == e.Store.Slots[0].Audio.Length && host.MoxOns == ons && !host.PreviewOn, "preview F1: " + got + " samples heard, not keyed");
            }
            e.Stop();

            // 7. the module: F-keys and Esc through the key handler, the pane drawn; and on an older Kainos
            {
                FakeHost mh = new FakeHost(folder("module")) { Speed = Speed };
                using (Form f = new Form { Width = 1700, Height = 120, StartPosition = FormStartPosition.Manual, Location = new Point(-2000, -2000), ShowInTaskbar = false })
                {
                    mh.Window = null;
                    DvkModule mod = new DvkModule();
                    mod.Start(mh);
                    FakePane pane = mh.Panes[0];
                    pane.Content.Dock = DockStyle.Fill;
                    f.Controls.Add(pane.Content);
                    f.Show();
                    mh.Window = f;
                    mh.Menus[0].Click();
                    check(mh.DockLabel == "DVK" && mh.Menus.Count == 1, "a DVK button in the dock (and the Tools menu), nothing in Modes");
                    check(pane.IsOpen && mh.Keys != null && mh.Menus[0].Checked, "opened from its DVK button: lit, the F-keys handed over");
                    // a recording for F2 straight into the store, then F2 sends it and Esc stops it
                    DvkPane dp = (DvkPane)pane.Content;
                    FieldInfoEngine(mod).Store.Store(FieldInfoEngine(mod).Store.Slots[1], words(0, 3), 48000);
                    FieldInfoEngine(mod).Store.Rename(FieldInfoEngine(mod).Store.Slots[1], "CQ contest");
                    mh.Speed = 1;
                    bool used = mh.Keys(Keys.F2);
                    bool keyed = waitPump(() => mh.Mox, 2);
                    bool esc = mh.Keys(Keys.Escape);
                    bool off = waitPump(() => !mh.Mox && FieldInfoEngine(mod).State == DvkState.Idle, 2);
                    bool escIdle = mh.Keys(Keys.Escape);
                    check(used && keyed && esc && off && !escIdle, "F2 sends F2, Esc stops it; Esc with nothing going is left for Kainos");
                    pumpFor(0.3);
                    string png = Path.Combine(Path.GetTempPath(), "kainos-dvk-selftest", "pane.png");
                    Control pc = pane.Content;
                    using (Bitmap b = new Bitmap(pc.Width, pc.Height)) { pc.DrawToBitmap(b, new Rectangle(Point.Empty, b.Size)); b.Save(png, ImageFormat.Png); }
                    check(File.Exists(png), "the pane drawn: " + png);
                    mh.Menus[0].Click();
                    check(!pane.IsOpen && mh.Keys == null && !mh.Menus[0].Checked, "closed: the F-keys given back, the button unlit");
                    mod.Stop();
                    mod.Dispose();
                    f.Close();
                }

                OldHost old = new OldHost(folder("old"));
                DvkModule m2 = new DvkModule();
                m2.Start(old);
                check(old.Menus.Count == 1 && !old.Menus[0].Enabled && old.Menus[0].Text.Contains("1.0.12") && old.Panes.Count == 0, "Kainos 1.0.11: a disabled menu item saying it needs 1.0.12");
            }

            Console.WriteLine(_fail == 0 ? "All passed" : _fail + " failed");
            return _fail == 0 ? 0 : 1;
        }

        private static DvkEngine FieldInfoEngine(DvkModule m)
        {
            return (DvkEngine)typeof(DvkModule).GetField("_engine", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance).GetValue(m);
        }

        private static bool waitPump(Func<bool> cond, double seconds)
        {
            Stopwatch sw = Stopwatch.StartNew();
            while (sw.Elapsed.TotalSeconds < seconds) { Application.DoEvents(); if (cond()) return true; Thread.Sleep(5); }
            return cond();
        }

        private static void pumpFor(double seconds)
        {
            Stopwatch sw = Stopwatch.StartNew();
            while (sw.Elapsed.TotalSeconds < seconds) { Application.DoEvents(); Thread.Sleep(5); }
        }
    }
}
