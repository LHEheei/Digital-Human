using UnityEngine;
using UnityEngine.EventSystems;

namespace SrtpAvatar
{
    public sealed class ConversationInputFocus : MonoBehaviour, ISelectHandler, IDeselectHandler
    {
        private DigitalHumanAppController controller;
        public void Bind(DigitalHumanAppController owner) => controller = owner;
        public void OnSelect(BaseEventData data) => controller?.SetTextInputFocused(true);
        public void OnDeselect(BaseEventData data) => controller?.SetTextInputFocused(false);
        private void OnDisable() => controller?.SetTextInputFocused(false);
    }
}
