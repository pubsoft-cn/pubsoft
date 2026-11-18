namespace PubSoft.DependencyInjection;

/// <summary>
/// A named eager check run by the startup validator. Register one per lazy
/// dependency whose absence would otherwise surface only on first use - factory
/// lambdas such as <c>AddHttpMessageHandler(services => ...)</c> resolve outside
/// <c>ValidateOnBuild</c>'s reach.
/// </summary>
public sealed class StartupCheck
{
    /// <summary>Creates a check.</summary>
    /// <param name="name">Diagnostic name reported on failure.</param>
    /// <param name="check">Throws when the dependency graph is unsatisfiable.</param>
    public StartupCheck(string name, Action<IServiceProvider> check)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentNullException.ThrowIfNull(check);
        Name = name;
        Check = check;
    }

    /// <summary>Diagnostic name reported on failure.</summary>
    public string Name { get; }

    /// <summary>Throws when the dependency graph is unsatisfiable.</summary>
    public Action<IServiceProvider> Check { get; }
}
