using LifeVerse.Api.Modules.Identity;
using Xunit;

namespace LifeVerse.Api.Tests;

public sealed class AuthValidationTests
{
    [Fact]
    public void Register_rejects_weak_password()
    {
        var errors = AuthValidation.Validate(new RegisterRequest("valid_user", "user@example.test", "weak"));
        Assert.Contains("password", errors);
    }

    [Fact]
    public void Register_accepts_valid_input()
    {
        var errors = AuthValidation.Validate(new RegisterRequest("valid_user", "user@example.test", "StrongPassword123"));
        Assert.Empty(errors);
    }
}
