using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;

namespace PubSoft.EntityFrameworkCore.Migrations;


public interface IDataSeeder
{
    Task SeedAsync(CancellationToken cancellationToken = default);
}
