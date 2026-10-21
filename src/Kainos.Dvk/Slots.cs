/*  Slots.cs

The 12 recordings (F1 - F12): each a WAV in DataFolder\Slots (slot01.wav ...), its name and level in the module's
settings. Kept in memory once loaded (a minute of 48 kHz audio is under 12 MB).

This file is part of the Kainos Voice Keyer module. Copyright (C) 2026 Justin Cron K7JUS. GPLv3 or later.
*/

using System;
using System.Globalization;
using System.IO;
using Kainos.Modules;

namespace Kainos.Dvk
{
    internal class Slot
    {
        public int Number;                  // 1 - 12 (F1 - F12)
        public string Name = "";
        public double LevelDb;              // played this much louder or quieter than recorded (-20 to +10)
        public float[] Audio;               // mono
        public int Rate;

        public bool Empty { get { return Audio == null || Audio.Length == 0; } }
        public double Seconds { get { return Empty || Rate <= 0 ? 0 : Audio.Length / (double)Rate; } }
        public string Label { get { return Name.Length > 0 ? Name : Empty ? "empty" : "F" + Number; } }
    }

    internal class SlotStore
    {
        public const int Count = 12;
        public const double MinLevel = -20, MaxLevel = 10;
        private readonly IKainosHost _host;
        private readonly string _folder;
        public readonly Slot[] Slots = new Slot[Count];

        public SlotStore(IKainosHost host)
        {
            _host = host;
            _folder = Path.Combine(host.DataFolder, "Slots");
            for (int i = 0; i < Count; i++) Slots[i] = new Slot { Number = i + 1 };
        }

        public string FileOf(int number) { return Path.Combine(_folder, "slot" + number.ToString("00", CultureInfo.InvariantCulture) + ".wav"); }

        public void Load()
        {
            foreach (Slot s in Slots)
            {
                s.Name = _host.GetSetting("name." + s.Number, "");
                double lv;
                s.LevelDb = double.TryParse(_host.GetSetting("level." + s.Number, "0"), NumberStyles.Float, CultureInfo.InvariantCulture, out lv) ? clampLevel(lv) : 0;
                s.Audio = null; s.Rate = 0;
                string f = FileOf(s.Number);
                if (!File.Exists(f)) continue;
                try
                {
                    int rate; string why;
                    float[] a = Wav.Read(f, out rate, out why);
                    if (a == null) { _host.Log("slot " + s.Number + ": " + why); continue; }
                    s.Audio = a; s.Rate = rate;
                }
                catch (Exception ex) { _host.Log("slot " + s.Number + ": " + ex.Message); }
            }
        }

        public void Store(Slot s, float[] audio, int rate)
        {
            Wav.Write(FileOf(s.Number), audio, rate);
            s.Audio = audio; s.Rate = rate;
        }

        public void Clear(Slot s)
        {
            string f = FileOf(s.Number);
            if (File.Exists(f)) File.Delete(f);
            s.Audio = null; s.Rate = 0;
        }

        public void Rename(Slot s, string name)
        {
            s.Name = (name ?? "").Trim();
            if (s.Name.Length > 24) s.Name = s.Name.Substring(0, 24);
            _host.SetSetting("name." + s.Number, s.Name);
        }

        public void SetLevel(Slot s, double db)
        {
            s.LevelDb = clampLevel(db);
            _host.SetSetting("level." + s.Number, s.LevelDb.ToString("0.#", CultureInfo.InvariantCulture));
        }

        private static double clampLevel(double db) { return Math.Max(MinLevel, Math.Min(MaxLevel, db)); }

        // a WAV from elsewhere into a slot (kept at 48 kHz); null, or why not
        public string Import(Slot s, string path)
        {
            int rate; string why;
            float[] a = Wav.Read(path, out rate, out why);
            if (a == null) return why;
            if (a.Length / (double)rate > DvkEngine.MaxSeconds) return "longer than " + DvkEngine.MaxSeconds.ToString("0", CultureInfo.InvariantCulture) + " s";
            if (rate != 48000) { a = Wav.Resample(a, rate, 48000); rate = 48000; }
            Store(s, a, rate);
            if (s.Name.Length == 0) Rename(s, Path.GetFileNameWithoutExtension(path));
            return null;
        }

        public void Export(Slot s, string path)
        {
            if (!s.Empty) Wav.Write(path, s.Audio, s.Rate);
        }
    }
}
