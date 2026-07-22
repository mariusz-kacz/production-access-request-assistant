using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using GovernedAccess.Core.Application;
using GovernedAccess.Core.Domain;
using GovernedAccess.IntegrationTests.Infrastructure;
using GovernedAccess.Web.Authentication;
using GovernedAccess.Web.Demo;
using GovernedAccess.Web.Persistence;
using GovernedAccess.Web.Provisioning;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace GovernedAccess.IntegrationTests.Requests;

public sealed class RequestQueriesTests
{
    private const string DevOpsDecisionAction = "decideDevOpsRequest";
    private const string RetryProvisioningAction = "retryProvisioning";
    private const string Justification = "Investigate the active production incident.";

    [Fact]
    public async Task ListsAreParticipantFilteredAndMarkOnlyCurrentActions()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var factory = new GovernedAccessWebFactory();
        await factory.ResetDatabaseAsync(cancellationToken);
        var alphaAwaitingBusiness = await CreateRequestAsync(
            factory,
            DemoDataIds.ClientAlphaId,
            DemoDataIds.ClientAlphaEnvironmentId,
            DemoDataIds.PrimaryIncidentId,
            cancellationToken);
        var betaAwaitingBusiness = await CreateRequestAsync(
            factory,
            DemoDataIds.ClientBetaId,
            DemoDataIds.ClientBetaEnvironmentId,
            DemoDataIds.ClientBetaIncidentId,
            cancellationToken);
        var alphaAwaitingDevOps = await CreateRequestAsync(
            factory,
            DemoDataIds.ClientAlphaId,
            DemoDataIds.ClientAlphaEnvironmentId,
            DemoDataIds.PrimaryIncidentId,
            cancellationToken);
        await ApproveBusinessAsync(
            factory,
            alphaAwaitingDevOps,
            DemoPrincipalKeys.ClientAlphaApprover,
            cancellationToken);

        using var requester = await factory.CreateAuthenticatedClientAsync(
            DemoPrincipalKeys.Requester,
            cancellationToken);
        var requesterItems = await GetListAsync(requester, "/api/requests", cancellationToken);
        Assert.Equal(
            new[] { alphaAwaitingBusiness, betaAwaitingBusiness, alphaAwaitingDevOps }
                .Order(),
            requesterItems.Select(item => item.RequestId).Order());
        Assert.All(requesterItems, item => Assert.False(item.Actionable));

        using var alphaApprover = await factory.CreateAuthenticatedClientAsync(
            DemoPrincipalKeys.ClientAlphaApprover,
            cancellationToken);
        var alphaItems = await GetListAsync(
            alphaApprover,
            "/api/requests",
            cancellationToken);
        Assert.Equal(
            new[] { alphaAwaitingBusiness, alphaAwaitingDevOps }.Order(),
            alphaItems.Select(item => item.RequestId).Order());
        Assert.True(alphaItems.Single(item =>
            item.RequestId == alphaAwaitingBusiness).Actionable);
        Assert.False(alphaItems.Single(item =>
            item.RequestId == alphaAwaitingDevOps).Actionable);

        using var betaApprover = await factory.CreateAuthenticatedClientAsync(
            DemoPrincipalKeys.ClientBetaApprover,
            cancellationToken);
        var betaItems = await GetListAsync(betaApprover, "/api/requests", cancellationToken);
        var betaItem = Assert.Single(betaItems);
        Assert.Equal(betaAwaitingBusiness, betaItem.RequestId);
        Assert.True(betaItem.Actionable);

        using var devOps = await factory.CreateAuthenticatedClientAsync(
            DemoPrincipalKeys.DevOpsApprover,
            cancellationToken);
        var devOpsItems = await GetListAsync(devOps, "/api/requests", cancellationToken);
        var devOpsItem = Assert.Single(devOpsItems);
        Assert.Equal(alphaAwaitingDevOps, devOpsItem.RequestId);
        Assert.True(devOpsItem.Actionable);
    }

    [Fact]
    public async Task StatusFilterNarrowsResultsWithoutExpandingParticipantVisibility()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var factory = new GovernedAccessWebFactory();
        await factory.ResetDatabaseAsync(cancellationToken);
        var alphaAwaitingBusiness = await CreateRequestAsync(
            factory,
            DemoDataIds.ClientAlphaId,
            DemoDataIds.ClientAlphaEnvironmentId,
            DemoDataIds.PrimaryIncidentId,
            cancellationToken);
        var alphaAwaitingDevOps = await CreateRequestAsync(
            factory,
            DemoDataIds.ClientAlphaId,
            DemoDataIds.ClientAlphaEnvironmentId,
            DemoDataIds.PrimaryIncidentId,
            cancellationToken);
        await ApproveBusinessAsync(
            factory,
            alphaAwaitingDevOps,
            DemoPrincipalKeys.ClientAlphaApprover,
            cancellationToken);

        using var requester = await factory.CreateAuthenticatedClientAsync(
            DemoPrincipalKeys.Requester,
            cancellationToken);
        var filteredRequesterItems = await GetListAsync(
            requester,
            "/api/requests?status=AwaitingBusinessApproval",
            cancellationToken);
        Assert.Equal(alphaAwaitingBusiness, Assert.Single(filteredRequesterItems).RequestId);

        using var betaApprover = await factory.CreateAuthenticatedClientAsync(
            DemoPrincipalKeys.ClientBetaApprover,
            cancellationToken);
        var filteredNonparticipantItems = await GetListAsync(
            betaApprover,
            "/api/requests?status=AwaitingDevOpsApproval",
            cancellationToken);
        Assert.Empty(filteredNonparticipantItems);
    }

    [Fact]
    public async Task ActiveDetailContainsCurrentValidationAndCompleteOrderedEvidence()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var factory = new GovernedAccessWebFactory();
        await factory.ResetDatabaseAsync(cancellationToken);
        var requestId = await CreateActiveRequestAsync(factory, cancellationToken);
        using var client = await factory.CreateAuthenticatedClientAsync(
            DemoPrincipalKeys.Requester,
            cancellationToken);

        using var response = await client.GetAsync(
            $"/api/requests/{requestId:D}",
            cancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var body = await ReadJsonAsync(response, cancellationToken);
        var detail = body.RootElement;

        Assert.Equal(requestId, detail.GetProperty("requestId").GetGuid());
        Assert.Equal(nameof(RequestStatus.Active), detail.GetProperty("status").GetString());
        Assert.Empty(detail.GetProperty("availableActions").EnumerateArray());

        var validation = detail.GetProperty("validation");
        Assert.True(validation.GetProperty("isValid").GetBoolean());
        Assert.Empty(validation.GetProperty("fieldErrors").EnumerateArray());

        var decisions = detail.GetProperty("decisions").EnumerateArray().ToArray();
        Assert.Collection(
            decisions,
            business => AssertDecision(
                business,
                nameof(ApprovalStage.Business),
                DemoDataIds.ClientAlphaApproverPrincipalId,
                "Business approval for the immutable request."),
            devOps => AssertDecision(
                devOps,
                nameof(ApprovalStage.DevOps),
                DemoDataIds.DevOpsApproverPrincipalId,
                "DevOps approval for the immutable request."));

        var operation = detail.GetProperty("provisioningOperation");
        Assert.Equal(requestId, operation.GetProperty("requestId").GetGuid());
        Assert.Equal(
            DemoDataIds.ClientAlphaEnvironmentId,
            operation.GetProperty("environmentId").GetString());
        Assert.Equal(
            ProductionRoleIds.ReadOnly,
            operation.GetProperty("roleId").GetString());
        Assert.Equal(
            nameof(ProvisioningOperationStatus.Succeeded),
            operation.GetProperty("status").GetString());
        Assert.Equal(1, operation.GetProperty("attemptCount").GetInt32());
        Assert.Equal(
            ProtectedProvisioningService.SuccessCode,
            operation.GetProperty("lastOutcomeCode").GetString());

        var grant = detail.GetProperty("grant");
        Assert.Equal(requestId, grant.GetProperty("requestId").GetGuid());
        Assert.Equal(
            DemoDataIds.RequesterPrincipalId,
            grant.GetProperty("requesterId").GetString());
        Assert.Equal(
            DemoDataIds.ClientAlphaEnvironmentId,
            grant.GetProperty("environmentId").GetString());
        Assert.Equal(ProductionRoleIds.ReadOnly, grant.GetProperty("roleId").GetString());
        Assert.Equal(
            nameof(AccessGrantOutcome.Succeeded),
            grant.GetProperty("outcome").GetString());
        Assert.Equal(
            GovernedAccessWebFactory.DefaultUtcNow,
            grant.GetProperty("activatedAt").GetDateTimeOffset());
        Assert.Equal(
            GovernedAccessWebFactory.DefaultUtcNow.Add(AccessGrant.FixedLifetime),
            grant.GetProperty("expiresAt").GetDateTimeOffset());
        Assert.False(grant.GetProperty("isExpired").GetBoolean());

        var auditEvents = detail.GetProperty("auditEvents").EnumerateArray().ToArray();
        Assert.Equal(5, auditEvents.Length);
        Assert.Equal(
            new[]
            {
                nameof(AuditEventType.RequestCreated),
                nameof(AuditEventType.BusinessDecision),
                nameof(AuditEventType.DevOpsDecision),
                nameof(AuditEventType.ProvisioningAttempted),
                nameof(AuditEventType.ProvisioningSucceeded),
            }.Order(),
            auditEvents.Select(item => item.GetProperty("eventType").GetString()).Order());
        Assert.All(auditEvents, auditEvent =>
        {
            Assert.Equal(requestId, auditEvent.GetProperty("requestId").GetGuid());
            Assert.NotEqual(Guid.Empty, auditEvent.GetProperty("eventId").GetGuid());
            Assert.False(string.IsNullOrWhiteSpace(
                auditEvent.GetProperty("correlationId").GetString()));
            Assert.False(string.IsNullOrWhiteSpace(
                auditEvent.GetProperty("outcomeCode").GetString()));
            Assert.Equal(JsonValueKind.Object, auditEvent.GetProperty("details").ValueKind);
        });
        AssertOrderedByOccurredAt(auditEvents);
    }

    [Fact]
    public async Task LogicalExpiryUsesCurrentTimeWithoutChangingActiveWorkflowStatus()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var factory = new GovernedAccessWebFactory();
        await factory.ResetDatabaseAsync(cancellationToken);
        var requestId = await CreateActiveRequestAsync(factory, cancellationToken);
        using var client = await factory.CreateAuthenticatedClientAsync(
            DemoPrincipalKeys.Requester,
            cancellationToken);

        using (var beforeResponse = await client.GetAsync(
            $"/api/requests/{requestId:D}",
            cancellationToken))
        {
            Assert.Equal(HttpStatusCode.OK, beforeResponse.StatusCode);
            using var beforeBody = await ReadJsonAsync(beforeResponse, cancellationToken);
            Assert.False(beforeBody.RootElement
                .GetProperty("grant")
                .GetProperty("isExpired")
                .GetBoolean());
        }

        factory.Clock.Advance(AccessGrant.FixedLifetime);

        using var afterResponse = await client.GetAsync(
            $"/api/requests/{requestId:D}",
            cancellationToken);
        Assert.Equal(HttpStatusCode.OK, afterResponse.StatusCode);
        using var afterBody = await ReadJsonAsync(afterResponse, cancellationToken);
        Assert.Equal(
            nameof(RequestStatus.Active),
            afterBody.RootElement.GetProperty("status").GetString());
        Assert.True(afterBody.RootElement
            .GetProperty("grant")
            .GetProperty("isExpired")
            .GetBoolean());

        await using var scope = factory.Services.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<GovernedAccessDbContext>();
        var storedRequest = await dbContext.AccessRequests
            .AsNoTracking()
            .SingleAsync(item => item.Id == requestId, cancellationToken);
        Assert.Equal(RequestStatus.Active, storedRequest.Status);
    }

    [Fact]
    public async Task LaterStageActionsAreComputedFromActorAndStoredWorkflowState()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var factory = new GovernedAccessWebFactory();
        await factory.ResetDatabaseAsync(cancellationToken);
        var awaitingDevOpsId = await CreateRequestAsync(
            factory,
            DemoDataIds.ClientAlphaId,
            DemoDataIds.ClientAlphaEnvironmentId,
            DemoDataIds.PrimaryIncidentId,
            cancellationToken);
        await ApproveBusinessAsync(
            factory,
            awaitingDevOpsId,
            DemoPrincipalKeys.ClientAlphaApprover,
            cancellationToken);
        var provisioningFailedId = await CreateFailedRequestAsync(
            factory,
            cancellationToken);

        using var devOps = await factory.CreateAuthenticatedClientAsync(
            DemoPrincipalKeys.DevOpsApprover,
            cancellationToken);
        var decisionActions = await GetActionsAsync(
            devOps,
            awaitingDevOpsId,
            cancellationToken);
        Assert.Equal(DevOpsDecisionAction, Assert.Single(decisionActions));
        var retryActions = await GetActionsAsync(
            devOps,
            provisioningFailedId,
            cancellationToken);
        Assert.Equal(RetryProvisioningAction, Assert.Single(retryActions));

        using var requester = await factory.CreateAuthenticatedClientAsync(
            DemoPrincipalKeys.Requester,
            cancellationToken);
        Assert.Empty(await GetActionsAsync(
            requester,
            awaitingDevOpsId,
            cancellationToken));
        Assert.Empty(await GetActionsAsync(
            requester,
            provisioningFailedId,
            cancellationToken));
    }

    [Fact]
    public async Task ActiveRequestRemainsInvisibleToWrongClientNonparticipant()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var factory = new GovernedAccessWebFactory();
        await factory.ResetDatabaseAsync(cancellationToken);
        var requestId = await CreateActiveRequestAsync(factory, cancellationToken);
        using var client = await factory.CreateAuthenticatedClientAsync(
            DemoPrincipalKeys.ClientBetaApprover,
            cancellationToken);

        var items = await GetListAsync(client, "/api/requests", cancellationToken);
        Assert.DoesNotContain(items, item => item.RequestId == requestId);

        using var response = await client.GetAsync(
            $"/api/requests/{requestId:D}",
            cancellationToken);
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        using var problem = await ReadJsonAsync(response, cancellationToken);
        Assert.Equal(
            "request_not_found",
            problem.RootElement.GetProperty("code").GetString());
    }

    private static void AssertDecision(
        JsonElement decision,
        string stage,
        string approverId,
        string comment)
    {
        Assert.Equal(stage, decision.GetProperty("stage").GetString());
        Assert.Equal(
            nameof(ApprovalOutcome.Approved),
            decision.GetProperty("decision").GetString());
        Assert.Equal(approverId, decision.GetProperty("approverId").GetString());
        Assert.Equal(
            ProductionRoleIds.ReadOnly,
            decision.GetProperty("approvedRoleId").GetString());
        Assert.Equal(comment, decision.GetProperty("comment").GetString());
        Assert.Equal(
            GovernedAccessWebFactory.DefaultUtcNow,
            decision.GetProperty("decidedAt").GetDateTimeOffset());
        Assert.False(string.IsNullOrWhiteSpace(
            decision.GetProperty("correlationId").GetString()));
    }

    private static void AssertOrderedByOccurredAt(IReadOnlyList<JsonElement> auditEvents)
    {
        var occurredAt = auditEvents
            .Select(item => item.GetProperty("occurredAt").GetDateTimeOffset())
            .ToArray();
        Assert.Equal(occurredAt.Order(), occurredAt);
    }

    private static async Task<IReadOnlyList<RequestListItem>> GetListAsync(
        HttpClient client,
        string path,
        CancellationToken cancellationToken)
    {
        using var response = await client.GetAsync(path, cancellationToken);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var body = await ReadJsonAsync(response, cancellationToken);
        return body.RootElement
            .GetProperty("items")
            .EnumerateArray()
            .Select(item => new RequestListItem(
                item.GetProperty("requestId").GetGuid(),
                item.GetProperty("clientId").GetString()!,
                item.GetProperty("environmentId").GetString()!,
                item.GetProperty("requesterId").GetString()!,
                item.GetProperty("status").GetString()!,
                item.GetProperty("lastModifiedAt").GetDateTimeOffset(),
                item.GetProperty("actionable").GetBoolean()))
            .ToArray();
    }

    private static async Task<string?[]> GetActionsAsync(
        HttpClient client,
        Guid requestId,
        CancellationToken cancellationToken)
    {
        using var response = await client.GetAsync(
            $"/api/requests/{requestId:D}",
            cancellationToken);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var body = await ReadJsonAsync(response, cancellationToken);
        return body.RootElement
            .GetProperty("availableActions")
            .EnumerateArray()
            .Select(action => action.GetString())
            .ToArray();
    }

    private static async Task<Guid> CreateActiveRequestAsync(
        GovernedAccessWebFactory factory,
        CancellationToken cancellationToken)
    {
        var requestId = await CreateRequestAsync(
            factory,
            DemoDataIds.ClientAlphaId,
            DemoDataIds.ClientAlphaEnvironmentId,
            DemoDataIds.PrimaryIncidentId,
            cancellationToken);
        await ApproveBusinessAsync(
            factory,
            requestId,
            DemoPrincipalKeys.ClientAlphaApprover,
            cancellationToken);
        using var client = await factory.CreateAuthenticatedClientAsync(
            DemoPrincipalKeys.DevOpsApprover,
            cancellationToken);
        using var request = CreateDecisionMessage(
            requestId,
            "devops-decisions",
            "DevOps approval for the immutable request.");
        using var response = await GovernedAccessWebFactory.SendWithAntiforgeryAsync(
            client,
            request,
            cancellationToken);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return requestId;
    }

    private static async Task<Guid> CreateFailedRequestAsync(
        GovernedAccessWebFactory factory,
        CancellationToken cancellationToken)
    {
        var requestId = await CreateRequestAsync(
            factory,
            DemoDataIds.ClientAlphaId,
            DemoDataIds.ClientAlphaEnvironmentId,
            DemoDataIds.PrimaryIncidentId,
            cancellationToken);
        await ApproveBusinessAsync(
            factory,
            requestId,
            DemoPrincipalKeys.ClientAlphaApprover,
            cancellationToken);
        var control = factory.Services
            .GetRequiredService<SyntheticAccessProvisionerControl>();
        control.Configure(SyntheticAccessProvisioningBehavior.Fail);
        using var client = await factory.CreateAuthenticatedClientAsync(
            DemoPrincipalKeys.DevOpsApprover,
            cancellationToken);
        using var request = CreateDecisionMessage(
            requestId,
            "devops-decisions",
            "DevOps approval for the immutable request.");
        using var response = await GovernedAccessWebFactory.SendWithAntiforgeryAsync(
            client,
            request,
            cancellationToken);
        control.Reset();

        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        return requestId;
    }

    private static async Task<Guid> CreateRequestAsync(
        GovernedAccessWebFactory factory,
        string clientId,
        string environmentId,
        string incidentId,
        CancellationToken cancellationToken)
    {
        using var client = await factory.CreateAuthenticatedClientAsync(
            DemoPrincipalKeys.Requester,
            cancellationToken);
        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/requests")
        {
            Content = JsonContent.Create(new
            {
                clientId,
                environmentId,
                requestedRole = ProductionRoleIds.ReadOnly,
                justification = Justification,
                incidentId,
            }),
        };
        using var response = await GovernedAccessWebFactory.SendWithAntiforgeryAsync(
            client,
            request,
            cancellationToken);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        using var body = await ReadJsonAsync(response, cancellationToken);
        return body.RootElement.GetProperty("requestId").GetGuid();
    }

    private static async Task ApproveBusinessAsync(
        GovernedAccessWebFactory factory,
        Guid requestId,
        string principalKey,
        CancellationToken cancellationToken)
    {
        using var client = await factory.CreateAuthenticatedClientAsync(
            principalKey,
            cancellationToken);
        using var request = CreateDecisionMessage(
            requestId,
            "business-decisions",
            "Business approval for the immutable request.");
        using var response = await GovernedAccessWebFactory.SendWithAntiforgeryAsync(
            client,
            request,
            cancellationToken);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    private static HttpRequestMessage CreateDecisionMessage(
        Guid requestId,
        string decisionPath,
        string comment) =>
        new(HttpMethod.Post, $"/api/requests/{requestId:D}/{decisionPath}")
        {
            Content = JsonContent.Create(new
            {
                decision = "Approve",
                comment,
            }),
        };

    private static async Task<JsonDocument> ReadJsonAsync(
        HttpResponseMessage response,
        CancellationToken cancellationToken)
    {
        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        return await JsonDocument.ParseAsync(
            stream,
            cancellationToken: cancellationToken);
    }

    private sealed record RequestListItem(
        Guid RequestId,
        string ClientId,
        string EnvironmentId,
        string RequesterId,
        string Status,
        DateTimeOffset LastModifiedAt,
        bool Actionable);
}
