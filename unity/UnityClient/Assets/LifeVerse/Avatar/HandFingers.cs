using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using LifeVerse.Shared.Networking;
using UnityEngine;

namespace LifeVerse.Unity.Avatar
{
    public enum Finger { Thumb, Index, Middle, Ring, Pinky }

    /// <summary>How much of a fist each finger of a hand makes: 0 is straight, 1 is closed.</summary>
    public readonly struct HandShape
    {
        public readonly float Thumb, Index, Middle, Ring, Pinky;
        public HandShape(float thumb, float index, float middle, float ring, float pinky) { Thumb = thumb; Index = index; Middle = middle; Ring = ring; Pinky = pinky; }
        public float Of(Finger f) => f == Finger.Thumb ? Thumb : f == Finger.Index ? Index : f == Finger.Middle ? Middle : f == Finger.Ring ? Ring : Pinky;
        public static readonly HandShape Relaxed = new HandShape(.2f, .22f, .28f, .34f, .4f), Open = new HandShape(.05f, .06f, .06f, .06f, .08f), Typing = new HandShape(.18f, .42f, .44f, .44f, .46f), Loose = new HandShape(.35f, .5f, .55f, .6f, .65f), Grip = new HandShape(.55f, .6f, .62f, .64f, .66f), Pointing = new HandShape(.6f, .02f, .95f, .95f, .95f);

        /// <summary>The shape a hand takes now: closed on what it holds, a fist with one finger out to point, open to wave, curled to type, loose when running, relaxed otherwise.</summary>
        public static HandShape For(NetworkMotionState motion, AvatarView.Gesture gesture, char side, bool holds)
        {
            if (gesture == AvatarView.Gesture.Point && side == 'r') return Pointing; if (holds) return Grip;
            if (motion == NetworkMotionState.Wave || motion == NetworkMotionState.Dance || gesture == AvatarView.Gesture.Applaud || gesture == AvatarView.Gesture.Greet || motion == NetworkMotionState.Swim) return Open;
            if (motion == NetworkMotionState.Typing) return Typing; if (motion == NetworkMotionState.Run) return Loose; return Relaxed;
        }
    }

    /// <summary>
    /// The fingers of a custom body. The game's animations move the arms and the hands, but a body whose hands have finger bones would keep them straight as boards, so after the animation has posed the body the fingers are curled toward
    /// the palm, by the shape of the hand, blending from one shape to the next. Bodies without finger bones are left as they are. Finger bones are found by name (Unreal style "index_01_l" and Mixamo style "LeftHandIndex1").
    /// </summary>
    public sealed class HandFingers
    {
        /// <summary>The angles, in degrees, that each of the three joints of a finger turns at a full fist.</summary>
        private static readonly float[][] FistDegrees = { new[] { 28f, 38f, 48f }, new[] { 82f, 100f, 68f }, new[] { 84f, 104f, 70f }, new[] { 84f, 104f, 70f }, new[] { 80f, 100f, 68f } };
        private static readonly Regex Unreal = new Regex(@"^(thumb|index|middle|ring|pinky)_?0?([1-3])_?([lr])$", RegexOptions.IgnoreCase | RegexOptions.Compiled);
        private static readonly Regex Mixamo = new Regex(@"(left|right)hand(thumb|index|middle|ring|pinky)([1-3])$", RegexOptions.IgnoreCase | RegexOptions.Compiled);

        private readonly Dictionary<(char, Finger, int), Transform> _bones = new Dictionary<(char, Finger, int), Transform>(); private readonly Dictionary<char, Vector3> _palmLocal = new Dictionary<char, Vector3>(); private readonly Dictionary<char, Transform> _hands = new Dictionary<char, Transform>();
        private readonly Dictionary<(char, Finger), float> _shown = new Dictionary<(char, Finger), float>(); private readonly Dictionary<Transform, Quaternion> _rest = new Dictionary<Transform, Quaternion>();
        public bool HasFingers => _bones.Count > 0 && _palmLocal.Count > 0;
        public int BoneCount => _bones.Count;

        public static (char Side, Finger Finger, int Segment)? Parse(string name)
        {
            var m = Unreal.Match(name); if (m.Success) return (char.ToLowerInvariant(m.Groups[3].Value[0]), (Finger)Enum.Parse(typeof(Finger), m.Groups[1].Value, true), int.Parse(m.Groups[2].Value));
            m = Mixamo.Match(name); if (m.Success) return (char.ToLowerInvariant(m.Groups[1].Value[0]), (Finger)Enum.Parse(typeof(Finger), m.Groups[2].Value, true), int.Parse(m.Groups[3].Value));
            return null;
        }

        /// <summary>Finds the finger bones and works out, for each hand, which way its palm faces (in the frame of the hand, so it holds whatever the arm does). Call with the body at rest.</summary>
        public void Prepare(IEnumerable<Transform> all, Func<char, Transform> handOf)
        {
            _bones.Clear(); _palmLocal.Clear(); _shown.Clear(); _hands.Clear(); _rest.Clear();
            foreach (var t in all) if (Parse(t.name) is { } key && !_bones.ContainsKey((key.Side, key.Finger, key.Segment))) _bones[(key.Side, key.Finger, key.Segment)] = t;
            foreach (var bone in _bones.Values) _rest[bone] = bone.localRotation;
            foreach (var side in new[] { 'l', 'r' })
            {
                var hand = handOf(side); if (hand == null || !_bones.TryGetValue((side, Finger.Index, 1), out var index) || !_bones.TryGetValue((side, Finger.Pinky, 1), out var pinky) || !_bones.TryGetValue((side, Finger.Middle, 1), out var middle)) continue;
                var across = index.position - pinky.position; var along = middle.position - hand.position; var palm = Vector3.Cross(across, along); if (palm.sqrMagnitude < 1e-12f) continue; palm.Normalize();
                // in the pose the model is made in (arms out, palms down) the palm looks down; with the arms down along the body it looks at the body
                var outward = Mathf.Sign(Mathf.Abs(hand.position.x) < 1e-4f ? 1f : hand.position.x);
                if (Mathf.Abs(palm.y) > .35f ? palm.y > 0f : palm.x * outward > 0f) palm = -palm;
                _hands[side] = hand; _palmLocal[side] = Quaternion.Inverse(hand.rotation) * palm;
            }
        }

        public float CurlOf(char side, Finger finger) => _shown.TryGetValue((side, finger), out var v) ? v : 0f;

        /// <summary>How bent a finger is, in degrees: the angle between its first and its last segment.</summary>
        public float BendOf(char side, Finger finger)
        {
            if (!_bones.TryGetValue((side, finger, 1), out var a) || !_bones.TryGetValue((side, finger, 2), out var b) || !_bones.TryGetValue((side, finger, 3), out var c)) return 0f;
            return Vector3.Angle(b.position - a.position, c.position - b.position);
        }

        /// <summary>After the animation: curls every finger toward the palm by the current shape of its hand.</summary>
        public void Apply(Func<char, HandShape> shapeOf, float delta)
        {
            if (!HasFingers) return; var blend = 1f - Mathf.Exp(-12f * Mathf.Max(delta, 1f / 240f));
            foreach (var pair in _rest) if (pair.Key != null) pair.Key.localRotation = pair.Value;   // the animations do not move fingers: every frame starts from the rest shape, then curls
            foreach (var pair in _palmLocal)
            {
                var side = pair.Key; var hand = _hands[side]; var shape = shapeOf(side); var palm = (hand.rotation * pair.Value).normalized;
                foreach (Finger finger in Enum.GetValues(typeof(Finger)))
                {
                    var target = shape.Of(finger); var shown = _shown.TryGetValue((side, finger), out var s) ? s : target;
                    shown = _shown[(side, finger)] = Mathf.Abs(shown - target) < .004f ? target : Mathf.Lerp(shown, target, blend); if (shown < .002f) continue;
                    for (var segment = 1; segment <= 3; segment++)
                    {
                        if (!_bones.TryGetValue((side, finger, segment), out var bone)) continue; var here = bone.position; Vector3 direction;
                        if (_bones.TryGetValue((side, finger, segment + 1), out var next)) direction = next.position - here; else if (_bones.TryGetValue((side, finger, segment - 1), out var before)) direction = here - before.position; else direction = bone.up;
                        if (direction.sqrMagnitude < 1e-10f) continue; direction.Normalize(); var inPlane = palm - direction * Vector3.Dot(palm, direction); if (inPlane.sqrMagnitude < 1e-8f) continue;
                        var axis = Vector3.Cross(direction, inPlane.normalized).normalized; bone.rotation = Quaternion.AngleAxis(FistDegrees[(int)finger][segment - 1] * shown, axis) * bone.rotation;
                    }
                }
            }
        }
    }
}
