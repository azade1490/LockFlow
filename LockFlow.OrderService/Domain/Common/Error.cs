namespace LockFlow.OrderService.Domain.Common;
public sealed record Error(
string Code,
string Description,
ErrorType Type)
{
    public static readonly Error None =
        new(string.Empty, string.Empty, ErrorType.None);

    public static readonly Error NullValue =
        new(
            "General.Null",
            "A null value was provided.",
            ErrorType.Failure);

    public static Error Validation(string code, string description) =>
        new(code, description, ErrorType.Validation);

    public static Error NotFound(string code, string description) =>
        new(code, description, ErrorType.NotFound);

    public static Error Conflict(string code, string description) =>
        new(code, description, ErrorType.Conflict);

    public static Error Failure(string code, string description) =>
        new(code, description, ErrorType.Failure);

    public static Error ArgumentOutOfRange(string code, string description) =>
        new(code, description, ErrorType.ArgumentOutOfRange);
}
