using System;
using ScamWYF.Modding.Core.Ui;
using UnityEngine;
using UnityEngine.UIElements;

namespace ScamWYF.DemoContent
{
    /// <summary>
    /// Lucky Sevens, drawn the way an app in the game's OS looks: a panel with the reels on it, the
    /// numbers that matter under them, and one obvious action.
    /// </summary>
    /// <remarks>
    /// Two things to copy from this file:
    ///
    ///  - <b>The state lives here, not in the elements.</b> The game rebuilds an app window whenever
    ///    it likes, and every rebuild calls the draw callback with an empty tree, so credits, spins
    ///    and the reels are fields on this class and the drawing is a function of them.
    ///  - <b>One refresh, one source of truth.</b> Every action calls the same closure at the end, so
    ///    there is exactly one place that decides what the window says.
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

        /// <summary>Where the settings bind and a config reload land.</summary>
        internal static void Configure(int startingCredits)
        {
            _startingCredits = startingCredits;
            if (_spins == 0) _credits = startingCredits;
        }

        internal static int Credits { get { return _credits; } }
        internal static int Spins { get { return _spins; } }

        internal static void Draw(VisualElement root)
        {
            // The reels, on their own panel, because they are the thing the window is about.
            var stage = AppKit.Card(root);
            stage.style.alignItems = Align.Center;

            var reelRow = AppKit.Row(stage);
            reelRow.style.justifyContent = Justify.Center;
            var reels = new Label[3];
            for (var i = 0; i < reels.Length; i++)
            {
                var reel = AppKit.Text(reelRow, "", 56, false);
                reel.style.unityFontStyleAndWeight = FontStyle.Bold;
                reel.style.unityTextAlign = TextAnchor.MiddleCenter;
                reel.style.minWidth = 74f;
                reel.style.marginLeft = 6f;
                reel.style.marginRight = 6f;
                reels[i] = reel;
            }

            var status = AppKit.Display(stage, 15);
            status.style.marginTop = 6f;

            // The numbers under it, in the order a player asks for them.
            var totals = AppKit.Row(root);
            totals.style.justifyContent = Justify.SpaceBetween;
            var credits = Totals(totals, "CREDITS");
            var spins = Totals(totals, "SPINS");
            var best = Totals(totals, "BEST WIN");

            var actions = AppKit.Row(root);
            actions.style.marginTop = 12f;

            Action refresh = null;
            var spin = AppKit.Action(actions, "SPIN - " + SpinCost + " CREDIT", delegate
            {
                if (Spin() && refresh != null) refresh();
            });
            spin.style.marginRight = 6f;
            var reset = AppKit.Key(actions, "New session", delegate
            {
                Reset();
                if (refresh != null) refresh();
            }, false);
            reset.style.flexGrow = 0f;
            reset.style.minWidth = 130f;

            AppKit.Hint(root, "Three sevens pay " + TripleSevens + ", any other triple " + OtherTriple +
                              ", two of a kind " + Pair + ".");

            refresh = delegate
            {
                for (var i = 0; i < reels.Length; i++)
                {
                    reels[i].text = Reels[i] == 0 ? "-" : Reels[i].ToString();
                }

                status.text = Status();
                SetTotals(credits, _credits);
                SetTotals(spins, _spins);
                SetTotals(best, _best);

                // Nothing to spin with is a state, not an error: say it and let the button go grey.
                spin.SetEnabled(_credits >= SpinCost);
                spin.text = _credits >= SpinCost
                    ? "SPIN - " + SpinCost + " CREDIT"
                    : "OUT OF CREDITS";
            };

            refresh();
        }

        /// <summary>A small caption over a big number, which is the shape the game's own HUD uses.</summary>
        private static Label Totals(VisualElement parent, string caption)
        {
            var column = new VisualElement();
            column.style.flexGrow = 1f;
            column.style.alignItems = Align.Center;
            parent.Add(column);

            var label = AppKit.Text(column, caption, 11, true);
            label.style.unityTextAlign = TextAnchor.MiddleCenter;
            label.style.letterSpacing = 1f;

            var value = AppKit.Text(column, "0", 26, false);
            value.style.unityFontStyleAndWeight = FontStyle.Bold;
            value.style.unityTextAlign = TextAnchor.MiddleCenter;
            return value;
        }

        private static void SetTotals(Label label, int value)
        {
            label.text = value.ToString();
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
            return _spins == 0 ? "Press spin to play." : "No win on that spin.";
        }
    }
}
