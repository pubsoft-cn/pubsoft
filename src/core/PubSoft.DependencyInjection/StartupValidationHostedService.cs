using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace PubSoft.DependencyInjection;

/// <summary>
/// Runs every registered <see cref="StartupCheck"/> inside a service scope once at
/// host start. Aggregates failures; <see cref="StartupValidationFailureMode.Fail"/>
/// aborts startup, <see cref="StartupValidationFailureMode.Warn"/> logs and proceeds.
/// </summary>
public sealed class StartupValidationHostedService : IHostedService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IEnumerable<StartupCheck> _checks;
    private readonly StartupValidationOptions _options;
    private readonly ILogger<StartupValidationHostedService>? _logger;

    /// <summary>Creates the validator.</summary>
    public StartupValidationHostedService(
        IServiceScopeFactory scopeFactory,
        IEnumerable<StartupCheck> checks,
        StartupValidationOptions options,
        ILogger<StartupValidationHostedService>? logger = null)
    {
        _scopeFactory = scopeFactory ?? throw new ArgumentNullException(nameof(scopeFactory));
        _checks = checks ?? throw new ArgumentNullException(nameof(checks));
        _options = options ?? throw new ArgumentNullException(nameof(options));
        _logger = logger;
    }

    /// <inheritdoc />
    public Task StartAsync(CancellationToken cancellationToken)
    {
        var failures = new List<string>();
        using var scope = _scopeFactory.CreateScope();
        foreach (var check in _checks)
        {
            try
            {
                check.Check(scope.ServiceProvider);
            }
            catch (Exception ex)
            {
                failures.Add($"{check.Name}: {ex.Message}");
            }
        }

        if (failures.Count == 0)
            return Task.CompletedTask;

        var message = $"Startup validation failed: {string.Join("; ", failures)}";
        if (_options.FailureMode == StartupValidationFailureMode.Fail)
            throw new InvalidOperationException(message);
        _logger?.LogWarning("{Message}", message);
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
