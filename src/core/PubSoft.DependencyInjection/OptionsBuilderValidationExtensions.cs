using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace PubSoft.DependencyInjection;

/// <summary>OptionsBuilder validation helpers.</summary>
public static class OptionsBuilderValidationExtensions
{
    /// <summary>
    /// Registers a validator whose delegate returns the failure message (null when
    /// valid). Use instead of <c>Validate(Func&lt;TOptions, bool&gt;, string)</c>
    /// whenever the validation can explain itself - the detail reaches
    /// <see cref="OptionsValidationException"/> instead of a static "options are
    /// invalid" placeholder.
    /// </summary>
    public static OptionsBuilder<TOptions> ValidateDetail<TOptions>(
        this OptionsBuilder<TOptions> builder, Func<TOptions, string?> validate) where TOptions : class
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentNullException.ThrowIfNull(validate);
        builder.Services.AddSingleton<IValidateOptions<TOptions>>(new DelegateValidateOptions<TOptions>(validate));
        return builder;
    }
}
