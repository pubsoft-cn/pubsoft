using PubSoft.Mapping;
using Xunit;

namespace PubSoft.Core.Tests;

[Collection("object-mapper-static")]
public sealed class ObjectMapperWaveTests
{
    private sealed class Src { public string Name { get; set; } = ""; public int Age { get; set; } public string? Skip { get; set; } }
    private sealed class Dst { public string Name { get; set; } = ""; public int Age { get; set; } public string? Skip { get; set; } public string Tenant { get; set; } = ""; }
    // ObjectMapper caches mappings in a static registry — each test needs its own type pair
    private sealed class Src2 { public string Name { get; set; } = ""; }
    private sealed class Dst2 { public string Name { get; set; } = ""; }
    private sealed class Src3 { public int Age { get; set; } }
    private sealed class Dst3 { public string Name { get; set; } = ""; }

    [Fact]
    public void RegisterMapConstIgnore()
    {
        ObjectMapper.Register<Src, Dst>(b => b
            .Map(d => d.Name, s => s.Name.ToUpperInvariant()) // expression mapping
            .Const(d => d.Tenant, "t1")                        // constant
            .Ignore(d => d.Skip));                             // ignored
        var r = ObjectMapper.Map<Src, Dst>(new Src { Name = "ada", Age = 30, Skip = "x" });
        Assert.Equal("ADA", r.Name);
        Assert.Equal(30, r.Age);      // name-match fallback for unmapped props
        Assert.Equal("t1", r.Tenant);
        Assert.Null(r.Skip);          // ignored — not copied
    }

    [Fact]
    public void MapListBatch()
    {
        ObjectMapper.Register<Src2, Dst2>();
        var list = ObjectMapper.MapList<Src2, Dst2>([new Src2 { Name = "a" }, new Src2 { Name = "b" }]);
        Assert.Equal(2, list.Count);
        Assert.Equal("a", list[0].Name);
    }

    [Fact]
    public void ConvertRegistersTypeConverter()
    {
        // s_converters feeds ConvertType — property-level type conversion inside compiled mappings
        ObjectMapper.Convert<int, string>(i => $"#{i}");
        ObjectMapper.Register<Src3, Dst3>(b => b.Map(d => d.Name, s => s.Age)); // int→string via registered converter
        var r = ObjectMapper.Map<Src3, Dst3>(new Src3 { Age = 7 });
        Assert.Equal("#7", r.Name);
    }
}
