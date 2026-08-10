using System.Text.Json;

namespace GovernedAccess.Core.Domain.AccessRequests;

internal static class WorkflowEvidenceValidation
{
    public static void EnsureNotEmpty(Guid value, string parameterName)
    {
        if (value == Guid.Empty)
        {
            throw new ArgumentException("The identifier must not be empty.", parameterName);
        }
    }

    public static void EnsureDefined<TEnum>(TEnum value, string parameterName)
        where TEnum : struct, Enum
    {
        if (!Enum.IsDefined(value))
        {
            throw new ArgumentOutOfRangeException(
                parameterName,
                value,
                "The enumeration value is not supported.");
        }
    }

    public static string? NormalizeOptionalText(
        string? value,
        int maximumLength,
        string parameterName)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        value = value.Trim();

        if (value.Length > maximumLength)
        {
            throw new ArgumentOutOfRangeException(
                parameterName,
                value.Length,
                $"The value must not exceed {maximumLength} characters.");
        }

        return value;
    }

    public static string EnsureJsonObject(string value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value);

        using var document = JsonDocument.Parse(value);

        if (document.RootElement.ValueKind != JsonValueKind.Object)
        {
            throw new ArgumentException("Audit details must be a JSON object.", nameof(value));
        }

        return value;
    }
}
