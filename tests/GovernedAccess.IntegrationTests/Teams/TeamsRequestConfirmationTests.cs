using System.Net;
using System.Net.Http.Json;
using GovernedAccess.Core.Domain;
using GovernedAccess.IntegrationTests.Infrastructure;
using GovernedAccess.Web.Ai;
using GovernedAccess.Web.Authentication;
using GovernedAccess.Web.Persistence;
using GovernedAccess.Web.Teams;
using Microsoft.Agents.Core.Models;
using Microsoft.Agents.Core.Serialization;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace GovernedAccess.IntegrationTests.Teams;

public sealed class TeamsRequestConfirmationTests
{
    private const string CompleteRequest =
        "I need production read-only access to PROD-ALPHA-EU to investigate "
        + "INC-1042 because customer-facing errors require diagnosis.";

    [Fact]
    public async Task FirstConfirmationAtomicallySubmitsExactScopeWithoutGrantingAccess()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var factory = new GovernedAccessWebFactory(
            DeterministicChatMode.Candidate);
        using var client = factory.CreateTeamsClient();

        using (var preparationResponse = await client.PostAsJsonAsync(
                   "/api/messages",
                   CreateExpectRepliesMessage(CompleteRequest),
                   ProtocolJsonSerializer.SerializationOptions,
                   cancellationToken))
        {
            Assert.Equal(HttpStatusCode.OK, preparationResponse.StatusCode);
        }

        PreparedAccessRequest preparedBeforeConfirmation;
        await using (var preparationScope = factory.Services.CreateAsyncScope())
        {
            var dbContext = preparationScope.ServiceProvider
                .GetRequiredService<GovernedAccessDbContext>();
            preparedBeforeConfirmation = await dbContext.PreparedAccessRequests
                .AsNoTracking()
                .SingleAsync(cancellationToken);

            Assert.Equal(
                PreparedAccessRequestStatus.Ready,
                preparedBeforeConfirmation.Status);
            Assert.Empty(
                await dbContext.AccessRequests
                    .AsNoTracking()
                    .ToListAsync(cancellationToken));
            Assert.Empty(
                await dbContext.AuditEvents
                    .AsNoTracking()
                    .ToListAsync(cancellationToken));
        }

        using var confirmationResponse = await client.PostAsJsonAsync(
            "/api/messages",
            CreateConfirmationActivity(preparedBeforeConfirmation.PreparationId),
            ProtocolJsonSerializer.SerializationOptions,
            cancellationToken);

        var confirmationBody = await confirmationResponse.Content
            .ReadAsStringAsync(cancellationToken);
        Assert.True(
            confirmationResponse.StatusCode == HttpStatusCode.OK,
            $"Expected 200 but received {(int)confirmationResponse.StatusCode}: "
            + confirmationBody);

        var expectedRequestLink = new Uri(
            factory.TrustedWebBaseUri,
            $"requests/{preparedBeforeConfirmation.ReservedRequestId:D}");
        Assert.Contains(
            preparedBeforeConfirmation.ReservedRequestId.ToString("D"),
            confirmationBody,
            StringComparison.Ordinal);
        Assert.Contains(
            expectedRequestLink.AbsoluteUri,
            confirmationBody,
            StringComparison.Ordinal);
        Assert.Contains(
            "not yet approved or granted",
            confirmationBody,
            StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(
            "smba.trafficmanager.net",
            confirmationBody,
            StringComparison.OrdinalIgnoreCase);

        await using var confirmationScope = factory.Services.CreateAsyncScope();
        var confirmationDbContext = confirmationScope.ServiceProvider
            .GetRequiredService<GovernedAccessDbContext>();

        var submittedPreparation = await confirmationDbContext.PreparedAccessRequests
            .AsNoTracking()
            .SingleAsync(cancellationToken);
        Assert.Equal(
            preparedBeforeConfirmation.PreparationId,
            submittedPreparation.PreparationId);
        Assert.Equal(
            PreparedAccessRequestStatus.Submitted,
            submittedPreparation.Status);
        Assert.Equal(
            preparedBeforeConfirmation.ReservedRequestId,
            submittedPreparation.SubmittedRequestId);
        Assert.Equal(factory.Clock.UtcNow, submittedPreparation.SubmittedAt);

        var submittedConversation = await confirmationDbContext
            .RequestPreparationConversations
            .AsNoTracking()
            .SingleAsync(cancellationToken);
        Assert.Equal(
            RequestPreparationConversationStatus.Submitted,
            submittedConversation.Status);
        Assert.Equal(
            preparedBeforeConfirmation.PreparationId,
            submittedConversation.ActivePreparationId);
        Assert.Null(submittedConversation.ClientId);
        Assert.Null(submittedConversation.EnvironmentId);
        Assert.Null(submittedConversation.RequestedRoleId);
        Assert.Null(submittedConversation.Justification);
        Assert.Null(submittedConversation.IncidentId);
        Assert.Null(submittedConversation.PendingClarification);

        var request = await confirmationDbContext.AccessRequests
            .AsNoTracking()
            .SingleAsync(cancellationToken);
        Assert.Equal(preparedBeforeConfirmation.ReservedRequestId, request.Id);
        Assert.Equal(preparedBeforeConfirmation.RequesterId, request.RequesterId);
        Assert.Equal(preparedBeforeConfirmation.ClientId, request.ClientId);
        Assert.Equal(preparedBeforeConfirmation.EnvironmentId, request.EnvironmentId);
        Assert.Equal(
            preparedBeforeConfirmation.RequestedRoleId,
            request.RequestedRoleId);
        Assert.Equal(
            preparedBeforeConfirmation.Justification,
            request.Justification);
        Assert.Equal(preparedBeforeConfirmation.IncidentId, request.IncidentId);
        Assert.Equal(RequestStatus.AwaitingBusinessApproval, request.Status);
        Assert.Equal(factory.Clock.UtcNow, request.CreatedAt);

        var auditEvent = await confirmationDbContext.AuditEvents
            .AsNoTracking()
            .SingleAsync(cancellationToken);
        Assert.Equal(request.Id, auditEvent.RequestId);
        Assert.Equal(AuditEventType.RequestCreated, auditEvent.EventType);
        Assert.Equal(DemoPrincipalKeys.Requester, auditEvent.ActorId);
        Assert.Equal(factory.Clock.UtcNow, auditEvent.OccurredAt);

        Assert.Empty(
            await confirmationDbContext.ApprovalDecisions
                .AsNoTracking()
                .ToListAsync(cancellationToken));
        Assert.Empty(
            await confirmationDbContext.ProvisioningOperations
                .AsNoTracking()
                .ToListAsync(cancellationToken));
        Assert.Empty(
            await confirmationDbContext.AccessGrants
                .AsNoTracking()
                .ToListAsync(cancellationToken));
    }

    private static Activity CreateExpectRepliesMessage(string text)
    {
        var activity = new FakeTeamsActivityBuilder()
            .WithText(text)
            .Build()
            .Activity;
        activity.DeliveryMode = DeliveryModes.ExpectReplies;
        return activity;
    }

    private static Activity CreateConfirmationActivity(Guid preparationId)
    {
        return new FakeTeamsActivityBuilder()
            .WithText(null)
            .WithActivityId("teams-confirmation-activity")
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
                        preparedRequestId = preparationId.ToString("D"),
                    },
                },
            })
            .Build()
            .Activity;
    }
}
