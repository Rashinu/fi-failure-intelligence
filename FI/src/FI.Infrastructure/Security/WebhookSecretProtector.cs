using Microsoft.AspNetCore.DataProtection;

namespace FI.Infrastructure.Security;

/// <summary>
/// Bkz. Bölüm 33.4/34. Webhook secret'lar API key'in aksine tek yönlü hash olarak saklanamaz
/// (HMAC doğrulaması sırrın kendisiyle imza hesaplamayı gerektirir - bkz. Integration.WebhookSecret
/// XML doc'u). Bu, düz metin yerine ASP.NET Core Data Protection ile simetrik şifreleme
/// kullanarak "sadece hash" ile "hiç korumasız düz metin" arasındaki boşluğu kapatır; anahtarlar
/// FiDbContext üzerinden kalıcı olarak saklanır (bkz. FiDbContext.DataProtectionKeys).
/// </summary>
public interface IWebhookSecretProtector
{
    string Protect(string plaintextSecret);
    string Unprotect(string protectedSecret);
}

public sealed class WebhookSecretProtector : IWebhookSecretProtector
{
    private const string Purpose = "FI.WebhookSecret.v1";

    private readonly IDataProtector _protector;

    public WebhookSecretProtector(IDataProtectionProvider provider)
    {
        _protector = provider.CreateProtector(Purpose);
    }

    public string Protect(string plaintextSecret) => _protector.Protect(plaintextSecret);

    public string Unprotect(string protectedSecret) => _protector.Unprotect(protectedSecret);
}
