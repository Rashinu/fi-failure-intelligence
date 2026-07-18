using FI.Domain.Classification;
using FluentAssertions;
using Xunit;

namespace FI.Domain.Tests.Classification;

public class EventClassifierTests
{
    private static ClassificationInput Base(
        int statusCode = 200,
        bool hasInvalidSignatureHeader = false,
        bool hasRetryAfterHeader = false,
        string? errorBodyText = null,
        bool hasSchemaValidationFailure = false,
        IReadOnlyList<string>? missingSchemaFields = null,
        bool isDuplicateWithinWindow = false,
        bool isTimeoutError = false,
        bool isNetworkError = false,
        string? networkExceptionType = null,
        string? normalizedErrorCode = null,
        string? normalizedEndpointPath = null) => new(
        statusCode, hasInvalidSignatureHeader, hasRetryAfterHeader, errorBodyText,
        hasSchemaValidationFailure, missingSchemaFields ?? Array.Empty<string>(),
        isDuplicateWithinWindow, isTimeoutError, isNetworkError, networkExceptionType,
        normalizedErrorCode, normalizedEndpointPath);

    [Fact]
    public void HasInvalidSignatureHeader_ClassifiesAsSignatureError()
    {
        var result = EventClassifier.Classify(Base(statusCode: 400, hasInvalidSignatureHeader: true));
        result.Category.Should().Be(EventCategory.SignatureError);
    }

    [Fact]
    public void SignatureErrorBodyText_ClassifiesAsSignatureError()
    {
        var result = EventClassifier.Classify(Base(statusCode: 400, errorBodyText: "Invalid Signature provided"));
        result.Category.Should().Be(EventCategory.SignatureError);
    }

    [Fact]
    public void NonSignatureCase_DoesNotClassifyAsSignatureError()
    {
        var result = EventClassifier.Classify(Base(statusCode: 500));
        result.Category.Should().NotBe(EventCategory.SignatureError);
    }

    [Fact]
    public void StatusCode401_ClassifiesAsAuthenticationError()
    {
        var result = EventClassifier.Classify(Base(statusCode: 401, normalizedErrorCode: "invalid_api_key"));
        result.Category.Should().Be(EventCategory.AuthenticationError);
        result.ErrorSignature.Should().Be("401_invalid_api_key");
    }

    [Fact]
    public void StatusCode403WithoutAuthMessage_DoesNotClassifyAsAuthenticationError()
    {
        var result = EventClassifier.Classify(Base(statusCode: 403, errorBodyText: "permission_denied"));
        result.Category.Should().NotBe(EventCategory.AuthenticationError);
    }

    [Fact]
    public void StatusCode403WithForbiddenMessage_ClassifiesAsAuthorizationError()
    {
        var result = EventClassifier.Classify(Base(statusCode: 403, errorBodyText: "permission_denied for this scope"));
        result.Category.Should().Be(EventCategory.AuthorizationError);
    }

    [Fact]
    public void StatusCode401_NeverClassifiesAsAuthorizationError()
    {
        var result = EventClassifier.Classify(Base(statusCode: 401, errorBodyText: "permission_denied"));
        result.Category.Should().Be(EventCategory.AuthenticationError);
    }

    [Fact]
    public void StatusCode429_ClassifiesAsRateLimitError()
    {
        var result = EventClassifier.Classify(Base(statusCode: 429));
        result.Category.Should().Be(EventCategory.RateLimitError);
        result.ErrorSignature.Should().Be("rate_limit");
    }

    [Fact]
    public void RetryAfterHeaderWithout429_ClassifiesAsRateLimitError()
    {
        var result = EventClassifier.Classify(Base(statusCode: 503, hasRetryAfterHeader: true));
        result.Category.Should().Be(EventCategory.RateLimitError);
    }

    [Fact]
    public void StatusCode503WithoutRetryAfter_DoesNotClassifyAsRateLimitError()
    {
        var result = EventClassifier.Classify(Base(statusCode: 503));
        result.Category.Should().NotBe(EventCategory.RateLimitError);
    }

    [Fact]
    public void SchemaValidationFailure_ClassifiesAsSchemaMismatch()
    {
        var result = EventClassifier.Classify(Base(statusCode: 400, hasSchemaValidationFailure: true, missingSchemaFields: new[] { "customer_email" }));
        result.Category.Should().Be(EventCategory.SchemaMismatch);
        result.ErrorSignature.Should().Be("field_missing:customer_email");
    }

    [Fact]
    public void NoSchemaFailureFlag_DoesNotClassifyAsSchemaMismatch()
    {
        var result = EventClassifier.Classify(Base(statusCode: 400));
        result.Category.Should().NotBe(EventCategory.SchemaMismatch);
    }

    [Fact]
    public void IsDuplicateWithinWindow_ClassifiesAsDuplicateEvent()
    {
        var result = EventClassifier.Classify(Base(statusCode: 200, isDuplicateWithinWindow: true));
        result.Category.Should().Be(EventCategory.DuplicateEvent);
    }

    [Fact]
    public void NotDuplicate_DoesNotClassifyAsDuplicateEvent()
    {
        var result = EventClassifier.Classify(Base(statusCode: 200));
        result.Category.Should().NotBe(EventCategory.DuplicateEvent);
    }

    [Fact]
    public void IsTimeoutError_ClassifiesAsTimeout()
    {
        var result = EventClassifier.Classify(Base(statusCode: 0, isTimeoutError: true, normalizedEndpointPath: "/v1/payments/{id}/capture"));
        result.Category.Should().Be(EventCategory.Timeout);
        result.ErrorSignature.Should().Be("/v1/payments/{id}/capture");
    }

    [Theory]
    [InlineData(500)]
    [InlineData(503)]
    [InlineData(599)]
    public void ServerErrorStatusCodes_ClassifyAsProviderError(int statusCode)
    {
        var result = EventClassifier.Classify(Base(statusCode: statusCode));
        result.Category.Should().Be(EventCategory.ProviderError);
        result.ErrorSignature.Should().Be(statusCode.ToString());
    }

    [Theory]
    [InlineData(400)]
    [InlineData(404)]
    [InlineData(422)]
    public void UnmatchedClientErrorStatusCodes_ClassifyAsClientErrorOther(int statusCode)
    {
        var result = EventClassifier.Classify(Base(statusCode: statusCode));
        result.Category.Should().Be(EventCategory.ClientErrorOther);
    }

    [Fact]
    public void IsNetworkError_ClassifiesAsNetworkError()
    {
        var result = EventClassifier.Classify(Base(statusCode: 0, isNetworkError: true, networkExceptionType: "ConnectionRefused"));
        result.Category.Should().Be(EventCategory.NetworkError);
        result.ErrorSignature.Should().Be("ConnectionRefused_flag");
    }

    [Fact]
    public void NoRuleMatches_ClassifiesAsUnknownError()
    {
        var result = EventClassifier.Classify(Base(statusCode: 200));
        result.Category.Should().Be(EventCategory.UnknownError);
    }

    [Fact]
    public void SamePrefixText_ProducesSameUnknownErrorSignature()
    {
        var r1 = EventClassifier.Classify(Base(statusCode: 200, errorBodyText: "weird body text"));
        var r2 = EventClassifier.Classify(Base(statusCode: 200, errorBodyText: "weird body text"));
        r1.ErrorSignature.Should().Be(r2.ErrorSignature);
    }

    [Fact]
    public void SignatureError_TakesPriorityOverAuthenticationError()
    {
        var result = EventClassifier.Classify(Base(statusCode: 401, hasInvalidSignatureHeader: true));
        result.Category.Should().Be(EventCategory.SignatureError);
    }

    [Fact]
    public void StatusCode401AlwaysClassifiesAsAuthenticationErrorNotProviderError()
    {
        var result = EventClassifier.Classify(Base(statusCode: 401));
        result.Category.Should().Be(EventCategory.AuthenticationError);
    }
}
