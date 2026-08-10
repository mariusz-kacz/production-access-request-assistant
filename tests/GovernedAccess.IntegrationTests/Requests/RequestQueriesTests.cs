using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using GovernedAccess.Core.Application;
using GovernedAccess.Core.Domain;
using GovernedAccess.Core.Ports;
using GovernedAccess.IntegrationTests.Infrastructure;
using GovernedAccess.Web.Authentication;
using GovernedAccess.Web.Demo;
using GovernedAccess.Web.Persistence;
using GovernedAccess.Web.Provisioning;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace GovernedAccess.IntegrationTests.Requests;

public sealed class RequestQueriesTests(DefaultWebApplicationFixture fixture)
    : IClassFixture<DefaultWebApplicationFixture>
{
    private readonly GovernedAccessWebFactory factory = fixture.Factory;

    [Fact]
    public async Task ActiveDetailContainsCurrentValidationAndCompleteOrderedEvidence()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
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
        return (await factory.CreateRequestFixtureAsync(
            clientId,
            environmentId,
            incidentId,
            cancellationToken)).Id;
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

public sealed class RequestQueryComponentTests
{
    [Fact]
    public async Task ListsAreParticipantFilteredAndMarkOnlyCurrentActions()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var fixture = await ProvisioningTestFixture.CreateAsync(
            cancellationToken);
        await using var dbContext = fixture.CreateDbContext();
        var alphaAwaitingBusiness = CreateRequest(
            DemoDataIds.ClientAlphaId,
            DemoDataIds.ClientAlphaEnvironmentId,
            DemoDataIds.PrimaryIncidentId,
            fixture.Clock.UtcNow);
        var betaAwaitingBusiness = CreateRequest(
            DemoDataIds.ClientBetaId,
            DemoDataIds.ClientBetaEnvironmentId,
            DemoDataIds.ClientBetaIncidentId,
            fixture.Clock.UtcNow);
        var alphaAwaitingDevOps = CreateRequest(
            DemoDataIds.ClientAlphaId,
            DemoDataIds.ClientAlphaEnvironmentId,
            DemoDataIds.PrimaryIncidentId,
            fixture.Clock.UtcNow);
        var businessDecision = ApplyBusinessApproval(
            alphaAwaitingDevOps,
            fixture.Clock.UtcNow);
        dbContext.AccessRequests.AddRange(
            alphaAwaitingBusiness,
            betaAwaitingBusiness,
            alphaAwaitingDevOps);
        dbContext.ApprovalDecisions.Add(businessDecision);
        await dbContext.SaveChangesAsync(cancellationToken);
        var service = CreateQueryService(dbContext, fixture.Clock);

        var requester = await service.ListAsync(
            DemoDataIds.RequesterPrincipalId,
            status: null,
            cancellationToken);
        Assert.True(requester.IsSuccess);
        Assert.Equal(
            new[]
            {
                alphaAwaitingBusiness.Id,
                betaAwaitingBusiness.Id,
                alphaAwaitingDevOps.Id,
            }.Order(),
            requester.Value.Select(item => item.RequestId).Order());
        Assert.All(requester.Value, item => Assert.False(item.Actionable));

        var alphaApprover = await service.ListAsync(
            DemoDataIds.ClientAlphaApproverPrincipalId,
            status: null,
            cancellationToken);
        Assert.True(alphaApprover.IsSuccess);
        Assert.Equal(
            new[] { alphaAwaitingBusiness.Id, alphaAwaitingDevOps.Id }.Order(),
            alphaApprover.Value.Select(item => item.RequestId).Order());
        Assert.True(alphaApprover.Value.Single(
            item => item.RequestId == alphaAwaitingBusiness.Id).Actionable);
        Assert.False(alphaApprover.Value.Single(
            item => item.RequestId == alphaAwaitingDevOps.Id).Actionable);

        var betaApprover = await service.ListAsync(
            DemoDataIds.ClientBetaApproverPrincipalId,
            status: null,
            cancellationToken);
        Assert.True(betaApprover.IsSuccess);
        var betaItem = Assert.Single(betaApprover.Value);
        Assert.Equal(betaAwaitingBusiness.Id, betaItem.RequestId);
        Assert.True(betaItem.Actionable);

        var devOps = await service.ListAsync(
            DemoDataIds.DevOpsApproverPrincipalId,
            status: null,
            cancellationToken);
        Assert.True(devOps.IsSuccess);
        Assert.Equal(alphaAwaitingDevOps.Id, Assert.Single(devOps.Value).RequestId);
        Assert.True(Assert.Single(devOps.Value).Actionable);

        var filtered = await service.ListAsync(
            DemoDataIds.RequesterPrincipalId,
            RequestStatus.AwaitingBusinessApproval,
            cancellationToken);
        Assert.Equal(
            new[] { alphaAwaitingBusiness.Id, betaAwaitingBusiness.Id }.Order(),
            filtered.Value.Select(item => item.RequestId).Order());
    }

    [Fact]
    public async Task LaterStageActionsAreComputedFromActorAndStoredWorkflowState()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var fixture = await ProvisioningTestFixture.CreateAsync(
            cancellationToken);
        await using var dbContext = fixture.CreateDbContext();
        var awaitingDevOps = CreateRequest(
            DemoDataIds.ClientAlphaId,
            DemoDataIds.ClientAlphaEnvironmentId,
            DemoDataIds.PrimaryIncidentId,
            fixture.Clock.UtcNow);
        var failedRequest = CreateRequest(
            DemoDataIds.ClientAlphaId,
            DemoDataIds.ClientAlphaEnvironmentId,
            DemoDataIds.PrimaryIncidentId,
            fixture.Clock.UtcNow);
        var awaitingDecision = ApplyBusinessApproval(
            awaitingDevOps,
            fixture.Clock.UtcNow);
        var failedBusinessDecision = ApplyBusinessApproval(
            failedRequest,
            fixture.Clock.UtcNow);
        dbContext.AccessRequests.AddRange(awaitingDevOps, failedRequest);
        dbContext.ApprovalDecisions.AddRange(
            awaitingDecision,
            failedBusinessDecision);
        await dbContext.SaveChangesAsync(cancellationToken);

        var workflowService = CreateWorkflowService(
            dbContext,
            new AlwaysFailProvisioner(),
            fixture.Clock);
        var failed = await workflowService.DecideAsync(
            ApprovalStage.DevOps,
            failedRequest.Id,
            DemoDataIds.DevOpsApproverPrincipalId,
            ApprovalOutcome.Approved,
            null,
            "failed-provisioning",
            cancellationToken);
        Assert.True(failed.IsFailure);

        var queryService = CreateQueryService(dbContext, fixture.Clock);
        var decisionDetail = await queryService.GetDetailAsync(
            awaitingDevOps.Id,
            DemoDataIds.DevOpsApproverPrincipalId,
            cancellationToken);
        var retryDetail = await queryService.GetDetailAsync(
            failedRequest.Id,
            DemoDataIds.DevOpsApproverPrincipalId,
            cancellationToken);
        Assert.Equal(
            RequestQueryService.DevOpsDecisionAction,
            Assert.Single(decisionDetail.Value.AvailableActions));
        Assert.Equal(
            RequestQueryService.RetryProvisioningAction,
            Assert.Single(retryDetail.Value.AvailableActions));

        var requesterDecisionDetail = await queryService.GetDetailAsync(
            awaitingDevOps.Id,
            DemoDataIds.RequesterPrincipalId,
            cancellationToken);
        var requesterRetryDetail = await queryService.GetDetailAsync(
            failedRequest.Id,
            DemoDataIds.RequesterPrincipalId,
            cancellationToken);
        Assert.Empty(requesterDecisionDetail.Value.AvailableActions);
        Assert.Empty(requesterRetryDetail.Value.AvailableActions);
    }

    [Fact]
    public async Task ActiveRequestRemainsInvisibleToWrongClientNonparticipant()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var fixture = await ProvisioningTestFixture.CreateAsync(
            cancellationToken);
        await using var dbContext = fixture.CreateDbContext();
        var request = CreateRequest(
            DemoDataIds.ClientAlphaId,
            DemoDataIds.ClientAlphaEnvironmentId,
            DemoDataIds.PrimaryIncidentId,
            fixture.Clock.UtcNow);
        var businessDecision = ApplyBusinessApproval(request, fixture.Clock.UtcNow);
        dbContext.AccessRequests.Add(request);
        dbContext.ApprovalDecisions.Add(businessDecision);
        await dbContext.SaveChangesAsync(cancellationToken);
        var workflowService = CreateWorkflowService(
            dbContext,
            new AlwaysSucceedProvisioner(fixture.Clock),
            fixture.Clock);
        var activation = await workflowService.DecideAsync(
            ApprovalStage.DevOps,
            request.Id,
            DemoDataIds.DevOpsApproverPrincipalId,
            ApprovalOutcome.Approved,
            null,
            "activate-request",
            cancellationToken);
        Assert.True(activation.IsSuccess);

        var queryService = CreateQueryService(dbContext, fixture.Clock);
        var list = await queryService.ListAsync(
            DemoDataIds.ClientBetaApproverPrincipalId,
            status: null,
            cancellationToken);
        var detail = await queryService.GetDetailAsync(
            request.Id,
            DemoDataIds.ClientBetaApproverPrincipalId,
            cancellationToken);

        Assert.DoesNotContain(list.Value, item => item.RequestId == request.Id);
        Assert.True(detail.IsFailure);
        Assert.Equal(ApplicationFailureKind.NotFound, detail.Failure!.Kind);
        Assert.Equal("request_not_found", detail.Failure.Code);
    }

    private static RequestQueryService CreateQueryService(
        GovernedAccessDbContext dbContext,
        IClock clock)
    {
        var requestContext = new EfRequestContextReader(dbContext);
        return new RequestQueryService(
            requestContext,
            new EfWorkflowStore(dbContext),
            clock);
    }

    private static AccessRequestWorkflowService CreateWorkflowService(
        GovernedAccessDbContext dbContext,
        IAccessProvisioner provisioner,
        IClock clock)
    {
        var requestContext = new EfRequestContextReader(dbContext);
        var workflowStore = new EfWorkflowStore(dbContext);
        return new AccessRequestWorkflowService(
            requestContext,
            workflowStore,
            new RequestValidator(requestContext),
            new ProtectedProvisioningService(workflowStore, provisioner, clock),
            clock);
    }

    private static AccessRequest CreateRequest(
        string clientId,
        string environmentId,
        string incidentId,
        DateTimeOffset createdAt) =>
        new(
            Guid.NewGuid(),
            DemoDataIds.RequesterPrincipalId,
            new ValidatedRequestDetails(
                clientId,
                environmentId,
                ProductionRoleIds.ReadOnly,
                "Investigate the active production incident.",
                incidentId),
            createdAt,
            "request-correlation");

    private static ApprovalDecision ApplyBusinessApproval(
        AccessRequest request,
        DateTimeOffset decidedAt)
    {
        return Assert.IsType<ApprovalDecisionApplied>(
            ApprovalDecisionPolicy.Apply(
                request,
                ApprovalStage.Business,
                priorApproval: null,
                new ApprovalCommand(
                    Guid.NewGuid(),
                    ApprovalOutcome.Approved,
                    request.Details.ClientId == DemoDataIds.ClientAlphaId
                        ? DemoDataIds.ClientAlphaApproverPrincipalId
                        : DemoDataIds.ClientBetaApproverPrincipalId,
                    null,
                    decidedAt,
                    "business-correlation"),
                hasExistingDecision: false)).Decision;
    }

    private sealed class AlwaysFailProvisioner : IAccessProvisioner
    {
        public Task<AccessProvisioningOutcome> GetOrCreateAsync(
            AccessProvisioningRequest request,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult<AccessProvisioningOutcome>(
                new AccessProvisioningFailed(
                    new ApplicationFailure(
                        ApplicationFailureKind.DependencyFailure,
                        "component_provisioning_failed",
                        "Component provisioning failed safely.")));
        }
    }

    private sealed class AlwaysSucceedProvisioner(IClock clock) : IAccessProvisioner
    {
        public Task<AccessProvisioningOutcome> GetOrCreateAsync(
            AccessProvisioningRequest request,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult<AccessProvisioningOutcome>(
                new AccessProvisioningSucceeded(Guid.NewGuid(), clock.UtcNow));
        }
    }
}
