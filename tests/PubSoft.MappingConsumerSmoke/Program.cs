using PubSoft.Mapping;

var mapped = ObjectMapper.Map<MappingSource, MappingDestination>(new MappingSource
{
    Id = 42L,
    CreatedAt = 1_790_755_200_000L
});

if (mapped.Id != 42 || mapped.CreatedAtMs != 1_790_755_200_000L)
    throw new InvalidOperationException("Mapping package consumer check failed.");

Console.WriteLine("Isolated PubSoft.Mapping consumer smoke check passed.");

sealed class MappingSource
{
    public long Id { get; init; }
    public long CreatedAt { get; init; }
}

sealed class MappingDestination
{
    public int Id { get; set; }
    public long CreatedAtMs { get; set; }
}