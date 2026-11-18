using Microsoft.Extensions.Options;

namespace PubSoft.DependencyInjection;

/// <summary>
/// <see cref="IValidateOptions{TOptions}"/> over a delegate that returns the
/// field-level failure message. <c>OptionsBuilder.Validate(Func, string)</c> only
/// carries a static message, so validators with a detailed error channel
/// (for example an <c>IsValid(out error)</c> member) lose the very text operators
/// need; this type routes the detail into <see cref="ValidateOptionsResult.Fail"/>.
/// </summary>
public sealed class DelegateValidateOptions<TOptions> : IValidateOptions<TOptions> where TOptions : class
{
    private readonly Func<TOptions, string?> _validate;

    /// <summary>Creates the validator. The delegate returns null when valid, else the failure detail.</summary>
    public DelegateValidateOptions(Func<TOptions, string?> validate)
        => _validate = validate ?? throw new ArgumentNullException(nameof(validate));

    /// <inheritdoc />
    public ValidateOptionsResult Validate(string? name, TOptions options)
        => _validate(options) is { Length: > 0 } failure
            ? ValidateOptionsResult.Fail(failure)
            : ValidateOptionsResult.Success;
}
