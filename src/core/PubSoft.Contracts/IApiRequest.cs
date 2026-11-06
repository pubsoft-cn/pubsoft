namespace PubSoft.Contracts;

/// <summary>Marker for an outbound request contract.</summary>
public interface IApiRequest;

/// <summary>Outbound request contract producing <typeparamref name="TResponse"/>.</summary>
public interface IApiRequest<TResponse> : IApiRequest where TResponse : notnull;