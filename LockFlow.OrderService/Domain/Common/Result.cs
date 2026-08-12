using System.Diagnostics.CodeAnalysis;
using static System.Runtime.InteropServices.JavaScript.JSType;

namespace LockFlow.OrderService.Domain.Common;
public class Result
{
    protected Result(bool isSuccess, Error error)
    {
        if (isSuccess && error != Error.None)
            throw new ArgumentException("A successful result cannot contain an error.", nameof(error));

        if (!isSuccess && error == Error.None)
            throw new ArgumentException("A failed result must contain an error.", nameof(error));

        IsSuccess = isSuccess;
        Error = error;
    }

    public bool IsSuccess { get; }

    public bool IsFailure => !IsSuccess;

    public Error Error { get; }

    public static Result Success() =>
        new(true, Error.None);

    public static Result Failure(Error error) =>
        new(false, error);

    public static Result<T> Success<T>(T value) =>
        Result<T>.Success(value);

    public static Result<T> Failure<T>(Error error) =>
        Result<T>.Failure(error);
}

public sealed class Result<T> : Result
{
    private readonly T? _value;

    private Result(T value)
        : base(true, Error.None)
    {
        _value = value;
    }

    private Result(Error error)
        : base(false, error)
    {
    }

    [NotNull]
    public T Value =>
        IsSuccess
            ? _value!
            : throw new InvalidOperationException("Cannot access the value of a failed result.");

    public static Result<T> Success(T value) =>
        new(value);

    public static Result<T> Failure(Error error) =>
        new(error);

    public static implicit operator Result<T>(T value) =>
        Success(value);
}
