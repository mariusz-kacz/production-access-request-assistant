using System.Data.Common;
using GovernedAccess.Core.Application;
using GovernedAccess.Core.Domain;
using GovernedAccess.Core.Ports;
using Microsoft.EntityFrameworkCore;

namespace GovernedAccess.Web.Persistence;

internal sealed class EfWorkflowStore(GovernedAccessDbContext dbContext) : IWorkflowStore
{
    public void AddRequest(AccessRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        dbContext.AccessRequests.Add(request);
    }

    public Task<ApplicationResult<AccessRequest>> GetRequestAsync(
        Guid requestId,
        CancellationToken cancellationToken)
    {
        return FindAsync(
            dbContext.AccessRequests.Where(request => request.Id == requestId),
            "request_not_found",
            "The access request was not found.",
            cancellationToken);
    }

    public async Task<ApplicationResult<AccessRequest>> ReloadRequestAsync(
        Guid requestId,
        CancellationToken cancellationToken)
    {
        var trackedRequest = dbContext.AccessRequests.Local
            .SingleOrDefault(request => request.Id == requestId);

        if (trackedRequest is null)
        {
            return await GetRequestAsync(requestId, cancellationToken);
        }

        try
        {
            await dbContext.Entry(trackedRequest).ReloadAsync(cancellationToken);
            return dbContext.Entry(trackedRequest).State == EntityState.Detached
                ? NotFound<AccessRequest>(
                    "request_not_found",
                    "The access request was not found.")
                : ApplicationResult.Succeeded(trackedRequest);
        }
        catch (DbException)
        {
            return ReadUnavailable<AccessRequest>();
        }
    }

    public Task<ApplicationResult<IReadOnlyList<AccessRequest>>> ListRequestsAsync(
        CancellationToken cancellationToken)
    {
        return ListAsync(
            dbContext.AccessRequests
                .AsNoTracking()
                .OrderByDescending(request => request.LastModifiedAt),
            cancellationToken);
    }

    public void AddApprovalDecision(ApprovalDecision decision)
    {
        ArgumentNullException.ThrowIfNull(decision);
        dbContext.ApprovalDecisions.Add(decision);
    }

    public Task<ApplicationResult<ApprovalDecision>> GetApprovalDecisionAsync(
        Guid requestId,
        ApprovalStage stage,
        CancellationToken cancellationToken)
    {
        return FindAsync(
            dbContext.ApprovalDecisions.Where(decision =>
                decision.RequestId == requestId
                && decision.Stage == stage),
            "approval_decision_not_found",
            "The approval decision was not found.",
            cancellationToken);
    }

    public Task<ApplicationResult<IReadOnlyList<ApprovalDecision>>> ListApprovalDecisionsAsync(
        Guid requestId,
        CancellationToken cancellationToken)
    {
        return ListAsync(
            dbContext.ApprovalDecisions
                .AsNoTracking()
                .Where(decision => decision.RequestId == requestId)
                .OrderBy(decision => decision.DecidedAt),
            cancellationToken);
    }

    public void AddProvisioningOperation(ProvisioningOperation operation)
    {
        ArgumentNullException.ThrowIfNull(operation);
        dbContext.ProvisioningOperations.Add(operation);
    }

    public Task<ApplicationResult<ProvisioningOperation>> GetProvisioningOperationAsync(
        string operationId,
        CancellationToken cancellationToken)
    {
        return FindAsync(
            dbContext.ProvisioningOperations.Where(operation => operation.Id == operationId),
            "provisioning_operation_not_found",
            "The provisioning operation was not found.",
            cancellationToken);
    }

    public async Task<ApplicationResult<ProvisioningOperation>> ReloadProvisioningOperationAsync(
        string operationId,
        CancellationToken cancellationToken)
    {
        var trackedOperation = dbContext.ProvisioningOperations.Local
            .SingleOrDefault(operation => operation.Id == operationId);

        if (trackedOperation is null)
        {
            return await GetProvisioningOperationAsync(operationId, cancellationToken);
        }

        try
        {
            await dbContext.Entry(trackedOperation).ReloadAsync(cancellationToken);
            return dbContext.Entry(trackedOperation).State == EntityState.Detached
                ? NotFound<ProvisioningOperation>(
                    "provisioning_operation_not_found",
                    "The provisioning operation was not found.")
                : ApplicationResult.Succeeded(trackedOperation);
        }
        catch (DbException)
        {
            return ReadUnavailable<ProvisioningOperation>();
        }
    }

    public Task<ApplicationResult<ProvisioningOperation>> GetProvisioningOperationForRequestAsync(
        Guid requestId,
        CancellationToken cancellationToken)
    {
        return FindAsync(
            dbContext.ProvisioningOperations.Where(operation =>
                operation.RequestId == requestId),
            "provisioning_operation_not_found",
            "The provisioning operation was not found.",
            cancellationToken);
    }

    public void AddAccessGrant(AccessGrant grant)
    {
        ArgumentNullException.ThrowIfNull(grant);
        dbContext.AccessGrants.Add(grant);
    }

    public Task<ApplicationResult<AccessGrant>> GetAccessGrantByOperationAsync(
        string operationId,
        CancellationToken cancellationToken)
    {
        return FindAsync(
            dbContext.AccessGrants.Where(grant => grant.OperationId == operationId),
            "access_grant_not_found",
            "The access grant was not found.",
            cancellationToken);
    }

    public Task<ApplicationResult<AccessGrant>> GetAccessGrantForRequestAsync(
        Guid requestId,
        CancellationToken cancellationToken)
    {
        return FindAsync(
            dbContext.AccessGrants.Where(grant => grant.RequestId == requestId),
            "access_grant_not_found",
            "The access grant was not found.",
            cancellationToken);
    }

    public void AddAuditEvent(AuditEvent auditEvent)
    {
        ArgumentNullException.ThrowIfNull(auditEvent);
        dbContext.AuditEvents.Add(auditEvent);
    }

    public Task<ApplicationResult<IReadOnlyList<AuditEvent>>> ListAuditEventsAsync(
        Guid requestId,
        CancellationToken cancellationToken)
    {
        return ListAsync(
            dbContext.AuditEvents
                .AsNoTracking()
                .Where(auditEvent => auditEvent.RequestId == requestId)
                .OrderBy(auditEvent => auditEvent.OccurredAt)
                .ThenBy(auditEvent => auditEvent.Id),
            cancellationToken);
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
                    "workflow_concurrency_conflict",
                    "The request changed while the operation was being saved."));
        }
        catch (DbUpdateException)
        {
            return ApplicationResult.Failed(
                new ApplicationFailure(
                    ApplicationFailureKind.DependencyFailure,
                    "workflow_persistence_failed",
                    "The workflow change could not be saved."));
        }
        catch (DbException)
        {
            return ApplicationResult.Failed(
                new ApplicationFailure(
                    ApplicationFailureKind.DependencyUnavailable,
                    "workflow_persistence_unavailable",
                    "Workflow persistence is currently unavailable."));
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

    private static async Task<ApplicationResult<IReadOnlyList<T>>> ListAsync<T>(
        IQueryable<T> query,
        CancellationToken cancellationToken)
        where T : class
    {
        try
        {
            var entities = await query.ToArrayAsync(cancellationToken);
            return ApplicationResult.Succeeded<IReadOnlyList<T>>(entities);
        }
        catch (DbException)
        {
            return ReadUnavailable<IReadOnlyList<T>>();
        }
    }

    private static ApplicationResult<T> NotFound<T>(string code, string message)
        where T : notnull
    {
        return ApplicationResult.Failed<T>(
            new ApplicationFailure(ApplicationFailureKind.NotFound, code, message));
    }

    private static ApplicationResult<T> ReadUnavailable<T>()
        where T : notnull
    {
        return ApplicationResult.Failed<T>(
            new ApplicationFailure(
                ApplicationFailureKind.DependencyUnavailable,
                "workflow_persistence_unavailable",
                "Workflow persistence is currently unavailable."));
    }
}
