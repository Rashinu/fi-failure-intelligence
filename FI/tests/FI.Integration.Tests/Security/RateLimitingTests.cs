using System.Net;
using FI.Integration.Tests.Fixtures;
using FluentAssertions;
using Xunit;

namespace FI.Integration.Tests.Security;

/// <summary>
/// Bkz. docs/CTO_REVIEW_ANALYSIS.md Due Diligence D9 - hiçbir yerde rate limiting yoktu; D7 ile
/// control plane authentication gerektirse bile, geçerli bir API key veya admin kimlik bilgisiyle
/// hacimli istek atan biri DB'yi/AI çağrılarını kontrolsüz tüketebilirdi. Bu, yalnızca "/api/v1"
/// altındaki rotalar için (bkz. RateLimitingExtensions) IP başına sabit-pencere limitini doğrular.
/// Bu sınıf kasıtlı olarak izole - FiApiFactory'nin sabit-pencere sayaçları class-fixture ömrü
/// boyunca paylaşıldığından, aynı path'e yüzlerce istek atan bir test başka test sınıflarını
/// etkilememeli (her sınıf kendi FiApiFactory örneğini alır).
/// </summary>
public class RateLimitingTests : IClassFixture<FiApiFactory>
{
    private readonly FiApiFactory _factory;

    public RateLimitingTests(FiApiFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task ApiV1Endpoint_ExceedingPermitLimit_Returns429()
    {
        var client = _factory.CreateClient();

        HttpResponseMessage? last = null;
        for (var i = 0; i < 101; i++)
        {
            last = await client.GetAsync("/api/v1/incidents");
        }

        last!.StatusCode.Should().Be((HttpStatusCode)429);
    }

    [Fact]
    public async Task NonApiPath_IsNeverRateLimited()
    {
        var client = _factory.CreateClient();

        for (var i = 0; i < 150; i++)
        {
            var response = await client.GetAsync("/health/live");
            response.StatusCode.Should().NotBe((HttpStatusCode)429);
        }
    }
}
