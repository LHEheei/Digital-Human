using UnityEngine;

namespace SrtpAvatar
{
    public sealed class MobilePresentationLayout : MonoBehaviour
    {
        [SerializeField] private Camera targetCamera;
        [SerializeField] private RectTransform hintText;

        private bool conversationLayout;
        private bool conversationPortrait;

        private void Awake()
        {
            if (targetCamera == null)
            {
                targetCamera = Camera.main;
            }

            Apply();
        }

        private void Start()
        {
            Apply();
        }

        public void Apply() => ApplyLayout(conversationLayout ? conversationPortrait : Screen.height >= Screen.width, conversationLayout);
        public void ApplyConversationLayout(bool portrait)
        { conversationLayout = true; conversationPortrait = portrait; Apply(); }
        private void ApplyLayout(bool portrait, bool conversation)
        {
            if (targetCamera != null)
            {
                targetCamera.clearFlags = CameraClearFlags.SolidColor;
                targetCamera.backgroundColor = new Color(0.86f, 0.94f, 0.985f);
                targetCamera.fieldOfView = portrait ? 27f : 22f;
                targetCamera.transform.position = portrait ? new Vector3(0f, 1.08f, -4.05f) : new Vector3(0f, 1.08f, -3.2f);
                if (conversation)
                {
                    targetCamera.fieldOfView = portrait ? 27f : 24f;
                    targetCamera.transform.position = portrait ? new Vector3(0f, .62f, -4.05f) : new Vector3(.65f, .76f, -3.7f);
                    targetCamera.transform.LookAt(portrait ? new Vector3(0f, .46f, 0f) : new Vector3(.65f, .60f, 0f));
                }
                else targetCamera.transform.LookAt(new Vector3(0f, .92f, 0f));
            }
            if (hintText != null && !Application.isEditor)
            {
                hintText.gameObject.SetActive(false);
            }
        }
    }
}
