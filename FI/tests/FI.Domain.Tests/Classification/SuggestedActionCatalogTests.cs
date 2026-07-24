using FI.Domain.Classification;
using FluentAssertions;

namespace FI.Domain.Tests.Classification;

public class SuggestedActionCatalogTests
{
    [Theory]
    [InlineData(EventCategory.SignatureError)]
    [InlineData(EventCategory.AuthenticationError)]
    [InlineData(EventCategory.AuthorizationError)]
    [InlineData(EventCategory.RateLimitError)]
    [InlineData(EventCategory.SchemaMismatch)]
    [InlineData(EventCategory.DuplicateEvent)]
    [InlineData(EventCategory.Timeout)]
    [InlineData(EventCategory.ProviderError)]
    [InlineData(EventCategory.ClientErrorOther)]
    [InlineData(EventCategory.NetworkError)]
    [InlineData(EventCategory.UnknownError)]
    public void For_EveryCategory_ReturnsNonEmptySuggestion(EventCategory category)
    {
        SuggestedActionCatalog.For(category).Should().NotBeNullOrWhiteSpace();
    }

    [Fact]
    public void For_DifferentCategories_ReturnDifferentSuggestions()
    {
        var auth = SuggestedActionCatalog.For(EventCategory.AuthenticationError);
        var rateLimit = SuggestedActionCatalog.For(EventCategory.RateLimitError);

        auth.Should().NotBe(rateLimit);
    }
}
