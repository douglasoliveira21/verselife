using LifeVerse.Shared.Avatar;
using LifeVerse.Shared.Networking;
using LifeVerse.Unity.Audio;
using UnityEngine;

namespace LifeVerse.Unity.Avatar
{
    /// <summary>
    /// The sound of walking and of typing, at the avatar, in 3D (it fades with distance, so the steps and the clicks of people near are heard and the others are not). While the avatar walks or runs on the ground a recording of
    /// sneakers on concrete plays, as one long loop that runs faster when running, each avatar starting at another point so a crowd does not step as one; while it types a message the recording of a keyboard
    /// plays at its hands. Both fade out as soon as the avatar stops. The state comes from the island, so it is the same on everybody's screen.
    /// </summary>
    public sealed class AvatarSounds : MonoBehaviour
    {
        public const float StepsLoud = .35f, TypingLoud = .4f, StepFadePerSecond = 4f, TypingFadePerSecond = 3f;
        public static bool FootstepsEnabled = true, TypingSoundEnabled = true;
        private AvatarView _view; private BusSource _steps, _typing; private bool _stepsWanted, _typingWanted;
        public bool FootstepsPlaying => _steps != null && _steps.Source.isPlaying && _stepsWanted;
        public float StepPitch => _steps != null ? _steps.Source.pitch : 0f;
        public bool TypingPlaying => _typing != null && _typing.Source.isPlaying && _typingWanted;
        public bool StepsWanted => _stepsWanted;
        public bool TypingWanted => _typingWanted;
        public float StepVolume => _steps != null ? _steps.Fade : 0f;
        public float TypingVolume => _typing != null ? _typing.Fade : 0f;

        private void Awake() { _view = GetComponent<AvatarView>(); }

        private void Update()
        {
            if (_view == null) return; var motion = _view.Motion; var shown = _view.Visible && _view.Detail != AvatarDetail.Hidden;
            var walking = motion == NetworkMotionState.Walk || motion == NetworkMotionState.Run;
            Follow(ref _steps, ref _stepsWanted, FootstepsEnabled && walking && shown, SoundLibrary.Footsteps, StepsLoud, 28f, Vector3.zero, StepFadePerSecond);
            if (_steps != null) _steps.Source.pitch = motion == NetworkMotionState.Run ? 1.45f : 1f;
            Follow(ref _typing, ref _typingWanted, TypingSoundEnabled && motion == NetworkMotionState.Typing && shown, SoundLibrary.Typing, TypingLoud, 24f, new Vector3(0f, 1.1f, .25f), TypingFadePerSecond);
        }

        private void Follow(ref BusSource source, ref bool wantedNow, bool wanted, string clipName, float volume, float range, Vector3 offset, float fadePerSecond)
        {
            wantedNow = wanted;
            if (wanted && source == null)
            {
                var clip = SoundLibrary.Get(clipName); if (clip == null) { wantedNow = false; return; }
                var host = new GameObject("Sound_" + clipName); host.transform.SetParent(transform, false); host.transform.localPosition = offset;
                source = BusSource.Make(host, Bus.Effects, volume, true, range, true); source.Source.clip = clip; source.FadeSeconds = 1f / fadePerSecond; source.FadeTarget = 0f; source.Source.time = Random.value * Mathf.Max(0f, clip.length - 1f);   // each avatar starts at another point
            }
            if (source == null) return;
            source.FadeTarget = wanted ? 1f : 0f;
            if (wanted && !source.Source.isPlaying) source.Source.Play();
            if (!wanted && source.Source.isPlaying && source.Fade <= .01f) source.Source.Stop();
        }
    }
}
