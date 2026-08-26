using UnityEngine;

namespace SrtpAvatar
{
    public sealed class MobilePresentationLayout : MonoBehaviour
    {
        [SerializeField] private Camera targetCamera;
        [SerializeField] private RectTransform hintText;

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

        public void Apply()
        {
            var portrait = Screen.height >= Screen.width;
            if (targetCamera != null)
            {
                targetCamera.clearFlags = CameraClearFlags.SolidColor;
                targetCamera.backgroundColor = new Color(0.86f, 0.94f, 0.985f);
                targetCamera.fieldOfView = portrait ? 27f : 22f;
                targetCamera.transform.position = portrait ? new Vector3(0f, 1.08f, -4.05f) : new Vector3(0f, 1.08f, -3.2f);
                targetCamera.transform.LookAt(new Vector3(0f, 0.92f, 0f));
            }
            if (hintText != null && !Application.isEditor)
            {
                hintText.gameObject.SetActive(false);
            }
        }
    }
}
