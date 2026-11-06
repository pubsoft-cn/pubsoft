using System.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using PubSoft.EntityFrameworkCore;

namespace PubSoft.Messaging.Outbox;


public interface IOutboxScopeProvider
{
    IAsyncEnumerable<AsyncServiceScope> CreateScopesAsync(CancellationToken cancellationToken = default);
}
