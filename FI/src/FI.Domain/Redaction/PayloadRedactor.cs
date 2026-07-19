using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace FI.Domain.Redaction;

/// <summary>
/// Bkz. docs/FAILURE_INTELLIGENCE_ARCHITECTURE.md Bölüm 33.3 — "EvidenceMaskingPolicy domain
/// katmanında merkezi uygulanır" ifadesinin karşılığı. Saf, framework'ten bağımsız, idempotent
/// ve deterministiktir. İki aşamalı redaction'ın (A: ingestion sırasında, B: AI'a gönderilmeden
/// hemen önce) TEK ortak motoru — connector'ların kendi <c>Redact</c> implementasyonları da
/// buraya delege eder; ikinci bir maskeleme mantığı olması iki kaynağın sapması riskini doğurur.
/// </summary>
public static class PayloadRedactor
{
    private const string Redacted = "[REDACTED]";

    /// <summary>Bölüm 33.3 tablosu — bilinen hassas JSON alan adları (field-based, öncelikli).</summary>
    private static readonly HashSet<string> SensitiveFieldNames = new(StringComparer.Ordinal)
    {
        "authorization", "xapikey", "xauthtoken",
        "apikey", "secret", "clientsecret", "password", "token"
    };

    private static readonly Regex BearerTokenRegex = new(@"Bearer\s+[A-Za-z0-9\-_\.]+", RegexOptions.Compiled);
    private static readonly Regex JwtRegex = new(@"\bey[A-Za-z0-9_-]+\.[A-Za-z0-9_-]+\.[A-Za-z0-9_-]+\b", RegexOptions.Compiled);
    private static readonly Regex EmailRegex = new(@"\b[A-Za-z0-9._%+\-]+@[A-Za-z0-9.\-]+\.[A-Za-z]{2,}\b", RegexOptions.Compiled);
    private static readonly Regex CreditCardCandidateRegex = new(@"\b(?:\d[ -]?){13,19}\b", RegexOptions.Compiled);
    // Kasıtlı olarak yalnızca '-'/'.' ayraçlı gruplaşmayı eşleştirir (ör. 555-234-5678); salt
    // boşlukla ayrılmış rakam gruplarını (kredi kartı biçimiyle çakışan bir görünüm) kapsam dışı
    // bırakır — Luhn-invalid bir kredi kartı benzeri sayının yanlışlıkla telefon olarak
    // maskelenmesindense (precision > recall, Bölüm 33.3'ün "best-effort, MVP kapsamı" ilkesi).
    private static readonly Regex PhoneRegex = new(@"(?<!\d)\+?\d(?:[\-.]\d{1,4}){2,4}\b", RegexOptions.Compiled);

    /// <summary>
    /// Bir JSON ağacını yerinde değiştirmeden, tamamen yeni bir ağaç olarak döner. Bilinen hassas
    /// alan adları tam maskelenir (field-based); geri kalan tüm string yaprak değerleri
    /// pattern-based redaction'dan geçirilir (yedek katman, Bölüm 33.3).
    /// </summary>
    public static JsonNode? RedactJson(JsonNode? node)
    {
        if (node is null) return null;

        switch (node)
        {
            case JsonObject obj:
                var result = new JsonObject();
                foreach (var (key, value) in obj)
                {
                    if (IsSensitiveFieldName(key))
                    {
                        result[key] = Redacted;
                    }
                    else
                    {
                        result[key] = RedactJson(value?.DeepClone());
                    }
                }
                return result;

            case JsonArray arr:
                var newArray = new JsonArray();
                foreach (var item in arr)
                    newArray.Add(RedactJson(item?.DeepClone()));
                return newArray;

            case JsonValue value when value.TryGetValue(out string? text):
                return RedactText(text);

            default:
                return node.DeepClone();
        }
    }

    /// <summary>Yalnızca pattern-based redaction — serbest metin (ör. evidence özetleri) için.</summary>
    public static string? RedactText(string? text)
    {
        if (string.IsNullOrEmpty(text)) return text;

        var redacted = BearerTokenRegex.Replace(text, "Bearer " + Redacted);
        redacted = JwtRegex.Replace(redacted, Redacted);
        redacted = EmailRegex.Replace(redacted, MaskEmail);
        redacted = CreditCardCandidateRegex.Replace(redacted, MaskCreditCardIfValid);
        redacted = PhoneRegex.Replace(redacted, MaskPhone);

        return redacted;
    }

    private static bool IsSensitiveFieldName(string fieldName)
    {
        var normalized = new string(fieldName.Where(char.IsLetterOrDigit).ToArray()).ToLowerInvariant();
        return SensitiveFieldNames.Contains(normalized);
    }

    private static string MaskEmail(Match match)
    {
        var value = match.Value;
        var atIndex = value.IndexOf('@');
        if (atIndex <= 0) return Redacted;

        var localPart = value[..atIndex];
        var domain = value[atIndex..];
        return $"{localPart[0]}***{domain}";
    }

    private static string MaskCreditCardIfValid(Match match)
    {
        var digitsOnly = new string(match.Value.Where(char.IsDigit).ToArray());
        if (digitsOnly.Length is < 13 or > 19 || !PassesLuhnCheck(digitsOnly))
            return match.Value;

        var last4 = digitsOnly[^4..];
        return $"**** **** **** {last4}";
    }

    private static bool PassesLuhnCheck(string digits)
    {
        var sum = 0;
        var alternate = false;
        for (var i = digits.Length - 1; i >= 0; i--)
        {
            var digit = digits[i] - '0';
            if (alternate)
            {
                digit *= 2;
                if (digit > 9) digit -= 9;
            }
            sum += digit;
            alternate = !alternate;
        }
        return sum % 10 == 0;
    }

    private static string MaskPhone(Match match)
    {
        var digitsOnly = new string(match.Value.Where(char.IsDigit).ToArray());
        if (digitsOnly.Length < 7) return match.Value;

        var last4 = digitsOnly[^4..];
        return $"***-***-{last4}";
    }
}
