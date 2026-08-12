using System.IO;
using SrtpAvatar;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace SrtpAvatarEditor
{
    public static class DemoSceneBuilder
    {
        private const string ScenePath = "Assets/Scenes/DesktopAvatarDemo.unity";
        private const string ConfigPath = "Assets/Resources/OpenAIConfig.asset";

        [MenuItem("SRTP/Generate Demo Scene")]
        public static void Generate()
        {
            Directory.CreateDirectory("Assets/Scenes");
            Directory.CreateDirectory("Assets/Resources");

            var config = AssetDatabase.LoadAssetAtPath<AppConfig>(ConfigPath);
            if (config == null)
            {
                config = ScriptableObject.CreateInstance<AppConfig>();
                AssetDatabase.CreateAsset(config, ConfigPath);
            }

            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            scene.name = "DesktopAvatarDemo";

            CreateEnvironment();

            var controllerObject = new GameObject("Digital Human App Controller");
            var audioSource = controllerObject.AddComponent<AudioSource>();
            var androidSpeech = controllerObject.AddComponent<AndroidNativeSpeechBridge>();
            var controller = controllerObject.AddComponent<DigitalHumanAppController>();
            controllerObject.AddComponent<ApiConfigPanel>();
            var testPanel = controllerObject.AddComponent<AvatarTestPanel>();
            var mobileLayout = controllerObject.AddComponent<MobilePresentationLayout>();

            var ui = CreateUi();
            WireController(controller, config, null, androidSpeech, audioSource, ui);
            WireTestPanel(testPanel, null, audioSource);
            WireMobileLayout(mobileLayout, ui);

            EditorSceneManager.SaveScene(scene, ScenePath);
            EditorBuildSettings.scenes = new[] { new EditorBuildSettingsScene(ScenePath, true) };

            ConfigureAndroidPlayerSettings();

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            Debug.Log($"Generated demo scene: {ScenePath}");
        }

        [MenuItem("SRTP/Build Android APK")]
        public static void BuildAndroidApk()
        {
            BuildAndroidApk("Builds/DesktopAvatarCar.apk", BuildOptions.None);
        }

        [MenuItem("SRTP/Build Android Development APK (Validation)")]
        public static void BuildAndroidDevelopmentApk()
        {
            BuildAndroidApk(
                "Builds/Validation/DesktopAvatarCar-development.apk",
                BuildOptions.Development | BuildOptions.AllowDebugging);
        }

        private static void BuildAndroidApk(string outputPath, BuildOptions buildOptions)
        {
            var outputDirectory = Path.GetDirectoryName(outputPath);
            if (!string.IsNullOrWhiteSpace(outputDirectory))
            {
                Directory.CreateDirectory(outputDirectory);
            }

            ConfigureAndroidPlayerSettings();

            var scene = SceneManager.GetActiveScene();
            var scenePath = scene.IsValid() && !string.IsNullOrWhiteSpace(scene.path) ? scene.path : ScenePath;
            if (scene.IsValid() && scene.isDirty)
            {
                EditorSceneManager.SaveScene(scene);
            }

            if (!File.Exists(scenePath))
            {
                Debug.LogError($"Scene not found: {scenePath}. Open or generate the demo scene first.");
                return;
            }

            EditorBuildSettings.scenes = new[] { new EditorBuildSettingsScene(scenePath, true) };

            var options = new BuildPlayerOptions
            {
                scenes = new[] { scenePath },
                locationPathName = outputPath,
                target = BuildTarget.Android,
                options = buildOptions
            };

            var report = BuildPipeline.BuildPlayer(options);
            Debug.Log($"Android build finished: {report.summary.result}, {report.summary.totalSize} bytes, {outputPath}");
        }

        [MenuItem("SRTP/Use Selected VRM Avatar")]
        public static void UseSelectedVrmAvatar()
        {
            var selected = Selection.activeGameObject;
            if (selected == null)
            {
                Debug.LogError("Select the VRM avatar root in the Hierarchy first.");
                return;
            }

            selected.name = "VRM Digital Human";
            selected.transform.position = new Vector3(0f, -0.42f, 0f);
            selected.transform.rotation = Quaternion.Euler(0f, 180f, 0f);
            selected.transform.localScale = Vector3.one;

            var bridge = selected.GetComponent<VrmAvatarBridge>();
            if (bridge == null)
            {
                bridge = selected.AddComponent<VrmAvatarBridge>();
            }

            var controller = Object.FindFirstObjectByType<DigitalHumanAppController>();
            if (controller == null)
            {
                Debug.LogError("Digital Human App Controller was not found. Generate the demo scene first.");
                return;
            }

            var controllerObject = new SerializedObject(controller);
            controllerObject.FindProperty("avatarDriver").objectReferenceValue = bridge;
            controllerObject.ApplyModifiedPropertiesWithoutUndo();

            var audioSource = controller.GetComponent<AudioSource>();
            if (audioSource == null)
            {
                audioSource = controller.gameObject.AddComponent<AudioSource>();
            }

            var androidSpeech = controller.GetComponent<AndroidNativeSpeechBridge>();
            if (androidSpeech == null)
            {
                androidSpeech = controller.gameObject.AddComponent<AndroidNativeSpeechBridge>();
            }

            controllerObject.FindProperty("androidSpeech").objectReferenceValue = androidSpeech;
            controllerObject.ApplyModifiedPropertiesWithoutUndo();

            var bridgeObject = new SerializedObject(bridge);
            bridgeObject.FindProperty("speechAudio").objectReferenceValue = audioSource;
            bridgeObject.ApplyModifiedPropertiesWithoutUndo();

            var testPanel = controller.GetComponent<AvatarTestPanel>();
            if (testPanel == null)
            {
                testPanel = controller.gameObject.AddComponent<AvatarTestPanel>();
            }

            WireTestPanel(testPanel, bridge, audioSource);

            var mobileLayout = controller.GetComponent<MobilePresentationLayout>();
            if (mobileLayout == null)
            {
                mobileLayout = controller.gameObject.AddComponent<MobilePresentationLayout>();
            }

            WireMobileLayout(mobileLayout, FindUiRefs());

            var fallback = GameObject.Find("Procedural 3D Digital Human");
            if (fallback != null)
            {
                Object.DestroyImmediate(fallback);
            }

            var camera = Camera.main;
            if (camera != null)
            {
                ConfigureCameraForVrm(camera);
            }

            ApplySoftBluePresentationLook();

            EditorSceneManager.MarkSceneDirty(SceneManager.GetActiveScene());
            Debug.Log("Selected VRM avatar is now bound to the SRTP demo controller.");
        }

        [MenuItem("SRTP/Use Selected VRM Avatar", true)]
        public static bool ValidateUseSelectedVrmAvatar()
        {
            return Selection.activeGameObject != null;
        }

        [MenuItem("SRTP/Apply Soft Blue Look")]
        public static void ApplySoftBluePresentationLook()
        {
            var camera = Camera.main;
            if (camera != null)
            {
                ConfigureCameraForVrm(camera);
            }

            var baseObject = GameObject.Find("Desktop Base");
            if (baseObject != null)
            {
                baseObject.transform.position = new Vector3(0f, -0.46f, 0f);
                baseObject.transform.localScale = new Vector3(1.34f, 0.035f, 1.34f);
                var renderer = baseObject.GetComponent<Renderer>();
                if (renderer != null)
                {
                    renderer.material = Material("Soft Blue Desktop Base", new Color(0.74f, 0.86f, 0.93f));
                }
            }

            var canvas = GameObject.Find("Canvas");
            if (canvas != null)
            {
                var status = canvas.transform.Find("Status") as RectTransform;
                var subtitle = canvas.transform.Find("Subtitle") as RectTransform;
                var hint = canvas.transform.Find("Hint") as RectTransform;
                var mic = canvas.transform.Find("Microphone Button") as RectTransform;

                if (status != null)
                {
                    SetRect(status, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0f, -96f), new Vector2(420f, 82f));
                    var text = status.GetComponent<Text>();
                    if (text != null)
                    {
                        text.color = new Color(0.06f, 0.22f, 0.36f);
                    }
                }

                if (subtitle != null)
                {
                    SetRect(subtitle, new Vector2(0.08f, 1f), new Vector2(0.92f, 1f), new Vector2(0f, -172f), new Vector2(0f, 108f));
                    var text = subtitle.GetComponent<Text>();
                    if (text != null)
                    {
                        text.fontSize = 30;
                        text.color = new Color(0.08f, 0.18f, 0.28f);
                    }
                }

                if (hint != null)
                {
                    SetRect(hint, new Vector2(1f, 0f), new Vector2(1f, 0f), new Vector2(-214f, 56f), new Vector2(390f, 52f));
                    var text = hint.GetComponent<Text>();
                    if (text != null)
                    {
                        text.fontSize = 18;
                        text.alignment = TextAnchor.MiddleRight;
                        text.color = new Color(0.16f, 0.36f, 0.48f, 0.72f);
                    }
                }

                if (mic != null)
                {
                    mic.pivot = new Vector2(1f, 0f);
                    SetRect(mic, new Vector2(1f, 0f), new Vector2(1f, 0f), new Vector2(-42f, 44f), new Vector2(116f, 116f));
                    var image = mic.GetComponent<Image>();
                    if (image != null)
                    {
                        image.color = new Color(0.08f, 0.58f, 0.68f, 0.9f);
                    }
                }
            }

            EditorSceneManager.MarkSceneDirty(SceneManager.GetActiveScene());
            Debug.Log("Applied soft blue presentation look.");
        }

        private static void CreateEnvironment()
        {
            var cameraObject = new GameObject("Main Camera");
            cameraObject.tag = "MainCamera";

            var camera = cameraObject.AddComponent<Camera>();
            cameraObject.AddComponent<AudioListener>();
            camera.clearFlags = CameraClearFlags.SolidColor;
            ConfigureCameraForVrm(camera);

            var key = new GameObject("Key Light").AddComponent<Light>();
            key.type = LightType.Directional;
            key.intensity = 1.15f;
            key.transform.rotation = Quaternion.Euler(45f, -25f, 0f);

            var fill = new GameObject("Soft Fill Light").AddComponent<Light>();
            fill.type = LightType.Point;
            fill.intensity = 1.6f;
            fill.range = 5f;
            fill.transform.position = new Vector3(0f, 1.5f, -2.2f);

            var floor = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            floor.name = "Desktop Base";
            floor.transform.position = new Vector3(0f, -0.46f, 0f);
            floor.transform.localScale = new Vector3(1.34f, 0.035f, 1.34f);
            Object.DestroyImmediate(floor.GetComponent<Collider>());
            floor.GetComponent<Renderer>().material = Material("Soft Blue Desktop Base", new Color(0.74f, 0.86f, 0.93f));
        }

        private static UiRefs CreateUi()
        {
            var eventSystem = new GameObject("EventSystem");
            eventSystem.AddComponent<EventSystem>();
            eventSystem.AddComponent<StandaloneInputModule>();

            var canvasObject = new GameObject("Canvas");
            var canvas = canvasObject.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;

            var scaler = canvasObject.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1080f, 1920f);
            scaler.matchWidthOrHeight = 0.55f;
            canvasObject.AddComponent<GraphicRaycaster>();

            var status = CreateText("Status", canvasObject.transform, "\u7a7a\u95f2", 44, TextAnchor.MiddleCenter, new Color(0.06f, 0.22f, 0.36f));
            SetRect(status.rectTransform, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0f, -96f), new Vector2(420f, 82f));

            var subtitle = CreateText("Subtitle", canvasObject.transform, "\u70b9\u51fb\u9ea6\u514b\u98ce\u5f00\u59cb\u5bf9\u8bdd", 30, TextAnchor.MiddleCenter, new Color(0.08f, 0.18f, 0.28f));
            SetRect(subtitle.rectTransform, new Vector2(0.08f, 1f), new Vector2(0.92f, 1f), new Vector2(0f, -172f), new Vector2(0f, 108f));

            var hint = CreateText("Hint", canvasObject.transform, "\u6f14\u793a\u6a21\u5f0f", 18, TextAnchor.MiddleRight, new Color(0.16f, 0.36f, 0.48f, 0.72f));
            SetRect(hint.rectTransform, new Vector2(1f, 0f), new Vector2(1f, 0f), new Vector2(-214f, 56f), new Vector2(390f, 52f));

            var buttonObject = new GameObject("Microphone Button");
            buttonObject.transform.SetParent(canvasObject.transform, false);
            var buttonImage = buttonObject.AddComponent<Image>();
            buttonImage.color = new Color(0.08f, 0.57f, 0.66f, 0.92f);
            var button = buttonObject.AddComponent<Button>();
            var buttonRect = buttonObject.GetComponent<RectTransform>();
            buttonRect.pivot = new Vector2(1f, 0f);
            SetRect(buttonRect, new Vector2(1f, 0f), new Vector2(1f, 0f), new Vector2(-42f, 44f), new Vector2(116f, 116f));

            var buttonLabel = CreateText("Mic Label", buttonObject.transform, "MIC", 24, TextAnchor.MiddleCenter, Color.white);
            SetRect(buttonLabel.rectTransform, Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);

            return new UiRefs
            {
                microphoneButton = button,
                statusText = status,
                subtitleText = subtitle,
                hintText = hint
            };
        }

        private static Text CreateText(string name, Transform parent, string content, int size, TextAnchor anchor, Color color)
        {
            var obj = new GameObject(name);
            obj.transform.SetParent(parent, false);
            var text = obj.AddComponent<Text>();
            text.text = content;
            text.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            text.fontSize = size;
            text.alignment = anchor;
            text.color = color;
            text.horizontalOverflow = HorizontalWrapMode.Wrap;
            text.verticalOverflow = VerticalWrapMode.Overflow;
            return text;
        }

        private static void WireController(
            DigitalHumanAppController controller,
            AppConfig config,
            DigitalHumanAvatarBase avatarDriver,
            AndroidNativeSpeechBridge androidSpeech,
            AudioSource audioSource,
            UiRefs ui)
        {
            var serialized = new SerializedObject(controller);
            serialized.FindProperty("config").objectReferenceValue = config;
            serialized.FindProperty("avatarDriver").objectReferenceValue = avatarDriver;
            serialized.FindProperty("androidSpeech").objectReferenceValue = androidSpeech;
            serialized.FindProperty("audioSource").objectReferenceValue = audioSource;
            serialized.FindProperty("microphoneButton").objectReferenceValue = ui.microphoneButton;
            serialized.FindProperty("statusText").objectReferenceValue = ui.statusText;
            serialized.FindProperty("subtitleText").objectReferenceValue = ui.subtitleText;
            serialized.FindProperty("hintText").objectReferenceValue = ui.hintText;
            serialized.ApplyModifiedPropertiesWithoutUndo();
        }

        private static void WireTestPanel(AvatarTestPanel panel, DigitalHumanAvatarBase avatarDriver, AudioSource audioSource)
        {
            if (panel == null)
            {
                return;
            }

            var serialized = new SerializedObject(panel);
            serialized.FindProperty("avatarDriver").objectReferenceValue = avatarDriver;
            serialized.FindProperty("audioSource").objectReferenceValue = audioSource;
            serialized.ApplyModifiedPropertiesWithoutUndo();
        }

        private static UiRefs FindUiRefs()
        {
            var canvas = GameObject.Find("Canvas");
            if (canvas == null)
            {
                return new UiRefs();
            }

            return new UiRefs
            {
                microphoneButton = canvas.transform.Find("Microphone Button")?.GetComponent<Button>(),
                hintText = canvas.transform.Find("Hint")?.GetComponent<Text>()
            };
        }

        private static void WireMobileLayout(MobilePresentationLayout layout, UiRefs ui)
        {
            if (layout == null)
            {
                return;
            }

            var serialized = new SerializedObject(layout);
            serialized.FindProperty("targetCamera").objectReferenceValue = Camera.main;
            serialized.FindProperty("microphoneButton").objectReferenceValue = ui.microphoneButton == null ? null : ui.microphoneButton.GetComponent<RectTransform>();
            serialized.FindProperty("hintText").objectReferenceValue = ui.hintText == null ? null : ui.hintText.GetComponent<RectTransform>();
            serialized.ApplyModifiedPropertiesWithoutUndo();
        }

        private static void SetRect(RectTransform rect, Vector2 anchorMin, Vector2 anchorMax, Vector2 anchoredPosition, Vector2 sizeDelta)
        {
            rect.anchorMin = anchorMin;
            rect.anchorMax = anchorMax;
            rect.anchoredPosition = anchoredPosition;
            rect.sizeDelta = sizeDelta;
        }

        private static void ConfigureCameraForVrm(Camera camera)
        {
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = new Color(0.86f, 0.94f, 0.985f);
            camera.fieldOfView = 22f;
            camera.transform.position = new Vector3(0f, 1.08f, -3.2f);
            camera.transform.LookAt(new Vector3(0f, 0.92f, 0f));
        }

        private static void ConfigureAndroidPlayerSettings()
        {
            PlayerSettings.companyName = "SRTP";
            PlayerSettings.productName = "DesktopAvatarCar";
            PlayerSettings.SetApplicationIdentifier(NamedBuildTarget.Android, "com.srtp.desktopavatarcar");
            PlayerSettings.defaultInterfaceOrientation = UIOrientation.Portrait;
            PlayerSettings.Android.minSdkVersion = AndroidSdkVersions.AndroidApiLevel23;
            PlayerSettings.Android.targetSdkVersion = AndroidSdkVersions.AndroidApiLevel34;
            PlayerSettings.Android.targetArchitectures = AndroidArchitecture.ARM64;
            EditorUserBuildSettings.SwitchActiveBuildTarget(BuildTargetGroup.Android, BuildTarget.Android);
        }

        private static Material Material(string name, Color color)
        {
            var material = new Material(Shader.Find("Standard"));
            material.name = name;
            material.color = color;
            return material;
        }

        private sealed class UiRefs
        {
            public Button microphoneButton;
            public Text statusText;
            public Text subtitleText;
            public Text hintText;
        }
    }
}
