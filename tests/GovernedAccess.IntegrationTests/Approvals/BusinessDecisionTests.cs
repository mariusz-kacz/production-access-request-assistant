using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using GovernedAccess.Core.Domain;
using GovernedAccess.IntegrationTests.Infrastructure;
using GovernedAccess.Web.Authentication;
using GovernedAccess.Web.Demo;
using GovernedAccess.Web.Observability;
using GovernedAccess.Web.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace GovernedAccess.IntegrationTests.Approvals;

public sealed class BusinessDecisionTests(DefaultWebApplicationFixture fixture)
    : IClassFixture<DefaultWebApplicationFixture>
{
    private readonly GovernedAccessWebFactory factory = fixture.Factory;

    [Fact]
    public async Task ConfiguredApproverApprovalIgnoresOverpostedActorAndScope()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await factory.ResetDatabaseAsync(cancellationToken);
        var requestId = await CreateSubmittedRequestAsync(factory, cancellationToken);
        using var client = await factory.CreateAuthenticatedClientAsync(
            DemoPrincipalKeys.ClientAlphaApprover,
            cancellationToken);
        var body = ValidDecisionBody("Approve", " Approved for incident response. ");
        body["actorId"] = DemoDataIds.ClientBetaApproverPrincipalId;
        body["approverId"] = DemoDataIds.ClientBetaApproverPrincipalId;
        body["clientId"] = DemoDataIds.ClientBetaId;
        body["environmentId"] = DemoDataIds.ClientBetaEnvironmentId;
        body["approvedRoleId"] = ProductionRoleIds.Support;
        body["approvedDurationMinutes"] = 480;
        body["roles"] = new[] { "Requester", "DevOpsApprover" };
        using var request = CreateDecisionMessage(requestId, body);

        using var response = await GovernedAccessWebFactory.SendWithAntiforgeryAsync(
            client,
            request,
            cancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var responseBody = await ReadJsonAsync(response, cancellationToken);
        Assert.Equal(
            "AwaitingDevOpsApproval",
            responseBody.RootElement.GetProperty("status").GetString());
        Assert.False(responseBody.RootElement.TryGetProperty("version", out _));
        var actionCorrelationId = ReadCorrelationId(response);

        await using var scope = factory.Services.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<GovernedAccessDbContext>();
        var storedRequest = await dbContext.AccessRequests
            .AsNoTracking()
            .SingleAsync(item => item.Id == requestId, cancellationToken);
        var decision = await dbContext.ApprovalDecisions
            .AsNoTracking()
            .SingleAsync(cancellationToken);
        var auditEvent = await dbContext.AuditEvents
            .AsNoTracking()
            .SingleAsync(
                item => item.RequestId == requestId
                    && item.EventType == AuditEventType.BusinessDecision,
                cancellationToken);

        Assert.Equal(RequestStatus.AwaitingDevOpsApproval, storedRequest.Status);
        Assert.Equal(DemoDataIds.ClientAlphaId, storedRequest.ClientId);
        Assert.Equal(DemoDataIds.ClientAlphaEnvironmentId, storedRequest.EnvironmentId);
        Assert.Equal(ProductionRoleIds.ReadOnly, storedRequest.RequestedRoleId);
        Assert.Equal(2, storedRequest.PersistenceVersion);
        Assert.Equal(requestId, decision.RequestId);
        Assert.Equal(ApprovalStage.Business, decision.Stage);
        Assert.Equal(ApprovalOutcome.Approved, decision.Decision);
        Assert.Equal(DemoDataIds.ClientAlphaApproverPrincipalId, decision.ApproverId);
        Assert.Equal(ProductionRoleIds.ReadOnly, decision.ApprovedRoleId);
        Assert.Equal("Approved for incident response.", decision.Comment);
        Assert.Equal(GovernedAccessWebFactory.DefaultUtcNow, decision.DecidedAt);
        Assert.Equal(actionCorrelationId, decision.CorrelationId);
        AssertAuditEvidence(
            auditEvent,
            requestId,
            AuditEventType.BusinessDecision,
            DemoDataIds.ClientAlphaApproverPrincipalId,
            actionCorrelationId);
    }

    private static Dictionary<string, object?> ValidDecisionBody(
        string decision,
        string? comment)
    {
        return new Dictionary<string, object?>(StringComparer.Ordinal)
        {
            ["decision"] = decision,
            ["comment"] = comment,
        };
    }

    private static HttpRequestMessage CreateDecisionMessage(Guid requestId, object body)
    {
        return new HttpRequestMessage(
            HttpMethod.Post,
            $"/api/requests/{requestId:D}/business-decisions")
        {
            Content = JsonContent.Create(body),
        };
    }

    private static async Task<Guid> CreateSubmittedRequestAsync(
        GovernedAccessWebFactory factory,
        CancellationToken cancellationToken)
    {
        return (await factory.CreateRequestFixtureAsync(cancellationToken)).Id;
    }

    private static void AssertAuditEvidence(
        AuditEvent auditEvent,
        Guid requestId,
        AuditEventType eventType,
        string actorId,
        string correlationId)
    {
        Assert.Equal(requestId, auditEvent.RequestId);
        Assert.Equal(eventType, auditEvent.EventType);
        Assert.Equal(actorId, auditEvent.ActorId);
        Assert.Equal(GovernedAccessWebFactory.DefaultUtcNow, auditEvent.OccurredAt);
        Assert.Equal(correlationId, auditEvent.CorrelationId);
        Assert.False(string.IsNullOrWhiteSpace(auditEvent.OutcomeCode));
        using var details = JsonDocument.Parse(auditEvent.DetailsJson);
        Assert.Equal(JsonValueKind.Object, details.RootElement.ValueKind);
    }

    private static string ReadCorrelationId(HttpResponseMessage response)
    {
        Assert.True(
            response.Headers.TryGetValues(CorrelationContext.HeaderName, out var values));
        var correlationId = Assert.Single(values);
        Assert.False(string.IsNullOrWhiteSpace(correlationId));
        return correlationId;
    }

    private static async Task<JsonDocument> ReadJsonAsync(
        HttpResponseMessage response,
        CancellationToken cancellationToken)
    {
        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        return await JsonDocument.ParseAsync(
            stream,
            cancellationToken: cancellationToken);
    }
}
