using GovernedAccess.Core.Application;
using GovernedAccess.Web.Endpoints;
using GovernedAccess.Web.Observability;
using Microsoft.AspNetCore.Http;

namespace GovernedAccess.IntegrationTests.Endpoints;

public sealed class ProblemDetailsMappingTests
{
    [Theory]
    [InlineData(ApplicationFailureKind.Validation, 422, "Validation failed.")]
    [InlineData(ApplicationFailureKind.InvalidInput, 400, "Invalid request.")]
    [InlineData(ApplicationFailureKind.NotFound, 404, "Resource not found.")]
    [InlineData(ApplicationFailureKind.Unauthenticated, 401, "Authentication required.")]
    [InlineData(ApplicationFailureKind.Unauthorized, 403, "Access denied.")]
    [InlineData(ApplicationFailureKind.StaleVersion, 409, "Request version is stale.")]
    [InlineData(ApplicationFailureKind.InvalidTransition, 409, "Workflow transition is invalid.")]
    [InlineData(ApplicationFailureKind.ConcurrencyConflict, 409, "Concurrent update conflict.")]
    [InlineData(ApplicationFailureKind.Timeout, 503, "A dependency timed out.")]
    [InlineData(ApplicationFailureKind.Cancelled, 499, "Request cancelled.")]
    [InlineData(ApplicationFailureKind.DependencyUnavailable, 503, "A dependency is unavailable.")]
    [InlineData(ApplicationFailureKind.DependencyFailure, 503, "A dependency operation failed.")]
    public void ToProblemDetailsMapsEveryFailureKind(
        ApplicationFailureKind kind,
        int expectedStatus,
        string expectedTitle)
    {
        var context = CreateContext("correlation-123");
        var failure = new ApplicationFailure(kind, "stable_code", "Safe detail.");

        var result = failure.ToProblemDetails(context);

        Assert.Equal(expectedStatus, result.StatusCode);
        Assert.Equal("application/problem+json", result.ContentType);
        Assert.Equal(expectedTitle, result.ProblemDetails.Title);
        Assert.Equal("Safe detail.", result.ProblemDetails.Detail);
        Assert.Equal("stable_code", result.ProblemDetails.Extensions["code"]);
        Assert.Equal("correlation-123", result.ProblemDetails.Extensions["correlationId"]);
    }

    [Fact]
    public void ToProblemDetailsIncludesOptionalFieldErrorsAndCurrentVersion()
    {
        var context = CreateContext("correlation-456");
        ApplicationFieldError[] fieldErrors =
        [
            new("durationMinutes", "duration_too_long", "Duration exceeds the maximum."),
            new("incidentId", "incident_inactive", "Incident is not active."),
        ];
        var failure = new ApplicationFailure(
            ApplicationFailureKind.Validation,
            "request_validation_failed",
            "Correct the highlighted fields.",
            fieldErrors,
            currentVersion: 3);

        var result = failure.ToProblemDetails(context);

        var mappedErrors = Assert.IsAssignableFrom<IReadOnlyList<ApplicationFieldError>>(
            result.ProblemDetails.Extensions["fieldErrors"]);
        Assert.Equal(fieldErrors, mappedErrors);
        Assert.Equal(3, result.ProblemDetails.Extensions["currentVersion"]);
    }

    [Fact]
    public void ToProblemDetailsOmitsOptionalExtensionsWhenTheyDoNotApply()
    {
        var context = CreateContext("correlation-789");
        var failure = new ApplicationFailure(
            ApplicationFailureKind.NotFound,
            "request_not_found",
            "The request was not found.");

        var result = failure.ToProblemDetails(context);

        Assert.DoesNotContain("fieldErrors", result.ProblemDetails.Extensions.Keys);
        Assert.DoesNotContain("currentVersion", result.ProblemDetails.Extensions.Keys);
    }

    private static DefaultHttpContext CreateContext(string correlationId)
    {
        var context = new DefaultHttpContext();
        context.Response.Headers[CorrelationContext.HeaderName] = correlationId;
        return context;
    }
}
