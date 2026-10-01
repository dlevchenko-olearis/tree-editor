using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using TreeEditor.Application.Ports;
using TreeEditor.Infrastructure.Hosting;
using TreeEditor.Infrastructure.Persistence;
using TreeEditor.Infrastructure.Sample;

namespace TreeEditor.Infrastructure;

/// <summary>Registers the PostgreSQL adapters of the application ports and the hosted database jobs.</summary>
public static class InfrastructureServiceCollectionExtensions
{
    /// <summary>
    /// Registers the context, the port adapters, the sample tree, the start-up initializer (migrate, then seed), and
    /// the retention job for idempotency keys.
    /// </summary>
    public static IServiceCollection AddInfrastructure(this IServiceCollection services, InfrastructureOptions options)
    {
        services.AddSingleton(options);
        services.AddDbContext<TreeDbContext>(builder => builder
            .UseNpgsql(options.ConnectionString)
            .ConfigureWarnings(warnings => warnings.Ignore(RelationalEventId.BoolWithDefaultWarning)));
        services.AddScoped<ITreeReader, PostgresTreeReader>();
        services.AddScoped<ITreeWriter, PostgresTreeWriter>();
        services.AddScoped<IUnitOfWork, EfUnitOfWork>();
        services.AddScoped<IIdempotencyStore, PostgresIdempotencyStore>();
        services.AddSingleton<ISampleTreeSource, FixedSampleTreeSource>();
        services.AddHostedService<DatabaseInitializer>();
        services.AddHostedService<IdempotencyRetentionJob>();
        return services;
    }
}
