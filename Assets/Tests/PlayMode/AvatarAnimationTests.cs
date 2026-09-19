#if UNITY_EDITOR
using System;
using System.Collections;
using System.IO;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.TestTools;
using VRM;
using Object = UnityEngine.Object;

namespace SrtpAvatar.Tests
{
    public sealed partial class AvatarAnimationTests
    {
        private GameObject rig;
        private VrmAvatarBridge bridge;
        private Animator animator;
        private Camera camera;
        private VRMBlendShapeProxy face;
        private int previousCaptureRate;
        private UnityEngine.Random.State previousRandomState;
        private Color previousAmbient;

        [UnitySetUp]
        public IEnumerator SetUp()
        {
            previousCaptureRate = Time.captureFramerate;
            previousRandomState = UnityEngine.Random.state;
            previousAmbient = RenderSettings.ambientLight;
            Time.captureFramerate = 60;
            UnityEngine.Random.InitState(8841);
            rig = new GameObject("Avatar animation validation");
            camera = new GameObject("Preview camera").AddComponent<Camera>();
            camera.transform.SetParent(rig.transform);
            camera.tag = "MainCamera";
            camera.transform.position = new Vector3(0f, 0.53f, -3.2f);
            camera.transform.LookAt(new Vector3(0f, 0.53f, 0f));
            camera.orthographic = true;
            camera.orthographicSize = 0.98f;
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = new Color(0.86f, 0.91f, 0.94f);
            var light = new GameObject("Preview key light").AddComponent<Light>();
            light.transform.SetParent(rig.transform);
            light.type = LightType.Directional;
            light.intensity = 1.1f;
            light.transform.rotation = Quaternion.Euler(35f, -25f, 0f);
            RenderSettings.ambientLight = new Color(0.7f, 0.7f, 0.7f);

            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Models/8590256991748008892.prefab");
            Assert.IsNotNull(prefab);
            var avatar = Object.Instantiate(prefab, rig.transform);
            avatar.transform.SetPositionAndRotation(new Vector3(0f, -0.42f, 0f), Quaternion.Euler(0f, 180f, 0f));
            animator = avatar.GetComponent<Animator>();
            face = avatar.GetComponent<VRMBlendShapeProxy>();
            bridge = avatar.AddComponent<VrmAvatarBridge>();
            yield return null;
            yield return null;
        }

        [UnityTearDown]
        public IEnumerator TearDown()
        {
            Object.Destroy(rig);
            Time.captureFramerate = previousCaptureRate;
            UnityEngine.Random.state = previousRandomState;
            RenderSettings.ambientLight = previousAmbient;
            yield return null;
        }

        [UnityTest]
        public IEnumerator InterruptingGestureDoesNotSnapBones()
        {
            bridge.PlayGesture(AvatarGesture.wave);
            for (var frame = 0; frame < 55; frame++) yield return null;
            var upperArm = animator.GetBoneTransform(HumanBodyBones.RightUpperArm);
            var before = upperArm.localRotation;
            bridge.PlayGesture(AvatarGesture.nod);
            Assert.Less(Quaternion.Angle(before, upperArm.localRotation), 0.01f,
                "Requesting a new gesture must not reset the displayed pose.");

            for (var frame = 0; frame < 100; frame++)
            {
                before = upperArm.localRotation;
                yield return null;
                Assert.LessOrEqual(Quaternion.Angle(before, upperArm.localRotation), 180f * Time.deltaTime + 0.2f);
            }
        }

        [UnityTest]
        public IEnumerator SilenceClosesMouthWhileSpeechStateRemainsActive()
        {
            bridge.SetState(AvatarState.speaking);
            bridge.SetSpeechLevel(0.9f);
            for (var frame = 0; frame < 30; frame++) yield return null;
            Assert.Greater(face.GetValue(BlendShapeKey.CreateFromPreset(BlendShapePreset.A)), 0.05f);
            bridge.SetSpeechLevel(0f);
            for (var frame = 0; frame < 45; frame++) yield return null;
            foreach (var vowel in new[] { BlendShapePreset.A, BlendShapePreset.I, BlendShapePreset.U, BlendShapePreset.E, BlendShapePreset.O })
            {
                Assert.Less(face.GetValue(BlendShapeKey.CreateFromPreset(vowel)), 0.025f);
            }
        }

        [UnityTest]
        public IEnumerator DisablingAvatarReleasesGestureBackToIdle()
        {
            var arm = animator.GetBoneTransform(HumanBodyBones.RightUpperArm);
            for (var frame = 0; frame < 30; frame++) yield return null;
            var resting = arm.localRotation;
            bridge.PlayGesture(AvatarGesture.wave);
            for (var frame = 0; frame < 60; frame++) yield return null;
            Assert.Greater(Quaternion.Angle(resting, arm.localRotation), 10f);
            bridge.enabled = false;
            yield return null;
            bridge.enabled = true;
            for (var frame = 0; frame < 100; frame++) yield return null;
            Assert.Less(Quaternion.Angle(resting, arm.localRotation), 5f);
        }

        [UnityTest]
        public IEnumerator NativeMouthWaitsForPlaybackAndStopsWhenDisabled()
        {
            var audio = rig.AddComponent<AudioSource>();
            var nativeSpeech = rig.AddComponent<AndroidNativeSpeechBridge>();
            var speech = rig.AddComponent<AvatarSpeechDriver>();
            speech.Bind(bridge, audio);
            bridge.SetState(AvatarState.speaking);
            speech.StartNativeSpeechMouth(nativeSpeech);
            for (var frame = 0; frame < 30; frame++) yield return null;
            var mouth = BlendShapeKey.CreateFromPreset(BlendShapePreset.A);
            Assert.Less(face.GetValue(mouth), 0.01f, "Mouth must stay closed while native TTS is preparing.");

            speech.StartNativeSpeechMouth();
            var peak = 0f;
            for (var frame = 0; frame < 60; frame++)
            {
                yield return null;
                peak = Mathf.Max(peak, face.GetValue(mouth));
            }
            Assert.Greater(peak, 0.05f);
            speech.enabled = false;
            for (var frame = 0; frame < 45; frame++) yield return null;
            Assert.Less(face.GetValue(mouth), 0.01f);
        }

        [UnityTest]
        public IEnumerator PreviewConversationMotion()
        {
            var directory = Environment.GetEnvironmentVariable("SRTP_AVATAR_PREVIEW");
            RenderTexture target = null;
            Texture2D image = null;
            if (!string.IsNullOrEmpty(directory))
            {
                Directory.CreateDirectory(directory);
                target = new RenderTexture(640, 800, 24);
                image = new Texture2D(640, 800, TextureFormat.RGB24, false);
                camera.targetTexture = target;
            }

            var rootPosition = bridge.transform.position;
            try
            {
                for (var frame = 0; frame < 840; frame++)
                {
                    if (frame == 90) bridge.SetState(AvatarState.listening);
                    if (frame == 180) bridge.SetState(AvatarState.thinking);
                    if (frame == 300)
                    {
                        bridge.SetState(AvatarState.speaking);
                        bridge.SetEmotion(AvatarEmotion.joy);
                        bridge.PlayGesture(AvatarGesture.wave);
                    }
                    if (frame == 450) bridge.PlayGesture(AvatarGesture.explain);
                    if (frame == 580) bridge.PlayGesture(AvatarGesture.nod);
                    if (frame == 660)
                    {
                        bridge.SetSpeechLevel(0f);
                        bridge.SetState(AvatarState.idle);
                        bridge.SetEmotion(AvatarEmotion.neutral);
                    }
                    if (frame >= 300 && frame < 660)
                    {
                        var time = (frame - 300) / 60f;
                        var phrase = time % 1.7f < 1.35f ? 1f : 0f;
                        bridge.SetSpeechLevel(phrase * (0.2f + Mathf.Pow(Mathf.Max(0f, Mathf.Sin(time * 17f)), 2f) * 0.65f));
                    }
                    yield return null;
                    if (target != null && frame % 6 == 0)
                    {
                        var previousTarget = RenderTexture.active;
                        camera.Render();
                        RenderTexture.active = target;
                        image.ReadPixels(new Rect(0, 0, target.width, target.height), 0, 0);
                        image.Apply();
                        RenderTexture.active = previousTarget;
                        File.WriteAllBytes(Path.Combine(directory, $"frame-{frame / 6:D4}.png"), image.EncodeToPNG());
                    }
                }
                Assert.Less(Vector3.Distance(rootPosition, bridge.transform.position), 0.0001f);
            }
            finally
            {
                camera.targetTexture = null;
                if (target != null) { target.Release(); Object.Destroy(target); }
                if (image != null) Object.Destroy(image);
            }
        }
    }
}
#endif