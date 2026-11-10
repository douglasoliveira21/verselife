namespace LifeVerse.Shared.Loading;

/// <summary>
/// When a person's trip to another island is confirmed there, the island they left must let go of them. This decides who is let go: someone who joined before the
/// arrival, never someone who came back afterwards (that is a new visit), so a person is on one island at a time and a quick return is not thrown out.
/// </summary>
public static class TravellerRelease
{
    public static bool ShouldDrop(DateTimeOffset joinedAt, long arrivedAtUnixMs) => joinedAt.ToUnixTimeMilliseconds() <= arrivedAtUnixMs;
}
