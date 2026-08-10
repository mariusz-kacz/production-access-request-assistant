using System.Net;
using System.Net.Http.Json;
using GovernedAccess.Core.Domain.Drafts;
using GovernedAccess.Core.Domain.ReferenceData;
using GovernedAccess.IntegrationTests.Infrastructure;
using GovernedAccess.Web.Ai;
using GovernedAccess.Web.Persistence;
using GovernedAccess.Web.Teams;
using Microsoft.Agents.Core.Models;
using Microsoft.Agents.Core.Serialization;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace GovernedAccess.IntegrationTests.Teams;

public sealed class TeamsConversationResetTests
{
    private const string ResetGuidance =
        "Started a new request. Send an incident ID or production environment ID when you are ready.";

    private const string InitialClarification =
        """
        {"kind":"clarification","candidate":{"clientId":null,"environmentId":null,"requestedRoleId":null,"justification":null,"incidentId":null},"clarification":{"target":"environmentId","message":"Choose an environment: PROD-ALPHA-EU or PROD-BETA-UK.","environmentOptionIds":["PROD-ALPHA-EU","PROD-BETA-UK"]}}
        """;

    private const string ReplacementClarification =
        """
        {"kind":"clarification","candidate":{"clientId":null,"environmentId":null,"requestedRoleId":null,"justification":null,"incidentId":null},"clarification":{"target":"environmentId","message":"Please choose an environment explicitly: PROD-ALPHA-EU or PROD-BETA-UK.","environmentOptionIds":["PROD-ALPHA-EU","PROD-BETA-UK"]}}
        """;

    [Fact]
    public async Task CollectingResetSkipsChatAndCreatesCleanReplacementIdentityAndHistory()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var chatClient = new ScriptedChatClient(
            InitialClarification,
            ReplacementClarification);
        await using var factory = new GovernedAccessWebFactory(chatClient);
        using var client = factory.CreateTeamsClient();

        _ = await PostMessageAsync(
            client,
            "I need temporary production access.",
            "reset-collecting-first",
            cancellationToken);
        var requestCountBeforeReset = chatClient.InvocationCount;
        Assert.True(requestCountBeforeReset > 0);

        RequestIntakeSession abandoned;
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var dbContext = scope.ServiceProvider
                .GetRequiredService<GovernedAccessDbContext>();
            abandoned = await dbContext.RequestIntakeSessions
                .AsNoTracking()
                .SingleAsync(cancellationToken);
            Assert.Equal(RequestIntakeStatus.Collecting, abandoned.Status);
            Assert.Null(abandoned.ClientId);
        }

        var resetBody = await PostMessageAsync(
            client,
            "  /NEW  ",
            "reset-collecting-command",
            cancellationToken);

        Assert.Contains(ResetGuidance, resetBody, StringComparison.Ordinal);
        Assert.Equal(requestCountBeforeReset, chatClient.InvocationCount);

        var replacementBody = await PostMessageAsync(
            client,
            "the first one",
            "reset-collecting-replacement",
            cancellationToken);

        Assert.Contains(
            "Please choose an environment explicitly",
            replacementBody,
            StringComparison.OrdinalIgnoreCase);
        Assert.Equal(requestCountBeforeReset + 1, chatClient.InvocationCount);
        var replacementRequest = chatClient.Invocations[1].Messages;
        Assert.DoesNotContain(
            replacementRequest,
            message => message.Role == Microsoft.Extensions.AI.ChatRole.Assistant);
        Assert.DoesNotContain(
            replacementRequest,
            message => message.Text?.Contains(
                "I need temporary production access.",
                StringComparison.Ordinal) == true);

        await using var verificationScope = factory.Services.CreateAsyncScope();
        var verificationDb = verificationScope.ServiceProvider
            .GetRequiredService<GovernedAccessDbContext>();
        var sessions = await verificationDb.RequestIntakeSessions
            .AsNoTracking()
            .OrderBy(session => session.CreatedAt)
            .ThenBy(session => session.Id)
            .ToListAsync(cancellationToken);
        Assert.Equal(2, sessions.Count);

        var terminal = Assert.Single(
            sessions,
            session => session.Id == abandoned.Id);
        Assert.Equal(RequestIntakeStatus.Superseded, terminal.Status);
        Assert.Null(terminal.ClientId);
        Assert.Null(terminal.EnvironmentId);
        Assert.Null(terminal.RequestedRoleId);
        Assert.Null(terminal.Justification);
        Assert.Null(terminal.IncidentId);

        var replacement = Assert.Single(
            sessions,
            session => session.Id != abandoned.Id);
        Assert.Equal(RequestIntakeStatus.Collecting, replacement.Status);
        Assert.Null(replacement.EnvironmentId);
        Assert.Null(replacement.RequestedRoleId);
        Assert.NotEqual(abandoned.Id, replacement.Id);
        Assert.Empty(
            await verificationDb.AccessRequests
                .AsNoTracking()
                .ToListAsync(cancellationToken));
        Assert.Empty(
            await verificationDb.AccessGrants
                .AsNoTracking()
                .ToListAsync(cancellationToken));
    }

    private static async Task<string> PostMessageAsync(
        HttpClient client,
        string text,
        string activityId,
        CancellationToken cancellationToken)
    {
        var activity = new FakeTeamsActivityBuilder()
            .WithText(text)
            .WithActivityId(activityId)
            .Build()
            .Activity;
        activity.DeliveryMode = DeliveryModes.ExpectReplies;
        using var response = await client.PostAsJsonAsync(
            "/api/messages",
            activity,
            ProtocolJsonSerializer.SerializationOptions,
            cancellationToken);
        var body = await response.Content.ReadAsStringAsync(cancellationToken);
        Assert.True(
            response.StatusCode == HttpStatusCode.OK,
            $"Expected 200 but received {(int)response.StatusCode}: {body}");
        return body;
    }

}
