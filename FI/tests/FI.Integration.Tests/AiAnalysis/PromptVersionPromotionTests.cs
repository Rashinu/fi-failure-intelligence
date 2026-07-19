using System.Net;
using System.Net.Http.Json;
using FI.Application.AiAnalysis;
using FI.Integration.Tests.Fixtures;
using FluentAssertions;
using Xunit;

namespace FI.Integration.Tests.AiAnalysis;

/// <summary>
/// Bkz. docs/FAILURE_INTELLIGENCE_ARCHITECTURE.md Bölüm 26.3 (prompt version yaşam döngüsü) ve
/// Bölüm 26.4 (golden dataset gate). <see cref="FiApiFactory.FakeAiClient"/> geneldir (golden
/// dataset senaryolarına özel değildir - bkz. <c>Eval/ScriptedAiAnalysisClient</c>), bu yüzden
/// burada "onaylandı/reddedildi" sonucunun kesin değerini değil, gate'in gerçekten çalıştığını
/// (değerlendirme yapıldı, sonuç kalıcı hale getirildi, karar tutarlı) doğruluyoruz.
/// </summary>
public class PromptVersionPromotionTests : IClassFixture<FiApiFactory>
{
    private readonly FiApiFactory _factory;

    public PromptVersionPromotionTests(FiApiFactory factory)
    {
        _factory = factory;
        _factory.FakeAiClient.NextResponseOverride = null;
        _factory.FakeAiClient.SimulateCallFailure = false;
    }

    [Fact]
    public async Task CreateDraft_ThenGetById_ReturnsCreatedDraft()
    {
        var client = _factory.CreateClient();

        var createResponse = await client.PostAsJsonAsync("/api/v1/prompt-versions",
            new CreatePromptVersionRequest($"fi-root-cause-{Guid.NewGuid():N}", "You are an evidence-only assistant."));
        createResponse.StatusCode.Should().Be(HttpStatusCode.Created);
        var created = await createResponse.Content.ReadFromJsonAsync<PromptVersionResponse>();

        var getResponse = await client.GetAsync($"/api/v1/prompt-versions/{created!.Id}");
        var fetched = await getResponse.Content.ReadFromJsonAsync<PromptVersionResponse>();

        fetched!.Status.Should().Be("Draft");
        fetched.RolloutPercentage.Should().Be(0);
        fetched.EvalOverallAverage.Should().BeNull();
    }

    [Fact]
    public async Task Promote_UnknownId_ReturnsNotFound()
    {
        var client = _factory.CreateClient();

        var response = await client.PostAsync($"/api/v1/prompt-versions/{Guid.NewGuid()}/promote", null);

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Promote_AlreadyActiveVersion_ReturnsConflict()
    {
        var client = _factory.CreateClient();

        var listResponse = await client.GetAsync("/api/v1/prompt-versions");
        var versions = await listResponse.Content.ReadFromJsonAsync<List<PromptVersionResponse>>();
        var seededActive = versions!.Single(v => v.Status == "Active");

        var promoteResponse = await client.PostAsync($"/api/v1/prompt-versions/{seededActive.Id}/promote", null);

        promoteResponse.StatusCode.Should().Be(HttpStatusCode.Conflict);
    }

    [Fact]
    public async Task Promote_Draft_RunsGoldenDatasetAndRecordsEvalResult()
    {
        var client = _factory.CreateClient();

        var createResponse = await client.PostAsJsonAsync("/api/v1/prompt-versions",
            new CreatePromptVersionRequest($"fi-root-cause-{Guid.NewGuid():N}", "You are an evidence-only assistant."));
        var created = await createResponse.Content.ReadFromJsonAsync<PromptVersionResponse>();

        var promoteResponse = await client.PostAsync($"/api/v1/prompt-versions/{created!.Id}/promote", null);
        promoteResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        var decision = await promoteResponse.Content.ReadFromJsonAsync<PromotePromptVersionResponse>();

        decision!.CandidatePerDimensionAverages.Should().NotBeEmpty();
        decision.CandidateOverallAverage.Should().BeInRange(0, 1);

        var refetched = await (await client.GetAsync($"/api/v1/prompt-versions/{created.Id}")).Content.ReadFromJsonAsync<PromptVersionResponse>();
        refetched!.EvalOverallAverage.Should().Be(decision.CandidateOverallAverage);
        refetched.EvaluatedAt.Should().NotBeNull();

        // Approved veya reddedildi - ama durum tutarlı olmalı: onaylandıysa ACTIVE, değilse hâlâ DRAFT.
        refetched.Status.Should().Be(decision.Approved ? "Active" : "Draft");
    }

    [Fact]
    public async Task Promote_SecondDraftAfterFirstApproved_ComparesAgainstCachedBaseline()
    {
        var client = _factory.CreateClient();

        var firstDraftResponse = await client.PostAsJsonAsync("/api/v1/prompt-versions",
            new CreatePromptVersionRequest($"fi-root-cause-{Guid.NewGuid():N}", "You are an evidence-only assistant."));
        var firstDraft = await firstDraftResponse.Content.ReadFromJsonAsync<PromptVersionResponse>();
        await client.PostAsync($"/api/v1/prompt-versions/{firstDraft!.Id}/promote", null);

        var secondDraftResponse = await client.PostAsJsonAsync("/api/v1/prompt-versions",
            new CreatePromptVersionRequest($"fi-root-cause-{Guid.NewGuid():N}", "You are an evidence-only assistant, v2."));
        var secondDraft = await secondDraftResponse.Content.ReadFromJsonAsync<PromptVersionResponse>();

        var secondPromoteResponse = await client.PostAsync($"/api/v1/prompt-versions/{secondDraft!.Id}/promote", null);
        secondPromoteResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        var secondDecision = await secondPromoteResponse.Content.ReadFromJsonAsync<PromotePromptVersionResponse>();

        // Since both drafts use the identical FakeAiAnalysisClient behavior, the second candidate's
        // score should be identical to the first's (no spurious regression against its own baseline).
        secondDecision!.Reasons.Should().NotContain(r => r.Contains("regresyon", StringComparison.OrdinalIgnoreCase));
    }
}
