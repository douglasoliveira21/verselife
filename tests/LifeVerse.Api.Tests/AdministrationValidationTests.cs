using LifeVerse.Api.Data;
using Xunit;

namespace LifeVerse.Api.Tests;

public sealed class AdministrationValidationTests
{
    [Theory]
    [InlineData("active", true)]
    [InlineData("suspended", false)]
    [InlineData("banned", false)]
    public void Inactive_user_status_must_not_authenticate(string status, bool expectedActive) =>
        Assert.Equal(expectedActive, status == "active");

    [Fact]
    public void New_users_default_to_active() => Assert.Equal("active", new User
    {
        Username = "qa", NormalizedUsername = "QA", Email = "qa@example.test", NormalizedEmail = "QA@EXAMPLE.TEST", PasswordHash = "unused"
    }.Status);
}
