using Xunit;

namespace PubSoft.Core.Tests;

/// <summary>Serializes tests that register converters on the static ObjectMapper registry -
/// two classes map int-&gt;string differently and race under parallel test scheduling.</summary>
[CollectionDefinition("object-mapper-static")]
public sealed class ObjectMapperCollection;
