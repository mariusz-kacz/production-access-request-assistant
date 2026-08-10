using System.Net.Http.Json;
using GovernedAccess.Core.Domain;
using GovernedAccess.IntegrationTests.Infrastructure;
using GovernedAccess.Web.Authentication;
using GovernedAccess.Web.Controllers;
using GovernedAccess.Web.Persistence;
using GovernedAccess.Web.Teams;
using Microsoft.Agents.Core.Models;
using Microsoft.Agents.Core.Serialization;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace GovernedAccess.IntegrationTests.Teams;

public sealed class TeamsGovernedWorkflowTests
{
    private const string CompleteRequest =
        "I need production read-only access to PROD-ALPHA-EU to investigate "
        + "INC-1042 because customer-facing errors require diagnosis.";

    private const string CompleteProviderCandidate =
        """
        {"kind":"candidate","candidate":{"clientId":"client-alpha","environmentId":"PROD-ALPHA-EU","requestedRoleId":"ProductionReadOnly","justification":"Investigate customer-facing errors during the active production incident.","incidentId":"INC-1042"},"clarification":null}
        """;

    [Fact]
    public async Task TeamsSubmittedRequestCompletesAuthenticatedGovernedWorkflow()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var factory = new GovernedAccessWebFactory(
            new RecordingChatClient(CompleteProviderCandidate),
            configurationOverrides: CreateFoundryResponsesProfileConfiguration());
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
            var preparationBody = await preparationResponse.Content
                .ReadAsStringAsync(cancellationToken);

            await using (var scope = factory.Services.CreateAsyncScope())
            {
                var dbContext = scope.ServiceProvider
                    .GetRequiredService<GovernedAccessDbContext>();
                intake = await dbContext.RequestIntakeSessions
                    .AsNoTracking()
                    .SingleAsync(cancellationToken);
            }

            Assert.Equal(RequestIntakeStatus.Ready, intake.Status);
            Assert.NotNull(intake.ReservedRequestId);
            Assert.DoesNotContain(
                intake.ReservedRequestId.Value.ToString("D"),
                preparationBody,
                StringComparison.Ordinal);
            Assert.DoesNotContain(
                "Request ID",
                preparationBody,
                StringComparison.Ordinal);

            using var confirmationResponse = await teamsClient.PostAsJsonAsync(
                "/api/messages",
                CreateConfirmation(intake.Id),
                ProtocolJsonSerializer.SerializationOptions,
                cancellationToken);
            confirmationResponse.EnsureSuccessStatusCode();
            var confirmationBody = await confirmationResponse.Content
                .ReadAsStringAsync(cancellationToken);
            Assert.Contains(
                PreparedRequestCardFactory.AdaptiveCardContentType,
                confirmationBody,
                StringComparison.Ordinal);
            Assert.Contains(
                "Request submitted",
                confirmationBody,
                StringComparison.Ordinal);
            Assert.Contains(
                intake.ReservedRequestId.Value.ToString("D"),
                confirmationBody,
                StringComparison.Ordinal);
            Assert.DoesNotContain(
                "Open request",
                confirmationBody,
                StringComparison.Ordinal);
            Assert.DoesNotContain(
                "Action.OpenUrl",
                confirmationBody,
                StringComparison.Ordinal);
            Assert.DoesNotContain(
                "Confirm and submit",
                confirmationBody,
                StringComparison.Ordinal);
        }

        var requestId = intake.ReservedRequestId!.Value;
        await using (var submittedScope = factory.Services.CreateAsyncScope())
        {
            var dbContext = submittedScope.ServiceProvider
                .GetRequiredService<GovernedAccessDbContext>();
            var submittedIntake = await dbContext.RequestIntakeSessions
                .AsNoTracking()
                .SingleAsync(cancellationToken);
            var submittedRequest = await dbContext.AccessRequests
                .AsNoTracking()
                .SingleAsync(cancellationToken);
            Assert.Equal(RequestIntakeStatus.Submitted, submittedIntake.Status);
            Assert.Equal(requestId, submittedRequest.Id);
            Assert.Equal(
                RequestStatus.AwaitingBusinessApproval,
                submittedRequest.Status);
        }

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
            var result = Assert.IsType<BusinessDecisionResponse>(
                await businessResponse.Content.ReadFromJsonAsync<BusinessDecisionResponse>(
                    cancellationToken));
            Assert.Equal(
                RequestStatus.AwaitingDevOpsApproval.ToString(),
                result.Status);
        }

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

        Assert.Equal(RequestStatus.Active.ToString(), devOpsResult.Status);
        Assert.NotNull(devOpsResult.Grant);

        await using var evidenceScope = factory.Services.CreateAsyncScope();
        var evidenceDbContext = evidenceScope.ServiceProvider
            .GetRequiredService<GovernedAccessDbContext>();
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

        Assert.Equal(RequestStatus.Active, request.Status);
        Assert.Collection(
            decisions,
            decision => Assert.Equal(ApprovalStage.Business, decision.Stage),
            decision => Assert.Equal(ApprovalStage.DevOps, decision.Stage));
        Assert.Equal(ProvisioningOperationStatus.Succeeded, operation.Status);
        Assert.Equal(requestId, grant.RequestId);
        Assert.Equal(AccessGrantOutcome.Succeeded, grant.Outcome);
        Assert.Equal(AccessGrant.FixedLifetime, grant.ExpiresAt - grant.ActivatedAt);
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

    private static Dictionary<string, string?> CreateFoundryResponsesProfileConfiguration() =>
        new()
        {
            ["RequestPreparationModel:ExecutionProfile"] = "FoundryResponses",
            ["RequestPreparationModel:FoundryResponses:Endpoint"] =
                "https://governed-access.services.ai.azure.com/openai/v1",
            ["RequestPreparationModel:FoundryResponses:DeploymentName"] =
                "governed-access-chat",
        };

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
}
