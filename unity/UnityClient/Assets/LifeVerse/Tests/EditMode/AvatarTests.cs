using LifeVerse.Shared.Networking;
using LifeVerse.Unity.Avatar;
using LifeVerse.Unity.World;
using NUnit.Framework;
using UnityEngine;

namespace LifeVerse.Unity.Tests.EditMode
{
    public sealed class AvatarTests
    {
        private GameObject _root;
        [TearDown] public void Clean() { if (_root != null) Object.DestroyImmediate(_root); }

        private AvatarView Make() { var view = AvatarView.Create(1, "teste", Color.red); _root = view.gameObject; return view; }

        [Test]
        public void ABodyStandsWhereTheGameSaysWithTheZAxisFlipped()
        {
            var view = Make(); view.PlaceAt(new Vector3(3f, 2f, -5f));
            Assert.AreEqual(0f, Vector3.Distance(view.transform.position, GameSpace.ToUnity(3f, 2f, -5f)), 1e-5f);
        }

        [Test]
        public void TheBodyFacesWhereTheServerYawSays()
        {
            var view = Make();
            // yaw 0 is walking toward -z of the game (+z of Unity); yaw +90 degrees is toward +x of the game
            view.ApplySnapshot(new PlayerSnapshot(1, 0, 0, 0, 0, 0, -3f, 0f, NetworkMotionState.Walk, 0)); view.Tick(1f);
            Assert.AreEqual(0f, Mathf.DeltaAngle(0f, view.transform.eulerAngles.y), .5f);
            view.ApplySnapshot(new PlayerSnapshot(1, 0, 0, 0, 3f, 0, 0, Mathf.PI / 2f, NetworkMotionState.Walk, 0)); for (var i = 0; i < 60; i++) view.Tick(.05f);
            var forward = view.transform.forward; Assert.AreEqual(1f, forward.x, .05f); Assert.AreEqual(0f, forward.z, .05f);
        }

        [Test]
        public void WalkingSwingsTheLegsAndStandingStillDoesNot()
        {
            var view = Make(); view.ApplySnapshot(new PlayerSnapshot(1, 0, 0, 0, 0, 0, -3.2f, 0f, NetworkMotionState.Walk, 0));
            var best = 0f; for (var i = 0; i < 60; i++) { view.Tick(.03f); best = Mathf.Max(best, Mathf.Abs(Mathf.DeltaAngle(0f, view.LeftLeg.localEulerAngles.x))); }
            Assert.Greater(best, 10f);
            view.ApplySnapshot(new PlayerSnapshot(1, 0, 0, 0, 0, 0, 0, 0f, NetworkMotionState.Idle, 0)); for (var i = 0; i < 120; i++) view.Tick(.05f);
            Assert.Less(Mathf.Abs(Mathf.DeltaAngle(0f, view.LeftLeg.localEulerAngles.x)), 1f);
        }

        [Test]
        public void SittingBendsTheLegsForwardAndLowersTheBody()
        {
            var view = Make(); view.ApplySnapshot(new PlayerSnapshot(1, 0, 0, 0, 0, 0, 0, 0f, NetworkMotionState.Sit, 0)); for (var i = 0; i < 120; i++) view.Tick(.05f);
            Assert.AreEqual(-85f, Mathf.DeltaAngle(0f, view.LeftLeg.localEulerAngles.x), 1f); Assert.Less(view.Torso.localPosition.y, -.4f);
        }

        [Test]
        public void AGestureLastsItsTimeAndWalkingEndsIt()
        {
            var view = Make(); view.ApplySnapshot(new PlayerSnapshot(1, 0, 0, 0, 0, 0, 0, 0f, NetworkMotionState.Idle, 0));
            view.PlayGesture(0, 1f); Assert.AreEqual(AvatarView.Gesture.Applaud, view.ActiveGesture); view.Tick(.5f); Assert.AreEqual(AvatarView.Gesture.Applaud, view.ActiveGesture);
            view.Tick(.6f); Assert.AreEqual(AvatarView.Gesture.None, view.ActiveGesture);
            view.PlayGesture(2, 5f); view.ApplySnapshot(new PlayerSnapshot(1, 0, 0, 0, 0, 0, -3f, 0f, NetworkMotionState.Walk, 0)); view.Tick(.05f);
            Assert.AreEqual(AvatarView.Gesture.None, view.ActiveGesture);
            view.PlayGesture(99); Assert.AreEqual(AvatarView.Gesture.None, view.ActiveGesture, "um número de gesto que a rede não carrega é ignorado");
        }

        [Test]
        public void HidingTheBodyHidesEveryPartAndShowingBringsThemBack()
        {
            var view = Make(); view.SetVisible(false);
            foreach (var renderer in view.GetComponentsInChildren<Renderer>(true)) Assert.IsFalse(renderer.enabled);
            view.SetVisible(true); Assert.IsTrue(view.Visible);
            foreach (var renderer in view.GetComponentsInChildren<Renderer>(true)) Assert.IsTrue(renderer.enabled);
        }

        [Test]
        public void TheAppearanceIsReadFromTheServerJsonAndBadNumbersAreBroughtIntoRange()
        {
            var a = AvatarAppearance.Parse("{\"displayName\":\"Ana\",\"skinColor\":\"#102030\",\"hairStyle\":\"long\",\"bodySettingsJson\":\"{\\\"height\\\":9,\\\"shoulder\\\":1.05}\",\"equippedItems\":[{\"slot\":\"hat\",\"itemKey\":\"cap_basic\",\"tintColor\":\"#FF0000\"}],\"customModelId\":\"11111111-2222-3333-4444-555555555555\",\"customModelScale\":0.01}");
            Assert.AreEqual("Ana", a.DisplayName); Assert.AreEqual("long", a.HairStyle); Assert.AreEqual(0x10 / 255f, a.Skin.r, .01f);
            Assert.AreEqual(1.15f, a.Height, .001f, "uma altura fora do intervalo volta ao limite"); Assert.AreEqual(1.05f, a.Shoulder, .001f);
            Assert.AreEqual("cap_basic", a.Item("hat", "none")); Assert.AreEqual(1f, a.Tint("hat", Color.black).r, .01f); Assert.IsNotNull(a.CustomModelId);
            var bad = AvatarAppearance.Parse("isto não é json"); Assert.AreEqual("short", bad.HairStyle); Assert.IsNull(bad.CustomModelId);
        }

        [Test]
        public void ADressedBodyHasItsHairHatAndBackpackAndTheHeightSlider()
        {
            var view = Make(); view.ApplyAppearance(AvatarAppearance.Parse("{\"hairStyle\":\"mohawk\",\"beardStyle\":\"full\",\"bodySettingsJson\":\"{\\\"height\\\":1.1}\",\"equippedItems\":[{\"slot\":\"hat\",\"itemKey\":\"cap_basic\"},{\"slot\":\"back\",\"itemKey\":\"backpack_basic\"}]}"));
            Assert.AreEqual(1.1f, view.Torso.localScale.y, .001f); var dressed = view.Dressing; Assert.AreEqual(11, dressed, "cabelo, barba, rosto (olhos, boca, queixo, sobrancelhas), boné e mochila");
            view.ApplyAppearance(AvatarAppearance.Parse("{\"hairStyle\":\"bald\"}")); var plain = view.Dressing; Assert.AreEqual(6, plain, "trocar a aparência tira o que não vale mais: ficam os olhos, a boca, o queixo e as sobrancelhas"); Assert.Less(plain, dressed);
        }

        [Test]
        public void TheClipsAreAskedForByWhatThePersonIsDoing()
        {
            Assert.AreEqual("Walk", DefaultClips.StateOf(NetworkMotionState.Walk, 3f, false)); Assert.AreEqual("FlyIdle", DefaultClips.StateOf(NetworkMotionState.Fly, 0f, false));
            Assert.AreEqual("FlyMove", DefaultClips.StateOf(NetworkMotionState.Fly, 6f, false)); Assert.AreEqual("SwimIdle", DefaultClips.StateOf(NetworkMotionState.Swim, 0f, false));
            Assert.IsNull(DefaultClips.StateOf(NetworkMotionState.Sit, 0f, false), "sentar não tem clipe"); Assert.IsNull(DefaultClips.StateOf(NetworkMotionState.Idle, 0f, true), "um gesto usa a pose procedural");
        }

        [Test]
        public void TheGameCarriesItsClipFilesAndTheSharedReaderOpensThem()
        {
            foreach (var name in new[] { "default_avatar_animations", "typing_animation", "idle_animation", "jump_animation", "jumpdown_animation" }) Assert.IsNotNull(Resources.Load<TextAsset>("Animations/" + name), name);
            var bytes = Resources.Load<TextAsset>("Animations/default_avatar_animations").bytes;
            var walk = LifeVerse.Shared.Avatar.SkeletonClip.Read(bytes, "Walk"); Assert.IsNotNull(walk); Assert.Greater(walk.Duration, .3f);
            var deltas = walk.SampleDeltas(.2f); Assert.IsTrue(deltas.ContainsKey("upper_leg_l") && deltas.ContainsKey("hips"));
        }

        [Test]
        public void TheHeadNeverBendsPastWhatAHeadCan()
        {
            var view = Make(); view.ApplyHeadLook(3f); Assert.AreEqual(LifeVerse.Shared.Networking.HeadLook.MaxPitch, view.HeadPitch, 1e-5f);
            view.ApplyHeadLook(float.NaN); Assert.AreEqual(0f, view.HeadPitch);
        }

        [Test]
        public void ACorrectionFromTheIslandIsGentleAndNeverAppliedTwice()
        {
            var host = new GameObject("jogador"); var local = host.AddComponent<LocalPlayer>();
            try
            {
                // running: the few centimetres a late packet makes are left alone, so the run does not shake
                local.State.VelocityX = 6.4f; local.Reconcile(new PlayerSnapshot(1, .2f, 0, 0, 6.4f, 0, 0, 0f, NetworkMotionState.Run, 7)); Assert.AreEqual(0f, local.State.X, 1e-4f); Assert.AreEqual(0, local.Corrections);
                // a real difference is closed a little at a time, and the old answers that keep arriving do not add up past it
                local.Remember(10, Vector3.zero); local.State.X = 1f;
                for (var i = 0; i < 60; i++) local.Reconcile(new PlayerSnapshot(1, .8f, 0, 0, 6.4f, 0, 0, 0f, NetworkMotionState.Run, 10));
                Assert.Greater(local.State.X, 1.3f); Assert.LessOrEqual(local.State.X, 1.8f);
                // standing still it comes closer to the island, and a place far away is taken at once
                local.State.VelocityX = 0f; local.State.X = 0f; local.Reconcile(new PlayerSnapshot(1, .2f, 0, 0, 0, 0, 0, 0f, NetworkMotionState.Idle, 99)); Assert.Greater(local.State.X, 0f); Assert.Less(local.State.X, .1f);
                local.Reconcile(new PlayerSnapshot(1, 20f, 0, 0, 0, 0, 0, 0f, NetworkMotionState.Idle, 100)); Assert.AreEqual(20f, local.State.X, 1e-3f);
            }
            finally { Object.DestroyImmediate(host); }
        }

        [Test]
        public void ADoubleClickFocusesOnWhatIsThereOrIsHandedToTheGame()
        {
            var host = new GameObject("camera"); var follow = host.AddComponent<FollowCamera>(); follow.Interactive = false;
            try
            {
                var handed = 0; follow.DoubleClicked = _ => handed++; Vector3? thing = new Vector3(0f, 1f, 5f); follow.PickFocus = _ => thing;
                follow.FreeOptions = follow.FreeOptions with { FocusOnDoubleClick = true };
                Assert.IsTrue(follow.DoubleClick(new Ray(Vector3.up, Vector3.forward)), "com uma pessoa ou objeto, a câmera olha para ele"); Assert.IsTrue(follow.ZoomTarget.HasValue); Assert.AreEqual(0, handed);
                thing = null; Assert.IsFalse(follow.DoubleClick(new Ray(Vector3.up, Vector3.down))); Assert.AreEqual(1, handed, "no chão, o jogo é avisado (para andar até lá)");
                thing = Vector3.one; follow.FreeOptions = follow.FreeOptions with { FocusOnDoubleClick = false }; Assert.IsFalse(follow.DoubleClick(new Ray(Vector3.up, Vector3.down))); Assert.AreEqual(2, handed);
                follow.Yaw = 200f; follow.Pitch = -30f; follow.Distance = 12f; follow.ZoomTarget = Vector3.one; follow.ResetBehind(45f, 5f);
                Assert.AreEqual(45f, follow.Yaw); Assert.AreEqual(FollowCamera.DefaultPitch, follow.Pitch); Assert.AreEqual(5f, follow.Distance); Assert.IsFalse(follow.ZoomTarget.HasValue);
                Assert.AreEqual(UnityEngine.InputSystem.Key.R, KeyBindings.DefaultOf(GameAction.CameraReset)); Assert.AreEqual("Redefinir a câmera", KeyBindings.Label(GameAction.CameraReset));
            }
            finally { Object.DestroyImmediate(host); }
        }

        [Test]
        public void TheSkyThePersonChoosesWinsOverTheIslandsAndGoesBack()
        {
            var host = new GameObject("céu"); var sky = host.AddComponent<IslandEnvironment>();
            try
            {
                sky.Hour = 9f; sky.Weather = "clear"; sky.SetLook(new LifeVerse.Shared.Land.ParcelEnvironment(22f, "rain", null, null, null)); sky.FinishBlend();
                sky.LocalHour = 12f; sky.LocalWeather = "fog"; Assert.AreEqual(12f, sky.HourShown); Assert.AreEqual("fog", sky.WeatherShown);
                sky.LocalHour = null; sky.LocalWeather = null; Assert.AreEqual(22f, sky.HourShown, .01f); Assert.AreEqual("rain", sky.WeatherShown);
            }
            finally { Object.DestroyImmediate(host); }
        }

        [Test]
        public void EachKindOfItemHasItsOwnPreview()
        {
            Assert.AreEqual(LifeVerse.Unity.UI.ItemPreviewKind.Package, LifeVerse.Unity.UI.ItemPreviewWindow.KindOf("packages", "application/json"));
            Assert.AreEqual(LifeVerse.Unity.UI.ItemPreviewKind.Animation, LifeVerse.Unity.UI.ItemPreviewWindow.KindOf("animations", "model/gltf-binary"));
            Assert.AreEqual(LifeVerse.Unity.UI.ItemPreviewKind.Model, LifeVerse.Unity.UI.ItemPreviewWindow.KindOf("accessories", "model/gltf-binary"));
            Assert.AreEqual(LifeVerse.Unity.UI.ItemPreviewKind.Texture, LifeVerse.Unity.UI.ItemPreviewWindow.KindOf("textures", ""));
            Assert.AreEqual(LifeVerse.Unity.UI.ItemPreviewKind.Audio, LifeVerse.Unity.UI.ItemPreviewWindow.KindOf("sounds", "audio/ogg"));
            Assert.AreEqual(LifeVerse.Unity.UI.ItemPreviewKind.None, LifeVerse.Unity.UI.ItemPreviewWindow.KindOf("scripts", "text/plain"));
        }

        [Test]
        public void ARemoteBodyFollowsTheSnapshotsAndASnapshotFarAwayMovesItAtOnce()
        {
            var view = Make(); view.ApplySnapshot(new PlayerSnapshot(1, 0, 0, 0, 0, 0, 0, 0f, NetworkMotionState.Idle, 0));
            view.ApplySnapshot(new PlayerSnapshot(1, 1f, 0, 0, 0, 0, 0, 0f, NetworkMotionState.Idle, 0)); for (var i = 0; i < 40; i++) view.Tick(.05f);
            Assert.AreEqual(1f, view.GamePosition.x, .02f);
            view.ApplySnapshot(new PlayerSnapshot(1, 50f, 0, 0, 0, 0, 0, 0f, NetworkMotionState.Idle, 0)); view.Tick(.016f);
            Assert.AreEqual(50f, view.GamePosition.x, .01f);
        }
    }
}
