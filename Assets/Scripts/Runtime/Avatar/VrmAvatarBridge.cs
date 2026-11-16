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
        private AvatarEmotion activeEmotion;
        private float surpriseTimeRemaining;
        private float blinkWeight;
        private float nextBlinkTime;
        private float blinkElapsed;
        private bool blinking;
        private AvatarGesture activeGesture;
        private bool completeGestureNaturally;
        private float gestureElapsed;
        private float gestureDuration;
        private float listeningWeight;
        private float thinkingWeight;
        private float speakingWeight;
        private float motionTime;
        private float motionPhase;
        private float blinkDuration = 0.2f;
        private bool doubleBlink;
        private Vector2 gazeOffset;
        private Vector2 gazeTarget;
        private float nextGazeTime;
        private Transform[] poseBones;
        private Quaternion[] displayedPose;
        private VRMLookAtHead eyeLook;
        private UpdateType previousLookUpdate;
        private Transform previousLookTarget;
        private FingerJoint[] fingers;

        private sealed class FingerJoint
        {
            public Transform bone;
            public Quaternion rest;
            public Quaternion displayed;
            public float curl;
            public bool right;
        }
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
        private static readonly int EmotionCount = System.Enum.GetValues(typeof(AvatarEmotion)).Length;
        private readonly float[] emotionWeights = new float[EmotionCount];
        private readonly float[] targetEmotionWeights = new float[EmotionCount];

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

            if (lookTarget == null && Camera.main != null)
            {
                lookTarget = Camera.main.transform;
            }
            eyeLook = GetComponentInChildren<VRMLookAtHead>();
            if (eyeLook != null)
            {
                previousLookUpdate = eyeLook.UpdateType;
                previousLookTarget = eyeLook.Target;
            }

            CacheBones();
            CachePose();
            motionPhase = Random.Range(0f, 20f);
            ApplyStatePose();
            for (var i = 0; i < poseBones.Length; i++)
            {
                if (poseBones[i] != null) displayedPose[i] = poseBones[i].localRotation;
            }
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

        private void OnEnable()
        {
            activeGesture = AvatarGesture.none;
            completeGestureNaturally = false;
            ScheduleNextBlink();
            if (eyeLook != null && lookTarget != null)
            {
                eyeLook.Target = lookTarget;
                eyeLook.UpdateType = UpdateType.None;
            }
        }

        private void OnDisable()
        {
            activeGesture = AvatarGesture.none;
            completeGestureNaturally = false;
            speechLevel = smoothedSpeechLevel = 0f;
            blinkWeight = 0f;
            blinking = doubleBlink = false;
            state = AvatarState.idle;
            SetEmotionTargets(AvatarEmotion.neutral);
            System.Array.Copy(targetEmotionWeights, emotionWeights, emotionWeights.Length);
            if (eyeLook != null)
            {
                eyeLook.Target = previousLookTarget;
                eyeLook.UpdateType = previousLookUpdate;
            }
            ApplyMouth();
            ApplyEmotionBlendShapes();
            if (blendShapeProxy != null) blendShapeProxy.Apply();
        }

        private void LateUpdate()
        {
            var dt = Time.deltaTime;
            motionTime += dt;
            var stateBlend = Damp(5f, dt);
            listeningWeight = Mathf.Lerp(listeningWeight, state == AvatarState.listening ? 1f : 0f, stateBlend);
            thinkingWeight = Mathf.Lerp(thinkingWeight, state == AvatarState.thinking ? 1f : 0f, stateBlend);
            speakingWeight = Mathf.Lerp(speakingWeight, state == AvatarState.speaking ? 1f : 0f, stateBlend);

            UpdateGaze(dt);
            ApplyStatePose();
            ApplyGesture(dt);
            SmoothPose(dt);
            ApplyFingers(dt);
            if (eyeLook != null && lookTarget != null) eyeLook.LookWorldPosition();

            UpdateEmotionWeights();
            UpdateBlink();
            ApplyMouth();
            ApplyEmotionBlendShapes();
            if (blendShapeProxy != null) blendShapeProxy.Apply();
        }

        public override void SetState(AvatarState next)
        {
            if (state != next && next != AvatarState.speaking) SetEmotionTargets(AvatarEmotion.neutral);
            if (state != next && next != AvatarState.speaking && activeGesture != AvatarGesture.nod &&
                !(next == AvatarState.idle && completeGestureNaturally))
            {
                // The displayed pose blends back through SmoothPose; do not reset the bones here.
                activeGesture = AvatarGesture.none;
            }
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
            activeGesture = gesture;
            completeGestureNaturally = false;
            gestureElapsed = 0f;
            gestureDuration = gesture switch
            {
                AvatarGesture.nod => 1.15f,
                AvatarGesture.wave => 2.65f,
                AvatarGesture.headtilt => 1.6f,
                AvatarGesture.offer => 2f,
                _ => 2.4f
            };
        }

        public override void FinishGesture()
        {
            completeGestureNaturally = activeGesture != AvatarGesture.none;
        }

        public void BindAudioSource(AudioSource source)
        {
            speechAudio = source;
        }

        private void ApplyGesture(float dt)
        {
            if (activeGesture == AvatarGesture.none) return;
            gestureElapsed += dt;
            var t = Mathf.Clamp01(gestureElapsed / gestureDuration);
            var weight = GestureWeight();
            switch (activeGesture)
            {
                case AvatarGesture.nod:
                    // One clear acknowledgment with a smaller recovery, rather than a repeated oscillation.
                    var down = Mathf.Sin(Mathf.PI * Mathf.Clamp01(t / 0.65f));
                    var recovery = Mathf.Sin(Mathf.PI * Mathf.Clamp01((t - 0.65f) / 0.35f));
                    if (head != null) head.localRotation *= Quaternion.Euler(down * 7f - recovery * 1.8f, 0f, down * 0.5f);
                    break;
                case AvatarGesture.wave:
                    var waveTime = Mathf.Max(0f, gestureElapsed - 0.5f);
                    ApplyWavePose(weight, Mathf.Sin(waveTime * 8.5f) * Mathf.SmoothStep(0f, 1f, waveTime / 0.3f));
                    break;
                case AvatarGesture.think:
                    ApplyThinkPose(weight, Mathf.Sin(motionTime * 1.1f) * 0.12f);
                    break;
                case AvatarGesture.explain:
                    ApplyExplainPose(weight, Mathf.Sin(Mathf.PI * Mathf.Clamp01((t - 0.27f) / 0.45f)) * 0.22f);
                    break;
                case AvatarGesture.headtilt:
                    AddChestGesture(0.45f * weight, 0f, 0.7f * weight);
                    if (head != null) head.localRotation *= Quaternion.Euler(-0.7f * weight, 0.8f * weight, 5.5f * weight);
                    break;
                case AvatarGesture.offer:
                    ApplyOfferPose(weight);
                    break;
            }
            if (gestureElapsed >= gestureDuration)
            {
                activeGesture = AvatarGesture.none;
                completeGestureNaturally = false;
            }
        }

        private float GestureWeight()
        {
            if (activeGesture == AvatarGesture.none) return 0f;
            return Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(gestureElapsed / 0.55f))
                * Mathf.SmoothStep(0f, 1f, Mathf.Clamp01((gestureDuration - gestureElapsed) / 0.7f));
        }

        private void ApplyWavePose(float blend, float wave)
        {
            AddChestGesture(0.45f * blend, -1.2f * blend, 0.8f * blend);

            var right = BodyRight();
            var up = BodyUp();
            var forward = BodyForward();
            var chestPos = ChestPosition();
            var headPos = HeadPosition();
            var elbowTarget = chestPos + right * 0.20f - up * 0.10f + forward * 0.07f;
            var handTarget = headPos + right * 0.20f - up * 0.14f + forward * 0.15f;

            handTarget += right * (wave * 0.028f) + up * (Mathf.Abs(wave) * 0.008f);

            ApplyArmTargets(
                rightUpperArm,
                rightLowerArm,
                rightHand,
                elbowTarget,
                handTarget,
                blend,
                Quaternion.Euler(-4f, wave * 11f, wave * 8f));
        }

        private void ApplyThinkPose(float blend, float pulse)
        {
            AddChestGesture(1.2f * blend, -4.5f * blend, 2f * blend);

            if (head != null)
            {
                head.localRotation *= Quaternion.Euler(3f * blend + pulse * blend, -4f * blend, 2f * blend);
            }

            var right = BodyRight();
            var up = BodyUp();
            var forward = BodyForward();
            var chestPos = ChestPosition();
            var headPos = HeadPosition();
            var elbowTarget = chestPos + right * 0.17f - up * 0.10f + forward * 0.08f;
            var handTarget = headPos + right * 0.09f - up * 0.13f + forward * 0.18f;

            handTarget += right * (pulse * 0.008f);

            ApplyArmTargets(
                rightUpperArm,
                rightLowerArm,
                rightHand,
                elbowTarget,
                handTarget,
                blend,
                Quaternion.Euler(0f, 0f, 6f));
        }

        private void ApplyExplainPose(float open, float beat)
        {
            var blend = Mathf.Clamp01(open);
            AddChestGesture(0.9f * blend + beat * 0.25f, 0f, 0f);

            if (head != null)
            {
                head.localRotation *= Quaternion.Euler(0.8f * blend, beat * blend, 0f);
            }

            var right = BodyRight();
            var up = BodyUp();
            var forward = BodyForward();
            var chestPos = ChestPosition();
            var rightShoulderPos = rightUpperArm == null ? chestPos + right * 0.16f : rightUpperArm.position;
            var leftShoulderPos = leftUpperArm == null ? chestPos - right * 0.16f : leftUpperArm.position;

            var rightElbow = rightShoulderPos + right * 0.08f - up * 0.17f + forward * 0.08f;
            var leftElbow = leftShoulderPos - right * 0.08f - up * 0.17f + forward * 0.08f;
            var rightHandTarget = chestPos + right * (0.11f + blend * 0.11f) - up * 0.02f + forward * (0.26f + beat * 0.018f);
            var leftHandTarget = chestPos - right * (0.11f + blend * 0.11f) - up * 0.02f + forward * (0.26f - beat * 0.018f);

            ApplyArmTargets(
                rightUpperArm,
                rightLowerArm,
                rightHand,
                rightElbow,
                rightHandTarget,
                blend,
                Quaternion.Euler(-8f, 8f, 7f));

            ApplyArmTargets(
                leftUpperArm,
                leftLowerArm,
                leftHand,
                leftElbow,
                leftHandTarget,
                blend * 0.62f,
                Quaternion.Euler(-8f, -8f, -7f));
        }

        private void ApplyOfferPose(float blend)
        {
            if (rightUpperArm == null) return;
            AddChestGesture(0.65f * blend, -0.9f * blend, 0.5f * blend);
            var right = BodyRight();
            var up = BodyUp();
            var forward = BodyForward();
            var elbowTarget = rightUpperArm.position + right * 0.09f - up * 0.21f + forward * 0.055f;
            var handTarget = ChestPosition() + right * 0.21f - up * 0.12f + forward * 0.24f;
            ApplyArmTargets(rightUpperArm, rightLowerArm, rightHand, elbowTarget, handTarget,
                blend, Quaternion.Euler(-6f, 10f, 12f));
        }

        private void AddChestGesture(float chestPitch, float chestYaw, float chestRoll)
        {
            if (chest != null)
            {
                chest.localRotation *= Quaternion.Euler(chestPitch, chestYaw, chestRoll);
            }

        }

        private void ApplyRelaxedArms(float breathe)
        {
            if (leftShoulder != null)
            {
                leftShoulder.localRotation = leftShoulderBase * Quaternion.Euler(0f, 0f, breathe * 0.18f);
            }

            if (rightShoulder != null)
            {
                rightShoulder.localRotation = rightShoulderBase * Quaternion.Euler(0f, 0f, -breathe * 0.15f);
            }

            if (leftUpperArm != null)
            {
                leftUpperArm.localRotation = leftUpperArmBase * Quaternion.Euler(-2.5f + breathe * 0.45f, 0f, 73f);
            }

            if (rightUpperArm != null)
            {
                rightUpperArm.localRotation = rightUpperArmBase * Quaternion.Euler(-2f + breathe * 0.35f, 0f, -71f);
            }

            if (leftLowerArm != null)
            {
                leftLowerArm.localRotation = leftLowerArmBase * Quaternion.Euler(0f, 12f, 0f);
            }

            if (rightLowerArm != null)
            {
                rightLowerArm.localRotation = rightLowerArmBase * Quaternion.Euler(0f, -12f, 0f);
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

            // Solve the two fixed-length segments together. The elbow target is a bend hint,
            // not a separate point to aim at independently of the wrist.
            var shoulder = upperArm.position;
            var upperLength = Vector3.Distance(shoulder, lowerArm.position);
            var lowerLength = Vector3.Distance(lowerArm.position, hand.position);
            var reach = handTarget - shoulder;
            var direction = reach.normalized;
            // These are relaxed conversational-pose bounds, not medical joint limits.
            var minimumReach = Mathf.Sqrt(upperLength * upperLength + lowerLength * lowerLength
                + 2f * upperLength * lowerLength * Mathf.Cos(140f * Mathf.Deg2Rad));
            var distance = Mathf.Clamp(reach.magnitude, minimumReach, (upperLength + lowerLength) * 0.98f);
            var along = (upperLength * upperLength - lowerLength * lowerLength + distance * distance) / (2f * distance);
            var height = Mathf.Sqrt(Mathf.Max(0f, upperLength * upperLength - along * along));
            var bend = Vector3.ProjectOnPlane(elbowTarget - shoulder, direction).normalized;
            var solvedElbow = shoulder + direction * along + bend * height;
            var solvedHand = shoulder + direction * distance;
            AimChildDirection(upperArm, lowerArm, solvedElbow - shoulder);

            // VRM's normalized T-pose uses +/-Y as the elbow hinge. Rotate the upper
            // arm into the bend plane so the forearm flexes instead of swinging sideways.
            var lowerBase = upperArm == rightUpperArm ? rightLowerArmBase : leftLowerArmBase;
            var hinge = upperArm == rightUpperArm ? Vector3.down : Vector3.up;
            var upperDirection = (lowerArm.position - shoulder).normalized;
            var planeNormal = Vector3.Cross(upperDirection, solvedHand - lowerArm.position).normalized;
            var currentHinge = upperArm.TransformDirection(lowerBase * hinge);
            var twist = Vector3.SignedAngle(currentHinge, planeNormal, upperDirection);
            upperArm.rotation = Quaternion.AngleAxis(twist, upperDirection) * upperArm.rotation;
            var flex = Vector3.Angle(upperDirection, solvedHand - lowerArm.position);
            lowerArm.localRotation = lowerBase * Quaternion.AngleAxis(flex, hinge);

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
            var emotion = (AvatarEmotion)index;
            if (emotion == AvatarEmotion.surprised && activeEmotion != emotion) surpriseTimeRemaining = 0.85f;
            activeEmotion = emotion;
            targetEmotionWeights[index] = 1f;
        }

        private void UpdateEmotionWeights()
        {
            if (activeEmotion == AvatarEmotion.surprised && (surpriseTimeRemaining -= Time.deltaTime) <= 0f)
                SetEmotionTargets(AvatarEmotion.neutral);
            var step = Damp(expressionBlendSpeed * 0.55f, Time.deltaTime);
            for (var i = 0; i < emotionWeights.Length; i++)
            {
                emotionWeights[i] = Mathf.Lerp(emotionWeights[i], targetEmotionWeights[i], step);
            }
        }

        private void ApplyEmotionBlendShapes()
        {
            // The rig's full-face presets also move the lips and eyelids.
            var fullFaceScale = (1f - 0.50f * smoothedSpeechLevel) * (1f - 0.40f * blinkWeight);
            SetPreset(BlendShapePreset.Neutral, emotionWeights[(int)AvatarEmotion.neutral]);
            SetPreset(BlendShapePreset.Joy, emotionWeights[(int)AvatarEmotion.joy] * 0.54f * fullFaceScale);
            SetPreset(BlendShapePreset.Angry, emotionWeights[(int)AvatarEmotion.angry] * 0.45f * fullFaceScale);
            SetPreset(BlendShapePreset.Sorrow, emotionWeights[(int)AvatarEmotion.sorrow] * 0.52f * fullFaceScale);
            SetPreset(BlendShapePreset.Fun, emotionWeights[(int)AvatarEmotion.fun] * 0.52f * fullFaceScale);
            SetUnknown("Surprised", emotionWeights[(int)AvatarEmotion.surprised] * 0.53f * fullFaceScale);
            SetUnknown("SoftSmile", emotionWeights[(int)AvatarEmotion.softsmile] * (1f - 0.26f * smoothedSpeechLevel));
            SetUnknown("Concerned", emotionWeights[(int)AvatarEmotion.concerned] * (1f - 0.18f * blinkWeight));
            SetPreset(BlendShapePreset.Blink, blinkWeight);
        }

        private void ApplyMouth()
        {
            var targetLevel = state == AvatarState.speaking ? speechLevel * 0.82f : 0f;
            var speed = mouthBlendSpeed * (targetLevel > smoothedSpeechLevel ? 2.4f : 1.8f);
            smoothedSpeechLevel = Mathf.Lerp(smoothedSpeechLevel, targetLevel, Damp(speed, Time.deltaTime));
            var rounded = 0.5f + 0.5f * Mathf.Sin(motionTime * 6.7f + 0.7f);
            var wide = 0.5f + 0.5f * Mathf.Sin(motionTime * 8.3f);
            SetPreset(BlendShapePreset.A, smoothedSpeechLevel * (0.45f + wide * 0.2f));
            SetPreset(BlendShapePreset.I, smoothedSpeechLevel * wide * 0.16f);
            SetPreset(BlendShapePreset.U, smoothedSpeechLevel * rounded * 0.12f);
            SetPreset(BlendShapePreset.E, smoothedSpeechLevel * (1f - rounded) * 0.12f);
            SetPreset(BlendShapePreset.O, smoothedSpeechLevel * rounded * 0.22f);
        }

        private void ApplyStatePose()
        {
            var phase = motionTime + motionPhase;
            var breathe = Mathf.Sin(phase * 1.35f);
            var sway = Mathf.Sin(phase * 0.53f) * 0.65f + Mathf.Sin(phase * 0.87f) * 0.35f;
            var speechBeat = smoothedSpeechLevel * Mathf.Sin(phase * 2.8f) * speakingWeight;
            if (chest != null)
            {
                chest.localRotation = chestBase * Quaternion.Euler(
                    listeningWeight * 0.9f + breathe * 0.45f + speechBeat * 0.4f,
                    sway * 0.35f, sway * 0.3f);
            }
            ApplyRelaxedArms(breathe);
            if (head != null)
            {
                head.localRotation = headBase * Quaternion.Euler(
                    gazeOffset.y + listeningWeight * 1.6f + breathe * 0.3f + speechBeat * 0.6f,
                    gazeOffset.x + sway * 0.5f, listeningWeight * 0.65f + sway * 0.3f);
            }
            if (activeGesture != AvatarGesture.think && thinkingWeight > 0.001f)
            {
                ApplyThinkPose(thinkingWeight, breathe * 0.12f);
            }
        }

        private void UpdateGaze(float dt)
        {
            if (motionTime >= nextGazeTime)
            {
                gazeTarget = new Vector2(Random.Range(-1.4f, 1.4f), Random.Range(-0.6f, 0.6f));
                nextGazeTime = motionTime + Random.Range(2.4f, 4.8f);
            }
            var target = gazeTarget;
            if (lookTarget != null && head != null)
            {
                var direction = transform.InverseTransformDirection(lookTarget.position - head.position);
                if (direction.z > 0.01f)
                {
                    target.x += Mathf.Clamp(Mathf.Atan2(direction.x, direction.z) * Mathf.Rad2Deg, -14f, 14f) * 0.6f;
                    target.y -= Mathf.Clamp(Mathf.Atan2(direction.y, new Vector2(direction.x, direction.z).magnitude) * Mathf.Rad2Deg, -9f, 9f) * 0.55f;
                }
            }
            gazeOffset = Vector2.Lerp(gazeOffset, target, Damp(2.8f, dt));
        }

        private void SmoothPose(float dt)
        {
            for (var i = 0; i < poseBones.Length; i++)
            {
                var bone = poseBones[i];
                if (bone == null) continue;
                var target = Quaternion.Slerp(displayedPose[i], bone.localRotation, Damp(12f, dt));
                displayedPose[i] = Quaternion.RotateTowards(displayedPose[i], target, (bone == head ? 90f : 160f) * dt);
                bone.localRotation = displayedPose[i];
            }
        }

        private void ApplyFingers(float dt)
        {
            var gestureWeight = GestureWeight();
            foreach (var finger in fingers)
            {
                if (finger.bone == null) continue;
                var curl = 1f;
                if (finger.right && activeGesture == AvatarGesture.wave) curl = Mathf.Lerp(1f, 0.2f, gestureWeight);
                if (finger.right && activeGesture == AvatarGesture.offer) curl = Mathf.Lerp(1f, 0.68f, gestureWeight);
                if (finger.right)
                {
                    var think = activeGesture == AvatarGesture.think ? gestureWeight : thinkingWeight;
                    curl += think * 0.7f;
                }
                var target = finger.rest * Quaternion.Euler(0f, 0f, finger.curl * curl);
                finger.displayed = Quaternion.Slerp(finger.displayed, target, Damp(9f, dt));
                finger.bone.localRotation = finger.displayed;
            }
        }

        private void UpdateBlink()
        {
            if (!blinking && Time.time >= nextBlinkTime)
            {
                blinking = true;
                blinkElapsed = 0f;
                blinkDuration = Random.Range(0.18f, 0.23f);
            }
            if (!blinking) return;
            blinkElapsed += Time.deltaTime;
            var t = Mathf.Clamp01(blinkElapsed / blinkDuration);
            blinkWeight = t < 0.32f ? Mathf.SmoothStep(0f, 1f, t / 0.32f)
                : t < 0.42f ? 1f : 1f - Mathf.SmoothStep(0f, 1f, (t - 0.42f) / 0.58f);
            if (t >= 1f)
            {
                blinking = false;
                blinkWeight = 0f;
                if (!doubleBlink && Random.value < 0.1f)
                {
                    doubleBlink = true;
                    nextBlinkTime = Time.time + 0.16f;
                }
                else
                {
                    doubleBlink = false;
                    ScheduleNextBlink();
                }
            }
        }

        private void ScheduleNextBlink()
        {
            nextBlinkTime = Time.time + Random.Range(blinkIntervalRange.x, blinkIntervalRange.y);
        }

        private static float Damp(float speed, float dt)
        {
            return 1f - Mathf.Exp(-speed * dt);
        }

        private void CachePose()
        {
            poseBones = new[] { head, chest, leftShoulder, rightShoulder, leftUpperArm, rightUpperArm,
                leftLowerArm, rightLowerArm, leftHand, rightHand };
            displayedPose = new Quaternion[poseBones.Length];
            var roots = new[] { HumanBodyBones.LeftIndexProximal, HumanBodyBones.LeftMiddleProximal,
                HumanBodyBones.LeftRingProximal, HumanBodyBones.LeftLittleProximal,
                HumanBodyBones.RightIndexProximal, HumanBodyBones.RightMiddleProximal,
                HumanBodyBones.RightRingProximal, HumanBodyBones.RightLittleProximal };
            fingers = new FingerJoint[roots.Length * 3];
            for (var i = 0; i < roots.Length; i++)
            {
                for (var joint = 0; joint < 3; joint++)
                {
                    var bone = animator != null && animator.isHuman
                        ? animator.GetBoneTransform((HumanBodyBones)((int)roots[i] + joint)) : null;
                    var rest = bone == null ? Quaternion.identity : bone.localRotation;
                    fingers[i * 3 + joint] = new FingerJoint { bone = bone, rest = rest, displayed = rest,
                        right = i >= 4, curl = (i < 4 ? 1f : -1f) * (joint == 1 ? 18f : 9f) * (0.85f + 0.1f * (i % 4)) };
                }
            }
        }

        private void SetPreset(BlendShapePreset preset, float value)
        {
            if (blendShapeProxy == null)
            {
                return;
            }

            blendShapeProxy.AccumulateValue(BlendShapeKey.CreateFromPreset(preset), Mathf.Clamp01(value));
        }

        private void SetUnknown(string name, float value)
        {
            if (blendShapeProxy == null)
            {
                return;
            }

            blendShapeProxy.AccumulateValue(BlendShapeKey.CreateUnknown(name), Mathf.Clamp01(value));
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

    }
}
