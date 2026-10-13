using PubSoft.MultiTenancy;

var tenant = new Tenant(42, "  acme  ", "Acme Operations", "Acme");
tenant.Update("Acme Operations", "Acme Services", "local package consumer");
tenant.SetEnabled(false);

if (tenant.Code != "acme"
    || tenant.DisplayName != "Acme Services"
    || tenant.Description != "local package consumer"
    || tenant.IsEnabled)
    throw new InvalidOperationException("Multi-tenancy package consumer check failed.");

try
{
    _ = new Tenant(43, " ", "Invalid");
    throw new InvalidOperationException("Blank tenant codes must be rejected.");
}
catch (ArgumentException)
{
}

try
{
    _ = new Tenant(44, new string('x', 65), "Invalid");
    throw new InvalidOperationException("Overlong tenant codes must be rejected.");
}
catch (ArgumentOutOfRangeException)
{
}

Console.WriteLine("Isolated PubSoft.MultiTenancy consumer smoke check passed.");