using Microsoft.AspNetCore.Mvc;

namespace LifeVerse.Api.Modules.Identity;

public static class IdentityEndpoints
{
    public static IEndpointRouteBuilder MapIdentityEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/api/v1/auth").WithTags("Identity").RequireRateLimiting("auth");
        group.MapPost("/register", RegisterAsync).Produces<AuthResponse>(201).ProducesValidationProblem().ProducesProblem(409);
        group.MapPost("/login", LoginAsync).Produces<AuthResponse>().ProducesValidationProblem().ProducesProblem(401);
        group.MapPost("/refresh", RefreshAsync).Produces<AuthResponse>().ProducesProblem(401);
        group.MapPost("/logout", LogoutAsync).Produces(204);
        return endpoints;
    }

    private static async Task<IResult> RegisterAsync(RegisterRequest request, AuthService auth, HttpContext context, CancellationToken ct)
    {
        var errors = AuthValidation.Validate(request);
        if (errors.Count > 0) return Results.ValidationProblem(errors);
        var result = await auth.RegisterAsync(request, context, ct);
        return result is null
            ? Results.Problem(statusCode: 409, title: "Username or email is already registered.", type: "https://lifeverse.local/problems/account-conflict")
            : Results.Created("/api/v1/users/me", result);
    }

    private static async Task<IResult> LoginAsync(LoginRequest request, AuthService auth, HttpContext context, CancellationToken ct)
    {
        var errors = AuthValidation.Validate(request);
        if (errors.Count > 0) return Results.ValidationProblem(errors);
        var result = await auth.LoginAsync(request, context, ct);
        return result is null
            ? Results.Problem(statusCode: 401, title: "Invalid credentials.", type: "https://lifeverse.local/problems/invalid-credentials")
            : Results.Ok(result);
    }

    private static async Task<IResult> RefreshAsync(RefreshRequest request, AuthService auth, HttpContext context, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(request.RefreshToken) || request.RefreshToken.Length > 1024) return Results.ValidationProblem(new Dictionary<string, string[]> { ["refreshToken"] = ["A valid refresh token is required."] });
        var result = await auth.RefreshAsync(request.RefreshToken, context, ct);
        return result is null
            ? Results.Problem(statusCode: 401, title: "Refresh token is invalid or expired.", type: "https://lifeverse.local/problems/invalid-session")
            : Results.Ok(result);
    }

    private static async Task<IResult> LogoutAsync(LogoutRequest request, AuthService auth, CancellationToken ct)
    {
        if (!string.IsNullOrWhiteSpace(request.RefreshToken)) await auth.LogoutAsync(request.RefreshToken, ct);
        return Results.NoContent();
    }
}
