using UnityEngine;

namespace LifeVerse.Unity.Audio
{
    /// <summary>"Testar microfone": reads the microphone only while the person asked for the test, and reports how loud it is. Nothing is recorded or sent anywhere, and the microphone stops when the test stops.</summary>
    public sealed class MicTest : MonoBehaviour
    {
        private AudioClip _clip; private string _device; private int _readAt;
        public bool Active { get; private set; }
        /// <summary>Smoothed loudness 0 to 1 (on the scale the open-microphone gate uses) and the loudest recent sample.</summary>
        public float Level { get; private set; }
        public float Peak { get; private set; }
        public string Problem { get; private set; } = "";

        public bool Begin()
        {
            Problem = ""; if (Active) return true;
            if (MicDevices.Names().Length == 0) { Problem = "Nenhum microfone foi encontrado."; return false; }
            _device = MicDevices.Pick(); _clip = Microphone.Start(_device, true, 1, 16000); if (_clip == null) { Problem = "Não foi possível abrir o microfone."; return false; }
            _readAt = 0; Active = true; return true;
        }

        public void End() { if (!Active) return; Microphone.End(_device); _clip = null; Active = false; Level = Peak = 0f; }

        /// <summary>One reading (loudness and the loudest sample of a batch). Tests feed readings directly.</summary>
        public void Feed(float rms, float peak) { Level = Mathf.Max(Level, Mathf.Clamp01(rms)); Peak = Mathf.Max(Peak, Mathf.Clamp01(peak)); }

        private void Update()
        {
            Level = Mathf.MoveTowards(Level, 0f, Time.unscaledDeltaTime * 2.5f); Peak = Mathf.MoveTowards(Peak, 0f, Time.unscaledDeltaTime * 2f);
            if (!Active || _clip == null) return; var at = Microphone.GetPosition(_device); if (at < 0 || at == _readAt) return;
            var count = at > _readAt ? at - _readAt : _clip.samples - _readAt + at; if (count <= 0) return; var data = new float[count]; _clip.GetData(data, _readAt); _readAt = at;
            double sum = 0; var peak = 0f; foreach (var s in data) { sum += s * s; peak = Mathf.Max(peak, Mathf.Abs(s)); }
            Feed(Mathf.Sqrt((float)(sum / data.Length)) * 8f, peak);   // the gate's scale: a speaking voice is around 0.1
        }

        private void OnDestroy() => End();
    }
}
