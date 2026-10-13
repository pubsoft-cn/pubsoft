namespace PubSoft.Contracts;

public interface IApiRequest;

public interface IApiRequest<TResponse> : IApiRequest where TResponse : notnull;