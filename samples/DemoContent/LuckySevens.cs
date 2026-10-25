using System;
using ScamWYF.Modding.Core.Ui;
using UnityEngine;
using UnityEngine.UIElements;

namespace ScamWYF.DemoContent
{
    /// <summary>
    /// The demo's game, drawn into the app window by <see cref="Draw"/> - no game builder, no UXML, no
    /// controller code.
    /// </summary>
    /// <remarks>
    /// Two things worth copying from this file:
    ///
    ///  - <b>The state lives here, not in the elements.</b> The game rebuilds an app's window whenever
    ///    it likes (opening it after an in-game event, rebuilding the desktop, a scene reload), and each
    ///    of those calls <see cref="Draw"/> again with an empty tree. Anything kept in a label is gone by
    ///    then, so credits and reels are fields on this class and the drawing is a function of them.
    ///  - <b>The tree is cheap to rebuild.</b> Every element is made on the spot from those fields, so a
    ///    rebuild gives a fresh, correct window rather than a half-restored one.
    /// </remarks>
    internal static class LuckySevens
    {
        private const int SpinCost = 1;
        private const int TripleSevens = 20;
        private const int OtherTriple = 8;
        private const int Pair = 2;

        private static readonly int[] Reels = { 0, 0, 0 };

        private static int _startingCredits = 20;
        private static int _credits = 20;
        private static int _spins;
        private static int _best;
        private static int _lastWin;

        /// <summary>What the settings bound and the config reload pushed in.</summary>
        internal static void Configure(int startingCredits)
        {
            _startingCredits = startingCredits;

            // Only before the first spin. A config reload in the middle of a session should not hand
            // out free credits, or take away credits already played for.
            if (_spins == 0) _credits = startingCredits;
        }

        internal static int Credits { get { return _credits; } }
        internal static int Spins { get { return _spins; } }

        /// <summary>
        /// The body of the app's window. The library has already added the app's heading and the
        /// scrolling container; everything from here down is ours to build.
        /// </summary>
        internal static void Draw(VisualElement root)
        {
            var reels = new Label[3];
            var reelRow = Widgets.Row(root);
            for (var i = 0; i < reels.Length; i++)
            {
                var reel = new Label("?");
                reel.style.fontSize = 34f;
                reel.style.unityFontStyleAndWeight = FontStyle.Bold;
                reel.style.minWidth = 42f;
                reel.style.marginRight = 12f;
                reelRow.Add(reel);
                reels[i] = reel;
            }

            var totals = Widgets.Label(root, "", false);
            var status = Widgets.Label(root, "", true);

            Action refresh = null;
            var buttons = Widgets.WrapRow(root);
            var spin = Widgets.PrimaryButton(buttons, "Spin (" + SpinCost + " credit)", delegate
            {
                if (Spin()) refresh();
            });
            Widgets.Button(buttons, "New session", delegate
            {
                Reset();
                refresh();
            });

            Widgets.Note(root, "Three sevens pays " + TripleSevens + ", any other triple pays " +
                               OtherTriple + ", and a pair pays " + Pair + ".");

            refresh = delegate
            {
                for (var i = 0; i < reels.Length; i++)
                {
                    reels[i].text = Reels[i] == 0 ? "?" : Reels[i].ToString();
                }

                totals.text = "Credits " + _credits + "    Spins " + _spins + "    Best win " + _best;
                status.text = Status();
                spin.SetEnabled(_credits >= SpinCost);
            };

            // One place decides what the window says, and both buttons go through it.
            refresh();
        }

        private static bool Spin()
        {
            if (_credits < SpinCost) return false;

            _credits -= SpinCost;
            _spins++;

            for (var i = 0; i < Reels.Length; i++) Reels[i] = UnityEngine.Random.Range(1, 8);

            int a = Reels[0], b = Reels[1], c = Reels[2];
            var won = 0;
            if (a == b && b == c) won = a == 7 ? TripleSevens : OtherTriple;
            else if (a == b || b == c || a == c) won = Pair;

            _credits += won;
            _lastWin = won;
            if (won > _best) _best = won;

            if (won > 0 && Plugin.Instance != null) Plugin.Instance.NotifyWin(won);
            return true;
        }

        private static void Reset()
        {
            _credits = _startingCredits;
            _spins = 0;
            _best = 0;
            _lastWin = 0;
            for (var i = 0; i < Reels.Length; i++) Reels[i] = 0;
        }

        private static string Status()
        {
            if (_credits < SpinCost) return "Out of credits. Take a new session.";
            if (_lastWin > 0) return "Paid " + _lastWin + " credits.";
            return _spins == 0 ? "Press Spin to start." : "No win on that spin.";
        }
    }
}
