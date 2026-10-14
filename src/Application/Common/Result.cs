namespace Application.Common;

/// <summary>
/// The one blessed pattern for expected failures (docs/conventions.md, Error handling): a business
/// rule violation or an unreachable dependency is a VALUE, not an exception - flow control never
/// rides on exceptions.
/// </summary>
public sealed class Result<T>
{
    private readonly T? _value;

    private Result(T value)
    {
        _value = value;
        Error = null;
    }

    private Result(Error error)
    {
        _value = default;
        Error = error;
    }

    public Error? Error { get; }

    public bool IsSuccess => Error is null;

    public T Value => IsSuccess
        ? _value!
        : throw new InvalidOperationException($"No value: the result failed with '{Error!.Code}'.");

    public static Result<T> Success(T value) => new(value);

    public static Result<T> Failure(Error error) => new(error);
}
