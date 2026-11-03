using Godot;
using LifeVerse.Shared.Voice;

namespace LifeVerse.Client.Scripts;

/// The dot above a name that says "voice is on", with sound waves around it while the avatar speaks. The waves are
/// green while the sound is clean and turn red and wider when it is too loud. Always faces the camera.
public partial class VoiceIndicator : Node3D
{
    private const string ShaderCode = """
        shader_type spatial;
        render_mode unshaded, blend_mix, depth_test_disabled, cull_disabled;
        uniform float level = 0.0;
        uniform float clip = 0.0;
        uniform float active = 1.0;
        uniform float speaking = 0.0;
        uniform vec3 ok_color : source_color = vec3(0.30, 0.95, 0.40);
        uniform vec3 hot_color : source_color = vec3(1.0, 0.18, 0.15);
        uniform vec3 idle_color : source_color = vec3(0.65, 0.65, 0.68);

        void vertex() {
            MODELVIEW_MATRIX = VIEW_MATRIX * mat4(INV_VIEW_MATRIX[0], INV_VIEW_MATRIX[1], INV_VIEW_MATRIX[2], MODEL_MATRIX[3]);
        }

        void fragment() {
            vec2 p = UV * 2.0 - 1.0;
            float r = length(p);
            vec3 wave_color = mix(ok_color, hot_color, clip);
            vec3 dot_color = mix(idle_color, wave_color, speaking);
            float dot_alpha = smoothstep(0.14, 0.11, r) * active;
            float reach = mix(0.46, 0.98, clamp(level * 1.4 + clip * 0.5, 0.0, 1.0));
            float waves = 0.0;
            for (int i = 0; i < 3; i++) {
                float phase = fract(TIME * 0.85 + float(i) / 3.0);
                float radius = 0.17 + phase * (reach - 0.17);
                float width = 0.028 + 0.03 * clamp(level + clip, 0.0, 1.0);
                waves += smoothstep(width, 0.0, abs(r - radius)) * (1.0 - phase);
            }
            waves = clamp(waves, 0.0, 1.0) * speaking * active;
            float alpha = max(dot_alpha, waves * 0.95);
            vec3 color = dot_alpha >= waves ? dot_color : wave_color;
            ALBEDO = color;
            ALPHA = alpha;
        }
        """;

    private readonly ShaderMaterial _material = new() { Shader = new Shader { Code = ShaderCode } };
    private float _speaking;
    private float _clip;
    private float _level;

    /// Voice is switched on for this avatar (the dot shows).
    public bool Active { get; set; } = true;
    public float Speaking => _speaking;
    public float Clip => _clip;
    public float Level => _level;
    /// True while the rings are visible and red.
    public bool Red => _clip > .5f && _speaking > .3f;

    private float _targetLevel;
    private float _targetClip;
    private double _clipHold;

    public override void _Ready()
    {
        AddChild(new MeshInstance3D { Name = "Waves", Mesh = new QuadMesh { Size = new Vector2(.5f, .5f) }, MaterialOverride = _material, Visible = false, CastShadow = GeometryInstance3D.ShadowCastingSetting.Off });
        Push(0);
    }

    /// Latest loudness: `level` is the smoothed volume (0..1), `peak` the highest sample (0..1; near 1 means too loud).
    public void SetSignal(float level, float peak)
    {
        _targetLevel = Mathf.Clamp(level, 0f, 1f);
        if (VoiceMeter.IsClipping(peak)) { _targetClip = 1f; _clipHold = .35; }
    }

    public override void _Process(double delta)
    {
        Advance((float)delta);
    }

    /// Moves the animation along; separate from _Process so tests can step it without a frame loop.
    public void Advance(float delta)
    {
        if (_clipHold > 0) _clipHold -= delta; else _targetClip = 0f;
        var speakingTarget = _targetLevel > .04f && Active ? 1f : 0f;
        _speaking = Mathf.MoveToward(_speaking, speakingTarget, delta * (speakingTarget > _speaking ? 10f : 3f));
        _level = Mathf.Lerp(_level, _targetLevel, 1f - Mathf.Exp(-14f * delta));
        _clip = Mathf.MoveToward(_clip, _targetClip, delta * (_targetClip > _clip ? 12f : 2.5f));
        _targetLevel = Mathf.MoveToward(_targetLevel, 0f, delta * 2.2f);
        Push(delta);
    }

    private void Push(float delta)
    {
        _material.SetShaderParameter("level", _level);
        _material.SetShaderParameter("clip", _clip);
        _material.SetShaderParameter("active", Active ? 1f : 0f);
        _material.SetShaderParameter("speaking", _speaking);
    }
}
