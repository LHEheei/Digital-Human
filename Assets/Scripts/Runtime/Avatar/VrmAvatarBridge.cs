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
        [SerializeField] private float expressionBlendSpeed = 6f;
        [SerializeField] private float mouthBlendSpeed = 10f;
        [SerializeField] private Vector2 blinkIntervalRange = new Vector2(2.8f, 5.2f);

        private AvatarState state = AvatarState.idle;
        private float speechLevel;
        private float smoothedSpeechLevel;
        private float blinkWeight;
        private float nextBlinkTime;
        private float blinkElapsed;
        private bool blinking;
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
        private Quaternion chestBase;
        private Quaternion leftShoulderBase;
        private Quaternion rightShoulderBase;
        private Quaternion leftUpperArmBase;
        private Quaternion rightUpperArmBase;
        private Quaternion leftLowerArmBase;
        private Quaternion rightLowerArmBase;
        private Quaternion leftHandBase;
        private Quaternion rightHandBase;
        private readonly float[] emotionWeights = new float[6];
        private readonly float[] targetEmotionWeights = new float[6];

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
            SetEmotionTargets(AvatarEmotion.neutral);
            emotionWeights[(int)AvatarEmotion.neutral] = 1f;
            ScheduleNextBlink();
        }

        private void Start()
        {
            if (blendShapeProxy != null)
            {
                blendShapeProxy.Reinitialize();
            }
        }

        private void OnValidate()
        {
            expressionBlendSpeed = Mathf.Max(0.1f, expressionBlendSpeed);
            mouthBlendSpeed = Mathf.Max(0.1f, mouthBlendSpeed);
            blinkIntervalRange.x = Mathf.Max(0.5f, blinkIntervalRange.x);
            blinkIntervalRange.y = Mathf.Max(blinkIntervalRange.x, blinkIntervalRange.y);
        }

        private void LateUpdate()
        {
            var idle = Mathf.Sin(Time.time * 1.2f);
            var breathe = Mathf.Sin(Time.time * 1.5f);
            if (gestureRoutine == null)
            {
                ApplyStatePose(idle, breathe);
            }

            UpdateEmotionWeights();
            UpdateBlink();
            ApplyEmotionBlendShapes();
            ApplyMouth();
        }

        public override void SetState(AvatarState next)
        {
            state = next;
        }

        public override void SetEmotion(AvatarEmotion next)
        {
            SetEmotionTargets(next);
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
                ApplyStatePose(0f, 0f);
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

            yield return ReturnUpperBodyToBase(0.35f);
            gestureRoutine = null;
        }

        private IEnumerator Nod()
        {
            const float duration = 1.55f;
            var elapsed = 0f;
            while (elapsed < duration)
            {
                elapsed += Time.deltaTime;
                var normalized = Mathf.Clamp01(elapsed / duration);
                var phase = Mathf.Sin(EaseInOut(normalized) * Mathf.PI * 4f);
                if (head != null)
                {
                    head.localRotation = headBase * Quaternion.Euler(phase * 9f, 0f, 0f);
                }

                yield return null;
            }
        }

        private IEnumerator Wave()
        {
            const float raiseDuration = 0.55f;
            const float waveDuration = 1.75f;
            var elapsed = 0f;

            while (elapsed < raiseDuration)
            {
                elapsed += Time.deltaTime;
                var t = EaseInOut(Mathf.Clamp01(elapsed / raiseDuration));
                ApplyWavePose(t, 0f);
                yield return null;
            }

            elapsed = 0f;
            while (elapsed < waveDuration)
            {
                elapsed += Time.deltaTime;
                var normalized = Mathf.Clamp01(elapsed / waveDuration);
                var phase = Mathf.Sin(normalized * Mathf.PI * 6f);
                ApplyWavePose(1f, phase);
                yield return null;
            }
        }

        private IEnumerator Think()
        {
            const float duration = 2.05f;
            var elapsed = 0f;
            while (elapsed < duration)
            {
                elapsed += Time.deltaTime;
                var normalized = Mathf.Clamp01(elapsed / duration);
                var enter = EaseInOut(Mathf.Clamp01(elapsed / 0.55f));
                var pulse = Mathf.Sin(normalized * Mathf.PI * 2f) * 0.35f;
                ApplyThinkPose(enter, pulse);
                yield return null;
            }
        }

        private IEnumerator Explain()
        {
            const float duration = 2.15f;
            var elapsed = 0f;
            while (elapsed < duration)
            {
                elapsed += Time.deltaTime;
                var normalized = Mathf.Clamp01(elapsed / duration);
                var open = EaseInOut(Mathf.Clamp01(Mathf.Sin(normalized * Mathf.PI)));
                var beat = Mathf.Sin(normalized * Mathf.PI * 4f) * 0.35f;
                ApplyExplainPose(open, beat);

                yield return null;
            }
        }

        private void ApplyWavePose(float blend, float wave)
        {
            ResetUpperBodyForGesture(0.45f * blend, -1.2f * blend, 0.8f * blend);

            var right = BodyRight();
            var up = BodyUp();
            var forward = BodyForward();
            var chestPos = ChestPosition();
            var headPos = HeadPosition();
            var elbowTarget = chestPos + right * 0.18f - up * 0.17f + forward * 0.09f;
            var handTarget = Vector3.Lerp(
                chestPos + right * 0.23f + up * 0.06f + forward * 0.10f,
                headPos + right * 0.16f - up * 0.07f + forward * 0.08f,
                0.65f);

            handTarget += right * (wave * 0.035f) + up * (Mathf.Abs(wave) * 0.01f);

            ApplyArmTargets(
                rightUpperArm,
                rightLowerArm,
                rightHand,
                rightUpperArmBase,
                rightLowerArmBase,
                rightHandBase,
                elbowTarget,
                handTarget,
                blend,
                Quaternion.Euler(-4f, wave * 14f, wave * 10f));
        }

        private void ApplyThinkPose(float blend, float pulse)
        {
            ResetUpperBodyForGesture(1.2f * blend, -4.5f * blend, 2f * blend);

            if (head != null)
            {
                head.localRotation = headBase * Quaternion.Euler(4.5f * blend + pulse, -7f * blend, 2.5f * blend);
            }

            var right = BodyRight();
            var up = BodyUp();
            var forward = BodyForward();
            var chestPos = ChestPosition();
            var headPos = HeadPosition();
            var elbowTarget = chestPos + right * 0.15f - up * 0.18f + forward * 0.05f;
            var handTarget = Vector3.Lerp(
                chestPos + right * 0.11f - up * 0.02f + forward * 0.08f,
                headPos + right * 0.10f - up * 0.18f + forward * 0.04f,
                0.55f);

            handTarget += right * (pulse * 0.008f);

            ApplyArmTargets(
                rightUpperArm,
                rightLowerArm,
                rightHand,
                rightUpperArmBase,
                rightLowerArmBase,
                rightHandBase,
                elbowTarget,
                handTarget,
                blend,
                Quaternion.Euler(0f, 0f, 6f));
        }

        private void ApplyExplainPose(float open, float beat)
        {
            var blend = Mathf.Clamp01(open);
            ResetUpperBodyForGesture(0.9f * blend + beat * 0.25f, 0f, 0f);

            if (head != null)
            {
                head.localRotation = headBase * Quaternion.Euler(1.1f * blend, beat * 1.2f, 0f);
            }

            var right = BodyRight();
            var up = BodyUp();
            var forward = BodyForward();
            var chestPos = ChestPosition();
            var rightShoulderPos = rightUpperArm == null ? chestPos + right * 0.16f : rightUpperArm.position;
            var leftShoulderPos = leftUpperArm == null ? chestPos - right * 0.16f : leftUpperArm.position;

            var rightElbow = rightShoulderPos + right * 0.08f - up * 0.17f + forward * 0.08f;
            var leftElbow = leftShoulderPos - right * 0.08f - up * 0.17f + forward * 0.08f;
            var rightHandTarget = chestPos + right * (0.11f + blend * 0.11f) - up * 0.15f + forward * (0.12f + beat * 0.018f);
            var leftHandTarget = chestPos - right * (0.11f + blend * 0.11f) - up * 0.15f + forward * (0.12f - beat * 0.018f);

            ApplyArmTargets(
                rightUpperArm,
                rightLowerArm,
                rightHand,
                rightUpperArmBase,
                rightLowerArmBase,
                rightHandBase,
                rightElbow,
                rightHandTarget,
                blend,
                Quaternion.Euler(-8f, 8f, 7f));

            ApplyArmTargets(
                leftUpperArm,
                leftLowerArm,
                leftHand,
                leftUpperArmBase,
                leftLowerArmBase,
                leftHandBase,
                leftElbow,
                leftHandTarget,
                blend,
                Quaternion.Euler(-8f, -8f, -7f));
        }

        private void ResetUpperBodyForGesture(float chestPitch, float chestYaw, float chestRoll)
        {
            if (chest != null)
            {
                chest.localRotation = chestBase * Quaternion.Euler(chestPitch, chestYaw, chestRoll);
            }

            ApplyRelaxedArms(0f);
        }

        private void ApplyRelaxedArms(float breathe)
        {
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
                leftUpperArm.localRotation = leftUpperArmBase * Quaternion.Euler(8f + breathe * 0.8f, 0f, 72f);
            }

            if (rightUpperArm != null)
            {
                rightUpperArm.localRotation = rightUpperArmBase * Quaternion.Euler(8f + breathe * 0.8f, 0f, -72f);
            }

            if (leftLowerArm != null)
            {
                leftLowerArm.localRotation = leftLowerArmBase * Quaternion.Euler(-8f, 0f, -4f);
            }

            if (rightLowerArm != null)
            {
                rightLowerArm.localRotation = rightLowerArmBase * Quaternion.Euler(-8f, 0f, 4f);
            }

            if (leftHand != null)
            {
                leftHand.localRotation = leftHandBase * Quaternion.Euler(0f, 0f, -5f);
            }

            if (rightHand != null)
            {
                rightHand.localRotation = rightHandBase * Quaternion.Euler(0f, 0f, 5f);
            }
        }

        private void ApplyArmTargets(
            Transform upperArm,
            Transform lowerArm,
            Transform hand,
            Quaternion upperBase,
            Quaternion lowerBase,
            Quaternion handBase,
            Vector3 elbowTarget,
            Vector3 handTarget,
            float blend,
            Quaternion handOffset)
        {
            if (upperArm == null || lowerArm == null || hand == null)
            {
                return;
            }

            blend = Mathf.Clamp01(blend);
            var upperStart = upperArm.localRotation;
            var lowerStart = lowerArm.localRotation;
            var handStart = hand.localRotation;

            AimChildDirection(upperArm, lowerArm, elbowTarget - upperArm.position);
            AimChildDirection(lowerArm, hand, handTarget - lowerArm.position);

            var targetUpper = upperArm.localRotation;
            var targetLower = lowerArm.localRotation;
            var targetHand = handStart * handOffset;

            upperArm.localRotation = Quaternion.Slerp(upperStart, targetUpper, blend);
            lowerArm.localRotation = Quaternion.Slerp(lowerStart, targetLower, blend);
            hand.localRotation = Quaternion.Slerp(handStart, targetHand, blend);
        }

        private static void AimChildDirection(Transform bone, Transform child, Vector3 targetDirection)
        {
            if (bone == null || child == null || targetDirection.sqrMagnitude < 0.000001f)
            {
                return;
            }

            var currentDirection = child.position - bone.position;
            if (currentDirection.sqrMagnitude < 0.000001f)
            {
                return;
            }

            var delta = Quaternion.FromToRotation(currentDirection.normalized, targetDirection.normalized);
            bone.rotation = delta * bone.rotation;
        }

        private Vector3 BodyRight()
        {
            var right = Vector3.ProjectOnPlane(transform.right, Vector3.up);
            if (right.sqrMagnitude > 0.0001f)
            {
                return right.normalized;
            }

            return chest == null ? Vector3.right : chest.right.normalized;
        }

        private Vector3 BodyUp()
        {
            return transform.up.sqrMagnitude < 0.0001f ? Vector3.up : transform.up.normalized;
        }

        private Vector3 BodyForward()
        {
            var forward = Vector3.ProjectOnPlane(transform.forward, Vector3.up);
            if (forward.sqrMagnitude > 0.0001f)
            {
                return forward.normalized;
            }

            return chest == null ? Vector3.forward : chest.forward.normalized;
        }

        private Vector3 HeadPosition()
        {
            return head == null ? ChestPosition() + BodyUp() * 0.26f : head.position;
        }

        private Vector3 ChestPosition()
        {
            return chest == null ? transform.position + BodyUp() * 1.2f : chest.position;
        }

        private void SetEmotionTargets(AvatarEmotion next)
        {
            for (var i = 0; i < targetEmotionWeights.Length; i++)
            {
                targetEmotionWeights[i] = 0f;
            }

            var index = (int)next;
            if (index < 0 || index >= targetEmotionWeights.Length)
            {
                index = (int)AvatarEmotion.neutral;
            }

            targetEmotionWeights[index] = 1f;
        }

        private void UpdateEmotionWeights()
        {
            var step = expressionBlendSpeed * Time.deltaTime;
            for (var i = 0; i < emotionWeights.Length; i++)
            {
                emotionWeights[i] = Mathf.MoveTowards(emotionWeights[i], targetEmotionWeights[i], step);
            }
        }

        private void ApplyEmotionBlendShapes()
        {
            SetPreset(BlendShapePreset.Neutral, emotionWeights[(int)AvatarEmotion.neutral]);
            SetPreset(BlendShapePreset.Joy, emotionWeights[(int)AvatarEmotion.joy]);
            SetPreset(BlendShapePreset.Angry, emotionWeights[(int)AvatarEmotion.angry] * 0.9f);
            SetPreset(BlendShapePreset.Sorrow, emotionWeights[(int)AvatarEmotion.sorrow] * 0.9f);
            SetPreset(BlendShapePreset.Fun, emotionWeights[(int)AvatarEmotion.fun]);
            SetUnknown("Surprised", emotionWeights[(int)AvatarEmotion.surprised]);
            SetPreset(BlendShapePreset.Blink, blinkWeight);
        }

        private void ApplyMouth()
        {
            var targetLevel = state == AvatarState.speaking ? Mathf.Clamp01(0.08f + speechLevel * 0.82f) : 0f;
            smoothedSpeechLevel = Mathf.MoveTowards(smoothedSpeechLevel, targetLevel, mouthBlendSpeed * Time.deltaTime);
            SetPreset(BlendShapePreset.A, smoothedSpeechLevel);
        }

        private void ApplyStatePose(float idle, float breathe)
        {
            if (state == AvatarState.thinking)
            {
                var pulse = Mathf.Sin(Time.time * 1.8f) * 0.18f;
                ApplyThinkPose(0.48f, pulse);
                return;
            }

            ApplyHeadMotion(idle);
            ApplyIdlePose(breathe);
        }

        private void ApplyIdlePose(float breathe)
        {
            var listening = state == AvatarState.listening ? 1f : 0f;
            var speaking = state == AvatarState.speaking ? 1f : 0f;

            if (chest != null)
            {
                var speechBeat = Mathf.Sin(Time.time * 3.2f) * speaking;
                chest.localRotation = chestBase * Quaternion.Euler(1.3f * listening + breathe * 0.8f + speechBeat * 0.45f, 0f, breathe * 0.35f);
            }

            if (leftUpperArm != null)
            {
                leftUpperArm.localRotation = leftUpperArmBase * Quaternion.Euler(8f + breathe * 0.8f, 0f, 72f);
            }

            if (rightUpperArm != null)
            {
                rightUpperArm.localRotation = rightUpperArmBase * Quaternion.Euler(8f + breathe * 0.8f, 0f, -72f);
            }

            if (leftLowerArm != null)
            {
                leftLowerArm.localRotation = leftLowerArmBase * Quaternion.Euler(-8f, 0f, -4f);
            }

            if (rightLowerArm != null)
            {
                rightLowerArm.localRotation = rightLowerArmBase * Quaternion.Euler(-8f, 0f, 4f);
            }

            if (leftHand != null)
            {
                leftHand.localRotation = leftHandBase * Quaternion.Euler(0f, 0f, -5f);
            }

            if (rightHand != null)
            {
                rightHand.localRotation = rightHandBase * Quaternion.Euler(0f, 0f, 5f);
            }
        }

        private void ApplyHeadMotion(float idle)
        {
            if (head == null)
            {
                return;
            }

            var listenPitch = state == AvatarState.listening ? 3.8f : 0f;
            var speakingPitch = state == AvatarState.speaking ? 1.2f : 0f;
            var speechBeat = state == AvatarState.speaking ? Mathf.Sin(Time.time * 3.7f) * 0.8f : 0f;
            head.localRotation = headBase * Quaternion.Euler(listenPitch + speakingPitch + idle * 0.9f + speechBeat, idle * 0.75f, 0f);
        }

        private void UpdateBlink()
        {
            if (!blinking && Time.time >= nextBlinkTime)
            {
                blinking = true;
                blinkElapsed = 0f;
            }

            if (!blinking)
            {
                blinkWeight = Mathf.MoveTowards(blinkWeight, 0f, Time.deltaTime * 8f);
                return;
            }

            blinkElapsed += Time.deltaTime;
            const float duration = 0.18f;
            var normalized = Mathf.Clamp01(blinkElapsed / duration);
            blinkWeight = Mathf.Sin(normalized * Mathf.PI);
            if (normalized >= 1f)
            {
                blinking = false;
                blinkWeight = 0f;
                ScheduleNextBlink();
            }
        }

        private void ScheduleNextBlink()
        {
            nextBlinkTime = Time.time + Random.Range(blinkIntervalRange.x, blinkIntervalRange.y);
        }

        private IEnumerator ReturnUpperBodyToBase(float duration)
        {
            var elapsed = 0f;
            var headStart = head == null ? Quaternion.identity : head.localRotation;
            var chestStart = chest == null ? Quaternion.identity : chest.localRotation;
            var leftShoulderStart = leftShoulder == null ? Quaternion.identity : leftShoulder.localRotation;
            var rightShoulderStart = rightShoulder == null ? Quaternion.identity : rightShoulder.localRotation;
            var leftUpperStart = leftUpperArm == null ? Quaternion.identity : leftUpperArm.localRotation;
            var rightUpperStart = rightUpperArm == null ? Quaternion.identity : rightUpperArm.localRotation;
            var leftLowerStart = leftLowerArm == null ? Quaternion.identity : leftLowerArm.localRotation;
            var rightLowerStart = rightLowerArm == null ? Quaternion.identity : rightLowerArm.localRotation;
            var leftHandStart = leftHand == null ? Quaternion.identity : leftHand.localRotation;
            var rightHandStart = rightHand == null ? Quaternion.identity : rightHand.localRotation;
            var leftUpperTarget = leftUpperArmBase * Quaternion.Euler(8f, 0f, 72f);
            var rightUpperTarget = rightUpperArmBase * Quaternion.Euler(8f, 0f, -72f);
            var leftLowerTarget = leftLowerArmBase * Quaternion.Euler(-8f, 0f, -4f);
            var rightLowerTarget = rightLowerArmBase * Quaternion.Euler(-8f, 0f, 4f);
            var leftHandTarget = leftHandBase * Quaternion.Euler(0f, 0f, -5f);
            var rightHandTarget = rightHandBase * Quaternion.Euler(0f, 0f, 5f);

            while (elapsed < duration)
            {
                elapsed += Time.deltaTime;
                var t = EaseInOut(Mathf.Clamp01(elapsed / duration));
                SlerpBone(head, headStart, headBase, t);
                SlerpBone(chest, chestStart, chestBase, t);
                SlerpBone(leftShoulder, leftShoulderStart, leftShoulderBase, t);
                SlerpBone(rightShoulder, rightShoulderStart, rightShoulderBase, t);
                SlerpBone(leftUpperArm, leftUpperStart, leftUpperTarget, t);
                SlerpBone(rightUpperArm, rightUpperStart, rightUpperTarget, t);
                SlerpBone(leftLowerArm, leftLowerStart, leftLowerTarget, t);
                SlerpBone(rightLowerArm, rightLowerStart, rightLowerTarget, t);
                SlerpBone(leftHand, leftHandStart, leftHandTarget, t);
                SlerpBone(rightHand, rightHandStart, rightHandTarget, t);
                yield return null;
            }
        }

        private static void SlerpBone(Transform bone, Quaternion from, Quaternion to, float t)
        {
            if (bone != null)
            {
                bone.localRotation = Quaternion.Slerp(from, to, t);
            }
        }

        private static float EaseInOut(float t)
        {
            return Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(t));
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

            if (chest != null)
            {
                chestBase = chest.localRotation;
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

            if (chest != null)
            {
                chest.localRotation = chestBase;
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
