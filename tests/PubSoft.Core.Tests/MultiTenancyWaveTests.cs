using PubSoft.MultiTenancy;
using Xunit;

namespace PubSoft.Core.Tests;

public sealed class MultiTenancyWaveTests
{
    [Fact]
    public void TenantDtoDefaultsAndAssigns()
    {
        var dto = new TenantDto
        {
            Id = 1,
            Code = "c1",
            Name = "n1",
            DisplayName = "d1",
            IsEnabled = true,
            Description = "desc",
        };
        Assert.Equal(1, dto.Id);
        Assert.Equal("c1", dto.Code);
        Assert.True(dto.IsEnabled);
        Assert.Equal("desc", dto.Description);
        Assert.Equal(default, new TenantDto().CreationTime);
    }

    [Fact]
    public void CreateTenantInputDefaults()
    {
        var input = new CreateTenantInput { Code = "c", Name = "n" };
        Assert.Equal("c", input.Code);
        Assert.Null(input.DisplayName);
        Assert.Null(input.Description);
    }

    [Fact]
    public void UpdateTenantInputIsEnabledDefaultsTrue()
    {
        var input = new UpdateTenantInput { Name = "n" };
        Assert.True(input.IsEnabled);
        Assert.Equal("n", input.Name);
    }

    [Fact]
    public void TenantContractsSurfaceTypes()
    {
        Assert.NotNull(typeof(TenantDto));
        Assert.NotNull(typeof(CreateTenantInput));
        Assert.NotNull(typeof(UpdateTenantInput));
        Assert.NotNull(typeof(ITenantAppService));
    }
}
