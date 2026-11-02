namespace LifeVerse.Api.Modules.Identity;

public sealed record RegisterRequest(string Username, string Email, string Password);
public sealed record LoginRequest(string Login, string Password);
public sealed record RefreshRequest(string RefreshToken);
public sealed record LogoutRequest(string RefreshToken);
public sealed record AuthResponse(string AccessToken, string RefreshToken, DateTimeOffset AccessTokenExpiresAt, UserSummary User);
public sealed record UserSummary(Guid Id, string Username, string Email, string DisplayName);

public static class AuthValidation
{
    public static Dictionary<string, string[]> Validate(RegisterRequest request)
    {
        var errors = new Dictionary<string, string[]>();
        if (string.IsNullOrWhiteSpace(request.Username) || request.Username.Length is < 3 or > 32 ||
            request.Username.Any(c => !char.IsLetterOrDigit(c) && c != '_'))
            errors["username"] = ["Username must be 3-32 letters, numbers, or underscores."];
        if (!System.Net.Mail.MailAddress.TryCreate(request.Email, out _) || request.Email is null || request.Email.Length > 254)
            errors["email"] = ["A valid email address is required."];
        if (!IsStrongPassword(request.Password))
            errors["password"] = ["Password must be 12-128 characters and include upper, lower, and numeric characters."];
        return errors;
    }

    public static Dictionary<string, string[]> Validate(LoginRequest request)
    {
        var errors = new Dictionary<string, string[]>();
        if (string.IsNullOrWhiteSpace(request.Login)) errors["login"] = ["Login is required."];
        if (string.IsNullOrEmpty(request.Password)) errors["password"] = ["Password is required."];
        return errors;
    }

    private static bool IsStrongPassword(string? value) => value is not null && value.Length is >= 12 and <= 128 &&
        value.Any(char.IsUpper) && value.Any(char.IsLower) && value.Any(char.IsDigit);
}
