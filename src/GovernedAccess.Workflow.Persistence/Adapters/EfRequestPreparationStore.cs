using System.Data.Common;
using GovernedAccess.Core.Application;
using GovernedAccess.Core.Domain.Preparations;
using GovernedAccess.Core.Ports;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace GovernedAccess.Workflow.Persistence;

internal sealed class EfRequestPreparationStore(WorkflowDbContext dbContext)
    : IRequestPreparationStore
{
    private const int SqliteUniqueConstraint = 2067;
    private readonly Dictionary<Guid, TrackedPreparation> trackedPreparations = [];

    public void Add(RequestPreparation preparation)
    {
        ArgumentNullException.ThrowIfNull(preparation);
        if (trackedPreparations.ContainsKey(preparation.PreparationId))
        {
            throw new InvalidOperationException(
                "The request preparation is already tracked by this store.");
        }

        var record = RequestPreparationRecordMapper.ToRecord(preparation);
        dbContext.RequestPreparations.Add(record);
        trackedPreparations.Add(
            preparation.PreparationId,
            new TrackedPreparation(preparation, record, isAdded: true));
    }

    public async Task<ApplicationResult<RequestPreparation>> GetActiveAsync(
        PreparationBinding binding,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(binding);
        var locallyTracked = trackedPreparations.Values
            .Where(item => item.Preparation.Lifecycle is PreparationLifecycle.Collecting
                or PreparationLifecycle.Ready)
            .Where(item => item.Preparation.Binding == binding)
            .Select(item => item.Preparation)
            .ToArray();
        if (locallyTracked.Length == 1)
        {
            return ApplicationResult.Succeeded(locallyTracked[0]);
        }

        if (locallyTracked.Length > 1)
        {
            return ApplicationResult.Failed<RequestPreparation>(
                WorkflowPersistenceFailures.ActiveRace());
        }

        return await FindAsync(
            QueryPreparations().Where(record =>
                record.Channel == binding.Channel
                && record.TenantId == binding.TenantId
                && record.ChannelActorId == binding.ChannelActorId
                && record.ConversationId == binding.ConversationId
                && record.RequesterId == binding.RequesterId
                && (record.Lifecycle == nameof(PreparationLifecycle.Collecting)
                    || record.Lifecycle == nameof(PreparationLifecycle.Ready))),
            cancellationToken);
    }

    public async Task<ApplicationResult<RequestPreparation>> GetAsync(
        Guid preparationId,
        CancellationToken cancellationToken)
    {
        if (trackedPreparations.TryGetValue(preparationId, out var tracked))
        {
            return ApplicationResult.Succeeded(tracked.Preparation);
        }

        return await FindAsync(
            QueryPreparations().Where(record =>
                record.PreparationId == preparationId),
            cancellationToken);
    }

    public async Task<ApplicationResult> SaveChangesAsync(
        CancellationToken cancellationToken)
    {
        try
        {
            foreach (var tracked in trackedPreparations.Values)
            {
                if (!tracked.IsAdded)
                {
                    RequestPreparationRecordMapper.Synchronize(
                        tracked.Record,
                        tracked.Preparation);
                }
            }

            await dbContext.SaveChangesAsync(cancellationToken);
            foreach (var tracked in trackedPreparations.Values)
            {
                tracked.IsAdded = false;
            }

            return ApplicationResult.Succeeded();
        }
        catch (DbUpdateConcurrencyException)
        {
            return ApplicationResult.Failed(WorkflowPersistenceFailures.Conflict());
        }
        catch (DbUpdateException exception) when (IsActivePreparationRace(exception))
        {
            DetachActiveCreationLosers();
            return ApplicationResult.Failed(WorkflowPersistenceFailures.ActiveRace());
        }
        catch (DbUpdateException exception) when (IsDatabaseUnavailable(exception))
        {
            return ApplicationResult.Failed(WorkflowPersistenceFailures.Unavailable());
        }
        catch (DbUpdateException)
        {
            return ApplicationResult.Failed(WorkflowPersistenceFailures.SaveFailed());
        }
        catch (DbException)
        {
            return ApplicationResult.Failed(WorkflowPersistenceFailures.Unavailable());
        }
        catch (ArgumentException)
        {
            return ApplicationResult.Failed(WorkflowPersistenceFailures.MalformedState());
        }
        catch (InvalidOperationException)
        {
            return ApplicationResult.Failed(WorkflowPersistenceFailures.MalformedState());
        }
    }

    private DbSet<RequestPreparationRecord> QueryPreparations() =>
        dbContext.RequestPreparations;

    private async Task<ApplicationResult<RequestPreparation>> FindAsync(
        IQueryable<RequestPreparationRecord> query,
        CancellationToken cancellationToken)
    {
        try
        {
            var record = await query.SingleOrDefaultAsync(cancellationToken);
            if (record is null)
            {
                return ApplicationResult.Failed<RequestPreparation>(
                    WorkflowPersistenceFailures.NotFound());
            }

            var preparation = RequestPreparationRecordMapper.ToAggregate(record);
            trackedPreparations.Add(
                preparation.PreparationId,
                new TrackedPreparation(preparation, record, isAdded: false));
            return ApplicationResult.Succeeded(preparation);
        }
        catch (DbException)
        {
            return ApplicationResult.Failed<RequestPreparation>(
                WorkflowPersistenceFailures.Unavailable());
        }
        catch (ArgumentException)
        {
            return ApplicationResult.Failed<RequestPreparation>(
                WorkflowPersistenceFailures.MalformedState());
        }
        catch (InvalidOperationException)
        {
            return ApplicationResult.Failed<RequestPreparation>(
                WorkflowPersistenceFailures.MalformedState());
        }
    }

    private bool IsActivePreparationRace(DbUpdateException exception) =>
        exception.InnerException is SqliteException
        {
            SqliteExtendedErrorCode: SqliteUniqueConstraint,
        }
        && trackedPreparations.Values.Any(tracked =>
            tracked.IsAdded
            && tracked.Preparation.Lifecycle is PreparationLifecycle.Collecting
                or PreparationLifecycle.Ready);

    private static bool IsDatabaseUnavailable(DbUpdateException exception) =>
        exception.InnerException is DbException
        && exception.InnerException is not SqliteException { SqliteErrorCode: 19 };

    private void DetachActiveCreationLosers()
    {
        var loserIds = trackedPreparations.Values
            .Where(tracked => tracked.IsAdded)
            .Where(tracked => tracked.Preparation.Lifecycle is PreparationLifecycle.Collecting
                or PreparationLifecycle.Ready)
            .Select(tracked => tracked.Preparation.PreparationId)
            .ToHashSet();
        foreach (var entry in dbContext.ChangeTracker.Entries()
                     .Where(entry => BelongsToPreparation(entry.Entity, loserIds))
                     .ToArray())
        {
            entry.State = EntityState.Detached;
        }

        foreach (var loserId in loserIds)
        {
            trackedPreparations.Remove(loserId);
        }
    }

    private static bool BelongsToPreparation(
        object entity,
        HashSet<Guid> preparationIds) =>
        entity switch
        {
            RequestPreparationRecord record =>
                preparationIds.Contains(record.PreparationId),
            _ => false,
        };

    private sealed class TrackedPreparation(
        RequestPreparation preparation,
        RequestPreparationRecord record,
        bool isAdded)
    {
        internal RequestPreparation Preparation { get; } = preparation;

        internal RequestPreparationRecord Record { get; } = record;

        internal bool IsAdded { get; set; } = isAdded;
    }
}
