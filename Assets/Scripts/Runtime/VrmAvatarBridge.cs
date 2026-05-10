using System.Collections;
using UnityEngine;
using VRM;

namespace SrtpAvatar
{
    public sealed class VrmAvatarBridge : DigitalHumanAvatarBase
    {
        [SerializeField] private Animator animator;
        [SerializeField] private VRMBlendShapeProxy blendShapeProxy;
        [SerializeField] private AudioSource speechAudio;
        [SerializeField] private Transform lookTarget;

        private AvatarState state = AvatarState.idle;
        private AvatarEmotion emotion = AvatarEmotion.neutral;
        private float speechLevel;
        private Coroutine gestureRoutine;
        private Transform head;
        private Transform chest;
        private Transform leftUpperArm;
        private Transform rightUpperArm;
        private Transform leftLowerArm;
        private Transform rightLowerArm;
        private Transform leftHand;
        private Transform rightHand;
        private Transform leftShoulder;
        private Transform rightShoulder;
        private Quaternion headBase;
        private Quaternion leftShoulderBase;
        private Quaternion rightShoulderBase;
        private Quaternion leftUpperArmBase;
        private Quaternion rightUpperArmBase;
        private Quaternion leftLowerArmBase;
        private Quaternion rightLowerArmBase;
        private Quaternion leftHandBase;
        private Quaternion rightHandBase;

        private void Awake()
        {
            if (animator == null)
            {
                animator = GetComponentInChildren<Animator>();
            }

            if (blendShapeProxy == null)
            {
                blendShapeProxy = GetComponentInChildren<VRMBlendShapeProxy>();
            }

            CacheBones();
        }

        private void Start()
        {
            if (blendShapeProxy != null)
            {
                blendShapeProxy.Reinitialize();
            }

            ApplyEmotion(AvatarEmotion.neutral);
        }

        private void LateUpdate()
        {
            var idle = Mathf.Sin(Time.time * 1.2f);
            var breathe = Mathf.Sin(Time.time * 1.5f);
            if (head != null && gestureRoutine == null)
            {
                var listenPitch = state == AvatarState.listening ? 5f : 0f;
                head.localRotation = headBase * Quaternion.Euler(listenPitch + idle * 1.2f, idle * 1.1f, 0f);
            }

            if (gestureRoutine == null)
            {
                ApplyIdlePose(breathe);
            }

            ApplyMouth();
        }

        public override void SetState(AvatarState next)
        {
            state = next;
        }

        public override void SetEmotion(AvatarEmotion next)
        {
            emotion = next;
            ApplyEmotion(next);
        }

        public override void SetSpeechLevel(float level)
        {
            speechLevel = Mathf.Clamp01(level);
        }

        public override void PlayGesture(AvatarGesture gesture)
        {
            if (gestureRoutine != null)
            {
                StopCoroutine(gestureRoutine);
                ResetBones();
            }

            if (gesture != AvatarGesture.none)
            {
                gestureRoutine = StartCoroutine(PlayGestureRoutine(gesture));
            }
        }

        public void BindAudioSource(AudioSource source)
        {
            speechAudio = source;
        }

        private IEnumerator PlayGestureRoutine(AvatarGesture gesture)
        {
            switch (gesture)
            {
                case AvatarGesture.nod:
                    yield return Nod();
                    break;
                case AvatarGesture.wave:
                    yield return Wave();
                    break;
                case AvatarGesture.think:
                    yield return Think();
                    break;
                case AvatarGesture.explain:
                    yield return Explain();
                    break;
            }

            ResetBones();
            gestureRoutine = null;
        }

        private IEnumerator Nod()
        {
            const float duration = 1.35f;
            var elapsed = 0f;
            while (elapsed < duration)
            {
                elapsed += Time.deltaTime;
                var normalized = Mathf.Clamp01(elapsed / duration);
                var phase = Mathf.Sin(normalized * Mathf.PI * 4f);
                if (head != null)
                {
                    head.localRotation = headBase * Quaternion.Euler(phase * 11f, 0f, 0f);
                }

                yield return null;
            }
        }

        private IEnumerator Wave()
        {
            const float raiseDuration = 0.55f;
            const float waveDuration = 1.85f;
            var elapsed = 0f;
            var shoulderTarget = rightShoulderBase * Quaternion.Euler(0f, 0f, -12f);
            var upperTarget = rightUpperArmBase * Quaternion.Euler(-62f, 0f, -74f);
            var lowerTarget = rightLowerArmBase * Quaternion.Euler(-74f, 0f, 8f);

            while (elapsed < raiseDuration)
            {
                elapsed += Time.deltaTime;
                var t = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(elapsed / raiseDuration));
                if (rightShoulder != null)
                {
                    rightShoulder.localRotation = Quaternion.Slerp(rightShoulderBase, shoulderTarget, t);
                }

                if (rightUpperArm != null)
                {
                    rightUpperArm.localRotation = Quaternion.Slerp(rightUpperArmBase, upperTarget, t);
                }

                if (rightLowerArm != null)
                {
                    rightLowerArm.localRotation = Quaternion.Slerp(rightLowerArmBase, lowerTarget, t);
                }

                yield return null;
            }

            elapsed = 0f;
            while (elapsed < waveDuration)
            {
                elapsed += Time.deltaTime;
                var normalized = Mathf.Clamp01(elapsed / waveDuration);
                var phase = Mathf.Sin(normalized * Mathf.PI * 7f);
                if (rightShoulder != null)
                {
                    rightShoulder.localRotation = shoulderTarget;
                }

                if (rightUpperArm != null)
                {
                    rightUpperArm.localRotation = upperTarget * Quaternion.Euler(0f, phase * 20f, phase * 8f);
                }

                if (rightLowerArm != null)
                {
                    rightLowerArm.localRotation = lowerTarget * Quaternion.Euler(phase * 20f, 0f, 0f);
                }

                if (rightHand != null)
                {
                    rightHand.localRotation = rightHandBase * Quaternion.Euler(0f, phase * 26f, 0f);
                }

                yield return null;
            }
        }

        private IEnumerator Think()
        {
            const float duration = 1.8f;
            var elapsed = 0f;
            while (elapsed < duration)
            {
                elapsed += Time.deltaTime;
                var t = Mathf.SmoothStep(0f, 1f, Mathf.PingPong(elapsed / 0.9f, 1f));
                if (rightUpperArm != null)
                {
                    rightUpperArm.localRotation = rightUpperArmBase * Quaternion.Euler(Mathf.Lerp(0f, -34f, t), 0f, Mathf.Lerp(0f, -30f, t));
                }

                if (head != null)
                {
                    head.localRotation = headBase * Quaternion.Euler(4f, -8f, 0f);
                }

                yield return null;
            }
        }

        private IEnumerator Explain()
        {
            const float duration = 1.75f;
            var elapsed = 0f;
            while (elapsed < duration)
            {
                elapsed += Time.deltaTime;
                var normalized = Mathf.Clamp01(elapsed / duration);
                var phase = Mathf.Sin(normalized * Mathf.PI * 3f);
                var open = Mathf.SmoothStep(0f, 1f, Mathf.Sin(normalized * Mathf.PI));
                if (leftUpperArm != null)
                {
                    leftUpperArm.localRotation = leftUpperArmBase * Quaternion.Euler(-18f + phase * 5f, -8f, 28f * open);
                }

                if (rightUpperArm != null)
                {
                    rightUpperArm.localRotation = rightUpperArmBase * Quaternion.Euler(-18f - phase * 5f, 8f, -28f * open);
                }

                yield return null;
            }
        }

        private void ApplyEmotion(AvatarEmotion next)
        {
            SetPreset(BlendShapePreset.Neutral, next == AvatarEmotion.neutral ? 1f : 0f);
            SetPreset(BlendShapePreset.Joy, next == AvatarEmotion.joy ? 1f : 0f);
            SetPreset(BlendShapePreset.Angry, next == AvatarEmotion.angry ? 1f : 0f);
            SetPreset(BlendShapePreset.Sorrow, next == AvatarEmotion.sorrow ? 1f : 0f);
            SetPreset(BlendShapePreset.Fun, next == AvatarEmotion.fun ? 1f : 0f);
            SetUnknown("Surprised", next == AvatarEmotion.surprised ? 1f : 0f);
        }

        private void ApplyMouth()
        {
            var level = state == AvatarState.speaking ? Mathf.Clamp01(0.12f + speechLevel) : 0f;
            SetPreset(BlendShapePreset.A, level);
        }

        private void ApplyIdlePose(float breathe)
        {
            if (leftUpperArm != null)
            {
                leftUpperArm.localRotation = leftUpperArmBase * Quaternion.Euler(12f + breathe * 1.5f, 0f, 58f);
            }

            if (rightUpperArm != null)
            {
                rightUpperArm.localRotation = rightUpperArmBase * Quaternion.Euler(12f + breathe * 1.5f, 0f, -58f);
            }

            if (leftLowerArm != null)
            {
                leftLowerArm.localRotation = leftLowerArmBase * Quaternion.Euler(-18f, 0f, -8f);
            }

            if (rightLowerArm != null)
            {
                rightLowerArm.localRotation = rightLowerArmBase * Quaternion.Euler(-18f, 0f, 8f);
            }

            if (leftHand != null)
            {
                leftHand.localRotation = leftHandBase * Quaternion.Euler(0f, 0f, -6f);
            }

            if (rightHand != null)
            {
                rightHand.localRotation = rightHandBase * Quaternion.Euler(0f, 0f, 6f);
            }
        }

        private void SetPreset(BlendShapePreset preset, float value)
        {
            if (blendShapeProxy == null)
            {
                return;
            }

            blendShapeProxy.ImmediatelySetValue(BlendShapeKey.CreateFromPreset(preset), Mathf.Clamp01(value));
        }

        private void SetUnknown(string name, float value)
        {
            if (blendShapeProxy == null)
            {
                return;
            }

            blendShapeProxy.ImmediatelySetValue(BlendShapeKey.CreateUnknown(name), Mathf.Clamp01(value));
        }

        private void CacheBones()
        {
            if (animator == null || !animator.isHuman)
            {
                return;
            }

            head = animator.GetBoneTransform(HumanBodyBones.Head);
            chest = animator.GetBoneTransform(HumanBodyBones.Chest);
            leftShoulder = animator.GetBoneTransform(HumanBodyBones.LeftShoulder);
            rightShoulder = animator.GetBoneTransform(HumanBodyBones.RightShoulder);
            leftUpperArm = animator.GetBoneTransform(HumanBodyBones.LeftUpperArm);
            rightUpperArm = animator.GetBoneTransform(HumanBodyBones.RightUpperArm);
            leftLowerArm = animator.GetBoneTransform(HumanBodyBones.LeftLowerArm);
            rightLowerArm = animator.GetBoneTransform(HumanBodyBones.RightLowerArm);
            leftHand = animator.GetBoneTransform(HumanBodyBones.LeftHand);
            rightHand = animator.GetBoneTransform(HumanBodyBones.RightHand);

            if (head != null)
            {
                headBase = head.localRotation;
            }

            if (leftShoulder != null)
            {
                leftShoulderBase = leftShoulder.localRotation;
            }

            if (rightShoulder != null)
            {
                rightShoulderBase = rightShoulder.localRotation;
            }

            if (leftUpperArm != null)
            {
                leftUpperArmBase = leftUpperArm.localRotation;
            }

            if (rightUpperArm != null)
            {
                rightUpperArmBase = rightUpperArm.localRotation;
            }

            if (leftLowerArm != null)
            {
                leftLowerArmBase = leftLowerArm.localRotation;
            }

            if (rightLowerArm != null)
            {
                rightLowerArmBase = rightLowerArm.localRotation;
            }

            if (leftHand != null)
            {
                leftHandBase = leftHand.localRotation;
            }

            if (rightHand != null)
            {
                rightHandBase = rightHand.localRotation;
            }
        }

        private void ResetBones()
        {
            if (head != null)
            {
                head.localRotation = headBase;
            }

            if (leftShoulder != null)
            {
                leftShoulder.localRotation = leftShoulderBase;
            }

            if (rightShoulder != null)
            {
                rightShoulder.localRotation = rightShoulderBase;
            }

            if (leftUpperArm != null)
            {
                leftUpperArm.localRotation = leftUpperArmBase;
            }

            if (rightUpperArm != null)
            {
                rightUpperArm.localRotation = rightUpperArmBase;
            }

            if (leftLowerArm != null)
            {
                leftLowerArm.localRotation = leftLowerArmBase;
            }

            if (rightLowerArm != null)
            {
                rightLowerArm.localRotation = rightLowerArmBase;
            }

            if (leftHand != null)
            {
                leftHand.localRotation = leftHandBase;
            }

            if (rightHand != null)
            {
                rightHand.localRotation = rightHandBase;
            }
        }
    }
}
