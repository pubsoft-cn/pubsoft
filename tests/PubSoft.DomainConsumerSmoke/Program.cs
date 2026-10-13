using PubSoft.Domain;

var first = new SmokeEntity { Id = 42, Name = "first" };
var sameIdentity = new SmokeEntity { Id = 42, Name = "second" };
var differentType = new OtherSmokeEntity { Id = 42 };
if (first != sameIdentity
    || first.GetHashCode() != sameIdentity.GetHashCode()
    || first.Equals(differentType))
    throw new InvalidOperationException("Domain entity identity smoke check failed.");

var unpersistedA = new NullableKeyEntity();
var unpersistedB = new NullableKeyEntity();
if (unpersistedA.Equals(unpersistedB))
    throw new InvalidOperationException("Domain unpersisted entity equality smoke check failed.");

IMultiTenant scope = new RoleDataScope(101, 12, "store", 8, tenantId: 7, isAll: true);
if (scope.TenantId != 7 || scope is not RoleDataScope { RoleId: 12, IsAll: true })
    throw new InvalidOperationException("Domain tenant scope smoke check failed.");

var dimensionRegistry = new DataScopeDimensionRegistry();
dimensionRegistry.Add("store");
var registeredDimensions = dimensionRegistry.Dimensions;
dimensionRegistry.Add("device");
if (!dimensionRegistry.Contains("STORE")
    || registeredDimensions.Count != 1
    || dimensionRegistry.Dimensions.Count != 2)
    throw new InvalidOperationException("Domain data-scope dimension registry smoke check failed.");

var allowedScopeIds = new List<long> { 8, 13 };
var dataScopeDimension = new DataScopeDimension("store", false, allowedScopeIds, currentId: 13);
allowedScopeIds.Add(21);
if (dataScopeDimension.AllowedIds.Count != 2 || dataScopeDimension.CurrentId != 13)
    throw new InvalidOperationException("Domain data-scope dimension snapshot smoke check failed.");
var dataScopeEvaluator = new DataScopeAccessEvaluator(dimensionRegistry);
if (!dataScopeEvaluator.CanAccess("store", 8, [dataScopeDimension])
    || dataScopeEvaluator.CanAccess("store", 21, [dataScopeDimension])
    || !dataScopeEvaluator.CanAccess("device", 1, [new DataScopeDimension("device", true)]))
    throw new InvalidOperationException("Domain data-scope evaluation smoke check failed.");

Console.WriteLine("Isolated PubSoft.Domain consumer smoke check passed.");

sealed class SmokeEntity : Entity<int>
{
    public string Name { get; init; } = string.Empty;
}

sealed class OtherSmokeEntity : Entity<int>;

sealed class NullableKeyEntity : Entity<int?>;