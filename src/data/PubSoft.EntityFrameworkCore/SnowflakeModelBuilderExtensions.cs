using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;

namespace PubSoft.EntityFrameworkCore;

/// <summary>EF conventions for Snowflake primary keys.</summary>
public static class SnowflakeModelBuilderExtensions
{
    /// <summary>Marks every long/long? PK as application-assigned (ValueGenerated.Never).</summary>
    public static ModelBuilder ApplySnowflakeIdConventions(this ModelBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);
        foreach (var entityType in builder.Model.GetEntityTypes())
        {
            var key = entityType.FindPrimaryKey();
            if (key is null) continue;
            foreach (var property in key.Properties)
                if (property.ClrType == typeof(long))
                    property.ValueGenerated = ValueGenerated.Never;
        }
        return builder;
    }
}
