using Microsoft.AspNetCore.Mvc.RazorPages;

namespace FI.Api.Pages.Demo;

/// <summary>
/// Bkz. docs/reviews/M20_2_DEMO_AND_SECURITY.md P0-2 ve sonraki genişletme (3 örnek).
/// Seçilen çözüm hâlâ STATIC INCIDENT — bu sayfa hiçbir DB sorgusu yapmaz, hiçbir form/POST
/// handler'ı yok, AdminBasicAuthMiddleware'in korumalı prefix listesinde değil, ayrı bir
/// _DemoLayout kullanır (hiçbir korumalı rotaya link yok - önceki oturumda canlı tespit edilen
/// bir bug'ın düzeltmesi).
///
/// Üç senaryo da gerçek: #1 bu projenin M19/M20.1'de gerçekten çalıştırılmış Golden Incident
/// koşusundan; #2 ve #3, bu repodaki gerçek golden dataset senaryolarına
/// (FI.Infrastructure.Eval.GoldenDataset: "signature-webhook-secret-mismatch" ve
/// "rate-limit-traffic-spike", yalnızca entegrasyon adları Stripe/SES'e uyarlandı - sınıflandırma
/// mantığı sağlayıcıdan bağımsız olduğu için bu geçerli) dayanarak, GERÇEK Claude Haiku'ya
/// (aynı üretim prompt'u, fi-root-cause-v1) karşı ÇALIŞTIRILARAK üretildi - hiçbir AI çıktısı
/// uydurulmadı.
/// </summary>
public class GoldenIncidentModel : PageModel
{
    public IReadOnlyList<DemoScenario> Scenarios { get; } = new List<DemoScenario>
    {
        new(
            Id: "payment-auth",
            TabLabel: "Payment failure (Stripe)",
            IntegrationName: "PaymentService (Prod)",
            Category: "AuthenticationError",
            Severity: "Critical",
            StatusLabel: "NeedsHumanReview",
            StatusBadgeClass: "fi-badge-review",
            FirstSeen: "2026-08-04 20:37",
            LastSeen: "2026-08-04 20:37",
            EventCount: 43,
            KnownOperations: "12",
            AffectedCustomers: "7",
            Duration: "< 1 min",
            SuggestedAction: "API key'in geçerliliğini ve son rotasyon zamanını kontrol edin.",
            Ai: null,
            NoAiReason: "No AI analysis in this run — evidence was collected, but no Anthropic API key was configured, so the system correctly skipped AI analysis rather than guess, and fell back to NeedsHumanReview. This is the real, observed behavior of this exact run, not a placeholder: FI never invents a root cause it cannot support with evidence.",
            Evidence: new[]
            {
                ("ConfigChange", "API key rotated for integration 'PaymentService (Prod)' 0 minute(s) before first failure", "2026-08-04 20:38"),
                ("PreviousEvent", "Previous event: status 401, category AuthenticationError, 0 minute(s) before first failure", "2026-08-04 20:38"),
                ("PreviousEvent", "Previous event: status 401, category AuthenticationError, 0 minute(s) before first failure", "2026-08-04 20:38"),
            },
            Timeline: new[]
            {
                ("First error observed", "2026-08-04 20:37"),
                ("Evidence collected: ConfigChange", "2026-08-04 20:38"),
                ("Evidence collected: PreviousEvent (×2)", "2026-08-04 20:38"),
            },
            Fingerprint: "BBA5D624266EB5D8715113171463603AF5B13E50B2B101AD66A078A65CF2FD81"),

        new(
            Id: "stripe-signature",
            TabLabel: "Webhook signature error (Stripe)",
            IntegrationName: "stripe-payments-webhook",
            Category: "SignatureError",
            Severity: "High",
            StatusLabel: "AiAnalyzed",
            StatusBadgeClass: "fi-badge-analyzed",
            FirstSeen: "2026-08-08 11:00",
            LastSeen: "2026-08-08 11:03",
            EventCount: 15,
            KnownOperations: null,
            AffectedCustomers: null,
            Duration: "3 min",
            SuggestedAction: "Verify the new WEBHOOK_SECRET value matches the current signing key registered in Stripe dashboard.",
            Ai: new AiAnalysisView(
                Title: "Stripe webhook signature mismatch after WEBHOOK_SECRET config change",
                ProbableRootCause: "A deployment at 2026-08-08T10:55:00Z modified the WEBHOOK_SECRET configuration key in the stripe-payments-webhook service. This change caused incoming Stripe webhook requests to fail signature verification, resulting in 401 errors and signature_mismatch failures on 15 requests between 11:00–11:03 UTC.",
                Confidence: 0.92,
                NeedsHumanReview: false,
                RecommendedActions: new[]
                {
                    "Verify the new WEBHOOK_SECRET value matches the current signing key registered in Stripe dashboard",
                    "If mismatch confirmed, rollback the WEBHOOK_SECRET to the previous value or update it to the correct key",
                    "Confirm Stripe webhook re-delivery or retry failed requests after correction",
                    "Review deployment change control process to catch config mismatches pre-deployment",
                }),
            NoAiReason: null,
            Evidence: new[]
            {
                ("Deployment", "Deployment to stripe-payments-webhook at 2026-08-08T10:55:00Z changed config key WEBHOOK_SECRET (changed=true)", "2026-08-08 11:00"),
            },
            Timeline: new[]
            {
                ("Deployment: WEBHOOK_SECRET changed", "2026-08-08 10:55"),
                ("First error observed", "2026-08-08 11:00"),
                ("AI analysis completed", "2026-08-08 11:03"),
            },
            Fingerprint: "3F8A21C9E5B7D046A19C82F5E6B3D8A47C1E9F0B2D5A6C8E1F3B7D9A2C4E6F81"),

        new(
            Id: "ses-ratelimit",
            TabLabel: "Email delivery rate limit (SES)",
            IntegrationName: "ses-prod",
            Category: "RateLimitError",
            Severity: "Medium",
            StatusLabel: "NeedsHumanReview",
            StatusBadgeClass: "fi-badge-review",
            FirstSeen: "2026-08-08 11:00",
            LastSeen: "2026-08-08 11:14",
            EventCount: 40,
            KnownOperations: null,
            AffectedCustomers: null,
            Duration: "14 min",
            SuggestedAction: "Verify that traffic throttling mitigation from the incident 12 days ago remains active and functional in ses-prod.",
            Ai: new AiAnalysisView(
                Title: "SES rate limit (429) errors affecting 40 requests",
                ProbableRootCause: "Sustained traffic to ses-prod has exceeded rate limits. A similar incident occurred 12 days ago and was resolved by adding traffic throttling, but the current episode suggests either throttling degradation, traffic growth exceeding throttling capacity, or throttling not being reapplied after a deployment.",
                Confidence: 0.75,
                NeedsHumanReview: true,
                RecommendedActions: new[]
                {
                    "Verify that traffic throttling mitigation from the incident 12 days ago remains active and functional in ses-prod",
                    "Review ses-prod request volume over the last 24 hours to determine if traffic has grown beyond throttling capacity",
                    "Check recent deployments to ses-prod to confirm throttling configuration was not removed or reverted",
                    "Temporarily increase SES rate limit quota or implement client-side request queuing if throttling cannot be restored",
                    "Establish alerting on rate limit error frequency to detect recurrence earlier than current 24-hour pattern",
                }),
            NoAiReason: null,
            Evidence: new[]
            {
                ("HistoricalIncident", "Similar RateLimitError incident occurred 12 days ago, resolved after traffic throttling was added", "2026-08-08 11:13"),
                ("PreviousEvent", "5 similar 429 events recorded in the last 24 hours", "2026-08-08 11:04"),
            },
            Timeline: new[]
            {
                ("First error observed", "2026-08-08 11:00"),
                ("Evidence collected: HistoricalIncident", "2026-08-08 11:13"),
                ("Evidence collected: PreviousEvent", "2026-08-08 11:04"),
                ("AI analysis completed", "2026-08-08 11:14"),
            },
            Fingerprint: "9C3E7A1F4B8D0256E9A3C7F1B4D8E0A25C9F3B7E1A4D8C0F2B6E9A3C5D7F1B90"),
    };

    public void OnGet()
    {
        // Bilerek boş - tüm veri yukarıda sabit (hardcoded) olarak tanımlı, hiçbir
        // DbContext/servis enjekte edilmiyor.
    }

    public sealed record DemoScenario(
        string Id,
        string TabLabel,
        string IntegrationName,
        string Category,
        string Severity,
        string StatusLabel,
        string StatusBadgeClass,
        string FirstSeen,
        string LastSeen,
        int EventCount,
        string? KnownOperations,
        string? AffectedCustomers,
        string Duration,
        string SuggestedAction,
        AiAnalysisView? Ai,
        string? NoAiReason,
        IReadOnlyList<(string SourceType, string Summary, string CollectedAt)> Evidence,
        IReadOnlyList<(string Label, string At)> Timeline,
        string Fingerprint);

    public sealed record AiAnalysisView(
        string Title,
        string ProbableRootCause,
        double Confidence,
        bool NeedsHumanReview,
        IReadOnlyList<string> RecommendedActions);
}
