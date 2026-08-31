namespace GovernedAccess.Workflow.Persistence;

internal sealed class WorkflowPrincipalRecord
{
    public string Id { get; set; } = string.Empty;

    public string DisplayName { get; set; } = string.Empty;

    public string Kind { get; set; } = string.Empty;

    public string? ClientId { get; set; }
}

internal sealed class RequestPreparationRecord
{
    public Guid PreparationId { get; set; }

    public Guid? PredecessorPreparationId { get; set; }

    public string Channel { get; set; } = string.Empty;

    public string TenantId { get; set; } = string.Empty;

    public string ChannelActorId { get; set; } = string.Empty;

    public string ConversationId { get; set; } = string.Empty;

    public string RequesterId { get; set; } = string.Empty;

    public string Lifecycle { get; set; } = string.Empty;

    public string? ClientId { get; set; }

    public string? EnvironmentId { get; set; }

    public string? RoleId { get; set; }

    public string? Justification { get; set; }

    public string? IncidentId { get; set; }

    public long ConcurrencyVersion { get; set; }

    public DateTimeOffset CreatedAt { get; set; }

    public DateTimeOffset UpdatedAt { get; set; }

    public DateTimeOffset? ReadyAt { get; set; }

    public DateTimeOffset? ReadyDeadline { get; set; }

    public DateTimeOffset? TerminalAt { get; set; }

    public string CorrelationId { get; set; } = string.Empty;

    public string? ClarificationJson { get; set; }

    public string MaterialChangeAttributionsJson { get; set; } = "[]";
}
