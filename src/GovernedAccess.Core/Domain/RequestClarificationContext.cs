namespace GovernedAccess.Core.Domain;

public enum RequestClarificationTarget
{
    ClientId,
    EnvironmentId,
    RequestedRoleId,
    Justification,
    IncidentId,
}

/// <summary>
/// One ordered choice for the current clarification. The value is a proposed stable
/// identifier until application code reloads and canonicalizes it.
/// </summary>
public sealed record RequestClarificationOption
{
    public const int MaximumValueLength = 200;

    public const int MaximumLabelLength = 200;

    public RequestClarificationOption(string value, string label)
    {
        Value = NormalizeBounded(
            value,
            MaximumValueLength,
            nameof(value),
            "A clarification option value");
        Label = NormalizeBounded(
            label,
            MaximumLabelLength,
            nameof(label),
            "A clarification option label");
    }

    public string Value { get; }

    public string Label { get; }

    private static string NormalizeBounded(
        string value,
        int maximumLength,
        string parameterName,
        string description)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value, parameterName);
        value = value.Trim();

        if (value.Length > maximumLength)
        {
            throw new ArgumentOutOfRangeException(
                parameterName,
                value.Length,
                $"{description} cannot exceed {maximumLength} characters.");
        }

        return value;
    }
}

/// <summary>
/// Bounded application-owned memory for one focused clarification. It is neither a
/// transcript nor authorization evidence.
/// </summary>
public sealed class RequestClarificationContext
{
    public const int MaximumPromptLength = 500;

    public const int MaximumOptions = 10;

    public RequestClarificationContext(
        RequestClarificationTarget target,
        string prompt,
        IEnumerable<RequestClarificationOption>? options = null)
    {
        if (!Enum.IsDefined(target))
        {
            throw new ArgumentOutOfRangeException(nameof(target));
        }

        ArgumentException.ThrowIfNullOrWhiteSpace(prompt);
        prompt = prompt.Trim();
        if (prompt.Length > MaximumPromptLength)
        {
            throw new ArgumentOutOfRangeException(
                nameof(prompt),
                prompt.Length,
                $"A clarification prompt cannot exceed {MaximumPromptLength} characters.");
        }

        var optionList = options?.ToArray() ?? [];
        if (optionList.Length > MaximumOptions)
        {
            throw new ArgumentOutOfRangeException(
                nameof(options),
                optionList.Length,
                $"A clarification cannot contain more than {MaximumOptions} options.");
        }

        if (optionList.Any(option => option is null))
        {
            throw new ArgumentException(
                "Clarification options cannot contain null values.",
                nameof(options));
        }

        var distinctValueCount = optionList
            .Select(option => option.Value)
            .Distinct(StringComparer.Ordinal)
            .Count();
        if (distinctValueCount != optionList.Length)
        {
            throw new ArgumentException(
                "Clarification option values must be unique.",
                nameof(options));
        }

        Target = target;
        Prompt = prompt;
        Options = Array.AsReadOnly(optionList);
    }

    public RequestClarificationTarget Target { get; }

    public string Prompt { get; }

    public IReadOnlyList<RequestClarificationOption> Options { get; }
}
