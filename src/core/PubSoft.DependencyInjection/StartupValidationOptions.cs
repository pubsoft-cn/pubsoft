namespace PubSoft.DependencyInjection;

/// <summary>Failure mode for startup checks.</summary>
public enum StartupValidationFailureMode
{
    /// <summary>Abort host startup when any check fails (default - fail closed).</summary>
    Fail,

    /// <summary>Log each failure and continue startup; intended for staged rollouts.</summary>
    Warn
}

/// <summary>Startup validation options.</summary>
public sealed class StartupValidationOptions
{
    /// <summary>How failed checks are handled. Defaults to <see cref="StartupValidationFailureMode.Fail"/>.</summary>
    public StartupValidationFailureMode FailureMode { get; set; } = StartupValidationFailureMode.Fail;
}
