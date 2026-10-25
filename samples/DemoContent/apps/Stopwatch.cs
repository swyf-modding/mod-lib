using System;
using ScamWYF.Modding.Core.Ui;
using UnityEngine;
using UnityEngine.UIElements;

namespace ScamWYF.DemoContent
{
    /// <summary>
    /// A stopwatch, which is the demo's example of an app that keeps drawing after it is built: the
    /// element's own scheduler ticks the display, and the tick is the only thing running.
    /// </summary>
    /// <remarks>
    /// The scheduler belongs to the element, so it stops when the window goes away - which is why
    /// this file needs no teardown and no Update. The elapsed time is stored as a start timestamp
    /// plus the time banked before, so a rebuild resumes the same stopwatch rather than a new one.
    /// </remarks>
    internal static class Stopwatch
    {
        private static double _banked;
        private static double _startedAt;
        private static bool _running;

        internal static void Draw(VisualElement root)
        {
            var card = AppKit.Card(root);
            card.style.alignItems = Align.Center;

            var display = AppKit.Display(card, 54);
            display.text = Format(Elapsed());

            var caption = AppKit.Hint(card, "Start, stop, reset.");
            caption.style.unityTextAlign = TextAnchor.MiddleCenter;

            var actions = AppKit.Row(root);

            // One tick a tenth of a second: twice what the display shows, so no tenth is skipped, and
            // nothing like the cost of a per-frame callback for a clock.
            Action tickAction = delegate { display.text = Format(Elapsed()); };
            var tick = root.schedule.Execute(tickAction).Every(100);
            if (!_running) tick.Pause();

            // Declared before the button so the button's own closure can rename it.
            Button toggle = null;
            toggle = AppKit.Action(actions, _running ? "STOP" : "START", delegate
            {
                if (_running)
                {
                    _banked = Elapsed();
                    _running = false;
                    toggle.text = "START";
                    tick.Pause();
                    display.text = Format(_banked);
                    return;
                }

                _startedAt = Time.realtimeSinceStartupAsDouble;
                _running = true;
                toggle.text = "STOP";
                tick.Resume();
            });
            toggle.style.marginRight = 6f;

            AppKit.Key(actions, "Reset", delegate
            {
                _banked = 0;
                _startedAt = 0;
                _running = false;
                toggle.text = "START";
                tick.Pause();
                display.text = Format(0);
            }, false);
        }

        private static double Elapsed()
        {
            return _running
                ? _banked + (Time.realtimeSinceStartupAsDouble - _startedAt)
                : _banked;
        }

        private static string Format(double seconds)
        {
            if (seconds < 0) seconds = 0;
            var minutes = (int)(seconds / 60);
            var rest = seconds - minutes * 60;
            return minutes.ToString("00") + ":" + rest.ToString("00.0");
        }
    }
}
