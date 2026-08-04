using FI.Api.Middleware;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Mvc;

namespace FI.Api.ErrorHandling;

/// <summary>
/// Bkz. docs/reviews/M20_1_PRODUCTION_SAFETY_REPORT.md P0-B. Tek, tutarlı bir global exception
/// handler - RFC 7807 ProblemDetails. Bilinçli olarak DAR bir eşleme yapar (bkz. M20.1 onay
/// kısıtı #2): her InvalidOperationException'ı 409'a çevirmez - yalnızca gerçek bir domain
/// lifecycle çakışması olduğu zaten bilinen yerler (örn. IncidentsController.Resolve'daki mevcut
/// yerel catch) 409 döner; buraya kadar ulaşan (yakalanmamış) herhangi bir InvalidOperationException
/// "beklenmeyen" kabul edilir ve sanitize edilmiş 500 olarak döner - yanlışlıkla client-conflict
/// gibi etiketlenmez. ArgumentException yalnızca ArgumentNullException OLMADIĞI durumda 400'e
/// çevrilir (ArgumentNullException çoğu zaman bir programlama hatasıdır, client girdisi değil -
/// bkz. M20.1 onay kısıtı #2, "do not hide programming defects behind 400").
/// </summary>
public class GlobalExceptionHandler : IExceptionHandler
{
    private readonly ILogger<GlobalExceptionHandler> _logger;

    public GlobalExceptionHandler(ILogger<GlobalExceptionHandler> logger)
    {
        _logger = logger;
    }

    public async ValueTask<bool> TryHandleAsync(HttpContext httpContext, Exception exception, CancellationToken cancellationToken)
    {
        var correlationId = httpContext.GetCorrelationId();

        var (status, title) = exception switch
        {
            ArgumentNullException => (StatusCodes.Status500InternalServerError, "Beklenmeyen bir hata oluştu."),
            ArgumentException => (StatusCodes.Status400BadRequest, "Geçersiz istek."),
            _ => (StatusCodes.Status500InternalServerError, "Beklenmeyen bir hata oluştu.")
        };

        _logger.LogError(exception,
            "Yakalanmamış exception. CorrelationId={CorrelationId}, Status={Status}",
            correlationId, status);

        var problemDetails = new ProblemDetails
        {
            Status = status,
            Title = title,
            Extensions = { ["traceId"] = correlationId.ToString() }
        };

        httpContext.Response.StatusCode = status;
        await httpContext.Response.WriteAsJsonAsync(problemDetails, cancellationToken);
        return true;
    }
}
