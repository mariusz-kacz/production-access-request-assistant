using System.Data.Common;
using GovernedAccess.Core.Application;
using GovernedAccess.Core.Domain.Drafts;
using GovernedAccess.Core.Ports;
using Microsoft.EntityFrameworkCore;

namespace GovernedAccess.Web.Persistence;

internal sealed class EfRequestIntakeStore(
    GovernedAccessDbContext dbContext) : IRequestIntakeStore
{
    public void Add(RequestIntakeSession session)
    {
        ArgumentNullException.ThrowIfNull(session);
        dbContext.RequestIntakeSessions.Add(session);
    }

    public Task<ApplicationResult<RequestIntakeSession>> GetActiveAsync(
        AuthenticatedChannelActor actor,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(actor);
        return FindAsync(
            dbContext.RequestIntakeSessions.Where(session =>
                session.Channel == actor.Channel
                && session.TenantId == actor.TenantId
                && session.ChannelActorId == actor.ChannelActorId
                && session.ConversationId == actor.ConversationId
                && session.RequesterId == actor.RequesterId
                && (session.Status == RequestIntakeStatus.Collecting
                    || session.Status == RequestIntakeStatus.Ready)),
            cancellationToken);
    }

    public async Task<ApplicationResult<RequestIntakeSession>> GetAsync(
        Guid sessionId,
        CancellationToken cancellationToken)
    {
        var tracked = dbContext.RequestIntakeSessions.Local
            .SingleOrDefault(session => session.Id == sessionId);
        if (tracked is null)
        {
            return await FindAsync(
                dbContext.RequestIntakeSessions.Where(
                    session => session.Id == sessionId),
                cancellationToken);
        }

        try
        {
            await dbContext.Entry(tracked).ReloadAsync(cancellationToken);
            return dbContext.Entry(tracked).State == EntityState.Detached
                ? NotFound()
                : ApplicationResult.Succeeded(tracked);
        }
        catch (DbException)
        {
            return Unavailable<RequestIntakeSession>();
        }
    }

    public async Task<ApplicationResult<Guid>> RecoverSubmittedRequestAsync(
        Guid sessionId,
        AuthenticatedChannelActor actor,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(actor);

        dbContext.ChangeTracker.Clear();
        var reload = await FindAsync(
            dbContext.RequestIntakeSessions.Where(
                session => session.Id == sessionId),
            cancellationToken);
        if (reload.IsFailure)
        {
            return ApplicationResult.Failed<Guid>(reload.Failure!);
        }

        var session = reload.Value;
        if (!session.IsOwnedBy(
                actor.Channel,
                actor.TenantId,
                actor.ChannelActorId,
                actor.ConversationId,
                actor.RequesterId))
        {
            return ApplicationResult.Failed<Guid>(NotFoundFailure());
        }

        if (session.Status != RequestIntakeStatus.Submitted)
        {
            return RecoveryFailed(
                ApplicationFailureKind.ConcurrencyConflict,
                "request_intake_concurrency_unresolved",
                "The concurrent request-intake change did not submit this intake.");
        }

        if (session.ReservedRequestId is not { } requestId)
        {
            return RecoveryFailed(
                ApplicationFailureKind.DependencyFailure,
                "request_intake_submission_evidence_invalid",
                "The submitted intake does not contain its reserved request identifier.");
        }

        return ApplicationResult.Succeeded(requestId);
    }

    public async Task<ApplicationResult> SaveChangesAsync(
        CancellationToken cancellationToken)
    {
        try
        {
            await dbContext.SaveChangesAsync(cancellationToken);
            return ApplicationResult.Succeeded();
        }
        catch (DbUpdateConcurrencyException)
        {
            return ApplicationResult.Failed(
                new ApplicationFailure(
                    ApplicationFailureKind.ConcurrencyConflict,
                    "request_intake_concurrency_conflict",
                    "The request intake changed while it was being saved."));
        }
        catch (DbUpdateException)
        {
            return ApplicationResult.Failed(
                new ApplicationFailure(
                    ApplicationFailureKind.DependencyFailure,
                    "request_intake_persistence_failed",
                    "The request intake could not be saved."));
        }
        catch (DbException)
        {
            return ApplicationResult.Failed(PersistenceUnavailable());
        }
    }

    private static async Task<ApplicationResult<RequestIntakeSession>> FindAsync(
        IQueryable<RequestIntakeSession> query,
        CancellationToken cancellationToken)
    {
        try
        {
            var session = await query.SingleOrDefaultAsync(cancellationToken);
            return session is null
                ? NotFound()
                : ApplicationResult.Succeeded(session);
        }
        catch (DbException)
        {
            return Unavailable<RequestIntakeSession>();
        }
    }

    private static ApplicationResult<RequestIntakeSession> NotFound() =>
        ApplicationResult.Failed<RequestIntakeSession>(
            NotFoundFailure());

    private static ApplicationFailure NotFoundFailure() =>
        new(
            ApplicationFailureKind.NotFound,
            "request_intake_not_found",
            "The request intake was not found.");

    private static ApplicationResult<Guid> RecoveryFailed(
        ApplicationFailureKind kind,
        string code,
        string message) =>
        ApplicationResult.Failed<Guid>(
            new ApplicationFailure(kind, code, message));

    private static ApplicationResult<T> Unavailable<T>()
        where T : notnull =>
        ApplicationResult.Failed<T>(PersistenceUnavailable());

    private static ApplicationFailure PersistenceUnavailable() =>
        new(
            ApplicationFailureKind.DependencyUnavailable,
            "request_intake_persistence_unavailable",
            "Request-intake persistence is currently unavailable.");
}
