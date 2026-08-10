using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
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
    [Fact]
    public async Task ClickingSupersededDraftReplacesItWithNonActionableCard()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var factory = new GovernedAccessWebFactory(
            new DeterministicChatClient(DeterministicChatMode.Candidate));
        using var client = factory.CreateTeamsClient();
        var session = await SeedReadySessionAsync(factory, cancellationToken);

        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var dbContext = scope.ServiceProvider
                .GetRequiredService<GovernedAccessDbContext>();
            var persisted = await dbContext.RequestIntakeSessions
                .SingleAsync(
                    intake => intake.Id == session.Id,
                    cancellationToken);
            persisted.MarkSuperseded(
                factory.Clock.UtcNow,
                "hosted-boundary-superseded");
            await dbContext.SaveChangesAsync(cancellationToken);
        }

        using var response = await client.PostAsJsonAsync(
            "/api/messages",
            CreateConfirmationActivity(ValidConfirmationData(session.Id)),
            ProtocolJsonSerializer.SerializationOptions,
            cancellationToken);
        var body = await response.Content.ReadAsStringAsync(cancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("Draft replaced", body, StringComparison.Ordinal);
        Assert.Contains(
            "can no longer be submitted",
            body,
            StringComparison.Ordinal);
        Assert.DoesNotContain(
            "Confirm and submit",
            body,
            StringComparison.Ordinal);

        await using var verificationScope = factory.Services.CreateAsyncScope();
        var verificationDb = verificationScope.ServiceProvider
            .GetRequiredService<GovernedAccessDbContext>();
        Assert.Empty(
            await verificationDb.AccessRequests.ToListAsync(cancellationToken));
    }

    [Fact]
    public async Task ConfirmationBoundaryRejectsMalformedAndConcealsForeignActions()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var chatClient = new DeterministicChatClient(
            DeterministicChatMode.Candidate);
        await using var factory = new GovernedAccessWebFactory(chatClient);
        using var client = factory.CreateTeamsClient();
        var session = await SeedReadySessionAsync(factory, cancellationToken);
        var concealedMessage =
            "The prepared request could not be found for this authenticated conversation. No request was submitted.";

        var malformedCases = new (Activity Activity, HttpStatusCode Status)[]
        {
            (
                CreateConfirmationActivity(
                    new
                    {
                        schemaVersion = 2,
                        preparedRequestId = session.Id.ToString("D"),
                    }),
                HttpStatusCode.BadRequest),
            (
                CreateConfirmationActivity(
                    ValidConfirmationData(session.Id),
                    verb: "unknownVerb"),
                HttpStatusCode.NotImplemented),
            (
                CreateConfirmationActivity(
                    new
                    {
                        schemaVersion = 1,
                        preparedRequestId = "not-a-guid",
                    }),
                HttpStatusCode.BadRequest),
            (
                CreateConfirmationActivity(
                    new
                    {
                        schemaVersion = 1,
                        preparedRequestId = session.Id.ToString("D"),
                        requestedRoleId = ProductionRoleIds.Support,
                    }),
                HttpStatusCode.BadRequest),
        };

        foreach (var malformedCase in malformedCases)
        {
            using var response = await client.PostAsJsonAsync(
                "/api/messages",
                malformedCase.Activity,
                ProtocolJsonSerializer.SerializationOptions,
                cancellationToken);
            var body = await response.Content.ReadAsStringAsync(cancellationToken);

            if (malformedCase.Status == HttpStatusCode.NotImplemented)
            {
                Assert.Equal(malformedCase.Status, response.StatusCode);
            }
            else
            {
                Assert.Equal(HttpStatusCode.OK, response.StatusCode);
                using var invokeResponse = JsonDocument.Parse(body);
                Assert.Equal(
                    (int)malformedCase.Status,
                    invokeResponse.RootElement
                        .GetProperty("statusCode")
                        .GetInt32());
            }
            Assert.DoesNotContain(
                session.ReservedRequestId!.Value.ToString("D"),
                body,
                StringComparison.Ordinal);
            Assert.DoesNotContain(
                session.ClientId!,
                body,
                StringComparison.Ordinal);
        }

        var concealedCases = new[]
        {
            CreateConfirmationActivity(
                ValidConfirmationData(Guid.NewGuid())),
            CreateConfirmationActivity(
                ValidConfirmationData(session.Id),
                actorId: "foreign-actor"),
            CreateConfirmationActivity(
                ValidConfirmationData(session.Id),
                conversationId: "foreign-conversation"),
        };

        foreach (var activity in concealedCases)
        {
            using var response = await client.PostAsJsonAsync(
                "/api/messages",
                activity,
                ProtocolJsonSerializer.SerializationOptions,
                cancellationToken);
            var body = await response.Content.ReadAsStringAsync(cancellationToken);

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            Assert.Contains(concealedMessage, body, StringComparison.Ordinal);
            Assert.DoesNotContain(
                session.ReservedRequestId!.Value.ToString("D"),
                body,
                StringComparison.Ordinal);
            Assert.DoesNotContain(
                session.ClientId!,
                body,
                StringComparison.Ordinal);
        }

        Assert.Equal(0, chatClient.RequestCount);
        await using var verificationScope = factory.Services.CreateAsyncScope();
        var dbContext = verificationScope.ServiceProvider
            .GetRequiredService<GovernedAccessDbContext>();
        var persisted = await dbContext.RequestIntakeSessions
            .AsNoTracking()
            .SingleAsync(cancellationToken);
        Assert.Equal(RequestIntakeStatus.Ready, persisted.Status);
        Assert.Empty(await dbContext.AccessRequests.ToListAsync(cancellationToken));
        Assert.Empty(await dbContext.AuditEvents.ToListAsync(cancellationToken));
        Assert.Empty(
            await dbContext.ApprovalDecisions.ToListAsync(cancellationToken));
        Assert.Empty(
            await dbContext.ProvisioningOperations.ToListAsync(cancellationToken));
        Assert.Empty(await dbContext.AccessGrants.ToListAsync(cancellationToken));
    }

    private static Activity CreateConfirmationActivity(
        object actionData,
        string? actorId = null,
        string? conversationId = null,
        string verb = PreparedRequestCardFactory.ConfirmationVerb)
    {
        var builder = new FakeTeamsActivityBuilder()
            .WithText(null)
            .WithActivityId($"teams-confirmation-{Guid.NewGuid():N}")
            .WithInvokeData(new
            {
                action = new
                {
                    type = "Action.Execute",
                    title = "Confirm and submit",
                    verb,
                    associatedInputs = "none",
                    data = actionData,
                },
            });
        if (actorId is not null)
        {
            builder.WithActor(actorId);
        }

        if (conversationId is not null)
        {
            builder.WithConversation(conversationId);
        }

        return builder.Build().Activity;
    }

    private static object ValidConfirmationData(Guid preparationId) =>
        new
        {
            schemaVersion = PreparedRequestCardFactory.ContractSchemaVersion,
            preparedRequestId = preparationId.ToString("D"),
        };

    private static async Task<RequestIntakeSession> SeedReadySessionAsync(
        GovernedAccessWebFactory factory,
        CancellationToken cancellationToken)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var dbContext = scope.ServiceProvider
            .GetRequiredService<GovernedAccessDbContext>();
        var session = new RequestIntakeSession(
            Guid.NewGuid(),
            RequestIntakeSession.TeamsChannel,
            FakeTeamsActivityBuilder.DefaultTenantId,
            FakeTeamsActivityBuilder.DefaultActorId,
            FakeTeamsActivityBuilder.DefaultConversationId,
            DemoPrincipalKeys.Requester,
            factory.Clock.UtcNow,
            "hosted-boundary-preparation");
        session.UpdateCandidate(
            "client-alpha",
            "PROD-ALPHA-EU",
            ProductionRoleIds.ReadOnly,
            "Investigate the active production incident.",
            "INC-1042",
            factory.Clock.UtcNow,
            "hosted-boundary-candidate");
        session.MarkReady(
            Guid.NewGuid(),
            factory.Clock.UtcNow,
            "hosted-boundary-ready");
        dbContext.RequestIntakeSessions.Add(session);
        await dbContext.SaveChangesAsync(cancellationToken);
        return session;
    }
}
