using System;
using UnityEngine;
using UnityEngine.UIElements;

namespace ScamWYF.DemoContent
{
    /// <summary>
    /// A calculator: the plainest possible demonstration that a mod-drawn app can take input and do
    /// something with it.
    /// </summary>
    /// <remarks>
    /// Immediate-execution arithmetic, the way a pocket calculator works: <c>7 + 5 =</c> is twelve,
    /// and <c>7 + 5 × 2</c> is twenty-four rather than seventeen, because there is no precedence.
    /// That is not an oversight, it is the smaller thing to read.
    ///
    /// The display and the entry string are fields, not elements: the window is rebuilt from scratch
    /// whenever the game feels like it, and a calculator that forgets half a sum because the desktop
    /// refreshed is worse than one that is simple.
    /// </remarks>
    internal static class Calculator
    {
        private static string _entry = "0";
        private static double _pending;
        private static string _operator = "";
        private static bool _freshEntry = true;
        private static string _error;

        internal static void Draw(VisualElement root)
        {
            var card = AppKit.Card(root);
            var display = AppKit.Display(card, 34);
            display.style.unityTextAlign = TextAnchor.MiddleRight;
            var trail = AppKit.Hint(card, "");
            trail.style.unityTextAlign = TextAnchor.MiddleRight;

            var keys = new VisualElement();
            keys.style.marginTop = 6f;
            root.Add(keys);

            // One row per line, four keys each: a keypad that wraps where the window decides is a
            // keypad whose layout changes with the window's width.
            var row = AppKit.Row(keys);

            Action refresh = delegate
            {
                display.text = _error ?? _entry;
                trail.text = _operator.Length > 0
                    ? Format(_pending) + " " + _operator
                    : "Enter a number.";
            };

            Action<string> digit = delegate(string key)
            {
                _error = null;

                if (_freshEntry)
                {
                    _entry = key == "." ? "0." : key;
                    _freshEntry = false;
                    refresh();
                    return;
                }

                if (key == ".")
                {
                    if (_entry.IndexOf('.') >= 0) return;
                    _entry = _entry + ".";
                    refresh();
                    return;
                }

                _entry = _entry == "0" ? key : _entry + key;
                refresh();
            };

            Action<string> apply = delegate(string op)
            {
                _error = null;

                // Chaining operators without a number between them just replaces the operator, which
                // is what pressing + then x on any desk calculator does.
                if (_operator.Length > 0 && _freshEntry)
                {
                    _operator = op;
                    refresh();
                    return;
                }

                if (_operator.Length > 0) Commit();
                else _pending = Parse(_entry);

                _operator = op;
                _freshEntry = true;
                refresh();
            };

            Action equals = delegate
            {
                _error = null;
                if (_operator.Length == 0) return;
                Commit();
                _operator = "";
                _freshEntry = true;
                refresh();
            };

            AddKey(row, "C", delegate { Clear(); refresh(); }, false);
            AddKey(row, "DEL", delegate
            {
                _error = null;
                _entry = _entry.Length > 1 ? _entry.Substring(0, _entry.Length - 1) : "0";
                if (_entry == "-" || _entry.Length == 0) _entry = "0";
                _freshEntry = false;
                refresh();
            }, false);
            AddKey(row, "+/-", delegate
            {
                _error = null;
                _entry = _entry.StartsWith("-", StringComparison.Ordinal)
                    ? _entry.Substring(1)
                    : "-" + _entry;
                refresh();
            }, false);
            AddKey(row, "/", delegate { apply("/"); }, true);

            row = AppKit.Row(keys);
            AddKey(row, "7", delegate { digit("7"); }, false);
            AddKey(row, "8", delegate { digit("8"); }, false);
            AddKey(row, "9", delegate { digit("9"); }, false);
            AddKey(row, "x", delegate { apply("x"); }, true);

            row = AppKit.Row(keys);
            AddKey(row, "4", delegate { digit("4"); }, false);
            AddKey(row, "5", delegate { digit("5"); }, false);
            AddKey(row, "6", delegate { digit("6"); }, false);
            AddKey(row, "-", delegate { apply("-"); }, true);

            row = AppKit.Row(keys);
            AddKey(row, "1", delegate { digit("1"); }, false);
            AddKey(row, "2", delegate { digit("2"); }, false);
            AddKey(row, "3", delegate { digit("3"); }, false);
            AddKey(row, "+", delegate { apply("+"); }, true);

            row = AppKit.Row(keys);
            AddKey(row, "0", delegate { digit("0"); }, false);
            AddKey(row, ".", delegate { digit("."); }, false);
            AddKey(row, "%", delegate
            {
                _error = null;
                _entry = Format(Parse(_entry) / 100.0);
                _freshEntry = false;
                refresh();
            }, false);
            AddKey(row, "=", delegate { equals(); }, true);

            refresh();
        }

        private static void AddKey(VisualElement row, string label, Action onClick, bool accent)
        {
            AppKit.Key(row, label, onClick, accent);
        }

        private static void Commit()
        {
            var value = Parse(_entry);

            switch (_operator)
            {
                case "+": _pending += value; break;
                case "-": _pending -= value; break;
                case "x": _pending *= value; break;
                case "/":
                    if (Math.Abs(value) < 0.0000001)
                    {
                        _error = "Cannot divide by zero";
                        _pending = 0;
                        _operator = "";
                        _entry = "0";
                        _freshEntry = true;
                        return;
                    }
                    _pending /= value;
                    break;
                default: _pending = value; break;
            }

            _entry = Format(_pending);
        }

        private static void Clear()
        {
            _entry = "0";
            _pending = 0;
            _operator = "";
            _freshEntry = true;
            _error = null;
        }

        private static double Parse(string text)
        {
            double value;
            return double.TryParse(text, out value) ? value : 0;
        }

        private static string Format(double value)
        {
            // Whole numbers stay whole: "12" reads as a calculator, "12.00000" reads as a float.
            if (Math.Abs(value - Math.Round(value)) < 0.0000001 && Math.Abs(value) < 1e15)
                return ((long)Math.Round(value)).ToString();

            return value.ToString("0.######");
        }
    }
}
