namespace PubSoft.Domain;

public abstract class Entity<TKey>
{
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

    public static bool operator ==(Entity<TKey>? a, Entity<TKey>? b)
        => a is null ? b is null : a.Equals(b);

    public static bool operator !=(Entity<TKey>? a, Entity<TKey>? b) => !(a == b);
}

public abstract class AggregateRoot<TKey> : Entity<TKey>
{
}
