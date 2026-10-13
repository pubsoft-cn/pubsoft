using PubSoft.Core;
using PubSoft.Domain;
using PubSoft.Abp.Core;
using PubSoft.Abp.AspNetCore;
using PubSoft.Authentication;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Xunit;

namespace PubSoft.Core.Tests;

public sealed class CorePrimitiveTests
{
    [Fact]
    public void Money_UsesMinorUnits_AndComparesByValue()
    {
        var first = Money.FromYuan(12.34m);
        var second = Money.FromFen(1234);

        Assert.Equal(second, first);
        Assert.Equal(12.34m, first.ToYuan());
        Assert.Equal(new Money(617), first.AllocateBps(5000));
    }

    [Fact]
    public void Money_RoundsMidpointsAwayFromZero()
    {
        Assert.Equal(Money.FromFen(101), Money.FromYuan(1.005m));
        Assert.Equal(Money.FromFen(-101), Money.FromYuan(-1.005m));
        Assert.Equal(Money.FromFen(101), Money.FromFen(201).AllocatePercent(50m));
        Assert.Equal(Money.FromFen(-101), Money.FromFen(-201).AllocatePercent(50m));
    }

    [Fact]
    public void Money_Arithmetic_ThrowsInsteadOfWrappingOnOverflow()
    {
        Assert.Throws<OverflowException>(() => _ = Money.FromFen(long.MaxValue) + Money.FromFen(1));
        Assert.Throws<OverflowException>(() => _ = Money.FromFen(long.MinValue) - Money.FromFen(1));
        Assert.Throws<OverflowException>(() => _ = -Money.FromFen(long.MinValue));
        Assert.Throws<OverflowException>(() => _ = Money.FromFen(long.MaxValue) * 2);
        Assert.Throws<OverflowException>(() => _ = Money.FromFen(long.MaxValue) * 2L);
    }

    [Fact]
    public void Result_PropagatesFailures_ThroughMap()
    {
        var failed = Result<int>.Fail("invalid", "Bad input");

        var mapped = failed.Map(value => value.ToString());

        Assert.False(mapped.IsOk);
        Assert.Equal("invalid", mapped.Error.Code);
        Assert.Throws<InvalidOperationException>(() => _ = mapped.Value);
    }

    [Fact]
    public void Result_MapAndBind_RejectNullSelectorsForSuccessAndFailure()
    {
        var success = Result<int>.Ok(1);
        var failure = Result<int>.Fail("invalid", "Bad input");

        Assert.Throws<ArgumentNullException>(() => success.Map<string>(null!));
        Assert.Throws<ArgumentNullException>(() => failure.Map<string>(null!));
        Assert.Throws<ArgumentNullException>(() => success.Bind<string>(null!));
        Assert.Throws<ArgumentNullException>(() => failure.Bind<string>(null!));
    }

    [Fact]
    public void ApiResponse_FailureFactories_RejectTheReservedSuccessCode()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => ApiResponse<int>.Fail(0, "Failure"));
        Assert.Throws<ArgumentOutOfRangeException>(() => ApiResponse.Fail(0, "Failure"));
        Assert.Equal(1, ApiResponse<int>.Fail(1, "Failure").Code);
        Assert.Equal(-1, ApiResponse.Fail(-1, "Failure").Code);
    }

    [Fact]
    public void Specification_RejectsNegativePagingWithoutChangingState()
    {
        var specification = new TestSpecification();

        Assert.Throws<ArgumentOutOfRangeException>(() => specification.SetPaging(-1, 10));
        Assert.Throws<ArgumentOutOfRangeException>(() => specification.SetPaging(0, -1));
        Assert.False(specification.IsPagingEnabled);
        Assert.Equal(0, specification.Skip);
        Assert.Equal(0, specification.Take);
    }

    [Fact]
    public void Specification_AllowsZeroPagingValues()
    {
        var specification = new TestSpecification();

        specification.SetPaging(0, 0);

        Assert.True(specification.IsPagingEnabled);
        Assert.Equal(0, specification.Skip);
        Assert.Equal(0, specification.Take);
    }

    [Fact]
    public void Guard_FlagsDefined_SupportsHighBitsInUnsignedEnums()
    {
        Assert.Equal(WideFlags.High, Guard.FlagsDefined(WideFlags.High));
        Assert.Throws<DomainException>(() => Guard.FlagsDefined((WideFlags)(1UL << 62)));
        Assert.Equal(SignedWideFlags.High, Guard.FlagsDefined(SignedWideFlags.High));
        Assert.Throws<DomainException>(() => Guard.FlagsDefined((SignedWideFlags)(1L << 62)));
    }

    [Fact]
    public void Guard_FlagsDefined_UsesTheUnderlyingWidthForSignedFlags()
    {
        Assert.Equal(NarrowSignedFlags.High | NarrowSignedFlags.Low,
            Guard.FlagsDefined(NarrowSignedFlags.High | NarrowSignedFlags.Low));
        Assert.Throws<DomainException>(() => Guard.FlagsDefined((NarrowSignedFlags)(sbyte)-1));
    }

    [Fact]
    public void Guard_MaxLength_RejectsNullWithDomainException()
    {
        Assert.Throws<DomainException>(() => Guard.MaxLength(null!, 8));
    }

    [Fact]
    public void Entity_Equality_RequiresSameRuntimeType_AndNonDefaultId()
    {
        var first = new TestEntity { Id = 7 };
        var same = new TestEntity { Id = 7 };
        var otherType = new OtherTestEntity { Id = 7 };

        Assert.Equal(first, same);
        Assert.False(first.Equals(otherType));
        Assert.False(new TestEntity().Equals(new TestEntity()));
        Assert.False(new TestStringEntity().Equals(new TestStringEntity()));
    }

    [Fact]
    public void FullAuditedEntity_InitializesCreationTime_AndSoftDeleteDefaults()
    {
        var before = DateTime.UtcNow;
        var entity = new TestAuditedEntity();
        var after = DateTime.UtcNow;

        Assert.InRange(entity.CreationTime, before, after);
        Assert.False(entity.IsDeleted);
        Assert.Null(entity.DeletionTime);
    }

    [Fact]
    public void SnowflakeGenerator_ProducesUniqueJavascriptSafeIdsConcurrently()
    {
        var generator = new SnowflakeIdGenerator(3);
        var ids = new long[20_000];
        Parallel.For(0, ids.Length, index => ids[index] = generator.NextId());

        Assert.Equal(ids.Length, ids.Distinct().Count());
        Assert.All(ids, id => Assert.InRange(id, 0, 9_007_199_254_740_991));
    }

    [Fact]
    public void SnowflakeGenerator_RejectsWorkerIdsOutsideRange()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new SnowflakeIdGenerator(32));
        Assert.Throws<ArgumentOutOfRangeException>(() => new SnowflakeIdGenerator(-1));
    }

    [Fact]
    public void AbpCoreRegistration_UsesTheConfiguredWorkerIdAndSingletonLifetime()
    {
        var services = new ServiceCollection();
        services.AddPubSoftCore();
        services.Configure<PubSoftSnowflakeOptions>(options => options.WorkerId = 17);
        using var provider = services.BuildServiceProvider();

        var generator = provider.GetRequiredService<SnowflakeIdGenerator>();

        Assert.Same(generator, provider.GetRequiredService<SnowflakeIdGenerator>());
        Assert.Equal(17, (generator.NextId() >> 7) & 31);
    }

    [Fact]
    public void AbpCoreRegistration_RejectsMissingWorkerId()
    {
        var services = new ServiceCollection();
        services.AddPubSoftCore();
        using var provider = services.BuildServiceProvider();

        Assert.Throws<OptionsValidationException>(() => provider.GetRequiredService<SnowflakeIdGenerator>());
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(32)]
    public void AbpCoreRegistration_RejectsWorkerIdsOutsideGeneratorRange(long workerId)
    {
        var services = new ServiceCollection();
        services.AddPubSoftCore();
        services.Configure<PubSoftSnowflakeOptions>(options => options.WorkerId = workerId);
        using var provider = services.BuildServiceProvider();

        Assert.Throws<OptionsValidationException>(() => provider.GetRequiredService<SnowflakeIdGenerator>());
    }

    [Fact]
    public void AbpAspNetCoreRegistration_UsesScopedPrincipalSnapshotAccessor()
    {
        var services = new ServiceCollection();
        services.AddPubSoftAbpAspNetCore();
        using var provider = services.BuildServiceProvider();
        using var firstScope = provider.CreateScope();
        using var secondScope = provider.CreateScope();

        var first = firstScope.ServiceProvider.GetRequiredService<IPrincipalSnapshotAccessor>();
        var second = secondScope.ServiceProvider.GetRequiredService<IPrincipalSnapshotAccessor>();

        Assert.NotSame(first, second);
        Assert.Null(first.GetCurrent());
        Assert.Null(second.GetCurrent());
    }

    [Fact]
    public void SnowflakeGenerator_RejectsTimestampsOutsideTheJavaScriptSafeWindow()
    {
        var beforeEpoch = new ManualTimeProvider(DateTimeOffset.FromUnixTimeMilliseconds(1_704_067_199_999));
        var afterWindow = new ManualTimeProvider(DateTimeOffset.FromUnixTimeMilliseconds(1_704_067_200_000 + (1L << 41)));

        Assert.Throws<InvalidOperationException>(() => new SnowflakeIdGenerator(0, beforeEpoch).NextId());
        Assert.Throws<InvalidOperationException>(() => new SnowflakeIdGenerator(0, afterWindow).NextId());
    }

    [Fact]
    public void SnowflakeGenerator_ProducesTheMaximumJavascriptSafeIdAtTheWindowBoundary()
    {
        var maxTimestamp = DateTimeOffset.FromUnixTimeMilliseconds(1_704_067_200_000 + (1L << 41) - 1);
        var generator = new SnowflakeIdGenerator(31, new ManualTimeProvider(maxTimestamp));
        long id = 0;

        for (var index = 0; index <= 127; index++)
            id = generator.NextId();

        Assert.Equal(9_007_199_254_740_991, id);
        Assert.Throws<InvalidOperationException>(() => generator.NextId());
    }

    private sealed class TestEntity : Entity<int>;
    private sealed class OtherTestEntity : Entity<int>;
    private sealed class TestStringEntity : Entity<string>;
    private sealed class TestAuditedEntity : FullAuditedEntity<int>;

    private sealed class TestSpecification : Specification<TestEntity>
    {
        public void SetPaging(int skip, int take) => ApplyPaging(skip, take);
    }

    [Flags]
    private enum WideFlags : ulong
    {
        None = 0,
        Low = 1,
        High = 1UL << 63
    }

    [Flags]
    private enum SignedWideFlags : long
    {
        None = 0,
        Low = 1,
        High = long.MinValue
    }

    [Flags]
    private enum NarrowSignedFlags : sbyte
    {
        None = 0,
        Low = 1,
        High = sbyte.MinValue
    }

    private sealed class ManualTimeProvider(DateTimeOffset utcNow) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => utcNow;
    }
}
