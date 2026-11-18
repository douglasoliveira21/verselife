using LifeVerse.Shared.Gameplay;

namespace LifeVerse.Shared.Networking;

/// <summary>
/// The short gestures besides wave and dance (those two travel as input flags): applaud, point, greet, laugh and sit on the floor.
/// The player says which one once, the island checks it and tells the players who can see them; each one plays for a few seconds.
/// </summary>
public static class ExtraGestures
{
    /// <summary>The gestures the network carries, by number. The number is what travels, so new ones go at the end.</summary>
    public static readonly AvatarMotionState[] States = [AvatarMotionState.Applaud, AvatarMotionState.Point, AvatarMotionState.Greet, AvatarMotionState.Laugh, AvatarMotionState.SitFloor];

    /// <summary>How long each gesture plays for the others, in seconds (the same as for the player who made it).</summary>
    public const float Seconds = 2.5f;

    /// <summary>Somebody may start a gesture again only after this long, so nobody floods the island.</summary>
    public static readonly TimeSpan MinInterval = TimeSpan.FromMilliseconds(300);

    public static bool IsValid(int id) => id >= 0 && id < States.Length;

    /// <summary>The number of a gesture, or -1 when the network does not carry it.</summary>
    public static int IdOf(AvatarMotionState state) => Array.IndexOf(States, state);

    public static bool MayRelay(DateTimeOffset? last, DateTimeOffset now) => last is null || now - last.Value >= MinInterval;
}
