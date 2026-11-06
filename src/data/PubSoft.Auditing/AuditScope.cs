using System.Collections.ObjectModel;

namespace PubSoft.Auditing;


/// <summary>Default <see cref="IAuditScope"/>; thread-safe, one-shot finalization.</summary>
public sealed class AuditScope : IAuditScope
{
    private const int MaximumFailureCodeLength = 128;
    private readonly object _gate = new();
    private readonly IAuditChangeCollector _collector;
    private readonly AuditScopeOptions _options;
    private readonly TimeProvider _timeProvider = TimeProvider.System;
    private AuditRequestContext? _request;
    private AuditBusinessContext _business = new();
    private string? _failureCode;
    private AuditRequestSnapshot? _snapshot;
    private bool _initialized;
    private bool _finalizing;

    /// <summary>Creates a scope; <paramref name="options"/> toggles auditing and GET capture.</summary>
    public AuditScope(
        IAuditChangeCollector collector,
        AuditScopeOptions? options = null,
        TimeProvider? timeProvider = null)
    {
        _collector = collector ?? throw new ArgumentNullException(nameof(collector));
        _options = new AuditScopeOptions
        {
            Enabled = options?.Enabled ?? true,
            AuditGetRequests = options?.AuditGetRequests ?? false
        };
        _timeProvider = timeProvider ?? TimeProvider.System;
    }

    /// <inheritdoc />
    public bool IsEnabled
    {
        get
        {
            lock (_gate)
                return _options.Enabled && _initialized;
        }
    }

    /// <inheritdoc />
    public bool IsFinalizing
    {
        get
        {
            lock (_gate)
                return _finalizing;
        }
    }

    /// <inheritdoc />
    public void Initialize(AuditRequestContext request)
    {
        ArgumentNullException.ThrowIfNull(request);
        lock (_gate)
        {
            if (_initialized)
                throw new InvalidOperationException("The audit scope has already been initialized.");
            _initialized = true;
            if (_options.Enabled)
                _request = request;
        }
    }

    /// <inheritdoc />
    public void SetBusiness(
        string? category = null,
        string? action = null,
        string? targetType = null,
        string? targetId = null,
        string? targetName = null,
        string? detail = null)
    {
        lock (_gate)
        {
            EnsureMutable();
            _business = new AuditBusinessContext(
                category ?? _business.Category,
                action ?? _business.Action,
                targetType ?? _business.TargetType,
                targetId ?? _business.TargetId,
                targetName ?? _business.TargetName,
                detail ?? _business.Detail);
        }
    }

    /// <inheritdoc />
    public void MarkFailure(string? failureCode = null)
    {
        if (failureCode is { Length: > MaximumFailureCodeLength }
            || failureCode?.Any(char.IsControl) == true)
            throw new ArgumentException("Failure codes must be at most 128 non-control characters.", nameof(failureCode));

        lock (_gate)
        {
            EnsureMutable();
            _failureCode = failureCode ?? "RequestFailed";
        }
    }

    /// <inheritdoc />
    public AuditRequestSnapshot? Finalize(bool success, int statusCode)
    {
        lock (_gate)
        {
            if (!_initialized)
                throw new InvalidOperationException("The audit scope has not been initialized.");
            if (_finalizing)
                return _snapshot;

            _finalizing = true;
            if (!_options.Enabled || _request is null)
                return null;

            var changes = _collector.GetSnapshot();
            var finalSuccess = success && statusCode < 400 && _failureCode is null;
            if (finalSuccess && changes.Count == 0 && !HasBusiness(_business)
                && (!_options.AuditGetRequests
                    || !string.Equals(_request.HttpMethod, "GET", StringComparison.OrdinalIgnoreCase)))
                return null;

            _snapshot = new AuditRequestSnapshot(
                _request,
                _business,
                finalSuccess,
                statusCode,
                _failureCode ?? (statusCode >= 400 ? $"Http{statusCode}" : null),
                MaxZero(_timeProvider.GetUtcNow() - _request.StartedAt),
                new ReadOnlyCollection<AuditEntityChange>(changes.ToArray()));
            return _snapshot;
        }
    }

    private void EnsureMutable()
    {
        if (!_initialized)
            throw new InvalidOperationException("The audit scope has not been initialized.");
        if (_finalizing)
            throw new InvalidOperationException("The audit scope has already been finalized.");
        if (!_options.Enabled)
            throw new InvalidOperationException("The audit scope is disabled.");
    }

    private static bool HasBusiness(AuditBusinessContext business)
        => business.Category is not null || business.Action is not null || business.TargetType is not null
            || business.TargetId is not null || business.TargetName is not null || business.Detail is not null;

    private static TimeSpan MaxZero(TimeSpan value) => value < TimeSpan.Zero ? TimeSpan.Zero : value;
}
