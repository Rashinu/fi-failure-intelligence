using System.Net;
using System.Text;
using System.Text.Json;
using FI.Integration.Tests.Fixtures;
using Xunit;

namespace FI.Integration.Tests.ErrorHandling;

/// <summary>
/// Bkz. docs/reviews/M20_1_PRODUCTION_SAFETY_REPORT.md P0-B. Gerçek uçtan uca HTTP çağrılarıyla
/// global exception handler'ı doğrular - controller'a özel try/catch'in birincil strateji
/// OLMADIĞINI kanıtlar. M20.1 onay kısıtı #2 gereği InvalidOperationException global olarak
/// 409'a ÇEVRİLMEZ - bu yüzden burada yalnızca ArgumentException->400 ve mevcut, dokunulmamış
/// IncidentsController.Resolve'un kendi yerel 409 eşlemesi (zaten IncidentResolutionTests'te
/// kapsanıyor) doğrulanır.
/// </summary>
public class GlobalExceptionHandlingTests : IClassFixture<FiApiFactory>
{
    private readonly FiApiFactory _factory;

    public GlobalExceptionHandlingTests(FiApiFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task InvalidBusinessCriticality_ReturnsProblemDetails400_NotRaw500()
    {
        var client = _factory.CreateClient();

        var body = new
        {
            name = "Bad Criticality Test",
            provider = "stripe",
            environment = "production",
            owner = "test",
            endpointUrl = "https://api.stripe.com",
            businessCriticality = "NotARealCriticalityValue"
        };
        var content = new StringContent(JsonSerializer.Serialize(body), Encoding.UTF8, "application/json");

        var response = await client.PostAsync("/api/v1/integrations", content);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);

        var json = await response.Content.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(json);
        Assert.Equal(400, doc.RootElement.GetProperty("status").GetInt32());
        Assert.True(doc.RootElement.TryGetProperty("traceId", out var traceId));
        Assert.False(string.IsNullOrWhiteSpace(traceId.GetString()));

        // Bkz. onay kısıtı #2 - programlama hatası (ArgumentNullException) ile gerçek client
        // girdi hatası (ArgumentException/Enum.Parse) ayrımı - burada asıl fırlatılan
        // ArgumentException'dır (Enum.Parse), bu yüzden 400 doğru. Yanıt gövdesi ham stack
        // trace/dosya yolu içermemeli.
        Assert.DoesNotContain(".cs:line", json);
        Assert.DoesNotContain("StackTrace", json, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task UnknownRoute_StillReturnsPlain404_NotAffectedByExceptionHandler()
    {
        // Bkz. Program.cs middleware sırası - exception handler yalnızca fırlatılan exception'ları
        // yakalar, routing'in kendi ürettiği normal 404'leri (bir exception değil) etkilemez.
        var client = _factory.CreateClient();
        var response = await client.GetAsync("/api/v1/this-route-does-not-exist");
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }
}
