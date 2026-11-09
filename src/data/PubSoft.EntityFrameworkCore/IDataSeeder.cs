using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;

namespace PubSoft.EntityFrameworkCore.Migrations;


/// <summary>Host-owned seed hook run at startup.</summary>
public interface IDataSeeder
{
    /// <summary>Runs the seed.</summary>
    Task SeedAsync(CancellationToken cancellationToken = default);
}
