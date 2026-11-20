using System;
using System.Numerics;
using LifeVerse.Shared.Networking;
using LifeVerse.Unity.World;
using NUnit.Framework;
using UnityEngine;

namespace LifeVerse.Unity.Tests.EditMode
{
    public sealed class ObstacleTests
    {
        private static string Create(Guid id, string type, string physics = "{}", float y = 5f) => "{\"operation\":\"create\",\"objectState\":{\"id\":\"" + id + "\",\"primitiveType\":\"" + type + "\",\"physicsJson\":\"" + physics.Replace("\"", "\\\"") + "\",\"positionX\":0,\"positionY\":" + y + ",\"positionZ\":0,\"scaleX\":4,\"scaleY\":10,\"scaleZ\":4,\"revision\":1}}";

        [Test]
        public void SolidObjectsBecomeObstaclesLikeOnTheServer()
        {
            var objects = new GameObject("test").AddComponent<WorldObjects>(); var cube = Guid.NewGuid(); var grass = Guid.NewGuid(); var ghost = Guid.NewGuid(); var gone = Guid.NewGuid();
            Assert.IsTrue(objects.ApplyOperation(Create(cube, "cube"))); Assert.IsTrue(objects.ApplyOperation(Create(grass, "grass"))); Assert.IsTrue(objects.ApplyOperation(Create(ghost, "cube", "{\"collision\":false}"))); Assert.IsTrue(objects.ApplyOperation(Create(gone, "sphere")));
            Assert.AreEqual(2, objects.Obstacles.Count, "the cube and the sphere stop people; grass and a no-collision cube do not");
            objects.ApplyOperation(Create(ghost, "cube", "{}")); Assert.AreEqual(3, objects.Obstacles.Count, "turning collision on makes it an obstacle"); objects.Obstacles.Remove(gone); Assert.AreEqual(2, objects.Obstacles.Count);
            UnityEngine.Object.DestroyImmediate(objects.gameObject);
        }

        [Test]
        public void StandingOnAnObjectAndJumpingLiftsTheAvatar()
        {
            var objects = new GameObject("test").AddComponent<WorldObjects>(); objects.ApplyOperation(Create(Guid.NewGuid(), "cube"));
            var state = new AuthoritativePlayerState { X = 0, Y = 10.5f, Z = 0 }; const float dt = 1f / 60f;
            for (var i = 0; i < 40; i++) AuthoritativeMovement.Step(state, new MovementInput((uint)i, 0, 0, InputFlags.None), dt, null, float.NegativeInfinity, float.PositiveInfinity, objects.Obstacles);
            var rest = state.Y; Assert.AreEqual(10f, rest, .2f, "it rests on top of the cube");
            var peak = rest; for (var i = 0; i < 90; i++) { AuthoritativeMovement.Step(state, new MovementInput((uint)(100 + i), 0, 0, i < 2 ? InputFlags.Jump : InputFlags.None), dt, null, float.NegativeInfinity, float.PositiveInfinity, objects.Obstacles); peak = Math.Max(peak, state.Y); }
            Assert.Greater(peak - rest, 1f, "a jump from the top lifts the avatar as it does on the ground");
            UnityEngine.Object.DestroyImmediate(objects.gameObject);
        }

        [Test]
        public void AWallStopsThePredictedAvatar()
        {
            var objects = new GameObject("test").AddComponent<WorldObjects>(); objects.ApplyOperation(Create(Guid.NewGuid(), "cube", "{}", 5f));
            var state = new AuthoritativePlayerState { X = -8, Y = 0, Z = 0 }; for (var i = 0; i < 400; i++) AuthoritativeMovement.Step(state, new MovementInput((uint)i, 1, 0, InputFlags.Run), 1f / 60f, null, float.NegativeInfinity, float.PositiveInfinity, objects.Obstacles);
            Assert.Less(state.X, 0f, "stopped at the wall instead of walking through: x=" + state.X);
            UnityEngine.Object.DestroyImmediate(objects.gameObject);
        }
    }
}
