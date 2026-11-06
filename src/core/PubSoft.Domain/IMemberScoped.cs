namespace PubSoft.Domain;


/// <summary>Marker for member-scoped rows - drives member query filtering.</summary>
public interface IMemberScoped
{
    /// <summary>Owning member.</summary>
    long? MemberId { get; set; }
}
