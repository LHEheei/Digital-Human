using System;
using UnityEngine.UI;

namespace SrtpAvatar
{
    public sealed class AppUi
    {

        private readonly Text statusText;
        private readonly Text subtitleText;
        private readonly Text hintText;

        public AppUi(Text statusText, Text subtitleText, Text hintText)
        {
            this.statusText = Require(statusText, nameof(statusText));
            this.subtitleText = Require(subtitleText, nameof(subtitleText));
            this.hintText = Require(hintText, nameof(hintText));
        }

        public void SetStatus(string text)
        {
            statusText.text = text;
        }

        public void SetSubtitle(string text)
        {
            subtitleText.text = text;
        }

        public void SetHint(string text)
        {
            hintText.text = text;
        }

        private static T Require<T>(T value, string name) where T : UnityEngine.Object
        {
            if (value == null)
            {
                throw new ArgumentNullException(name, "Digital human UI reference is required.");
            }

            return value;
        }
    }
}
