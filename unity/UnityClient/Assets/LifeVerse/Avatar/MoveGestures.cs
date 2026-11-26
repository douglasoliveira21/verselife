namespace LifeVerse.Unity.Avatar
{
    /// <summary>
    /// Two shortcuts of the Godot client, kept apart from the input so they can be tested: tapping forward twice quickly (within 0.32 s) runs until forward is let go ("Correr com toque duplo"),
    /// and holding jump for 0.45 s starts or stops flying once per hold ("Segurar pular para voar").
    /// </summary>
    public sealed class MoveGestures
    {
        public const float DoubleTapWindow = .32f, HoldToFlySeconds = .45f;
        public static bool DoubleTapRunEnabled = true, HoldToFlyEnabled = true;

        public bool Running { get; private set; }
        private float _lastTap = -10f, _jumpHeld; private bool _forwardWas, _flightTriggered;

        /// <summary>One frame: whether forward and jump are held now, the clock, and whether a flight toggle may start (not swimming or seated). Returns true when the flight should toggle.</summary>
        public bool Tick(float dt, float now, bool forward, bool jump, bool blocked, bool canFly)
        {
            if (!DoubleTapRunEnabled || blocked) { Running = false; if (blocked) _lastTap = -10f; }
            else
            {
                if (_forwardWas && !forward) Running = false;
                if (forward && !_forwardWas) { Running = now - _lastTap <= DoubleTapWindow; _lastTap = now; }
            }
            _forwardWas = forward && !blocked;
            if (!HoldToFlyEnabled || blocked || !jump) { _jumpHeld = 0f; _flightTriggered = false; return false; }
            if (_flightTriggered || !canFly) return false;
            _jumpHeld += dt; if (_jumpHeld < HoldToFlySeconds) return false;
            _flightTriggered = true; return true;
        }
    }
}
