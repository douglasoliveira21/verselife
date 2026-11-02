namespace LifeVerse.Shared.Terrain;

public sealed class TerrainEditBuffer
{
    private readonly float[] _heights;
    public int Width { get; }
    public int Height { get; }
    public IReadOnlyList<float> Heights => _heights;

    public TerrainEditBuffer(int width, int height, IEnumerable<float>? heights = null)
    {
        Width = Math.Clamp(width, 2, 2048); Height = Math.Clamp(height, 2, 2048); _heights = heights?.Take(Width * Height).ToArray() ?? new float[Width * Height];
        if (_heights.Length != Width * Height) Array.Resize(ref _heights, Width * Height);
    }

    public void Raise(int centerX, int centerY, int radius, float amount) => Paint(centerX, centerY, radius, (index, weight) => _heights[index] += amount * weight);
    public void Lower(int centerX, int centerY, int radius, float amount) => Raise(centerX, centerY, radius, -Math.Abs(amount));
    public void Flatten(int centerX, int centerY, int radius, float target, float strength = 1f) => Paint(centerX, centerY, radius, (index, weight) => _heights[index] = Lerp(_heights[index], target, Math.Clamp(strength, 0, 1) * weight));
    public void Smooth(int centerX, int centerY, int radius, float strength = 1f)
    {
        var copy = (float[])_heights.Clone(); Paint(centerX, centerY, radius, (index, weight) => { var x = index % Width; var y = index / Width; var sum = 0f; var count = 0; for (var oy = -1; oy <= 1; oy++) for (var ox = -1; ox <= 1; ox++) { var nx = x + ox; var ny = y + oy; if (nx >= 0 && nx < Width && ny >= 0 && ny < Height) { sum += copy[ny * Width + nx]; count++; } } _heights[index] = Lerp(copy[index], sum / count, Math.Clamp(strength, 0, 1) * weight); });
    }

    private void Paint(int centerX, int centerY, int radius, Action<int, float> operation)
    {
        radius = Math.Clamp(radius, 1, Math.Max(Width, Height)); var r2 = radius * radius;
        for (var y = Math.Max(0, centerY - radius); y <= Math.Min(Height - 1, centerY + radius); y++) for (var x = Math.Max(0, centerX - radius); x <= Math.Min(Width - 1, centerX + radius); x++) { var dx = x - centerX; var dy = y - centerY; var distance = dx * dx + dy * dy; if (distance <= r2) operation(y * Width + x, 1f - MathF.Sqrt(distance) / radius); }
    }
    private static float Lerp(float from, float to, float amount) => from + (to - from) * amount;
}
