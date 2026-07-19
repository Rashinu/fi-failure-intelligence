namespace FI.Domain.AiAnalysis.Eval;

/// <summary>
/// Bkz. Bölüm 26.4 — 20 sabit senaryodan biri. <c>Id</c> kalıcıdır (rapor/regresyon
/// karşılaştırmasında senaryo kimliği olarak kullanılır); <c>Description</c> yalnızca insan
/// okunabilirliği içindir.
/// </summary>
public sealed record GoldenScenario(
    string Id,
    string Description,
    DeterministicClassificationInput Deterministic,
    IReadOnlyList<EvidenceInput> Evidence,
    ScenarioExpectation Expectation);
