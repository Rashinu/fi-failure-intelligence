using FI.Api.Middleware;
using FI.Api.Security;
using Microsoft.Extensions.Configuration;
using Xunit;

namespace FI.Integration.Tests.Security;

/// <summary>
/// Bkz. docs/reviews/M20_1_PRODUCTION_SAFETY_REPORT.md P0-A, M20.1 onay kısıtı #3 - saf, host'suz
/// unit testler. ProductionSecretValidator statik ve host'suz olduğundan bu testler herhangi bir
/// WebApplicationFactory/Testcontainers gerektirmez (FI.Integration.Tests projesinde yaşamasının
/// tek nedeni FI.Api'ye referansı olan tek proje olması - bu bir Testcontainers testi değildir).
/// </summary>
public class ProductionSecretValidatorTests
{
    private static IConfiguration BuildConfig(string? adminSecret, string? pepper)
    {
        var dict = new Dictionary<string, string?>();
        if (adminSecret is not null) dict["Admin:SharedSecret"] = adminSecret;
        if (pepper is not null) dict["ApiKeys:Pepper"] = pepper;
        return new ConfigurationBuilder().AddInMemoryCollection(dict).Build();
    }

    [Fact]
    public void NotProduction_AlwaysReturnsEmpty_RegardlessOfSecretValues()
    {
        var config = BuildConfig(adminSecret: null, pepper: null);
        var problems = ProductionSecretValidator.Validate(config, isProduction: false);
        Assert.Empty(problems);
    }

    [Fact]
    public void Production_MissingAdminSecret_FlagsAdminSecretKey()
    {
        var config = BuildConfig(adminSecret: null, pepper: "a-real-strong-pepper-value");
        var problems = ProductionSecretValidator.Validate(config, isProduction: true);
        Assert.Contains("Admin:SharedSecret", problems);
    }

    [Fact]
    public void Production_PlaceholderAdminSecret_FlagsAdminSecretKey()
    {
        var config = BuildConfig(adminSecret: AdminBasicAuthMiddleware.LocalDevDefault, pepper: "a-real-strong-pepper-value");
        var problems = ProductionSecretValidator.Validate(config, isProduction: true);
        Assert.Contains("Admin:SharedSecret", problems);
    }

    [Fact]
    public void Production_MissingApiKeyPepper_FlagsApiKeyPepperKey()
    {
        var config = BuildConfig(adminSecret: "a-real-strong-admin-secret", pepper: null);
        var problems = ProductionSecretValidator.Validate(config, isProduction: true);
        Assert.Contains("ApiKeys:Pepper", problems);
    }

    [Fact]
    public void Production_PlaceholderApiKeyPepper_FlagsApiKeyPepperKey()
    {
        var config = BuildConfig(adminSecret: "a-real-strong-admin-secret", pepper: ApiKeyAuthMiddleware.LocalDevPepperDefault);
        var problems = ProductionSecretValidator.Validate(config, isProduction: true);
        Assert.Contains("ApiKeys:Pepper", problems);
    }

    [Fact]
    public void Production_WhitespaceOnlyValues_TreatedAsMissing()
    {
        var config = BuildConfig(adminSecret: "   ", pepper: "   ");
        var problems = ProductionSecretValidator.Validate(config, isProduction: true);
        Assert.Contains("Admin:SharedSecret", problems);
        Assert.Contains("ApiKeys:Pepper", problems);
    }

    [Fact]
    public void Production_BothMissing_FlagsBothKeys()
    {
        var config = BuildConfig(adminSecret: null, pepper: null);
        var problems = ProductionSecretValidator.Validate(config, isProduction: true);
        Assert.Equal(2, problems.Count);
        Assert.Contains("Admin:SharedSecret", problems);
        Assert.Contains("ApiKeys:Pepper", problems);
    }

    [Fact]
    public void Production_BothValidStrongValues_ReturnsEmpty()
    {
        var config = BuildConfig(adminSecret: "a-real-strong-admin-secret", pepper: "a-real-strong-pepper-value");
        var problems = ProductionSecretValidator.Validate(config, isProduction: true);
        Assert.Empty(problems);
    }
}
