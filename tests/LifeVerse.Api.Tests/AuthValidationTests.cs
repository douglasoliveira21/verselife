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

    [Theory]
    [InlineData("Abc123", true)]                       // 6 is the least
    [InlineData("Abc12", false)]                       // 5 is too short
    [InlineData("abc123", false)]                      // no upper case
    [InlineData("ABC123", false)]                      // no lower case
    [InlineData("Abcdef", false)]                      // no digit
    [InlineData("", false)]
    public void The_password_needs_six_characters_with_upper_lower_and_a_digit(string password, bool accepted)
    {
        var errors = AuthValidation.Validate(new RegisterRequest("valid_user", "user@example.test", password));
        Assert.Equal(accepted, !errors.ContainsKey("password"));
    }

    [Fact]
    public void A_password_over_128_characters_is_refused() => Assert.True(AuthValidation.Validate(new RegisterRequest("valid_user", "user@example.test", "Aa1" + new string('x', 126))).ContainsKey("password"));

    [Fact]
    public void Register_accepts_valid_input()
    {
        var errors = AuthValidation.Validate(new RegisterRequest("valid_user", "user@example.test", "StrongPassword123"));
        Assert.Empty(errors);
    }
}
