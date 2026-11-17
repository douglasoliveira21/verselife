using System.Reflection;
using LifeVerse.Api.Modules.Economy;
using Xunit;

namespace LifeVerse.Api.Tests;

public sealed class EconomyAdminPageTests
{
    private static string Html => (string)typeof(EconomyAdminPage).GetField("Html", BindingFlags.NonPublic | BindingFlags.Static)!.GetRawConstantValue()!;

    [Fact]
    public void ThePageNeverPutsServerDataInAsHtml() { Assert.DoesNotContain("innerHTML", Html); Assert.DoesNotContain("outerHTML", Html); Assert.DoesNotContain("document.write", Html); Assert.DoesNotContain("eval(", Html); }

    [Fact]
    public void ThePageHoldsNoSecretAndCallsTheAdminApiWithBothTokens() { Assert.Contains("X-Admin-MFA", Html); Assert.Contains("/api/v1/admin/", Html); Assert.Contains("type=\"password\"", Html); Assert.DoesNotContain("localStorage", Html); }
}
