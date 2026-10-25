using System;
using UnityEngine;
using UnityEngine.UIElements;

namespace ScamWYF.DemoContent
{
    /// <summary>
    /// A notepad: the demo's example of an app that takes text, keeps it, and puts it back when the
    /// window is rebuilt.
    /// </summary>
    /// <remarks>
    /// The text lives in a field, not in the text field's own value, for the same reason the game's
    /// state does: the element is destroyed whenever the game rebuilds the window. Writing it to
    /// disk would be the next step for a real app - this one keeps it for the session so the demo
    /// has nothing to clean up.
    /// </remarks>
    internal static class Notebook
    {
        private static string _text = "";

        internal static void Draw(VisualElement root)
        {
            var card = AppKit.Card(root);
            card.style.flexGrow = 1f;

            var field = new TextField();
            field.multiline = true;
            field.value = _text;
            field.style.flexGrow = 1f;
            field.style.minHeight = 190f;
            field.style.fontSize = 15f;
            field.style.whiteSpace = WhiteSpace.Normal;

            // Black on the OS's pale window, and the same ink the rest of the demo uses.
            field.style.color = AppKit.Ink;
            field.style.backgroundColor = new Color(0.95f, 0.95f, 0.96f);
            field.RegisterValueChangedCallback(delegate(ChangeEvent<string> evt)
            {
                _text = evt.newValue ?? "";
            });
            card.Add(field);

            var footer = AppKit.Row(root);
            footer.style.justifyContent = Justify.SpaceBetween;

            var count = AppKit.Hint(footer, Count());
            count.style.marginTop = 6f;

            var clear = AppKit.Key(footer, "Clear", delegate
            {
                _text = "";
                field.SetValueWithoutNotify("");
                count.text = Count();
            }, false);
            clear.style.flexGrow = 0f;
            clear.style.minWidth = 96f;

            field.RegisterValueChangedCallback(delegate(ChangeEvent<string> evt)
            {
                count.text = Count();
            });

            AppKit.Hint(root, "Kept for the session. A real app would write this to disk, and " +
                              "nothing on the computer's file system is a mod's to write without " +
                              "saying so.");
        }

        private static string Count()
        {
            return _text.Length + " characters";
        }
    }
}
