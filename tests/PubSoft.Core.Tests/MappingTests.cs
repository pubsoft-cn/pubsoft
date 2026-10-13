using System.Collections.Concurrent;
using PubSoft.Mapping;
using Xunit;

namespace PubSoft.Core.Tests;

public sealed class MappingTests
{
    [Fact]
    public void Map_MatchesNamesSuffixesAndSupportedConversions()
    {
        var mapped = ObjectMapper.Map<AutomaticSource, AutomaticDestination>(new AutomaticSource
        {
            Id = 42L,
            CreatedAt = 1234L,
            Status = 0
        });

        Assert.Equal(42, mapped.Id);
        Assert.Equal(1234, mapped.CreatedAtMs);
        Assert.True(mapped.Status);
    }

    [Fact]
    public void Register_SupportsExpressionsConstantsAndIgnoredProperties()
    {
        ObjectMapper.Register<ExplicitSource, ExplicitDestination>(mapping => mapping
            .Map(destination => destination.Name, source => source.FullName.Trim())
            .Const(destination => destination.Category, "member")
            .Ignore(destination => destination.InternalNote));

        var mapped = ObjectMapper.Map<ExplicitSource, ExplicitDestination>(new ExplicitSource
        {
            FullName = "  Ada Lovelace  ",
            InternalNote = "do not copy"
        });

        Assert.Equal("Ada Lovelace", mapped.Name);
        Assert.Equal("member", mapped.Category);
        Assert.Null(mapped.InternalNote);
    }

    [Fact]
    public void Convert_UsesRegisteredTypeConverter()
    {
        ObjectMapper.Convert<int, string>(value => $"converted:{value}");

        var mapped = ObjectMapper.Map<ConverterSource, ConverterDestination>(new ConverterSource { Value = 7 });

        Assert.Equal("converted:7", mapped.Value);
    }

    [Fact]
    public void Register_ReplacesPreviouslyCompiledDefaultMapping()
    {
        var source = new ReconfiguredSource { Value = "automatic" };
        Assert.Equal("automatic", ObjectMapper.Map<ReconfiguredSource, ReconfiguredDestination>(source).Value);

        ObjectMapper.Register<ReconfiguredSource, ReconfiguredDestination>(mapping =>
            mapping.Const(destination => destination.Value, "configured"));

        Assert.Equal("configured", ObjectMapper.Map<ReconfiguredSource, ReconfiguredDestination>(source).Value);
    }

    [Fact]
    public void MapList_MapsItemsAndPreservesNullReferenceItems()
    {
        var mapped = ObjectMapper.MapList<ListSource?, ListDestination?>(
        [new ListSource { Value = 1 }, null, new ListSource { Value = 3 }]);

        Assert.Equal(3, mapped.Count);
        Assert.Equal(1, mapped[0]!.Value);
        Assert.Null(mapped[1]);
        Assert.Equal(3, mapped[2]!.Value);
    }

    [Fact]
    public void Map_NullSourceReturnsDefaultDestination()
    {
        Assert.Null(ObjectMapper.Map<ListSource?, ListDestination?>(null));
    }

    [Fact]
    public void Map_NullNullableToNonNullableUsesDestinationDefault()
    {
        var mapped = ObjectMapper.Map<NullableSource, NonNullableDestination>(new NullableSource { Value = null });

        Assert.Equal(0, mapped.Value);
    }

    [Fact]
    public void Map_IncompatiblePropertyTypesFailWithConversionGuidance()
    {
        var error = Assert.Throws<InvalidOperationException>(() =>
            ObjectMapper.Map<IncompatibleSource, IncompatibleDestination>(new IncompatibleSource { Value = "text" }));

        Assert.Contains("Register a type converter", error.Message);
    }

    [Fact]
    public void Configuration_RejectsNestedDestinationSelectors()
    {
        Assert.Throws<ArgumentException>(() => ObjectMapper.Register<ExplicitSource, ExplicitDestination>(mapping =>
            mapping.Map(destination => destination.Name.Length, source => source.FullName.Length)));
    }

    [Fact]
    public void Map_CompilesSafelyForConcurrentFirstUse()
    {
        var outputs = new ConcurrentBag<int>();
        Parallel.For(0, 100, value =>
        {
            var mapped = ObjectMapper.Map<ConcurrentSource, ConcurrentDestination>(new ConcurrentSource { Value = value });
            outputs.Add(mapped.Value);
        });

        Assert.Equal(Enumerable.Range(0, 100).Order(), outputs.Order());
    }

    private sealed class AutomaticSource
    {
        public long Id { get; init; }
        public long CreatedAt { get; init; }
        public int Status { get; init; }
    }

    private sealed class AutomaticDestination
    {
        public int Id { get; set; }
        public long CreatedAtMs { get; set; }
        public bool Status { get; set; }
    }

    private sealed class ExplicitSource
    {
        public string FullName { get; init; } = "";
        public string InternalNote { get; init; } = "";
    }

    private sealed class ExplicitDestination
    {
        public string Name { get; set; } = "";
        public string Category { get; set; } = "";
        public string? InternalNote { get; set; }
    }

    private sealed class ConverterSource { public int Value { get; init; } }
    private sealed class ConverterDestination { public string Value { get; set; } = ""; }
    private sealed class ReconfiguredSource { public string Value { get; init; } = ""; }
    private sealed class ReconfiguredDestination { public string Value { get; set; } = ""; }
    private sealed class ListSource { public int Value { get; init; } }
    private sealed class ListDestination { public int Value { get; set; } }
    private sealed class NullableSource { public int? Value { get; init; } }
    private sealed class NonNullableDestination { public int Value { get; set; } }
    private sealed class IncompatibleSource { public string Value { get; init; } = ""; }
    private sealed class IncompatibleDestination { public DateTime Value { get; set; } }
    private sealed class ConcurrentSource { public int Value { get; init; } }
    private sealed class ConcurrentDestination { public int Value { get; set; } }
}