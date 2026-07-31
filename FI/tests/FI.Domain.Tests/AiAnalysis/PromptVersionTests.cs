using FI.Domain.AiAnalysis;
using FluentAssertions;

namespace FI.Domain.Tests.AiAnalysis;

public class PromptVersionTests
{
    [Fact]
    public void CreateDraft_SetsDraftStatus_AndZeroRollout()
    {
        var version = PromptVersion.CreateDraft("fi-root-cause-v2", "system prompt text");

        version.Status.Should().Be(PromptVersionStatus.Draft);
        version.RolloutPercentage.Should().Be(0);
        version.EvalOverallAverage.Should().BeNull();
    }

    [Fact]
    public void CreateActive_SetsActiveStatus_AndFullRollout()
    {
        var version = PromptVersion.CreateActive("fi-root-cause-v1", "system prompt text");

        version.Status.Should().Be(PromptVersionStatus.Active);
        version.RolloutPercentage.Should().Be(100);
    }

    /// <summary>
    /// Bkz. docs/product/M19_CLOSE_THE_PRODUCT_LOOP.md - "Aktif Prompt Kalitesi" bölümü.
    /// CreateActive, PromptPromotionGate'i BİLEREK atlıyor (yalnızca ilk bootstrap seed'i için,
    /// bkz. Program.cs) - bu yüzden hiçbir zaman sahte bir eval skoru üretmemeli.
    /// EvalOverallAverage/EvaluatedAt null kalmalı; API/UI bunu "hiç değerlendirilmedi" olarak
    /// dürüstçe gösterir, uydurulmuş bir 0.85+ skor değil.
    /// </summary>
    [Fact]
    public void CreateActive_NeverFakesAnEvalScore()
    {
        var version = PromptVersion.CreateActive("fi-root-cause-v1", "system prompt text");

        version.EvalOverallAverage.Should().BeNull();
        version.EvalPerDimensionJson.Should().BeNull();
        version.EvaluatedAt.Should().BeNull();
    }

    [Fact]
    public void RecordEvalResult_SetsOverallAverageAndTimestamp()
    {
        var version = PromptVersion.CreateDraft("v2", "prompt");

        version.RecordEvalResult(0.91, """{"CategoryEcho":1.0}""");

        version.EvalOverallAverage.Should().Be(0.91);
        version.EvalPerDimensionJson.Should().Contain("CategoryEcho");
        version.EvaluatedAt.Should().NotBeNull();
    }

    [Fact]
    public void Activate_TransitionsToActive_WithFullRollout()
    {
        var version = PromptVersion.CreateDraft("v2", "prompt");

        version.Activate();

        version.Status.Should().Be(PromptVersionStatus.Active);
        version.RolloutPercentage.Should().Be(100);
    }

    [Fact]
    public void Deprecate_TransitionsToDeprecated_WithZeroRollout()
    {
        var version = PromptVersion.CreateActive("v1", "prompt");

        version.Deprecate();

        version.Status.Should().Be(PromptVersionStatus.Deprecated);
        version.RolloutPercentage.Should().Be(0);
    }
}
