using System;
using ScamWYF.Modding.Core.Ui;
using UnityEngine;
using UnityEngine.UIElements;

namespace ScamWYF.DemoContent
{
    /// <summary>
    /// The handful of elements every demo app is built from, styled the way the game's own app
    /// content is: its panel class for a card, its typography, and colours read from the live theme
    /// rather than hardcoded.
    /// </summary>
    /// <remarks>
    /// The library hands a drawn app a content root that already has the game's
    /// <c>kolkata-app-content</c> class, its typography and the app's heading. What it does not do is
    /// invent controls, because a calculator key and a spin button are not the same thing - so these
    /// helpers keep the inline styling in one file instead of in every app.
    ///
    /// Nothing here holds state. Every app's drawing is a function of that app's own fields, because
    /// the game rebuilds an app window whenever it likes.
    /// </remarks>
    internal static class AppKit
    {
        /// <summary>A card on the window's background, drawn with the game's own panel class.</summary>
        internal static VisualElement Card(VisualElement root)
        {
            var card = new VisualElement();
            card.AddToClassList("kolkata-panel");
            card.style.marginTop = 2f;
            card.style.marginBottom = 10f;
            card.style.paddingLeft = 16f;
            card.style.paddingRight = 16f;
            card.style.paddingTop = 14f;
            card.style.paddingBottom = 14f;
            if (root != null) root.Add(card);
            return card;
        }

        /// <summary>A line of text. <paramref name="size"/> is in points, the way the game sizes its own.</summary>
        internal static Label Text(VisualElement parent, string text, int size, bool muted)
        {
            var label = new Label(text ?? "");
            label.style.fontSize = size;
            label.style.color = muted ? UiTheme.TextMuted : UiTheme.Text;
            label.style.whiteSpace = WhiteSpace.Normal;
            if (parent != null) parent.Add(label);
            return label;
        }

        /// <summary>A line that is centred, for a display: a total, a time, an answer.</summary>
        internal static Label Display(VisualElement parent, int size)
        {
            var label = Text(parent, "", size, false);
            label.style.unityTextAlign = TextAnchor.MiddleCenter;
            label.style.unityFontStyleAndWeight = FontStyle.Bold;
            label.style.marginTop = 2f;
            label.style.marginBottom = 2f;
            return label;
        }

        /// <summary>A horizontal strip that its children share.</summary>
        internal static VisualElement Row(VisualElement parent)
        {
            var row = new VisualElement();
            row.style.flexDirection = FlexDirection.Row;
            row.style.alignItems = Align.Center;
            if (parent != null) parent.Add(row);
            return row;
        }

        /// <summary>
        /// A button sized for a keypad or a row of actions. The accent variant is for the one action
        /// the app is about, so a calculator's digits and its equals key do not look alike.
        /// </summary>
        internal static Button Key(VisualElement parent, string text, Action onClick, bool accent)
        {
            var button = new Button(delegate
            {
                if (onClick == null) return;
                try
                {
                    onClick();
                }
                catch (Exception ex)
                {
                    Debug.LogWarning("[Demo Content] An app button threw: " + ex);
                }
            });
            button.text = text ?? "";
            button.style.height = 44f;
            button.style.flexGrow = 1f;
            button.style.marginLeft = 3f;
            button.style.marginRight = 3f;
            button.style.marginTop = 3f;
            button.style.marginBottom = 3f;
            button.style.fontSize = 17f;
            button.style.unityFontStyleAndWeight = FontStyle.Bold;
            button.style.unityTextAlign = TextAnchor.MiddleCenter;
            button.style.backgroundColor = accent ? UiTheme.Accent : UiTheme.Button;
            button.style.color = accent ? new Color(1f, 1f, 1f) : UiTheme.Text;
            if (parent != null) parent.Add(button);
            return button;
        }

        /// <summary>A full-width action button, for the one thing an app is for.</summary>
        internal static Button Action(VisualElement parent, string text, Action onClick)
        {
            var button = Key(parent, text, onClick, true);
            button.style.height = 46f;
            button.style.fontSize = 18f;
            return button;
        }

        /// <summary>A small muted line: instructions, a payout table, a hint.</summary>
        internal static Label Hint(VisualElement parent, string text)
        {
            var label = Text(parent, text, 12, true);
            label.style.marginTop = 2f;
            label.style.marginBottom = 6f;
            return label;
        }
    }
}
