using System.Collections.Concurrent;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using GovernedAccess.IntegrationTests.Infrastructure;
using GovernedAccess.IntegrationTests.Teams;
using GovernedAccess.Web.Ai;
using GovernedAccess.Web.Persistence;
using GovernedAccess.Web.Teams;
using Microsoft.Agents.Core.Models;
using Microsoft.Agents.Core.Serialization;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace GovernedAccess.IntegrationTests.Observability;

public sealed class TeamsIntakeLoggingTests
{
    private const string CompleteRequest =
        "I need production read-only access to PROD-ALPHA-EU to investigate "
        + "INC-1042 because customer-facing errors require diagnosis.";

    [Fact]
    public async Task PreparationAndConfirmationLogOnlyStructuredOperationMetadata()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var logs = new CapturingLoggerProvider();
        await using var factory = new GovernedAccessWebFactory(
            DeterministicChatMode.Candidate,
            loggerProvider: logs);
        using var client = factory.CreateTeamsClient();

        using var preparationResponse = await client.PostAsJsonAsync(
            "/api/messages",
            CreateMessage(CompleteRequest),
            ProtocolJsonSerializer.SerializationOptions,
            cancellationToken);
        Assert.Equal(HttpStatusCode.OK, preparationResponse.StatusCode);

        Guid sessionId;
        Guid requestId;
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var dbContext = scope.ServiceProvider
                .GetRequiredService<GovernedAccessDbContext>();
            var session = await dbContext.RequestIntakeSessions
                .AsNoTracking()
                .SingleAsync(cancellationToken);
            sessionId = session.Id;
            requestId = session.ReservedRequestId!.Value;
        }

        using var confirmationResponse = await client.PostAsJsonAsync(
            "/api/messages",
            CreateConfirmation(sessionId),
            ProtocolJsonSerializer.SerializationOptions,
            cancellationToken);
        Assert.Equal(HttpStatusCode.OK, confirmationResponse.StatusCode);

        var agentLogs = logs.Entries
            .Where(entry =>
                entry.Category == typeof(TeamsAccessRequestAgent).FullName)
            .ToArray();
        var preparation = Assert.Single(
            agentLogs,
            entry => entry.EventId.Name == "TeamsIntakePreparationCompleted");
        var confirmation = Assert.Single(
            agentLogs,
            entry => entry.EventId.Name == "TeamsIntakeConfirmationCompleted");

        AssertOperationMetadata(
            preparation,
            "Prepare",
            "ReadyForConfirmation",
            sessionId,
            requestId);
        AssertOperationMetadata(
            confirmation,
            "Confirm",
            "Submitted",
            sessionId,
            requestId);

        var capturedText = string.Join(
            Environment.NewLine,
            agentLogs.Select(entry =>
                entry.Message
                + " "
                + string.Join(
                    " ",
                    entry.Properties.Select(property =>
                        $"{property.Key}={property.Value}"))));
        Assert.DoesNotContain(CompleteRequest, capturedText, StringComparison.Ordinal);
        Assert.DoesNotContain(
            "Investigate the active production incident.",
            capturedText,
            StringComparison.Ordinal);
        Assert.DoesNotContain(
            "Confirm production access request",
            capturedText,
            StringComparison.Ordinal);
    }

    private static void AssertOperationMetadata(
        CapturedLog entry,
        string transition,
        string outcome,
        Guid sessionId,
        Guid requestId)
    {
        Assert.Equal(LogLevel.Information, entry.Level);
        Assert.Equal(transition, entry.Properties["Transition"]);
        Assert.Equal(outcome, entry.Properties["Outcome"]?.ToString());
        Assert.Equal(
            FakeTeamsActivityBuilder.DefaultTenantId,
            entry.Properties["TenantId"]);
        Assert.Equal(
            FakeTeamsActivityBuilder.DefaultActorId,
            entry.Properties["ChannelActorId"]);
        Assert.Equal(
            FakeTeamsActivityBuilder.DefaultConversationId,
            entry.Properties["ConversationId"]);
        Assert.Equal(sessionId, entry.Properties["SessionId"]);
        Assert.Equal(requestId, entry.Properties["RequestId"]);
        Assert.False(
            string.IsNullOrWhiteSpace(
                entry.Properties["CorrelationId"]?.ToString()));
        Assert.True(
            Convert.ToDouble(
                entry.Properties["DurationMs"],
                System.Globalization.CultureInfo.InvariantCulture) >= 0);
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

    private static Activity CreateConfirmation(Guid sessionId) =>
        new FakeTeamsActivityBuilder()
            .WithText(null)
            .WithActivityId("teams-logging-confirmation")
            .WithInvokeData(new
            {
                action = new
                {
                    type = "Action.Execute",
                    title = "Confirm and submit",
                    verb = PreparedRequestCardFactory.ConfirmationVerb,
                    associatedInputs = "none",
                    data = new
                    {
                        schemaVersion =
                            PreparedRequestCardFactory.ContractSchemaVersion,
                        preparedRequestId = sessionId.ToString("D"),
                    },
                },
            })
            .Build()
            .Activity;

    private sealed record CapturedLog(
        string Category,
        LogLevel Level,
        EventId EventId,
        string Message,
        IReadOnlyDictionary<string, object?> Properties);

    private sealed class CapturingLoggerProvider : ILoggerProvider
    {
        private readonly ConcurrentQueue<CapturedLog> entries = new();

        public IReadOnlyCollection<CapturedLog> Entries => entries.ToArray();

        public ILogger CreateLogger(string categoryName) =>
            new CapturingLogger(categoryName, entries);

        public void Dispose()
        {
        }
    }

    private sealed class CapturingLogger(
        string category,
        ConcurrentQueue<CapturedLog> entries) : ILogger
    {
        public IDisposable? BeginScope<TState>(TState state)
            where TState : notnull =>
            null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            var properties = state is IEnumerable<KeyValuePair<string, object?>> values
                ? values.ToDictionary(
                    pair => pair.Key,
                    pair => pair.Value,
                    StringComparer.Ordinal)
                : new Dictionary<string, object?>(StringComparer.Ordinal);
            entries.Enqueue(
                new CapturedLog(
                    category,
                    logLevel,
                    eventId,
                    formatter(state, exception),
                    properties));
        }
    }
}
