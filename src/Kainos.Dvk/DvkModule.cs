/*  DvkModule.cs

Kainos Voice Keyer: a digital voice keyer, as a downloadable module (Setup > Modules). 12 recordings on F1 - F12,
recorded from the mic as Kainos has it and sent in the same place, so the mic gain and the whole transmit chain
(EQ, Kainos Audio, AetherVoice) treat them as the live voice. Needs Kainos 1.0.12 (IKainosVoice); opened from its
DVK button in the left-hand dock, or Tools > Voice Keyer (DVK).

This program is free software: you can redistribute it and/or modify
it under the terms of the GNU General Public License as published by
the Free Software Foundation, either version 3 of the License, or
(at your option) any later version.

This program is distributed in the hope that it will be useful,
but WITHOUT ANY WARRANTY; without even the implied warranty of
MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.  See the
GNU General Public License for more details.

Copyright (C) 2026 Justin Cron K7JUS
*/

using System;
using Kainos.Dvk.UI;
using Kainos.Modules;

namespace Kainos.Dvk
{
    public class DvkModule : IKainosModule
    {
        private IKainosHost _host;
        private IKainosVoice _voice;
        private DvkEngine _engine;
        private DvkPane _view;
        private IKainosPane _pane;
        private IKainosMenuItem _menu;

        public void Start(IKainosHost host)
        {
            _host = host;
            _voice = host as IKainosVoice;
            if (_voice == null)
            {
                _menu = host.AddMenuItem("Voice Keyer (needs Kainos 1.0.12)", "The voice keyer needs Kainos 1.0.12 or later", () => { });
                _menu.Enabled = false;
                host.Log("Voice Keyer " + ModuleVersion + ": this Kainos (" + host.KainosVersion + ") has no voice interface");
                return;
            }
            _engine = new DvkEngine(host, _voice);
            _engine.Store.Load();
            _view = new DvkPane(host, _engine);
            _pane = host.CreatePane("Kainos Voice Keyer", _view);
            _pane.Closed += (s, e) => close();
            _view.PopToggled += () => _pane.Popped = !_pane.Popped;
            // a tool rather than a mode: a DVK button in the left-hand dock (and the Tools menu); the Modes menu on a
            // Kainos without the dock interface, or with its spare dock buttons all taken
            System.Action toggle = () => { if (_pane.IsOpen) close(); else open(); };
            const string tip = "The digital voice keyer (F1 - F12): open / close.";
            IKainosDock dock = host as IKainosDock;
            _menu = dock != null ? dock.AddDockButton("DVK", "Voice Keyer (DVK)", tip, toggle) : null;
            if (_menu == null) _menu = host.AddMenuItem("Voice Keyer", tip, toggle);
            // other modules (a logger's Enter / F1 - F3) can send a slot, or stop: Kainos 1.0.15's message bus. On an
            // older Kainos the bus's type doesn't exist, so it's named only in listen(), compiled when it's called.
            try { listen(host); }
            catch (TypeLoadException) { }
            catch (MissingMethodException) { }
            host.Log("Voice Keyer " + ModuleVersion + " started");
            if (host.GetSetting("open", "0") == "1") open();
        }

        // The pane needn't be open (opening it could move another module's pane out of the dock); the engine's own
        // checks still apply (voice modes only; an empty slot says so), and what it says goes back on dvk.status.
        [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.NoInlining)]
        private void listen(IKainosHost host)
        {
            IKainosKeyer keyer = host as IKainosKeyer;
            if (keyer == null) return;
            keyer.Subscribe("dvk.send", m =>
            {
                int n;
                if (!int.TryParse(m, out n) || n < 1 || n > SlotStore.Count) return;
                _engine.Start();
                _engine.Play(n);
            });
            keyer.Subscribe("dvk.stop", m => _engine.StopNow());
            _engine.Status += s => keyer.Publish("dvk.status", s);
        }

        internal static string ModuleVersion { get { System.Version v = typeof(DvkModule).Assembly.GetName().Version; return v.Major + "." + v.Minor + "." + v.Build; } }

        private void open()
        {
            _pane.Open();
            _engine.Start();
            _view.Running = true;
            _voice.SetKeyHandler(_view.Key);
            _menu.Checked = true;
            _host.SetSetting("open", "1");
        }

        private void close()
        {
            _voice.SetKeyHandler(null);
            _engine.Stop();
            _view.Running = false;
            _pane.Close();
            _menu.Checked = false;
            _host.SetSetting("open", "0");
        }

        public void Stop()
        {
            if (_voice != null) _voice.SetKeyHandler(null);
            if (_engine != null) _engine.Stop();
            if (_view != null) _view.Running = false;
        }

        public void Dispose()
        {
            if (_view != null && !_view.IsDisposed) _view.Dispose();
        }
    }
}
