/*  Wav.cs

WAV files for the recordings: written as 16-bit mono; read in any of the usual forms (8, 16, 24 or 32-bit PCM, or
32-bit float; mono or stereo, mixed to mono; any rate), and resampled.

This file is part of the Kainos Voice Keyer module. Copyright (C) 2026 Justin Cron K7JUS. GPLv3 or later.
*/

using System;
using System.IO;
using System.Text;

namespace Kainos.Dvk
{
    internal static class Wav
    {
        public static void Write(string path, float[] a, int rate)
        {
            string dir = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
            string tmp = path + ".tmp";
            using (BinaryWriter w = new BinaryWriter(File.Create(tmp)))
            {
                int data = a.Length * 2;
                w.Write(Encoding.ASCII.GetBytes("RIFF")); w.Write(36 + data); w.Write(Encoding.ASCII.GetBytes("WAVE"));
                w.Write(Encoding.ASCII.GetBytes("fmt ")); w.Write(16); w.Write((short)1); w.Write((short)1);
                w.Write(rate); w.Write(rate * 2); w.Write((short)2); w.Write((short)16);
                w.Write(Encoding.ASCII.GetBytes("data")); w.Write(data);
                foreach (float s in a) w.Write((short)Math.Round(Math.Max(-1, Math.Min(1, s)) * 32767));
            }
            if (File.Exists(path)) File.Delete(path);
            File.Move(tmp, path);
        }

        // null if it isn't a WAV this can read (why says what)
        public static float[] Read(string path, out int rate, out string why)
        {
            rate = 0; why = null;
            using (BinaryReader r = new BinaryReader(File.OpenRead(path)))
            {
                if (r.BaseStream.Length < 44 || Encoding.ASCII.GetString(r.ReadBytes(4)) != "RIFF") { why = "not a WAV file"; return null; }
                r.ReadInt32();
                if (Encoding.ASCII.GetString(r.ReadBytes(4)) != "WAVE") { why = "not a WAV file"; return null; }
                int format = 0, channels = 0, bits = 0;
                byte[] data = null;
                while (r.BaseStream.Position + 8 <= r.BaseStream.Length)
                {
                    string id = Encoding.ASCII.GetString(r.ReadBytes(4));
                    int len = r.ReadInt32();
                    if (len < 0 || r.BaseStream.Position + len > r.BaseStream.Length) len = (int)(r.BaseStream.Length - r.BaseStream.Position);
                    if (id == "fmt ")
                    {
                        byte[] f = r.ReadBytes(len);
                        format = BitConverter.ToInt16(f, 0);
                        channels = BitConverter.ToInt16(f, 2);
                        rate = BitConverter.ToInt32(f, 4);
                        bits = BitConverter.ToInt16(f, 14);
                        if (format == unchecked((short)0xFFFE) && len >= 26) format = BitConverter.ToInt16(f, 24);     // extensible: the sub-format
                    }
                    else if (id == "data") data = r.ReadBytes(len);
                    else r.BaseStream.Seek(len, SeekOrigin.Current);
                    if ((len & 1) == 1 && r.BaseStream.Position < r.BaseStream.Length) r.ReadByte();
                }
                if (data == null || channels < 1 || rate < 4000) { why = "no audio in it"; return null; }
                int bytes = bits / 8;
                if (!((format == 1 && (bits == 8 || bits == 16 || bits == 24 || bits == 32)) || (format == 3 && bits == 32))) { why = "an unusual WAV format (use 16-bit PCM)"; return null; }
                int frames = data.Length / (bytes * channels);
                float[] a = new float[frames];
                for (int i = 0; i < frames; i++)
                {
                    double sum = 0;
                    for (int c = 0; c < channels; c++)
                    {
                        int o = (i * channels + c) * bytes;
                        double v;
                        if (format == 3) v = BitConverter.ToSingle(data, o);
                        else if (bits == 8) v = (data[o] - 128) / 128.0;
                        else if (bits == 16) v = BitConverter.ToInt16(data, o) / 32768.0;
                        else if (bits == 24) v = ((data[o] | (data[o + 1] << 8) | (data[o + 2] << 16)) << 8 >> 8) / 8388608.0;
                        else v = BitConverter.ToInt32(data, o) / 2147483648.0;
                        sum += v;
                    }
                    a[i] = (float)(sum / channels);
                }
                return a;
            }
        }

        // any rate to any other: a windowed-sinc low-pass when going down, then linear interpolation (fine for voice)
        public static float[] Resample(float[] a, int from, int to)
        {
            if (a == null || from <= 0 || to <= 0 || from == to) return a;
            float[] src = a;
            if (to < from)
            {
                double fc = 0.45 * to / from;
                int n = 63;
                double[] h = new double[n];
                double sum = 0;
                for (int i = 0; i < n; i++)
                {
                    double x = i - (n - 1) / 2.0;
                    double w = 0.42 - 0.5 * Math.Cos(2 * Math.PI * i / (n - 1)) + 0.08 * Math.Cos(4 * Math.PI * i / (n - 1));
                    h[i] = (x == 0 ? 2 * fc : Math.Sin(2 * Math.PI * fc * x) / (Math.PI * x)) * w;
                    sum += h[i];
                }
                src = new float[a.Length];
                for (int k = 0; k < a.Length; k++)
                {
                    double acc = 0;
                    for (int i = 0; i < n; i++)
                    {
                        int j = k + i - (n - 1) / 2;
                        if (j >= 0 && j < a.Length) acc += a[j] * h[i];
                    }
                    src[k] = (float)(acc / sum);
                }
            }
            int outLen = (int)((long)a.Length * to / from);
            float[] o = new float[outLen];
            double step = from / (double)to;
            for (int i = 0; i < outLen; i++)
            {
                double p = i * step;
                int k = (int)p;
                double f = p - k;
                float s0 = src[Math.Min(k, src.Length - 1)], s1 = src[Math.Min(k + 1, src.Length - 1)];
                o[i] = (float)(s0 + (s1 - s0) * f);
            }
            return o;
        }
    }
}
