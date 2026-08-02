using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using GovernedAccess.Core.Application;
using GovernedAccess.Core.Domain;
using GovernedAccess.IntegrationTests.Infrastructure;
using GovernedAccess.Web.Ai;
using GovernedAccess.Web.Authentication;
using GovernedAccess.Web.Controllers;
using GovernedAccess.Web.Demo;
using GovernedAccess.Web.Persistence;
using GovernedAccess.Web.Teams;
using Microsoft.Agents.Core.Models;
using Microsoft.Agents.Core.Serialization;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace GovernedAccess.IntegrationTests.Teams;

[Trait(
    IntegrationTestCollections.TestLevelTrait,
    IntegrationTestCollections.FullHostLevel)]
public sealed class TeamsGovernedWorkflowTests
{
    private const string CompleteRequest =
        "I need production read-only access to PROD-ALPHA-EU to investigate "
        + "INC-1042 because customer-facing errors require diagnosis.";

    [Fact]
    public async Task TeamsSubmittedRequestCompletesAuthenticatedGovernedWorkflow()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var factory = new GovernedAccessWebFactory(
            DeterministicChatMode.Candidate);
        await factory.ResetDatabaseAsync(cancellationToken);

        RequestIntakeSession intake;
        using (var teamsClient = factory.CreateTeamsClient())
        {
            using var preparationResponse = await teamsClient.PostAsJsonAsync(
                "/api/messages",
                CreateMessage(CompleteRequest),
                ProtocolJsonSerializer.SerializationOptions,
                cancellationToken);
            preparationResponse.EnsureSuccessStatusCode();

            await using (var scope = factory.Services.CreateAsyncScope())
            {
                var dbContext = scope.ServiceProvider
                    .GetRequiredService<GovernedAccessDbContext>();
                intake = await dbContext.RequestIntakeSessions
                    .AsNoTracking()
                    .SingleAsync(cancellationToken);
            }

            Assert.Equal(RequestIntakeStatus.Ready, intake.Status);
            Assert.Equal(DemoDataIds.ClientAlphaId, intake.ClientId);
            Assert.Equal(
                DemoDataIds.ClientAlphaEnvironmentId,
                intake.EnvironmentId);
            Assert.Equal(ProductionRoleIds.ReadOnly, intake.RequestedRoleId);
            Assert.Equal(DemoDataIds.PrimaryIncidentId, intake.IncidentId);

            using var confirmationResponse = await teamsClient.PostAsJsonAsync(
                "/api/messages",
                CreateConfirmation(intake.Id),
                ProtocolJsonSerializer.SerializationOptions,
                cancellationToken);
            confirmationResponse.EnsureSuccessStatusCode();
        }

        var requestId = Assert.IsType<Guid>(intake.ReservedRequestId);

        using (var wrongClientApprover =
               await factory.CreateAuthenticatedClientAsync(
                   DemoPrincipalKeys.ClientBetaApprover,
                   cancellationToken))
        using (var wrongClientDecision = CreateDecisionRequest(
                   requestId,
                   "business-decisions",
                   "This approver belongs to another client."))
        using (var wrongClientResponse =
               await GovernedAccessWebFactory.SendWithAntiforgeryAsync(
                   wrongClientApprover,
                   wrongClientDecision,
                   cancellationToken))
        {
            Assert.Equal(HttpStatusCode.Forbidden, wrongClientResponse.StatusCode);
        }

        BusinessDecisionResponse businessResult;
        using (var businessApprover = await factory.CreateAuthenticatedClientAsync(
                   DemoPrincipalKeys.ClientAlphaApprover,
                   cancellationToken))
        using (var businessDecision = CreateDecisionRequest(
                   requestId,
                   "business-decisions",
                   "Approved for the active client incident."))
        using (var businessResponse =
               await GovernedAccessWebFactory.SendWithAntiforgeryAsync(
                   businessApprover,
                   businessDecision,
                   cancellationToken))
        {
            businessResponse.EnsureSuccessStatusCode();
            businessResult = Assert.IsType<BusinessDecisionResponse>(
                await businessResponse.Content.ReadFromJsonAsync<BusinessDecisionResponse>(
                    cancellationToken));
        }

        Assert.Equal(requestId, businessResult.RequestId);
        Assert.Equal(
            RequestStatus.AwaitingDevOpsApproval.ToString(),
            businessResult.Status);

        DevOpsDecisionResponse devOpsResult;
        using (var devOpsApprover = await factory.CreateAuthenticatedClientAsync(
                   DemoPrincipalKeys.DevOpsApprover,
                   cancellationToken))
        using (var devOpsDecision = CreateDecisionRequest(
                   requestId,
                   "devops-decisions",
                   "Provision the exact business-approved scope."))
        using (var devOpsResponse =
               await GovernedAccessWebFactory.SendWithAntiforgeryAsync(
                   devOpsApprover,
                   devOpsDecision,
                   cancellationToken))
        {
            devOpsResponse.EnsureSuccessStatusCode();
            devOpsResult = Assert.IsType<DevOpsDecisionResponse>(
                await devOpsResponse.Content.ReadFromJsonAsync<DevOpsDecisionResponse>(
                    cancellationToken));
        }

        Assert.Equal(requestId, devOpsResult.RequestId);
        Assert.Equal(RequestStatus.Active.ToString(), devOpsResult.Status);
        var responseGrant = Assert.IsType<DevOpsAccessGrantResponse>(
            devOpsResult.Grant);
        Assert.Equal(DemoDataIds.ClientAlphaEnvironmentId, responseGrant.EnvironmentId);
        Assert.Equal(ProductionRoleIds.ReadOnly, responseGrant.RoleId);
        Assert.Equal(
            AccessGrant.FixedLifetime,
            responseGrant.ExpiresAt - responseGrant.ActivatedAt);

        await using var evidenceScope = factory.Services.CreateAsyncScope();
        var evidenceDbContext = evidenceScope.ServiceProvider
            .GetRequiredService<GovernedAccessDbContext>();
        var submittedIntake = await evidenceDbContext.RequestIntakeSessions
            .AsNoTracking()
            .SingleAsync(cancellationToken);
        var request = await evidenceDbContext.AccessRequests
            .AsNoTracking()
            .SingleAsync(cancellationToken);
        var decisions = await evidenceDbContext.ApprovalDecisions
            .AsNoTracking()
            .OrderBy(item => item.Stage)
            .ToListAsync(cancellationToken);
        var operation = await evidenceDbContext.ProvisioningOperations
            .AsNoTracking()
            .SingleAsync(cancellationToken);
        var grant = await evidenceDbContext.AccessGrants
            .AsNoTracking()
            .SingleAsync(cancellationToken);
        var auditEvents = await evidenceDbContext.AuditEvents
            .AsNoTracking()
            .Where(item => item.RequestId == requestId)
            .ToListAsync(cancellationToken);

        Assert.Equal(RequestIntakeStatus.Submitted, submittedIntake.Status);
        Assert.Equal(requestId, submittedIntake.ReservedRequestId);
        Assert.Equal(requestId, request.Id);
        Assert.Equal(DemoPrincipalKeys.Requester, request.RequesterId);
        Assert.Equal(DemoDataIds.ClientAlphaId, request.ClientId);
        Assert.Equal(DemoDataIds.ClientAlphaEnvironmentId, request.EnvironmentId);
        Assert.Equal(ProductionRoleIds.ReadOnly, request.RequestedRoleId);
        Assert.Equal(intake.Justification, request.Justification);
        Assert.Equal(DemoDataIds.PrimaryIncidentId, request.IncidentId);
        Assert.Equal(RequestStatus.Active, request.Status);

        Assert.Collection(
            decisions,
            businessDecision => AssertDecision(
                businessDecision,
                requestId,
                ApprovalStage.Business,
                DemoDataIds.ClientAlphaApproverPrincipalId),
            devOpsDecision => AssertDecision(
                devOpsDecision,
                requestId,
                ApprovalStage.DevOps,
                DemoDataIds.DevOpsApproverPrincipalId));
        Assert.Equal(businessResult.CorrelationId, decisions[0].CorrelationId);
        Assert.Equal(devOpsResult.CorrelationId, decisions[1].CorrelationId);

        Assert.Equal(requestId, operation.RequestId);
        Assert.Equal(DemoDataIds.ClientAlphaEnvironmentId, operation.EnvironmentId);
        Assert.Equal(ProductionRoleIds.ReadOnly, operation.RoleId);
        Assert.Equal(ProvisioningOperationStatus.Succeeded, operation.Status);
        Assert.Equal(1, operation.AttemptCount);
        Assert.Equal(ProtectedProvisioningService.SuccessCode, operation.LastOutcomeCode);

        Assert.Equal(responseGrant.GrantId, grant.Id);
        Assert.Equal(requestId, grant.RequestId);
        Assert.Equal(DemoPrincipalKeys.Requester, grant.RequesterId);
        Assert.Equal(request.EnvironmentId, grant.EnvironmentId);
        Assert.Equal(request.RequestedRoleId, grant.RoleId);
        Assert.Equal(AccessGrantOutcome.Succeeded, grant.Outcome);
        Assert.Equal(AccessGrant.FixedLifetime, grant.ExpiresAt - grant.ActivatedAt);
        Assert.Equal(responseGrant.ActivatedAt, grant.ActivatedAt);
        Assert.Equal(responseGrant.ExpiresAt, grant.ExpiresAt);
        Assert.Equal(devOpsResult.CorrelationId, grant.CorrelationId);

        Assert.Equal(6, auditEvents.Count);
        AssertAuditEvent(
            auditEvents,
            AuditEventType.RequestCreated,
            DemoPrincipalKeys.Requester);
        AssertAuditEvent(
            auditEvents,
            AuditEventType.AuthorizationRejected,
            DemoDataIds.ClientBetaApproverPrincipalId,
            AccessRequestWorkflowService.BusinessApproverNotResponsibleCode);
        AssertAuditEvent(
            auditEvents,
            AuditEventType.BusinessDecision,
            DemoDataIds.ClientAlphaApproverPrincipalId);
        AssertAuditEvent(
            auditEvents,
            AuditEventType.DevOpsDecision,
            DemoDataIds.DevOpsApproverPrincipalId);
        AssertAuditEvent(
            auditEvents,
            AuditEventType.ProvisioningAttempted,
            DemoDataIds.DevOpsApproverPrincipalId);
        AssertAuditEvent(
            auditEvents,
            AuditEventType.ProvisioningSucceeded,
            DemoDataIds.DevOpsApproverPrincipalId);
    }

    private static Activity CreateMessage(string text)
    {
        var activity = new FakeTeamsActivityBuilder()
            .WithText(text)
            .Build()
            .Activity;
        activity.DeliveryMode = DeliveryModes.ExpectReplies;
        return activity;
    }

    private static Activity CreateConfirmation(Guid intakeId) =>
        new FakeTeamsActivityBuilder()
            .WithText(null)
            .WithActivityId("teams-governed-workflow-confirmation")
            .WithInvokeData(new
            {
                action = new
                {
                    type = "Action.Execute",
                    verb = PreparedRequestCardFactory.ConfirmationVerb,
                    data = new
                    {
                        schemaVersion =
                            PreparedRequestCardFactory.ContractSchemaVersion,
                        preparedRequestId = intakeId.ToString("D"),
                    },
                },
            })
            .Build()
            .Activity;

    private static HttpRequestMessage CreateDecisionRequest(
        Guid requestId,
        string action,
        string comment) =>
        new(HttpMethod.Post, $"/api/requests/{requestId:D}/{action}")
        {
            Content = JsonContent.Create(new
            {
                decision = "Approve",
                comment,
            }),
        };

    private static void AssertDecision(
        ApprovalDecision decision,
        Guid requestId,
        ApprovalStage stage,
        string approverId)
    {
        Assert.Equal(requestId, decision.RequestId);
        Assert.Equal(stage, decision.Stage);
        Assert.Equal(ApprovalOutcome.Approved, decision.Decision);
        Assert.Equal(approverId, decision.ApproverId);
        Assert.Equal(ProductionRoleIds.ReadOnly, decision.ApprovedRoleId);
        Assert.False(string.IsNullOrWhiteSpace(decision.CorrelationId));
    }

    private static void AssertAuditEvent(
        IReadOnlyCollection<AuditEvent> auditEvents,
        AuditEventType eventType,
        string actorId,
        string? outcomeCode = null)
    {
        var auditEvent = Assert.Single(
            auditEvents,
            item => item.EventType == eventType);
        Assert.Equal(actorId, auditEvent.ActorId);
        Assert.False(string.IsNullOrWhiteSpace(auditEvent.CorrelationId));
        Assert.False(string.IsNullOrWhiteSpace(auditEvent.OutcomeCode));
        if (outcomeCode is not null)
        {
            Assert.Equal(outcomeCode, auditEvent.OutcomeCode);
        }

        using var details = JsonDocument.Parse(auditEvent.DetailsJson);
        Assert.Equal(JsonValueKind.Object, details.RootElement.ValueKind);
    }
}
