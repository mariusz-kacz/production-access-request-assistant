using GovernedAccess.Core.Domain.AccessRequests;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace GovernedAccess.Workflow.Persistence;

public sealed record ConsequentialWorkflowRowCounts(
    int Requests,
    int ApprovalDecisions,
    int ProvisioningOperations,
    int AccessGrants);

public static class WorkflowPersistenceEvidence
{
    public static async Task<ConsequentialWorkflowRowCounts> CountConsequentialRowsAsync(
        IServiceProvider services,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(services);
        var context = services.GetRequiredService<WorkflowDbContext>();
        return new ConsequentialWorkflowRowCounts(
            await context.Set<AccessRequest>().CountAsync(cancellationToken),
            await context.Set<ApprovalDecision>().CountAsync(cancellationToken),
            await context.Set<ProvisioningOperation>().CountAsync(cancellationToken),
            await context.Set<AccessGrant>().CountAsync(cancellationToken));
    }
}
