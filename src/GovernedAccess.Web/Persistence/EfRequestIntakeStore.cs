using System.Data.Common;
using GovernedAccess.Core.Application;
using GovernedAccess.Core.Domain;
using GovernedAccess.Core.Ports;
using Microsoft.EntityFrameworkCore;

namespace GovernedAccess.Web.Persistence;

internal sealed class EfRequestIntakeStore(
    GovernedAccessDbContext dbContext) : IRequestIntakeStore
{
    public void AddConversation(RequestPreparationConversation conversation)
    {
        ArgumentNullException.ThrowIfNull(conversation);
        dbContext.RequestPreparationConversations.Add(conversation);
    }

    public Task<ApplicationResult<RequestPreparationConversation>>
        GetActiveConversationAsync(
            AuthenticatedChannelActor actor,
            CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(actor);

        return FindAsync(
            dbContext.RequestPreparationConversations.Where(conversation =>
                conversation.Channel == actor.Channel
                && conversation.TenantId == actor.TenantId
                && conversation.ChannelActorId == actor.ChannelActorId
                && conversation.ConversationId == actor.ConversationId
                && conversation.RequesterId == actor.RequesterId
                && (conversation.Status
                        == RequestPreparationConversationStatus.Collecting
                    || conversation.Status
                        == RequestPreparationConversationStatus.Ready)),
            "request_preparation_conversation_not_found",
            "The active request preparation was not found.",
            cancellationToken);
    }

    public Task<ApplicationResult<RequestPreparationConversation>>
        GetConversationAsync(
            Guid conversationRecordId,
            CancellationToken cancellationToken)
    {
        return FindAsync(
            dbContext.RequestPreparationConversations.Where(conversation =>
                conversation.Id == conversationRecordId),
            "request_preparation_conversation_not_found",
            "The request preparation conversation was not found.",
            cancellationToken);
    }

    public void AddPreparedRequest(PreparedAccessRequest preparedRequest)
    {
        ArgumentNullException.ThrowIfNull(preparedRequest);
        dbContext.PreparedAccessRequests.Add(preparedRequest);
    }

    public Task<ApplicationResult<PreparedAccessRequest>> GetPreparedRequestAsync(
        Guid preparationId,
        CancellationToken cancellationToken)
    {
        return FindAsync(
            dbContext.PreparedAccessRequests.Where(preparedRequest =>
                preparedRequest.PreparationId == preparationId),
            "prepared_request_not_found",
            "The prepared access request was not found.",
            cancellationToken);
    }

    public async Task<ApplicationResult<PreparedAccessRequest>>
        ReloadPreparedRequestAsync(
            Guid preparationId,
            CancellationToken cancellationToken)
    {
        var trackedPreparedRequest = dbContext.PreparedAccessRequests.Local
            .SingleOrDefault(preparedRequest =>
                preparedRequest.PreparationId == preparationId);

        if (trackedPreparedRequest is null)
        {
            return await GetPreparedRequestAsync(
                preparationId,
                cancellationToken);
        }

        try
        {
            await dbContext.Entry(trackedPreparedRequest)
                .ReloadAsync(cancellationToken);
            return dbContext.Entry(trackedPreparedRequest).State
                    == EntityState.Detached
                ? PreparedRequestNotFound()
                : ApplicationResult.Succeeded(trackedPreparedRequest);
        }
        catch (DbException)
        {
            return ReadUnavailable<PreparedAccessRequest>();
        }
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
                    "The request preparation changed while it was being saved."));
        }
        catch (DbUpdateException)
        {
            return ApplicationResult.Failed(
                new ApplicationFailure(
                    ApplicationFailureKind.DependencyFailure,
                    "request_intake_persistence_failed",
                    "The request preparation could not be saved."));
        }
        catch (DbException)
        {
            return ApplicationResult.Failed(
                PersistenceUnavailable());
        }
    }

    private static async Task<ApplicationResult<T>> FindAsync<T>(
        IQueryable<T> query,
        string notFoundCode,
        string notFoundMessage,
        CancellationToken cancellationToken)
        where T : class
    {
        try
        {
            var entity = await query.SingleOrDefaultAsync(cancellationToken);
            return entity is null
                ? NotFound<T>(notFoundCode, notFoundMessage)
                : ApplicationResult.Succeeded(entity);
        }
        catch (DbException)
        {
            return ReadUnavailable<T>();
        }
    }

    private static ApplicationResult<PreparedAccessRequest>
        PreparedRequestNotFound() =>
        NotFound<PreparedAccessRequest>(
            "prepared_request_not_found",
            "The prepared access request was not found.");

    private static ApplicationResult<T> NotFound<T>(
        string code,
        string message)
        where T : notnull =>
        ApplicationResult.Failed<T>(
            new ApplicationFailure(
                ApplicationFailureKind.NotFound,
                code,
                message));

    private static ApplicationResult<T> ReadUnavailable<T>()
        where T : notnull =>
        ApplicationResult.Failed<T>(PersistenceUnavailable());

    private static ApplicationFailure PersistenceUnavailable() =>
        new(
            ApplicationFailureKind.DependencyUnavailable,
            "request_intake_persistence_unavailable",
            "Request-intake persistence is currently unavailable.");
}
