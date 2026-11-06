using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;

namespace PubSoft.Core;

/// <summary>A success-or-typed-error union - the non-throwing alternative to <see cref="DomainException"/>.</summary>
public readonly struct Result<T>
{
    private readonly T? _value;
    private readonly ResultError _error;

    private Result(T value) { _value = value; _error = default; IsOk = true; }
    private Result(ResultError error) { _value = default; _error = error; IsOk = false; }

    /// <summary>Whether the result carries a value.</summary>
    [MemberNotNullWhen(true, nameof(_value))]
    public bool IsOk { get; }

    /// <summary>The success value; throws <see cref="InvalidOperationException"/> on a failed result.</summary>
    public T Value
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => IsOk ? _value! : ThrowNoValue();
    }

    /// <summary>The failure details; meaningless when <see cref="IsOk"/>.</summary>
    public ResultError Error
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => _error;
    }    /// <summary>Creates a successful result.</summary>


    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Result<T> Ok(T value) => new(value);    /// <summary>Creates a failed result from a code and message.</summary>


    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Result<T> Fail(string code, string message) => new(new ResultError(code, message));    /// <summary>Creates a failed result.</summary>


    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Result<T> Fail(ResultError error) => new(error);    /// <summary>Transforms the success value; failures pass through unchanged.</summary>


    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public Result<TNext> Map<TNext>(Func<T, TNext> selector)
    {
        ArgumentNullException.ThrowIfNull(selector);
        return IsOk ? Result<TNext>.Ok(selector(_value!)) : Result<TNext>.Fail(_error);
    }    /// <summary>Chains into another result-producing operation; failures short-circuit.</summary>


    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public Result<TNext> Bind<TNext>(Func<T, Result<TNext>> selector)
    {
        ArgumentNullException.ThrowIfNull(selector);
        return IsOk ? selector(_value!) : Result<TNext>.Fail(_error);
    }    /// <summary>Returns the value, or <paramref name="fallback"/> on failure.</summary>


    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public T Unwrap(T fallback) => IsOk ? _value! : fallback;    /// <summary>Wraps a value into a successful result.</summary>


    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static implicit operator Result<T>(T value) => Ok(value);

    public override string ToString() => IsOk ? $"Ok({_value})" : $"Fail({_error})";

    [DoesNotReturn]
    private static T ThrowNoValue() => throw new InvalidOperationException("Cannot access Value on a failed Result.");
}

/// <summary>Failure details carried by <see cref="Result{T}"/>.</summary>
public readonly struct ResultError(string code, string message)
{
    /// <summary>Machine-readable failure code.</summary>
    public string Code { get; } = code;
    /// <summary>Human-readable failure description.</summary>
    public string Message { get; } = message;
    public override string ToString() => $"{Code}: {Message}";
}
