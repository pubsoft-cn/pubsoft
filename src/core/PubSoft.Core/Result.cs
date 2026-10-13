using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;

namespace PubSoft.Core;

public readonly struct Result<T>
{
    private readonly T? _value;
    private readonly ResultError _error;

    [MemberNotNullWhen(true, nameof(_value))]
    public bool IsOk { get; }

    public T Value
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => IsOk ? _value! : ThrowNoValue();
    }

    public ResultError Error
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => _error;
    }

    private Result(T value) { _value = value; _error = default; IsOk = true; }
    private Result(ResultError error) { _value = default; _error = error; IsOk = false; }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Result<T> Ok(T value) => new(value);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Result<T> Fail(string code, string message) => new(new ResultError(code, message));

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Result<T> Fail(ResultError error) => new(error);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public Result<TNext> Map<TNext>(Func<T, TNext> selector)
    {
        ArgumentNullException.ThrowIfNull(selector);
        return IsOk ? Result<TNext>.Ok(selector(_value!)) : Result<TNext>.Fail(_error);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public Result<TNext> Bind<TNext>(Func<T, Result<TNext>> selector)
    {
        ArgumentNullException.ThrowIfNull(selector);
        return IsOk ? selector(_value!) : Result<TNext>.Fail(_error);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public T Unwrap(T fallback) => IsOk ? _value! : fallback;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static implicit operator Result<T>(T value) => Ok(value);

    [DoesNotReturn]
    private static T ThrowNoValue() => throw new InvalidOperationException("Cannot access Value on a failed Result.");

    public override string ToString() => IsOk ? $"Ok({_value})" : $"Fail({_error})";
}

public readonly struct ResultError(string code, string message)
{
    public string Code { get; } = code;
    public string Message { get; } = message;
    public override string ToString() => $"{Code}: {Message}";
}
