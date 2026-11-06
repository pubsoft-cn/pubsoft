namespace PubSoft.Domain;

/// <summary>Base entity with identity-based equality.</summary>
/// <typeparam name="TKey">The primary key type.</typeparam>
public abstract class Entity<TKey>
{
    /// <summary>The primary key.</summary>
    public TKey Id { get; set; } = default!;

    public override bool Equals(object? obj)
    {
        if (obj is not Entity<TKey> other)
            return false;
        if (ReferenceEquals(this, other))
            return true;
        if (GetType() != other.GetType())
            return false;
        if (EqualityComparer<TKey>.Default.Equals(Id, default!)
            || EqualityComparer<TKey>.Default.Equals(other.Id, default!))
            return false;
        return EqualityComparer<TKey>.Default.Equals(Id, other.Id);
    }

    public override int GetHashCode() => Id?.GetHashCode() ?? 0;

    /// <summary>Identity-based equality.</summary>
    public static bool operator ==(Entity<TKey>? a, Entity<TKey>? b)
        => a is null ? b is null : a.Equals(b);

    /// <summary>Identity-based inequality.</summary>
    public static bool operator !=(Entity<TKey>? a, Entity<TKey>? b) => !(a == b);
}

/// <summary>Aggregate boundary marker - repositories persist aggregates, not bare entities.</summary>
public abstract class AggregateRoot<TKey> : Entity<TKey>
{
}
