using System.Text.Json;
using Godot;

namespace LifeVerse.Client.Scripts;

/// "Lembrar usuário e senha": the login is kept in an encrypted file in the user's data folder, with a key tied to this
/// computer, so the file is useless if copied elsewhere. It protects against casual reading, not against someone who is
/// already logged in to the same Windows account, which is the usual limit of any remember-me.
public static class SavedLogin
{
    private const string FileName = "user://login.dat";

    public sealed record Credentials(string Login, string Password);

    private static string Key => "lifeverse-login-" + OS.GetUniqueId();

    public static bool Exists => Godot.FileAccess.FileExists(FileName);

    public static void Save(string login, string password)
    {
        using var file = Godot.FileAccess.OpenEncryptedWithPass(FileName, Godot.FileAccess.ModeFlags.Write, Key);
        if (file is null) return;
        file.StoreString(JsonSerializer.Serialize(new Credentials(login, password)));
    }

    public static Credentials? Load()
    {
        if (!Exists) return null;
        try
        {
            using var file = Godot.FileAccess.OpenEncryptedWithPass(FileName, Godot.FileAccess.ModeFlags.Read, Key);
            if (file is null) return null; // another computer, or a damaged file
            var saved = JsonSerializer.Deserialize<Credentials>(file.GetAsText());
            return saved is { Login.Length: > 0, Password.Length: > 0 } ? saved : null;
        }
        catch (JsonException) { return null; }
    }

    public static void Clear()
    {
        if (Exists) DirAccess.RemoveAbsolute(ProjectSettings.GlobalizePath(FileName));
    }
}
