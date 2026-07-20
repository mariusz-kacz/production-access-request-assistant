using GovernedAccess.Core.Application;
using GovernedAccess.Web.Observability;
using Microsoft.AspNetCore.Mvc;

namespace GovernedAccess.Web.Controllers;

public static class ProblemDetailsMapping
{
    public static ObjectResult ToProblemDetails(
        this ApplicationFailure failure,
        HttpContext context)
    {
        ArgumentNullException.ThrowIfNull(failure);
        ArgumentNullException.ThrowIfNull(context);

        var (status, title) = GetStatusAndTitle(failure.Kind);
        var problem = new ProblemDetails
        {
            Status = status,
            Title = title,
            Detail = failure.Message,
        };

        problem.Extensions["code"] = failure.Code;
        problem.Extensions["correlationId"] = GetCorrelationId(context);

        return CreateResult(problem, status);
    }

    public static ObjectResult ToValidationProblemDetails(
        string code,
        string detail,
        IEnumerable<FieldValidationError> fieldErrors,
        HttpContext context)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(code);
        ArgumentException.ThrowIfNullOrWhiteSpace(detail);
        ArgumentNullException.ThrowIfNull(fieldErrors);
        ArgumentNullException.ThrowIfNull(context);

        var errors = fieldErrors.ToArray();
        if (errors.Length == 0)
        {
            throw new ArgumentException(
                "At least one field validation error is required.",
                nameof(fieldErrors));
        }

        var problem = new ProblemDetails
        {
            Status = StatusCodes.Status422UnprocessableEntity,
            Title = "Validation failed.",
            Detail = detail,
        };

        problem.Extensions["code"] = code;
        problem.Extensions["correlationId"] = GetCorrelationId(context);
        problem.Extensions["fieldErrors"] = errors;
        return CreateResult(problem, StatusCodes.Status422UnprocessableEntity);
    }

    private static ObjectResult CreateResult(ProblemDetails problem, int statusCode)
    {
        var result = new ObjectResult(problem)
        {
            StatusCode = statusCode,
        };
        result.ContentTypes.Add("application/problem+json");
        return result;
    }

    private static (int Status, string Title) GetStatusAndTitle(
        ApplicationFailureKind kind)
    {
        return kind switch
        {
            ApplicationFailureKind.InvalidInput =>
                (StatusCodes.Status400BadRequest, "Invalid request."),
            ApplicationFailureKind.NotFound =>
                (StatusCodes.Status404NotFound, "Resource not found."),
            ApplicationFailureKind.Unauthenticated =>
                (StatusCodes.Status401Unauthorized, "Authentication required."),
            ApplicationFailureKind.Unauthorized =>
                (StatusCodes.Status403Forbidden, "Access denied."),
            ApplicationFailureKind.InvalidTransition =>
                (StatusCodes.Status409Conflict, "Workflow transition is invalid."),
            ApplicationFailureKind.ConcurrencyConflict =>
                (StatusCodes.Status409Conflict, "Concurrent update conflict."),
            ApplicationFailureKind.Timeout =>
                (StatusCodes.Status503ServiceUnavailable, "A dependency timed out."),
            ApplicationFailureKind.Cancelled =>
                (StatusCodes.Status499ClientClosedRequest, "Request cancelled."),
            ApplicationFailureKind.DependencyUnavailable =>
                (StatusCodes.Status503ServiceUnavailable, "A dependency is unavailable."),
            ApplicationFailureKind.DependencyFailure =>
                (StatusCodes.Status503ServiceUnavailable, "A dependency operation failed."),
            _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, null),
        };
    }

    private static string GetCorrelationId(HttpContext context)
    {
        var correlationId = context.Response.Headers[CorrelationContext.HeaderName].ToString();
        return string.IsNullOrWhiteSpace(correlationId)
            ? context.TraceIdentifier
            : correlationId;
    }
}
