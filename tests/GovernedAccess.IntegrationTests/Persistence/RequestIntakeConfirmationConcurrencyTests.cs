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

public sealed class RequestIntakeConfirmationConcurrencyTests
{
    private static readonly Guid SessionId =
        Guid.Parse("44444444-4444-4444-4444-444444444444");
    private static readonly Guid ReservedRequestId =
        Guid.Parse("55555555-5555-5555-5555-555555555555");
    private static readonly DateTimeOffset PreparedAt =
        new(2026, 8, 1, 10, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset ConfirmedAt =
        PreparedAt.AddMinutes(5);

    [Fact]
    public async Task RepeatedConfirmationReturnsOneStableRequest()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var database = await CreateReadyDatabaseAsync(
            cancellationToken);
        var options = CreateOptions(database.ConnectionString);

        await using (var context = new GovernedAccessDbContext(options))
        {
            var service = CreateService(context);

            var first = await service.ConfirmAsync(
                ConfirmationCommand("sequential-first"),
                cancellationToken);
            var replay = await service.ConfirmAsync(
                ConfirmationCommand("sequential-replay"),
                cancellationToken);

            Assert.Equal(RequestConfirmationResultKind.Submitted, first.Kind);
            Assert.Equal(
                RequestConfirmationResultKind.AlreadySubmitted,
                replay.Kind);
            Assert.Equal(ReservedRequestId, first.RequestId);
            Assert.Equal(first.RequestId, replay.RequestId);
        }

        await AssertSingleSubmissionAsync(options, cancellationToken);
    }

    [Fact]
    public async Task ConcurrentConfirmationRecoversOneStableRequest()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var database = await CreateReadyDatabaseAsync(
            cancellationToken);
        var coordinator = new ConfirmationSaveCoordinator();
        var winnerOptions = CreateOptions(
            database.ConnectionString,
            new ConfirmationSaveGate(coordinator, isWinner: true));
        var contenderOptions = CreateOptions(
            database.ConnectionString,
            new ConfirmationSaveGate(coordinator, isWinner: false));

        await using var winnerContext =
            new GovernedAccessDbContext(winnerOptions);
        await using var contenderContext =
            new GovernedAccessDbContext(contenderOptions);
        var winnerTask = CreateService(winnerContext).ConfirmAsync(
            ConfirmationCommand("concurrent-winner"),
            cancellationToken);
        var contenderTask = CreateService(contenderContext).ConfirmAsync(
            ConfirmationCommand("concurrent-contender"),
            cancellationToken);

        RequestConfirmationResult winner;
        try
        {
            await coordinator.BothSavesReady.WaitAsync(cancellationToken);
            winner = await winnerTask;
        }
        finally
        {
            coordinator.ReleaseContender();
        }

        var contender = await contenderTask;

        Assert.Equal(RequestConfirmationResultKind.Submitted, winner.Kind);
        Assert.True(
            contender.Kind == RequestConfirmationResultKind.AlreadySubmitted,
            $"Expected recovered confirmation, but received {contender.Kind}: "
            + $"{contender.Failure?.Kind}/{contender.Failure?.Code}.");
        Assert.Equal(ReservedRequestId, winner.RequestId);
        Assert.Equal(winner.RequestId, contender.RequestId);

        var verificationOptions = CreateOptions(database.ConnectionString);
        await AssertSingleSubmissionAsync(
            verificationOptions,
            cancellationToken);
    }

    private static async Task<SqliteConnection> CreateReadyDatabaseAsync(
        CancellationToken cancellationToken)
    {
        var connection = new SqliteConnection(
            $"Data Source=request-intake-{Guid.NewGuid():N};Mode=Memory;Cache=Shared");
        await connection.OpenAsync(cancellationToken);

        try
        {
            var options = CreateOptions(connection.ConnectionString);
            await using var context = new GovernedAccessDbContext(options);
            await SyntheticDataSeeder.SeedAsync(context, cancellationToken);

            var session = new RequestIntakeSession(
                SessionId,
                RequestIntakeSession.TeamsChannel,
                FakeTeamsActivityBuilder.DefaultTenantId,
                FakeTeamsActivityBuilder.DefaultActorId,
                FakeTeamsActivityBuilder.DefaultConversationId,
                DemoPrincipalKeys.Requester,
                PreparedAt,
                "concurrency-preparation");
            session.UpdateCandidate(
                "client-alpha",
                "PROD-ALPHA-EU",
                ProductionRoleIds.ReadOnly,
                "Investigate the active production incident.",
                "INC-1042",
                PreparedAt,
                "concurrency-candidate");
            session.MarkReady(
                ReservedRequestId,
                PreparedAt,
                "concurrency-ready");
            context.RequestIntakeSessions.Add(session);
            await context.SaveChangesAsync(cancellationToken);
            return connection;
        }
        catch
        {
            await connection.DisposeAsync();
            throw;
        }
    }

    private static DbContextOptions<GovernedAccessDbContext> CreateOptions(
        string connectionString,
        IInterceptor? interceptor = null)
    {
        var builder = new DbContextOptionsBuilder<GovernedAccessDbContext>()
            .UseSqlite(connectionString);
        if (interceptor is not null)
        {
            builder.AddInterceptors(interceptor);
        }

        return builder.Options;
    }

    private static RequestIntakeService CreateService(
        GovernedAccessDbContext context)
    {
        var requestContext = new EfRequestContextReader(context);
        var validator = new RequestValidator(requestContext);
        return new RequestIntakeService(
            new UnusedInterpreter(),
            validator,
            new EfRequestIntakeStore(context),
            new RequestSubmissionService(
                validator,
                requestContext,
                new EfWorkflowStore(context)),
            new DeterministicClock(ConfirmedAt));
    }

    private static ConfirmRequestIntakeCommand ConfirmationCommand(
        string correlationId) =>
        new(
            new AuthenticatedChannelActor(
                RequestIntakeSession.TeamsChannel,
                FakeTeamsActivityBuilder.DefaultTenantId,
                FakeTeamsActivityBuilder.DefaultActorId,
                FakeTeamsActivityBuilder.DefaultConversationId,
                DemoPrincipalKeys.Requester),
            SessionId,
            correlationId);

    private static async Task AssertSingleSubmissionAsync(
        DbContextOptions<GovernedAccessDbContext> options,
        CancellationToken cancellationToken)
    {
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

        Assert.Equal(RequestIntakeStatus.Submitted, session.Status);
        Assert.Equal(ReservedRequestId, session.ReservedRequestId);
        Assert.Equal(ReservedRequestId, request.Id);
        Assert.Equal(ReservedRequestId, auditEvent.RequestId);
        Assert.Equal(AuditEventType.RequestCreated, auditEvent.EventType);
        Assert.Empty(
            await verification.ApprovalDecisions
                .AsNoTracking()
                .ToListAsync(cancellationToken));
        Assert.Empty(
            await verification.ProvisioningOperations
                .AsNoTracking()
                .ToListAsync(cancellationToken));
        Assert.Empty(
            await verification.AccessGrants
                .AsNoTracking()
                .ToListAsync(cancellationToken));
    }

    private sealed class ConfirmationSaveCoordinator
    {
        private readonly TaskCompletionSource bothSavesReady =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly TaskCompletionSource contenderRelease =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        private int saveCount;

        public Task BothSavesReady => bothSavesReady.Task;

        public async Task WaitAsync(
            bool isWinner,
            CancellationToken cancellationToken)
        {
            if (Interlocked.Increment(ref saveCount) == 2)
            {
                bothSavesReady.TrySetResult();
            }

            await bothSavesReady.Task.WaitAsync(cancellationToken);
            if (!isWinner)
            {
                await contenderRelease.Task.WaitAsync(cancellationToken);
            }
        }

        public void ReleaseContender() => contenderRelease.TrySetResult();
    }

    private sealed class ConfirmationSaveGate(
        ConfirmationSaveCoordinator coordinator,
        bool isWinner) : SaveChangesInterceptor
    {
        public override async ValueTask<InterceptionResult<int>> SavingChangesAsync(
            DbContextEventData eventData,
            InterceptionResult<int> result,
            CancellationToken cancellationToken = default)
        {
            await coordinator.WaitAsync(isWinner, cancellationToken);
            return result;
        }
    }

    private sealed class UnusedInterpreter : IRequestPreparationInterpreter
    {
        public Task<RequestPreparationInterpretationOutcome> InterpretAsync(
            RequestPreparationTurn turn,
            CancellationToken cancellationToken) =>
            throw new NotSupportedException(
                "Confirmation does not invoke request interpretation.");
    }
}
