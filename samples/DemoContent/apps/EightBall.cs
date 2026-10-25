using System;
using ScamWYF.Modding.Core.Ui;
using UnityEngine;
using UnityEngine.UIElements;

namespace ScamWYF.DemoContent
{
    /// <summary>
    /// A magic 8-ball: the smallest possible app that does something different every time it is
    /// pressed, and the shape most novelty apps in a demo take.
    /// </summary>
    internal static class EightBall
    {
        private static readonly string[] Answers =
        {
            "It is certain.",
            "Without a doubt.",
            "Ask again tomorrow.",
            "Cannot predict now.",
            "Do not count on it.",
            "My sources say no.",
            "Outlook not so good.",
            "Signs point to yes.",
            "The check will clear.",
            "The caller is lying.",
            "The manager is watching.",
            "Try the other phone.",
            "Your quota says no.",
            "Very doubtful.",
            "Send it to voicemail.",
        };

        private static string _answer = "Ask a question, then shake.";

        internal static void Draw(VisualElement root)
        {
            var card = AppKit.Card(root);
            card.style.alignItems = Align.Center;

            var answer = AppKit.Display(card, 24);
            answer.text = _answer;
            answer.style.whiteSpace = WhiteSpace.Normal;
            answer.style.minHeight = 90f;

            var actions = AppKit.Row(root);
            var shake = AppKit.Action(actions, "SHAKE", delegate
            {
                _answer = Answers[UnityEngine.Random.Range(0, Answers.Length)];
                answer.text = _answer;
            });
            shake.style.marginRight = 6f;

            AppKit.Key(actions, "Clear", delegate
            {
                _answer = "Ask a question, then shake.";
                answer.text = _answer;
            }, false);

            AppKit.Hint(root, "Yes or no questions only. It is a ball.");
        }
    }
}
