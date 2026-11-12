namespace LifeVerse.Api.Modules.Worlds;

/// <summary>Some things on the island management window belong to the owner alone (the administrators may look but not change them).</summary>
public static class IslandOwnerOnly
{
    public const string Message = "Só o dono da ilha pode fazer isso.";

    public static IResult Refusal() => Results.Json(new { error = "owner_only", message = Message }, statusCode: StatusCodes.Status403Forbidden);
}
