/*  DvkEngine.cs

The voice keyer's own thread: recording from the mic, preview, sending (the radio keyed, the recording fed in where
the mic goes, then un-keyed) and repeat CQ. The pane asks for things with Record / Play / Preview / Stop; they're
done here in order, so the audio taps are only ever touched from this thread.

Sending follows the Digital Modes and SSTV modules: the transmit feed started, then MOX; about 0.25 s kept queued;
it stops at the end, on Stop or Esc, if the radio is un-keyed elsewhere (the MOX button, a foot switch), or if it
never keys. Repeat waits the listening gap, and gives up if you transmit yourself in it (you're answering someone).

This file is part of the Kainos Voice Keyer module. Copyright (C) 2026 Justin Cron K7JUS. GPLv3 or later.
*/

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Threading;
using Kainos.Modules;

namespace Kainos.Dvk
{
    internal enum DvkState { Idle, Recording, Preview, Playing, Waiting }

    internal class DvkEngine
    {
        public const double MaxSeconds = 120;       // the longest recording
        private const double TxLead = 0.15;         // silence first, for the radio to switch over
        private static readonly string[] VoiceModes = { "LSB", "USB", "DSB", "AM", "SAM", "FM" };

        private readonly IKainosHost _host;
        private readonly IKainosVoice _voice;
        public readonly SlotStore Store;

        public volatile DvkState State = DvkState.Idle;
        public volatile int Active = -1;            // the slot (1 - 12) being recorded, previewed or sent
        public volatile bool Repeat;
        private double _repeatSeconds = 5;
        public double RepeatSeconds { get { lock (_cmds) return _repeatSeconds; } set { lock (_cmds) _repeatSeconds = Math.Max(1, Math.Min(120, value)); } }
        public volatile int Sends;                  // times the active slot has gone out (repeat CQ)
        public volatile float MicDb = -120;         // the recording level, peak over the last 0.1 s (dB below full scale)

        public event Action Changed;                // State, Active or Sends changed (this thread)
        public event Action<string> Status;         // something to tell the operator (this thread)

        private readonly ConcurrentQueue<Action> _cmds = new ConcurrentQueue<Action>();
        private readonly Stopwatch _clock = new Stopwatch();
        private Thread _thread;
        private volatile bool _run;

        public DvkEngine(IKainosHost host, IKainosVoice voice)
        {
            _host = host;
            _voice = voice;
            Store = new SlotStore(host);
        }

        // seconds into the recording, preview or send; or left in the listening gap
        public double Elapsed
        {
            get
            {
                if (State == DvkState.Waiting) return Math.Max(0, (_waitUntil - DateTime.UtcNow).TotalSeconds);
                return _clock.Elapsed.TotalSeconds;
            }
        }

        public void Start()
        {
            if (_run) return;
            _run = true;
            _thread = new Thread(work) { IsBackground = true, Name = "Kainos voice keyer", Priority = ThreadPriority.AboveNormal };
            _thread.Start();
        }

        public void Stop()
        {
            if (!_run) return;
            _cmds.Enqueue(stopAll);
            _run = false;
            if (_thread != null && !_thread.Join(1500)) _host.Log("engine thread didn't stop in time");
            _thread = null;
            stopAll();          // in case the thread had already gone
        }

        // ---- what the pane asks for (any thread) ----

        public void Record(int slot) { _cmds.Enqueue(() => { stopAll(); startRecord(slot); }); }
        public void Play(int slot) { _cmds.Enqueue(() => { stopAll(); Sends = 0; startPlay(slot); }); }
        public void Preview(int slot) { _cmds.Enqueue(() => { stopAll(); startPreview(slot); }); }
        public void StopNow() { _cmds.Enqueue(stopAll); }

        public static bool IsVoiceMode(string mode) { return Array.IndexOf(VoiceModes, (mode ?? "").ToUpperInvariant()) >= 0; }

        // ---- the thread ----

        private void work()
        {
            while (_run)
            {
                try
                {
                    Action a;
                    while (_cmds.TryDequeue(out a)) a();
                    switch (State)
                    {
                        case DvkState.Recording: recordStep(); break;
                        case DvkState.Preview: previewStep(); break;
                        case DvkState.Playing: playStep(); break;
                        case DvkState.Waiting: waitStep(); break;
                    }
                }
                catch (Exception ex) { _host.Log("engine: " + ex); stopAll(); Thread.Sleep(200); }
                Thread.Sleep(10);
            }
        }

        private void set(DvkState s, int slot)
        {
            State = s;
            Active = s == DvkState.Idle ? -1 : slot;
            Changed?.Invoke();
        }

        private void say(string s) { Status?.Invoke(s); }

        private void stopAll()
        {
            switch (State)
            {
                case DvkState.Recording: finishRecord(); break;
                case DvkState.Preview: _voice.PreviewStop(); break;
                case DvkState.Playing: endPlay(false, null); break;
            }
            if (State != DvkState.Idle) set(DvkState.Idle, -1);
            _clock.Reset();
        }

        private Slot slotOf(int n) { return n >= 1 && n <= SlotStore.Count ? Store.Slots[n - 1] : null; }

        // ---- recording ----

        private List<float> _rec;
        private int _recRate;
        private readonly float[] _buf = new float[16384];
        private DateTime _peakAt;
        private float _peak;

        private void startRecord(int n)
        {
            if (slotOf(n) == null) return;
            if (_host.Radio.Transmitting) { say("Can't record while transmitting"); return; }
            _recRate = _voice.MicRate;
            if (_recRate <= 0) { say("No mic audio stream: is the radio on?"); return; }
            _rec = new List<float>(_recRate * 10);
            _voice.MicStart();
            _peak = 0; _peakAt = DateTime.UtcNow; MicDb = -120;
            _clock.Restart();
            set(DvkState.Recording, n);
            say("Recording F" + n + ": speak, then STOP (or Esc)");
        }

        private void recordStep()
        {
            int got;
            while ((got = _voice.ReadMic(_buf, _buf.Length)) > 0)
                for (int i = 0; i < got; i++)
                {
                    float s = _buf[i];
                    _rec.Add(s);
                    float m = Math.Abs(s);
                    if (m > _peak) _peak = m;
                }
            if ((DateTime.UtcNow - _peakAt).TotalSeconds >= 0.1)
            {
                MicDb = _peak > 1e-6f ? (float)(20 * Math.Log10(_peak)) : -120;
                _peak = 0;
                _peakAt = DateTime.UtcNow;
            }
            if (_rec.Count == 0 && _clock.Elapsed.TotalSeconds > 1.5)
            {
                _voice.MicStop();
                _rec = null;
                set(DvkState.Idle, -1);
                say("No mic audio is reaching Kainos: check the mic source in Setup > Audio");
                return;
            }
            if (_rec.Count >= MaxSeconds * _recRate)
            {
                int n = Active;
                finishRecord();
                set(DvkState.Idle, -1);
                say("F" + n + ": stopped at " + MaxSeconds.ToString("0", CultureInfo.InvariantCulture) + " s, the longest a recording can be");
            }
        }

        private void finishRecord()
        {
            _voice.MicStop();
            MicDb = -120;
            Slot s = slotOf(Active);
            List<float> rec = _rec;
            _rec = null;
            if (s == null || rec == null) return;
            float[] a = Trim(rec.ToArray(), _recRate);
            if (a.Length < _recRate * 0.3) { say("F" + s.Number + ": too short to keep (nothing was said?)"); return; }
            try
            {
                Store.Store(s, a, _recRate);
                say("F" + s.Number + " recorded: " + s.Seconds.ToString("0.0", CultureInfo.InvariantCulture) + " s");
                _host.Log("recorded F" + s.Number + ", " + s.Seconds.ToString("0.0", CultureInfo.InvariantCulture) + " s");
            }
            catch (Exception ex) { say("Couldn't save F" + s.Number + ": " + ex.Message); _host.Log("save F" + s.Number + ": " + ex); }
        }

        // the quiet before the first word and after the last cut off (30 dB under the loudest), 0.1 s kept each side,
        // with 10 ms fades so there's no click
        internal static float[] Trim(float[] a, int rate)
        {
            float peak = 0;
            foreach (float s in a) peak = Math.Max(peak, Math.Abs(s));
            if (peak < 1e-4f) return new float[0];
            float th = peak * 0.0316f;
            int first = 0, last = a.Length - 1;
            while (first < a.Length && Math.Abs(a[first]) < th) first++;
            while (last > first && Math.Abs(a[last]) < th) last--;
            int margin = rate / 10;
            first = Math.Max(0, first - margin);
            last = Math.Min(a.Length - 1, last + margin);
            float[] o = new float[last - first + 1];
            Array.Copy(a, first, o, 0, o.Length);
            int fade = Math.Min(o.Length / 2, rate / 100);
            for (int i = 0; i < fade; i++) { float g = i / (float)fade; o[i] *= g; o[o.Length - 1 - i] *= g; }
            return o;
        }

        // ---- preview (through the speakers, not sent) ----

        private float[] _pv;
        private int _pvPos;

        private void startPreview(int n)
        {
            Slot s = slotOf(n);
            if (s == null || s.Empty) { say("F" + n + " is empty"); return; }
            if (_host.Radio.Transmitting) { say("Can't preview while transmitting"); return; }
            int rate = _voice.PreviewRate;
            if (rate <= 0) { say("No receive audio: is the radio on?"); return; }
            _pv = Shape(Wav.Resample(s.Audio, s.Rate, rate), rate, s.LevelDb, 0);
            _pvPos = 0;
            _voice.PreviewStart();
            _clock.Restart();
            set(DvkState.Preview, n);
            say("Preview F" + n + " (only you hear it)");
        }

        private void previewStep()
        {
            if (_host.Radio.Transmitting) { _voice.PreviewStop(); set(DvkState.Idle, -1); return; }
            int ahead = Math.Max(2048, _voice.PreviewRate / 4);
            while (_pvPos < _pv.Length && _voice.PreviewQueued < ahead)
            {
                int n = Math.Min(4096, _pv.Length - _pvPos);
                float[] part = new float[n];
                Array.Copy(_pv, _pvPos, part, 0, n);
                int w = _voice.PreviewWrite(part, n);
                _pvPos += w;
                if (w < n) break;
            }
            if (_pvPos >= _pv.Length && _voice.PreviewQueued == 0)
            {
                _voice.PreviewStop();
                set(DvkState.Idle, -1);
            }
        }

        // at a level (dB), with silence first (seconds) and 5 ms ramps at each end
        internal static float[] Shape(float[] a, int rate, double levelDb, double lead)
        {
            float g = (float)Math.Pow(10, levelDb / 20.0);
            int pre = (int)(lead * rate), ramp = Math.Min(a.Length / 2, rate / 200);
            float[] o = new float[pre + a.Length];
            for (int i = 0; i < a.Length; i++)
            {
                float env = i < ramp ? i / (float)ramp : i >= a.Length - ramp ? (a.Length - 1 - i) / (float)ramp : 1f;
                o[pre + i] = Math.Max(-1f, Math.Min(1f, a[i] * g * env));
            }
            return o;
        }

        // ---- sending ----

        private float[] _tx;
        private int _txPos;
        private bool _moxSeen;
        private DateTime _moxAskedAt, _waitUntil;
        private bool _waitUnkeyed;

        // false (and why said) if it can't be sent now
        private bool startPlay(int n)
        {
            Slot s = slotOf(n);
            if (s == null) return false;
            if (s.Empty) { say("F" + n + " is empty: select it and press REC to record it"); return false; }
            if (!_host.Radio.PowerOn) { say("The radio is off"); return false; }
            string mode = _host.Radio.Mode(_host.Radio.TxReceiver);
            if (!IsVoiceMode(mode)) { say("The voice keyer sends in voice modes only (now " + mode + ")"); return false; }
            if (_host.Radio.Transmitting) { say("The radio is already transmitting"); return false; }
            int rate = _host.Audio.TxRate;
            if (rate <= 0) { say("No transmit audio stream"); return false; }
            _tx = Shape(Wav.Resample(s.Audio, s.Rate, rate), rate, s.LevelDb, TxLead);
            _txPos = 0;
            _moxSeen = false;
            _moxAskedAt = DateTime.UtcNow;
            _host.Audio.TxStart();
            _host.BeginInvoke(() => _host.Radio.SetMox(true));
            _clock.Restart();
            Sends++;
            set(DvkState.Playing, n);
            _host.Log("send F" + n + (Repeat ? " (repeat " + Sends + ")" : "") + ", " + s.Seconds.ToString("0.0", CultureInfo.InvariantCulture) + " s");
            playStep();
            return true;
        }

        private void playStep()
        {
            bool keyed = _host.Radio.Transmitting;
            if (keyed) _moxSeen = true;
            if (_moxSeen && !keyed) { endPlay(false, "Stopped: the radio was un-keyed"); return; }
            if (!_moxSeen && (DateTime.UtcNow - _moxAskedAt).TotalSeconds > 2) { endPlay(false, "The radio didn't go into transmit"); return; }
            int rate = _host.Audio.TxRate;
            int ahead = Math.Max(2048, rate / 4);
            while (_txPos < _tx.Length && _host.Audio.TxQueued < ahead)
            {
                int n = Math.Min(4096, _tx.Length - _txPos);
                float[] part = new float[n];
                Array.Copy(_tx, _txPos, part, 0, n);
                int w = _host.Audio.TxWrite(part, n);
                _txPos += w;
                if (w < n) break;
            }
            if (_txPos >= _tx.Length && _host.Audio.TxQueued == 0) endPlay(true, null);
        }

        private void endPlay(bool done, string why)
        {
            if (State != DvkState.Playing) return;
            int n = Active;
            _host.Audio.TxClear();
            _host.Audio.TxStop();
            _host.BeginInvoke(() => _host.Radio.SetMox(false));
            _tx = null;
            if (done && Repeat)
            {
                _waitUntil = DateTime.UtcNow.AddSeconds(RepeatSeconds);
                _waitUnkeyed = false;
                _clock.Reset();
                set(DvkState.Waiting, n);
                return;
            }
            set(DvkState.Idle, -1);
            _clock.Reset();
            if (why != null) say(why);
        }

        // the listening gap in repeat CQ: sent again at its end, unless you transmit in it (answering someone)
        private void waitStep()
        {
            bool keyed = _host.Radio.Transmitting;
            if (!_waitUnkeyed)
            {
                if (!keyed) _waitUnkeyed = true;            // our own un-keying has gone through
                else if (DateTime.UtcNow >= _waitUntil.AddSeconds(-RepeatSeconds + 2)) { set(DvkState.Idle, -1); say("The radio didn't un-key: repeat stopped"); }
                return;
            }
            if (keyed) { set(DvkState.Idle, -1); say("You transmitted: repeat stopped"); return; }
            if (DateTime.UtcNow >= _waitUntil)
            {
                int n = Active;
                State = DvkState.Idle;          // startPlay sets Playing, or it stays here
                if (!startPlay(n)) set(DvkState.Idle, -1);
            }
        }
    }
}
