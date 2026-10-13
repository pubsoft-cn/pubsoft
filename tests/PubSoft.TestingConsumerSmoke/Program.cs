using PubSoft.Domain;
using PubSoft.Testing;

ITenantContext tenant = new FixedTenantContext(7);
if (tenant.TenantId != 7)
    throw new InvalidOperationException("FixedTenantContext smoke check failed.");

IMemberScopeContext member = new FixedMemberScopeContext(42);
if (member.MemberId != 42 || !member.IsMemberScoped)
    throw new InvalidOperationException("FixedMemberScopeContext smoke check failed.");

var time = new TestTimeProvider(new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero));
time.Advance(TimeSpan.FromSeconds(30));
if (time.GetUtcNow() != new DateTimeOffset(2026, 1, 1, 0, 0, 30, TimeSpan.Zero))
    throw new InvalidOperationException("TestTimeProvider smoke check failed.");

IPubSoftSaveParticipant participant = new DelegateSaveParticipant(_ => Task.FromResult(3));
if (await participant.SaveChangesAsync() != 3)
    throw new InvalidOperationException("DelegateSaveParticipant smoke check failed.");

Console.WriteLine("Isolated PubSoft.Testing consumer smoke check passed.");
