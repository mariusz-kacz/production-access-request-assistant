using GovernedAccess.Core.Application;
using GovernedAccess.Core.Domain;
using GovernedAccess.Core.Ports;
using GovernedAccess.IntegrationTests.Infrastructure;
using GovernedAccess.Web.Authentication;
using GovernedAccess.Web.Persistence;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace GovernedAccess.IntegrationTests.Teams;

public sealed class RequestIntakeConfirmationComponentTests
{
    private static readonly Guid SessionId =
        Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
    private static readonly DateTimeOffset PreparedAt =
        new(2026, 8, 1, 10, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset ConfirmedAt =
        PreparedAt.AddMinutes(5);

    [Theory]
    [InlineData(ConfirmationCase.Unknown)]
    [InlineData(ConfirmationCase.Expired)]
    [InlineData(ConfirmationCase.Superseded)]
    [InlineData(ConfirmationCase.Invalidated)]
    [InlineData(ConfirmationCase.ForeignOwner)]
    [InlineData(ConfirmationCase.ConversationMismatch)]
    public async Task ConfirmationRejectsClosedLifecycleAndOwnershipCases(
        ConfirmationCase confirmationCase)
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync(cancellationToken);
        var options = new DbContextOptionsBuilder<GovernedAccessDbContext>()
            .UseSqlite(connection)
            .Options;
        await using var dbContext = new GovernedAccessDbContext(options);
        await SyntheticDataSeeder.SeedAsync(dbContext, cancellationToken);
        var session = confirmationCase == ConfirmationCase.Unknown
            ? null
            : CreateSession(confirmationCase);
        if (session is not null)
        {
            dbContext.RequestIntakeSessions.Add(session);
            await dbContext.SaveChangesAsync(cancellationToken);
        }

        var service = CreateService(dbContext);
        var expected = ExpectedFailure(confirmationCase);
        var outcome = await service.ConfirmAsync(
            new ConfirmRequestIntakeCommand(
                ActorFor(confirmationCase),
                confirmationCase == ConfirmationCase.Unknown
                    ? Guid.NewGuid()
                    : SessionId,
                "component-confirmation"),
            cancellationToken);

        Assert.Equal(RequestConfirmationResultKind.Failed, outcome.Kind);
        Assert.Equal(expected.Kind, outcome.Failure!.Kind);
        Assert.Equal(expected.Code, outcome.Failure.Code);
        Assert.Equal(Guid.Empty, outcome.RequestId);

        dbContext.ChangeTracker.Clear();
        Assert.Empty(await dbContext.AccessRequests.ToListAsync(cancellationToken));
        Assert.Empty(await dbContext.AuditEvents.ToListAsync(cancellationToken));
        Assert.Empty(await dbContext.ApprovalDecisions.ToListAsync(cancellationToken));
        Assert.Empty(
            await dbContext.ProvisioningOperations.ToListAsync(cancellationToken));
        Assert.Empty(await dbContext.AccessGrants.ToListAsync(cancellationToken));

        if (session is not null)
        {
            var persisted = await dbContext.RequestIntakeSessions
                .AsNoTracking()
                .SingleAsync(cancellationToken);
            Assert.Equal(session.Status, persisted.Status);
            Assert.Equal(session.ReservedRequestId, persisted.ReservedRequestId);
        }
    }

    private static RequestIntakeSession CreateSession(
        ConfirmationCase confirmationCase)
    {
        var session = new RequestIntakeSession(
            SessionId,
            RequestIntakeSession.TeamsChannel,
            FakeTeamsActivityBuilder.DefaultTenantId,
            FakeTeamsActivityBuilder.DefaultActorId,
            FakeTeamsActivityBuilder.DefaultConversationId,
            DemoPrincipalKeys.Requester,
            PreparedAt,
            "component-preparation");
        session.UpdateCandidate(
            "client-alpha",
            "PROD-ALPHA-EU",
            ProductionRoleIds.ReadOnly,
            "Investigate the active production incident.",
            "INC-1042",
            PreparedAt,
            "component-candidate");
        session.MarkReady(
            Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb"),
            PreparedAt,
            "component-ready");

        switch (confirmationCase)
        {
            case ConfirmationCase.Expired:
                session.MarkExpired(
                    PreparedAt.Add(RequestIntakeSession.ConfirmationLifetime),
                    "component-expired");
                break;
            case ConfirmationCase.Superseded:
                session.MarkSuperseded(
                    PreparedAt.AddMinutes(1),
                    "component-superseded");
                break;
            case ConfirmationCase.Invalidated:
                session.MarkInvalidated(
                    PreparedAt.AddMinutes(1),
                    "component-invalidated");
                break;
            case ConfirmationCase.ForeignOwner:
            case ConfirmationCase.ConversationMismatch:
                break;
            default:
                throw new InvalidOperationException(
                    "The component case does not use a persisted session.");
        }

        return session;
    }

    private static AuthenticatedChannelActor ActorFor(
        ConfirmationCase confirmationCase) =>
        new(
            RequestIntakeSession.TeamsChannel,
            FakeTeamsActivityBuilder.DefaultTenantId,
            confirmationCase == ConfirmationCase.ForeignOwner
                ? "foreign-actor"
                : FakeTeamsActivityBuilder.DefaultActorId,
            confirmationCase == ConfirmationCase.ConversationMismatch
                ? "foreign-conversation"
                : FakeTeamsActivityBuilder.DefaultConversationId,
            DemoPrincipalKeys.Requester);

    private static (ApplicationFailureKind Kind, string Code) ExpectedFailure(
        ConfirmationCase confirmationCase) =>
        confirmationCase switch
        {
            ConfirmationCase.Unknown =>
                (ApplicationFailureKind.NotFound, "request_intake_not_found"),
            ConfirmationCase.Expired =>
                (ApplicationFailureKind.InvalidTransition,
                    RequestIntakeService.ExpiredCode),
            ConfirmationCase.Superseded =>
                (ApplicationFailureKind.InvalidTransition,
                    RequestIntakeService.SupersededCode),
            ConfirmationCase.Invalidated =>
                (ApplicationFailureKind.InvalidTransition,
                    RequestIntakeService.InvalidatedCode),
            ConfirmationCase.ForeignOwner
                or ConfirmationCase.ConversationMismatch =>
                (ApplicationFailureKind.Unauthorized,
                    RequestIntakeService.ForbiddenCode),
            _ => throw new InvalidOperationException(
                "The confirmation component case is unsupported."),
        };

    private static RequestIntakeService CreateService(
        GovernedAccessDbContext dbContext)
    {
        var requestContext = new EfRequestContextReader(dbContext);
        var validator = new RequestValidator(requestContext);
        return new RequestIntakeService(
            new UnusedInterpreter(),
            validator,
            new EfRequestIntakeStore(dbContext),
            new RequestSubmissionService(
                validator,
                requestContext,
                new EfWorkflowStore(dbContext)),
            new DeterministicClock(ConfirmedAt));
    }

    public enum ConfirmationCase
    {
        Unknown,
        Expired,
        Superseded,
        Invalidated,
        ForeignOwner,
        ConversationMismatch,
    }

    private sealed class UnusedInterpreter : IRequestPreparationInterpreter
    {
        public Task<RequestPreparationInterpretationResult> InterpretAsync(
            RequestPreparationTurn turn,
            CancellationToken cancellationToken) =>
            throw new NotSupportedException(
                "Confirmation does not invoke request interpretation.");
    }
}
