namespace LifeVerse.Shared.Networking;

/// <summary>
/// How far up or down a person is looking, as the others see it on their avatar's head. The camera itself is never sent (it belongs to the person who owns it); only this angle is, a few times a second, and only
/// while the person looks through their own eyes. The island server checks it, limits how often it is passed on and who gets it, and the avatars of the others lean the head that far (never farther than a head can).
/// </summary>
public static class HeadLook
{
    /// <summary>The farthest up or down a head is drawn (about 52 degrees): the camera can look at the feet, but a head does not bend that far; the neck and the spine would take part.</summary>
    public const float MaxPitch = .9f;
    /// <summary>What the island accepts: the range of the first person camera, with a little room.</summary>
    public const float AcceptedPitch = 1.6f;
    /// <summary>The client sends a new angle when it changed by this much (radians)...</summary>
    public const float SendThreshold = .03f;
    /// <summary>...but not more often than this, and the same angle again after this long so that somebody who just arrived learns it.</summary>
    public static readonly TimeSpan MinInterval = TimeSpan.FromMilliseconds(80), RefreshInterval = TimeSpan.FromSeconds(1);

    /// <summary>Null when the angle can be passed on, otherwise what is wrong with it.</summary>
    public static string? Check(float pitch) => !float.IsFinite(pitch) ? "not_a_number" : Math.Abs(pitch) > AcceptedPitch ? "out_of_range" : null;

    /// <summary>What an avatar's head is drawn with: the angle brought within what a head can do.</summary>
    public static float Drawn(float pitch) => !float.IsFinite(pitch) ? 0f : Math.Clamp(pitch, -MaxPitch, MaxPitch);

    /// <summary>Whether an angle from this player may be passed on now.</summary>
    public static bool MayRelay(DateTimeOffset? last, DateTimeOffset now) => last is null || now - last.Value >= MinInterval;

    /// <summary>Whether the client has to send: the angle moved enough, or it is time to say it again.</summary>
    public static bool ShouldSend(float lastSent, float now, TimeSpan sinceLastSend) =>
        sinceLastSend >= MinInterval && (Math.Abs(now - lastSent) >= SendThreshold || sinceLastSend >= RefreshInterval);

    /// <summary>Whether somebody at this distance (metres) is told: the same interest range as the snapshots of the world.</summary>
    public static bool InRange(float distance, float interestRadius) => distance <= interestRadius;
}
