using PubSoft.Identity;

var user = new IdentityUser(42, " reader ", tenantId: 7, name: "Reader");
user.SetPasswordHash("opaque-password-hash");
var snapshot = IdentityUserSnapshot.FromUser(user);
if (snapshot.Id != 42 || snapshot.UserName != "reader" || snapshot.Name != "Reader" || snapshot.PhoneNumber is not null
    || user.NormalizedUserName != "READER" || user.TenantId != 7 || user.SecurityStamp.Length != 32)
    throw new InvalidOperationException("Identity package consumer smoke check failed.");

Console.WriteLine("Isolated PubSoft.Identity consumer smoke check passed.");