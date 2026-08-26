namespace GovernedAccess.Core.Preparations.Contracts;

public sealed record DraftPatch
{
    public DraftPatch(
        EnvironmentOperation? environment = null,
        RoleOperation? role = null,
        JustificationOperation? justification = null,
        IncidentOperation? incident = null)
    {
        if (environment is null
            && role is null
            && justification is null
            && incident is null)
        {
            throw new ArgumentException(
                "An update-draft patch must contain at least one operation.");
        }

        Environment = environment;
        Role = role;
        Justification = justification;
        Incident = incident;
    }

    public EnvironmentOperation? Environment { get; }

    public RoleOperation? Role { get; }

    public JustificationOperation? Justification { get; }

    public IncidentOperation? Incident { get; }
}

public abstract record EnvironmentOperation
{
    private protected EnvironmentOperation()
    {
    }
}

public sealed record SetEnvironmentOperation : EnvironmentOperation
{
    public SetEnvironmentOperation(EnvironmentReference reference)
    {
        ArgumentNullException.ThrowIfNull(reference);
        Reference = reference;
    }

    public EnvironmentReference Reference { get; }
}

public sealed record ClearEnvironmentOperation : EnvironmentOperation;

public abstract record EnvironmentReference
{
    private protected EnvironmentReference()
    {
    }
}

public sealed record ExactEnvironmentId : EnvironmentReference
{
    public ExactEnvironmentId(string id)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(id);
        Id = id.Trim();
    }

    public string Id { get; }
}

public sealed record EnvironmentSearchQuery : EnvironmentReference
{
    public const int MaximumLength = 200;

    public EnvironmentSearchQuery(string query)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(query);
        query = query.Trim();
        if (query.Length > MaximumLength)
        {
            throw new ArgumentOutOfRangeException(
                nameof(query),
                query.Length,
                $"An environment search query cannot exceed {MaximumLength} characters.");
        }

        Query = query;
    }

    public string Query { get; }
}

public abstract record RoleOperation
{
    private protected RoleOperation()
    {
    }
}

public sealed record SetRoleOperation : RoleOperation
{
    public SetRoleOperation(string roleId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(roleId);
        RoleId = roleId.Trim();
    }

    public string RoleId { get; }
}

public sealed record ClearRoleOperation : RoleOperation;

public abstract record JustificationOperation
{
    private protected JustificationOperation()
    {
    }
}

public sealed record SetJustificationOperation : JustificationOperation
{
    public SetJustificationOperation(JustificationProposal value)
    {
        ArgumentNullException.ThrowIfNull(value);
        Value = value;
    }

    public JustificationProposal Value { get; }
}

public sealed record ClearJustificationOperation : JustificationOperation;

public sealed record JustificationProposal
{
    public const int MaximumCanonicalLength = 2000;

    public JustificationProposal(string text)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(text);
        Text = text.Trim();
    }

    public string Text { get; }
}

public abstract record IncidentOperation
{
    private protected IncidentOperation()
    {
    }
}

public sealed record SetIncidentOperation : IncidentOperation
{
    public SetIncidentOperation(string incidentId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(incidentId);
        IncidentId = incidentId.Trim();
    }

    public string IncidentId { get; }
}

public sealed record ClearIncidentOperation : IncidentOperation;
