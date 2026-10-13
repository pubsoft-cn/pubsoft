using PubSoft.Auditing;

var collector = new AuditChangeCollector();
var scope = new AuditScope(collector);
scope.Initialize(new AuditRequestContext(DateTimeOffset.UtcNow.AddSeconds(-1), "POST", "/orders"));
scope.SetBusiness(category: "orders", action: "create", targetType: "Order", targetId: "order-42");
collector.Add(new AuditEntityChange(
    "Order",
    "order-42",
    AuditChangeKind.Created,
    DateTimeOffset.UtcNow,
    [new AuditPropertyChange("Status", null, AuditValueSerializer.Serialize("new", 64))]));
scope.MarkFailure("DomainRejected");

var snapshot = scope.Finalize(success: true, statusCode: 200);
if (snapshot is null
    || snapshot.Success
    || snapshot.FailureCode != "DomainRejected"
    || snapshot.Business.TargetId != "order-42"
    || snapshot.Changes.Count != 1
    || snapshot.Duration < TimeSpan.Zero
    || !ReferenceEquals(snapshot, scope.Finalize(success: false, statusCode: 500)))
    throw new InvalidOperationException("Auditing scope finalization smoke check failed.");

Console.WriteLine("Isolated PubSoft.Auditing consumer smoke check passed.");