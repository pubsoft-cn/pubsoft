using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using PubSoft.Auditing;
using PubSoft.EntityFrameworkCore;
using PubSoft.Messaging.Outbox;

namespace PubSoft.Auditing.Outbox;

/// <summary>Outbox event carrying a finalized audit snapshot for deferred persistence.</summary>
/// <param name="SchemaVersion">Event schema version.</param>
/// <param name="Snapshot">The finalized audit snapshot.</param>
public sealed record AuditSnapshotOutboxEvent(int SchemaVersion, AuditRequestSnapshot Snapshot);
