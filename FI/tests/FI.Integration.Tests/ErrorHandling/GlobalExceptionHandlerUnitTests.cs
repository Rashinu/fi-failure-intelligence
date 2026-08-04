using System.Text.Json;
using FI.Api.ErrorHandling;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace FI.Integration.Tests.ErrorHandling;

/// <summary>
/// Bkz. docs/reviews/M20_1_PRODUCTION_SAFETY_REPORT.md P0-B. GlobalExceptionHandler'ı doğrudan,
/// host'suz çağırarak "bilinmeyen exception -> sanitize edilmiş 500" yolunu test eder - bunu
/// tetiklemek için production controller'larına yapay bir "throw" endpoint'i EKLEMEK yerine
/// (bu, ilgisiz bir kapsam genişlemesi olurdu) handler'ın kendisi doğrudan çağrılıyor.
/// </summary>
public class GlobalExceptionHandlerUnitTests
{
    [Fact]
    public async Task UnknownException_ReturnsSanitized500_WithTraceIdAndNoInternalDetail()
    {
        var handler = new GlobalExceptionHandler(NullLogger<GlobalExceptionHandler>.Instance);
        var context = new DefaultHttpContext();
        context.Response.Body = new MemoryStream();

        var secretLookingMessage = "connection failed: Password=SuperSecretDbPassword123!";
        var handled = await handler.TryHandleAsync(context, new InvalidOperationException(secretLookingMessage), CancellationToken.None);

        Assert.True(handled);
        Assert.Equal(StatusCodes.Status500InternalServerError, context.Response.StatusCode);

        context.Response.Body.Seek(0, SeekOrigin.Begin);
        using var reader = new StreamReader(context.Response.Body);
        var json = await reader.ReadToEndAsync();

        using var doc = JsonDocument.Parse(json);
        Assert.Equal(500, doc.RootElement.GetProperty("status").GetInt32());
        Assert.True(doc.RootElement.TryGetProperty("traceId", out _));

        // En kritik doğrulama: exception mesajının kendisi (potansiyel olarak hassas veri
        // içerebilir) yanıt gövdesine asla sızmamalı - yalnızca genel bir başlık dönmeli.
        Assert.DoesNotContain("SuperSecretDbPassword123", json);
        Assert.DoesNotContain(secretLookingMessage, json);
    }

    [Fact]
    public async Task ArgumentNullException_IsTreatedAsProgrammingDefect_Returns500NotBadRequest()
    {
        // Bkz. M20.1 onay kısıtı #2 - "do not hide programming defects behind 400".
        // ArgumentNullException neredeyse her zaman bir programlama hatasıdır (beklenmedik null),
        // client girdi hatası değil - bu yüzden bilinçli olarak 400'e değil 500'e eşlenir.
        var handler = new GlobalExceptionHandler(NullLogger<GlobalExceptionHandler>.Instance);
        var context = new DefaultHttpContext();
        context.Response.Body = new MemoryStream();

        await handler.TryHandleAsync(context, new ArgumentNullException("someInternalParam"), CancellationToken.None);

        Assert.Equal(StatusCodes.Status500InternalServerError, context.Response.StatusCode);
    }

    [Fact]
    public async Task PlainArgumentException_IsTreatedAsClientInput_Returns400()
    {
        var handler = new GlobalExceptionHandler(NullLogger<GlobalExceptionHandler>.Instance);
        var context = new DefaultHttpContext();
        context.Response.Body = new MemoryStream();

        await handler.TryHandleAsync(context, new ArgumentException("Requested value 'X' was not found."), CancellationToken.None);

        Assert.Equal(StatusCodes.Status400BadRequest, context.Response.StatusCode);
    }
}
