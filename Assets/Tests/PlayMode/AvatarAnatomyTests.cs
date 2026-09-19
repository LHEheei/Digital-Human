#if UNITY_EDITOR
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Text;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using Object = UnityEngine.Object;

namespace SrtpAvatar.Tests
{
    public sealed partial class AvatarAnimationTests
    {
        [UnityTest]
        public IEnumerator ReturningToListeningReleasesSpeakingGesture()
        {
            var arm = animator.GetBoneTransform(HumanBodyBones.RightUpperArm);
            for (var frame = 0; frame < 30; frame++) yield return null;
            var resting = arm.localRotation;
            bridge.SetState(AvatarState.speaking);
            bridge.PlayGesture(AvatarGesture.wave);
            for (var frame = 0; frame < 45; frame++) yield return null;
            Assert.Greater(Quaternion.Angle(resting, arm.localRotation), 10f);
            var before = arm.localRotation;
            bridge.SetState(AvatarState.listening);
            Assert.Less(Quaternion.Angle(before, arm.localRotation), 0.01f);
            for (var frame = 0; frame < 54; frame++) yield return null;
            Assert.Less(Quaternion.Angle(resting, arm.localRotation), 5f,
                "A completed speaking turn must release its gesture when listening resumes.");
        }

        [UnityTest]
        public IEnumerator GesturesStayWithinRigJointLimits()
        {
            var directory = Environment.GetEnvironmentVariable("SRTP_ANATOMY_REPORT");
            if (!string.IsNullOrEmpty(directory)) Directory.CreateDirectory(directory);
            var report = new StringBuilder();
            var violations = new List<string>();
            var names = HumanTrait.MuscleName;
            var pose = new HumanPose();
            using (var handler = new HumanPoseHandler(animator.avatar, animator.transform))
            {
                foreach (var label in new[] { "idle", "listening", "thinking", "nod", "wave", "think", "explain" })
                {
                    bridge.PlayGesture(AvatarGesture.none);
                    bridge.SetState(AvatarState.idle);
                    for (var frame = 0; frame < 90; frame++) yield return null;
                    if (Enum.TryParse<AvatarState>(label, out var state)) bridge.SetState(state);
                    else
                    {
                        bridge.SetState(AvatarState.speaking);
                        bridge.PlayGesture((AvatarGesture)Enum.Parse(typeof(AvatarGesture), label));
                    }
                    var arms = new[] {
                        new[] { animator.GetBoneTransform(HumanBodyBones.LeftUpperArm), animator.GetBoneTransform(HumanBodyBones.LeftLowerArm), animator.GetBoneTransform(HumanBodyBones.LeftHand) },
                        new[] { animator.GetBoneTransform(HumanBodyBones.RightUpperArm), animator.GetBoneTransform(HumanBodyBones.RightLowerArm), animator.GetBoneTransform(HumanBodyBones.RightHand) } };
                    var lengths = new float[2, 2];
                    var maximumFlex = new float[2];
                    for (var side = 0; side < 2; side++)
                        for (var segment = 0; segment < 2; segment++)
                            lengths[side, segment] = Vector3.Distance(arms[side][segment].position, arms[side][segment + 1].position);
                    var minimum = new float[names.Length];
                    var maximum = new float[names.Length];
                    for (var i = 0; i < names.Length; i++) { minimum[i] = float.MaxValue; maximum[i] = float.MinValue; }
                    for (var frame = 0; frame < 180; frame++)
                    {
                        yield return null;
                        for (var side = 0; side < 2; side++)
                        {
                            var arm = arms[side];
                            var flex = Vector3.Angle(arm[1].position - arm[0].position, arm[2].position - arm[1].position);
                            maximumFlex[side] = Mathf.Max(maximumFlex[side], flex);
                            for (var segment = 0; segment < 2; segment++)
                                Assert.That(Vector3.Distance(arm[segment].position, arm[segment + 1].position),
                                    Is.EqualTo(lengths[side, segment]).Within(0.0001f), "Animation must preserve limb lengths.");
                        }
                        handler.GetHumanPose(ref pose);
                        for (var i = 0; i < names.Length; i++)
                        {
                            minimum[i] = Mathf.Min(minimum[i], pose.muscles[i]);
                            maximum[i] = Mathf.Max(maximum[i], pose.muscles[i]);
                        }
                        if (frame == (label == "nod" ? 28 : 72))
                        {
                            report.AppendLine($"{label} peak bone positions (root space):");
                            foreach (var boneId in new[] { HumanBodyBones.Chest, HumanBodyBones.Head,
                                HumanBodyBones.RightUpperArm, HumanBodyBones.RightLowerArm, HumanBodyBones.RightHand,
                                HumanBodyBones.RightMiddleProximal, HumanBodyBones.RightMiddleIntermediate })
                            {
                                var bone = animator.GetBoneTransform(boneId);
                                report.AppendLine($"  {boneId}: {bridge.transform.InverseTransformPoint(bone.position):F4} local rotation {bone.localEulerAngles:F2}");
                            }
                            var rightArm = arms[1];
                            var peakFlex = Vector3.Angle(rightArm[1].position - rightArm[0].position, rightArm[2].position - rightArm[1].position);
                            if (label == "explain" && peakFlex < 40f)
                                violations.Add($"Explain pose must bend its leading elbow, measured {peakFlex:F1} degrees.");
                            report.AppendLine($"  Right elbow flexion: {peakFlex:F1} degrees");
                            if (!string.IsNullOrEmpty(directory)) CaptureAnatomyViews(directory, label);
                        }
                    }
                    for (var side = 0; side < 2; side++)
                    {
                        report.AppendLine($"{label} {(side == 0 ? "left" : "right")} maximum elbow flexion: {maximumFlex[side]:F1} degrees");
                        // Conversation gestures stay away from a fully folded elbow.
                        if (maximumFlex[side] > 142f) violations.Add($"{label}: elbow folded to {maximumFlex[side]:F1} degrees");
                    }
                    report.AppendLine($"{label} muscle ranges:");
                    for (var i = 0; i < names.Length; i++)
                    {
                        if (!(names[i].Contains("Arm") || names[i].Contains("Forearm") || names[i].Contains("Hand")
                            || names[i].Contains("Head") || names[i].Contains("Chest") || names[i].Contains("Index")
                            || names[i].Contains("Middle") || names[i].Contains("Shoulder") || names[i].Contains("Thumb")
                            || names[i].Contains("Ring") || names[i].Contains("Little"))) continue;
                        report.AppendLine($"  {names[i]}: {minimum[i]:F3} .. {maximum[i]:F3}");
                        if (minimum[i] < -1.05f || maximum[i] > 1.05f)
                            violations.Add($"{label}: {names[i]} {minimum[i]:F3} .. {maximum[i]:F3}");
                    }
                }
            }
            if (!string.IsNullOrEmpty(directory)) File.WriteAllText(Path.Combine(directory, "joint-ranges.txt"), report.ToString());
            Assert.IsEmpty(violations, string.Join("\n", violations));
        }

        private void CaptureAnatomyViews(string directory, string label)
        {
            var oldPosition = camera.transform.position;
            var oldRotation = camera.transform.rotation;
            var oldSize = camera.orthographicSize;
            var oldTarget = camera.targetTexture;
            var oldActive = RenderTexture.active;
            var target = new RenderTexture(640, 640, 24);
            var image = new Texture2D(640, 640, TextureFormat.RGB24, false);
            try
            {
                camera.targetTexture = target;
                camera.orthographicSize = 0.62f;
                var center = new Vector3(0f, 0.87f, 0f);
                var positions = new[] { new Vector3(0f, 0.87f, -3.2f), new Vector3(-2.3f, 0.87f, -2.3f), new Vector3(-3.2f, 0.87f, 0f) };
                for (var i = 0; i < positions.Length; i++)
                {
                    camera.transform.SetPositionAndRotation(positions[i], Quaternion.LookRotation(center - positions[i]));
                    camera.Render();
                    RenderTexture.active = target;
                    image.ReadPixels(new Rect(0, 0, 640, 640), 0, 0);
                    image.Apply();
                    File.WriteAllBytes(Path.Combine(directory, $"{label}-{i}.png"), image.EncodeToPNG());
                }
            }
            finally
            {
                RenderTexture.active = oldActive;
                camera.targetTexture = oldTarget;
                camera.orthographicSize = oldSize;
                camera.transform.SetPositionAndRotation(oldPosition, oldRotation);
                target.Release();
                Object.Destroy(target);
                Object.Destroy(image);
            }
        }
    }
}
#endif