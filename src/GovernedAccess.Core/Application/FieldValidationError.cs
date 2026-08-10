namespace GovernedAccess.Core.Application;

public sealed class FieldValidationError
{
    public FieldValidationError(string field, string code, string message)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(field);
        ArgumentException.ThrowIfNullOrWhiteSpace(code);
        ArgumentException.ThrowIfNullOrWhiteSpace(message);

        Field = field;
        Code = code;
        Message = message;
    }

    public string Field { get; }

    public string Code { get; }

    public string Message { get; }
}
