using GovernedAccess.Core.Application;
using GovernedAccess.Core.Domain;
using GovernedAccess.Core.Ports;
using GovernedAccess.IntegrationTests.Infrastructure;
using GovernedAccess.IntegrationTests.Teams;
using GovernedAccess.Web.Authentication;
using GovernedAccess.Web.Persistence;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace GovernedAccess.IntegrationTests.Persistence;

public sealed class RequestIntakePersistenceTests
{
    private static readonly Guid ConversationRecordId =
        Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid PreparationId =
        Guid.Parse("22222222-2222-2222-2222-222222222222");
    private static readonly Guid ReservedRequestId =
        Guid.Parse("33333333-3333-3333-3333-333333333333");
    private static readonly DateTimeOffset PreparedAt =
        new(2026, 7, 27, 10, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset ConfirmedAt =
        PreparedAt.AddMinutes(5);

    [Fact]
    public async Task OneSharedSaveCommitsIntakeRequestAndAudit()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var saveCounter = new SaveCounter();
        await using var connection = await CreateReadyDatabaseAsync(
            saveCounter,
            cancellationToken);
        var options = CreateOptions(connection, saveCounter);

        await using (var context = new GovernedAccessDbContext(options))
        {
            var outcome = await CreateConfirmationService(context).ConfirmAsync(
                ConfirmationCommand(),
                cancellationToken);

            var success =
                Assert.IsType<PreparedRequestConfirmationSucceeded>(outcome);
            Assert.Equal(ReservedRequestId, success.RequestId);
            Assert.False(success.WasAlreadySubmitted);
            Assert.Equal(1, saveCounter.Count);
        }

        await using var verification = new GovernedAccessDbContext(options);
        var conversation = await verification.RequestPreparationConversations
            .AsNoTracking()
            .SingleAsync(cancellationToken);
        var prepared = await verification.PreparedAccessRequests
            .AsNoTracking()
            .SingleAsync(cancellationToken);
        var request = await verification.AccessRequests
            .AsNoTracking()
            .SingleAsync(cancellationToken);
        var auditEvent = await verification.AuditEvents
            .AsNoTracking()
            .SingleAsync(cancellationToken);

        Assert.Equal(
            RequestPreparationConversationStatus.Submitted,
            conversation.Status);
        Assert.Equal(PreparedAccessRequestStatus.Submitted, prepared.Status);
        Assert.Equal(ConfirmedAt, prepared.SubmittedAt);
        Assert.Equal(ReservedRequestId, prepared.SubmittedRequestId);

        Assert.Equal(ReservedRequestId, request.Id);
        Assert.Equal(prepared.RequesterId, request.RequesterId);
        Assert.Equal(prepared.ClientId, request.ClientId);
        Assert.Equal(prepared.EnvironmentId, request.EnvironmentId);
        Assert.Equal(prepared.RequestedRoleId, request.RequestedRoleId);
        Assert.Equal(prepared.Justification, request.Justification);
        Assert.Equal(prepared.IncidentId, request.IncidentId);
        Assert.Equal(RequestStatus.AwaitingBusinessApproval, request.Status);

        Assert.Equal(request.Id, auditEvent.RequestId);
        Assert.Equal(AuditEventType.RequestCreated, auditEvent.EventType);
        Assert.Equal(request.RequesterId, auditEvent.ActorId);
    }

    [Fact]
    public async Task ForcedSaveFailureLeavesNoPartialRowsOrStatusChanges()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var saveCounter = new SaveCounter();
        await using var connection = await CreateReadyDatabaseAsync(
            saveCounter,
            cancellationToken);
        var options = CreateOptions(connection, saveCounter);

        await using (var setup = new GovernedAccessDbContext(options))
        {
            _ = await setup.Database.ExecuteSqlRawAsync(
                """
                CREATE TRIGGER "FailRequestCreatedAudit"
                BEFORE INSERT ON "AuditEvents"
                BEGIN
                    SELECT RAISE(ABORT, 'forced request audit failure');
                END;
                """,
                cancellationToken);
        }

        await using (var context = new GovernedAccessDbContext(options))
        {
            var outcome = await CreateConfirmationService(context).ConfirmAsync(
                ConfirmationCommand(),
                cancellationToken);

            var failure =
                Assert.IsType<PreparedRequestConfirmationFailed>(outcome);
            Assert.Equal(
                ApplicationFailureKind.DependencyFailure,
                failure.Failure.Kind);
            Assert.Equal(1, saveCounter.Count);
        }

        await using var verification = new GovernedAccessDbContext(options);
        var conversation = await verification.RequestPreparationConversations
            .AsNoTracking()
            .SingleAsync(cancellationToken);
        var prepared = await verification.PreparedAccessRequests
            .AsNoTracking()
            .SingleAsync(cancellationToken);

        Assert.Equal(
            RequestPreparationConversationStatus.Ready,
            conversation.Status);
        Assert.Equal(PreparedAccessRequestStatus.Ready, prepared.Status);
        Assert.Null(prepared.SubmittedAt);
        Assert.Null(prepared.SubmittedRequestId);
        Assert.Empty(
            await verification.AccessRequests
                .AsNoTracking()
                .ToListAsync(cancellationToken));
        Assert.Empty(
            await verification.AuditEvents
                .AsNoTracking()
                .ToListAsync(cancellationToken));
    }

    private static async Task<SqliteConnection> CreateReadyDatabaseAsync(
        SaveCounter saveCounter,
        CancellationToken cancellationToken)
    {
        var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync(cancellationToken);

        try
        {
            var options = CreateOptions(connection, saveCounter);
            await using var context = new GovernedAccessDbContext(options);
            await SyntheticDataSeeder.SeedAsync(context, cancellationToken);

            var conversation = new RequestPreparationConversation(
                ConversationRecordId,
                RequestPreparationConversation.TeamsChannel,
                FakeTeamsActivityBuilder.DefaultTenantId,
                FakeTeamsActivityBuilder.DefaultActorId,
                FakeTeamsActivityBuilder.DefaultConversationId,
                DemoPrincipalKeys.Requester,
                PreparedAt,
                "prepare-correlation");
            conversation.UpdateCandidate(
                "client-alpha",
                "PROD-ALPHA-EU",
                ProductionRoleIds.ReadOnly,
                "Investigate the active production incident.",
                "INC-1042",
                pendingClarification: null,
                PreparedAt,
                "prepare-correlation");
            conversation.MarkReady(
                PreparationId,
                PreparedAt,
                "prepare-correlation");

            var prepared = new PreparedAccessRequest(
                PreparationId,
                ConversationRecordId,
                ReservedRequestId,
                RequestPreparationConversation.TeamsChannel,
                FakeTeamsActivityBuilder.DefaultTenantId,
                FakeTeamsActivityBuilder.DefaultActorId,
                FakeTeamsActivityBuilder.DefaultConversationId,
                DemoPrincipalKeys.Requester,
                "client-alpha",
                "PROD-ALPHA-EU",
                ProductionRoleIds.ReadOnly,
                "Investigate the active production incident.",
                "INC-1042",
                PreparedAt,
                "prepare-correlation");

            context.RequestPreparationConversations.Add(conversation);
            context.PreparedAccessRequests.Add(prepared);
            await context.SaveChangesAsync(cancellationToken);
            saveCounter.Reset();
            return connection;
        }
        catch
        {
            await connection.DisposeAsync();
            throw;
        }
    }

    private static DbContextOptions<GovernedAccessDbContext> CreateOptions(
        SqliteConnection connection,
        SaveCounter saveCounter) =>
        new DbContextOptionsBuilder<GovernedAccessDbContext>()
            .UseSqlite(connection)
            .AddInterceptors(saveCounter)
            .Options;

    private static PreparedRequestConfirmationService CreateConfirmationService(
        GovernedAccessDbContext context)
    {
        var requestContext = new EfRequestContextReader(context);
        var clock = new DeterministicClock(ConfirmedAt);
        return new PreparedRequestConfirmationService(
            new EfRequestIntakeStore(context),
            new RequestSubmissionService(
                new RequestValidator(requestContext),
                requestContext,
                new EfWorkflowStore(context),
                clock),
            clock);
    }

    private static ConfirmPreparedAccessRequestCommand ConfirmationCommand() =>
        new(
            new AuthenticatedChannelActor(
                RequestPreparationConversation.TeamsChannel,
                FakeTeamsActivityBuilder.DefaultTenantId,
                FakeTeamsActivityBuilder.DefaultActorId,
                FakeTeamsActivityBuilder.DefaultConversationId,
                DemoPrincipalKeys.Requester),
            PreparationId,
            "confirm-correlation");

    private sealed class SaveCounter : SaveChangesInterceptor
    {
        public int Count { get; private set; }

        public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
            DbContextEventData eventData,
            InterceptionResult<int> result,
            CancellationToken cancellationToken = default)
        {
            Count++;
            return ValueTask.FromResult(result);
        }

        public void Reset() => Count = 0;
    }
}
