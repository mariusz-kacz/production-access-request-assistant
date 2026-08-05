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
    private static readonly Guid SessionId =
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

            Assert.Equal(
                RequestConfirmationResultKind.Submitted,
                outcome.Kind);
            Assert.Equal(ReservedRequestId, outcome.RequestId);
            Assert.False(outcome.WasAlreadySubmitted);
            Assert.Equal(1, saveCounter.Count);
        }

        await using var verification = new GovernedAccessDbContext(options);
        var session = await verification.RequestIntakeSessions
            .AsNoTracking()
            .SingleAsync(cancellationToken);
        var request = await verification.AccessRequests
            .AsNoTracking()
            .SingleAsync(cancellationToken);
        var auditEvent = await verification.AuditEvents
            .AsNoTracking()
            .SingleAsync(cancellationToken);

        Assert.Equal(
            RequestIntakeStatus.Submitted,
            session.Status);
        Assert.Equal(ConfirmedAt, session.SubmittedAt);
        Assert.Equal(ReservedRequestId, session.ReservedRequestId);

        Assert.Equal(ReservedRequestId, request.Id);
        Assert.Equal(DemoPrincipalKeys.Requester, request.RequesterId);
        Assert.Equal("client-alpha", request.ClientId);
        Assert.Equal("PROD-ALPHA-EU", request.EnvironmentId);
        Assert.Equal(ProductionRoleIds.ReadOnly, request.RequestedRoleId);
        Assert.Equal(
            "Investigate the active production incident.",
            request.Justification);
        Assert.Equal("INC-1042", request.IncidentId);
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

            Assert.Equal(
                RequestConfirmationResultKind.Failed,
                outcome.Kind);
            Assert.Equal(
                ApplicationFailureKind.DependencyFailure,
                outcome.Failure!.Kind);
            Assert.Equal(1, saveCounter.Count);
        }

        await using var verification = new GovernedAccessDbContext(options);
        var session = await verification.RequestIntakeSessions
            .AsNoTracking()
            .SingleAsync(cancellationToken);

        Assert.Equal(
            RequestIntakeStatus.Ready,
            session.Status);
        Assert.Null(session.SubmittedAt);
        Assert.Equal(ReservedRequestId, session.ReservedRequestId);
        Assert.Empty(
            await verification.AccessRequests
                .AsNoTracking()
                .ToListAsync(cancellationToken));
        Assert.Empty(
            await verification.AuditEvents
                .AsNoTracking()
                .ToListAsync(cancellationToken));
    }

    [Fact]
    public async Task CompetingAggregateSaveReturnsTypedConcurrencyConflict()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var saveCounter = new SaveCounter();
        await using var connection = await CreateReadyDatabaseAsync(
            saveCounter,
            cancellationToken);
        var options = CreateOptions(connection, saveCounter);

        await using var firstContext = new GovernedAccessDbContext(options);
        await using var secondContext = new GovernedAccessDbContext(options);
        var firstStore = new EfRequestIntakeStore(firstContext);
        var secondStore = new EfRequestIntakeStore(secondContext);
        var first = await firstStore.GetAsync(SessionId, cancellationToken);
        var second = await secondStore.GetAsync(SessionId, cancellationToken);
        first.Value.MarkInvalidated(
            ConfirmedAt,
            "first-invalidation");
        second.Value.MarkInvalidated(
            ConfirmedAt,
            "competing-invalidation");

        var firstSave = await firstStore.SaveChangesAsync(cancellationToken);
        var secondSave = await secondStore.SaveChangesAsync(cancellationToken);

        Assert.True(firstSave.IsSuccess);
        Assert.True(secondSave.IsFailure);
        Assert.Equal(
            ApplicationFailureKind.ConcurrencyConflict,
            secondSave.Failure!.Kind);
    }

    [Fact]
    public async Task ConcurrencyRecoveryReturnsStoredRequestIdOnlyForExactBinding()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var saveCounter = new SaveCounter();
        await using var connection = await CreateReadyDatabaseAsync(
            saveCounter,
            cancellationToken);
        var options = CreateOptions(connection, saveCounter);

        await using var winnerContext = new GovernedAccessDbContext(options);
        await using var loserContext = new GovernedAccessDbContext(options);
        var winnerStore = new EfRequestIntakeStore(winnerContext);
        var loserStore = new EfRequestIntakeStore(loserContext);
        var winner = await winnerStore.GetAsync(SessionId, cancellationToken);
        var loser = await loserStore.GetAsync(SessionId, cancellationToken);
        winner.Value.MarkSubmitted(ConfirmedAt, "winner-confirmation");
        loser.Value.MarkSubmitted(ConfirmedAt, "loser-confirmation");

        var winnerSave = await winnerStore.SaveChangesAsync(cancellationToken);
        var loserSave = await loserStore.SaveChangesAsync(cancellationToken);
        var recovered = await loserStore.RecoverSubmittedRequestAsync(
            SessionId,
            ConfirmationCommand().Actor,
            cancellationToken);

        Assert.True(winnerSave.IsSuccess);
        Assert.Equal(
            ApplicationFailureKind.ConcurrencyConflict,
            loserSave.Failure!.Kind);
        Assert.True(recovered.IsSuccess);
        Assert.Equal(ReservedRequestId, recovered.Value);
        Assert.All(
            loserContext.ChangeTracker.Entries(),
            entry => Assert.Equal(EntityState.Unchanged, entry.State));

        AuthenticatedChannelActor[] foreignBindings =
        [
            new(
                RequestIntakeSession.TeamsChannel,
                FakeTeamsActivityBuilder.DefaultTenantId,
                "foreign-actor",
                FakeTeamsActivityBuilder.DefaultConversationId,
                DemoPrincipalKeys.Requester),
            new(
                RequestIntakeSession.TeamsChannel,
                FakeTeamsActivityBuilder.DefaultTenantId,
                FakeTeamsActivityBuilder.DefaultActorId,
                "foreign-conversation",
                DemoPrincipalKeys.Requester),
        ];

        foreach (var foreignBinding in foreignBindings)
        {
            var concealed = await loserStore.RecoverSubmittedRequestAsync(
                SessionId,
                foreignBinding,
                cancellationToken);

            Assert.True(concealed.IsFailure);
            Assert.Equal(
                ApplicationFailureKind.NotFound,
                concealed.Failure!.Kind);
            Assert.False(concealed.TryGetValue(out _));
        }
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
            await MinimalTestDataSeeder.SeedAsync(context, cancellationToken);

            var session = new RequestIntakeSession(
                SessionId,
                RequestIntakeSession.TeamsChannel,
                FakeTeamsActivityBuilder.DefaultTenantId,
                FakeTeamsActivityBuilder.DefaultActorId,
                FakeTeamsActivityBuilder.DefaultConversationId,
                DemoPrincipalKeys.Requester,
                PreparedAt,
                "prepare-correlation");
            session.UpdateCandidate(
                "client-alpha",
                "PROD-ALPHA-EU",
                ProductionRoleIds.ReadOnly,
                "Investigate the active production incident.",
                "INC-1042",
                PreparedAt,
                "prepare-correlation");
            session.MarkReady(
                ReservedRequestId,
                PreparedAt,
                "prepare-correlation");

            context.RequestIntakeSessions.Add(session);
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

    private static RequestIntakeService CreateConfirmationService(
        GovernedAccessDbContext context)
    {
        var requestContext = new EfRequestContextReader(context);
        var clock = new DeterministicClock(ConfirmedAt);
        var validator = new RequestValidator(requestContext);
        return new RequestIntakeService(
            new UnusedInterpreter(),
            validator,
            requestContext,
            new EfRequestIntakeStore(context),
            new RequestSubmissionService(
                validator,
                requestContext,
                new EfWorkflowStore(context)),
            clock);
    }

    private static ConfirmRequestIntakeCommand ConfirmationCommand() =>
        new(
            new AuthenticatedChannelActor(
                RequestIntakeSession.TeamsChannel,
                FakeTeamsActivityBuilder.DefaultTenantId,
                FakeTeamsActivityBuilder.DefaultActorId,
                FakeTeamsActivityBuilder.DefaultConversationId,
                DemoPrincipalKeys.Requester),
            SessionId,
            "confirm-correlation");

    private sealed class UnusedInterpreter : IRequestPreparationInterpreter
    {
        public Task<RequestPreparationInterpretationResult> InterpretAsync(
            RequestPreparationTurn turn,
            CancellationToken cancellationToken) =>
            throw new NotSupportedException(
                "Confirmation does not invoke request interpretation.");
    }

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
