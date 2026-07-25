using UnityEngine.UI;

namespace SrtpAvatar
{
    public sealed class AppUi
    {
        private readonly Button microphoneButton;
        private readonly Text statusText;
        private readonly Text subtitleText;
        private readonly Text hintText;

        public AppUi(Button microphoneButton, Text statusText, Text subtitleText, Text hintText)
        {
            this.microphoneButton = microphoneButton;
            this.statusText = statusText;
            this.subtitleText = subtitleText;
            this.hintText = hintText;
        }

        public void SetStatus(string text)
        {
            if (statusText != null)
            {
                statusText.text = text;
            }
        }

        public void SetSubtitle(string text)
        {
            if (subtitleText != null)
            {
                subtitleText.text = text;
            }
        }

        public void SetHint(string text)
        {
            if (hintText != null)
            {
                hintText.text = text;
            }
        }

        public void SetMicrophoneInteractable(bool interactable)
        {
            if (microphoneButton != null)
            {
                microphoneButton.interactable = interactable;
            }
        }
    }
}
