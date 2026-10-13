using PubSoft.Authorization;

var registry = new PermissionDefinitionRegistry([new RecordPermissions()]);
var permission = registry.GetByApiPath("GET", "/api/records/42").Single();
if (permission.ModuleKey != "records" || permission.Flags != (PermissionFlags.Query | PermissionFlags.Create))
    throw new InvalidOperationException("Authorization package consumer check failed.");
if (PermissionRequirementDeriver.DeriveFlags("POST", "ApproveRecord") != PermissionFlags.Audit)
    throw new InvalidOperationException("Permission flag derivation consumer check failed.");
var moduleRegistry = new PermissionModuleRegistry([new RecordModules()]);
if (PermissionRequirementDeriver.ResolveModuleKey("Record", "api/records/{id}", null, moduleRegistry) != "record")
    throw new InvalidOperationException("Permission module resolution consumer check failed.");
if (new RequireCustomPermissionAttribute("record", "export").PermissionName != "export"
    || new RequireExtPermissionAttribute("record", 1).ExtFlag != 1)
    throw new InvalidOperationException("Permission metadata consumer check failed.");

var authorization = new PermissionAuthorizationService(registry, new RecordGrants());
if (!await authorization.IsGrantedAsync(42, 7, "records", PermissionFlags.Query)
    || await authorization.IsGrantedAsync(42, 7, "records", PermissionFlags.Create))
    throw new InvalidOperationException("Explicit permission grant consumer check failed.");
Console.WriteLine("Isolated PubSoft.Authorization consumer smoke check passed.");

sealed class RecordPermissions : IPermissionDefinitionProvider
{
    public IReadOnlyList<PermissionDefinition> GetDefinitions() =>
    [
        new()
        {
            ModuleKey = "records",
            Flags = PermissionFlags.Query | PermissionFlags.Create,
            DisplayName = "Read records",
            ApiPaths = ["GET /api/records/{recordId}"]
        }
    ];
}

sealed class RecordGrants : IPermissionGrantProvider
{
    public Task<IReadOnlyList<PermissionGrant>> GetGrantsAsync(
        long subjectId,
        long? tenantId,
        CancellationToken cancellationToken = default)
        => Task.FromResult<IReadOnlyList<PermissionGrant>>(
            [new PermissionGrant(tenantId, "records", PermissionFlags.Query)]);
}

sealed class RecordModules : IPermissionModuleProvider
{
    public IReadOnlyCollection<string> All => ["record"];

    public string GetDisplayName(string moduleKey) => moduleKey == "record" ? "Record" : moduleKey;
}