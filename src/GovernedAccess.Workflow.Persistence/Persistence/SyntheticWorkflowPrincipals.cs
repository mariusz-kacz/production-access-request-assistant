using GovernedAccess.Core.Domain.AccessRequests;
using Microsoft.EntityFrameworkCore;

namespace GovernedAccess.Workflow.Persistence;

internal static class SyntheticWorkflowPrincipals
{
    internal static async Task SeedAsync(
        WorkflowDbContext dbContext,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(dbContext);

        WorkflowPrincipalRecord[] expected =
        [
            new()
            {
                Id = "requester",
                DisplayName = "Demo Requester",
                Kind = nameof(PrincipalKind.Requester),
            },
            BusinessApprover(
                "client-alpha-business-approver",
                "Client Alpha Business Approver",
                "client-alpha"),
            BusinessApprover(
                "client-beta-business-approver",
                "Client Beta Business Approver",
                "client-beta"),
            BusinessApprover(
                "client-gamma-business-approver",
                "Client Gamma Business Approver",
                "client-gamma"),
            BusinessApprover(
                "client-theta-business-approver",
                "Client Theta Business Approver",
                "client-theta"),
            new()
            {
                Id = "devops-approver",
                DisplayName = "DevOps Approver",
                Kind = nameof(PrincipalKind.DevOpsApprover),
            },
        ];

        var expectedById = expected.ToDictionary(principal => principal.Id);
        var existing = await dbContext.AuthenticatedPrincipals
            .ToListAsync(cancellationToken);
        var existingIds = new HashSet<string>(StringComparer.Ordinal);
        foreach (var principal in existing)
        {
            if (!expectedById.TryGetValue(principal.Id, out var expectedPrincipal)
                || principal.DisplayName != expectedPrincipal.DisplayName
                || principal.Kind != expectedPrincipal.Kind
                || principal.ClientId != expectedPrincipal.ClientId)
            {
                throw new InvalidOperationException(
                    $"WorkflowPrincipalRecord record '{principal.Id}' conflicts with the synthetic workflow dataset.");
            }

            existingIds.Add(principal.Id);
        }

        foreach (var principal in expected)
        {
            if (!existingIds.Contains(principal.Id))
            {
                dbContext.AuthenticatedPrincipals.Add(principal);
            }
        }

        await dbContext.SaveChangesAsync(cancellationToken);
    }

    private static WorkflowPrincipalRecord BusinessApprover(
        string id,
        string displayName,
        string clientId) =>
        new()
        {
            Id = id,
            DisplayName = displayName,
            Kind = nameof(PrincipalKind.BusinessApprover),
            ClientId = clientId,
        };
}
