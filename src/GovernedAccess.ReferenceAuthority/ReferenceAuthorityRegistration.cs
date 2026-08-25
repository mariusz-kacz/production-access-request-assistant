using GovernedAccess.Core.Ports;
using GovernedAccess.ReferenceAuthority.Adapters;
using GovernedAccess.ReferenceAuthority.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace GovernedAccess.ReferenceAuthority;

public static class ReferenceAuthorityRegistration
{
    public const string ConnectionStringName = "ReferenceAuthority";

    public static IServiceCollection AddReferenceAuthority(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);

        var connectionString = configuration.GetConnectionString(ConnectionStringName);
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            throw new InvalidOperationException(
                $"ConnectionStrings:{ConnectionStringName} is required for the isolated reference authority.");
        }

        services.AddDbContext<ReferenceAuthorityDbContext>(options =>
            options.UseSqlite(
                connectionString,
                sqlite => sqlite.MigrationsAssembly(
                    typeof(ReferenceAuthorityDbContext).Assembly.FullName)));
        services.AddScoped<
            IProductionEnvironmentSearchAuthority,
            EfProductionEnvironmentSearchAuthority>();
        services.AddScoped<
            IProductionEnvironmentAuthority,
            EfProductionEnvironmentAuthority>();
        services.AddScoped<IEnvironmentRoleAuthority, EfEnvironmentRoleAuthority>();
        services.AddScoped<IIncidentAuthority, EfIncidentAuthority>();
        return services;
    }
}

public static class ReferenceAuthorityDatabase
{
    public static async Task InitializeAsync(
        IServiceProvider services,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(services);

        await using var scope = services.CreateAsyncScope();
        var context = scope.ServiceProvider
            .GetRequiredService<ReferenceAuthorityDbContext>();
        await context.Database.MigrateAsync(cancellationToken);
        await SyntheticReferenceData.SeedAsync(context, cancellationToken);
    }
}

public sealed class ReferenceAuthorityDesignTimeDbContextFactory
    : IDesignTimeDbContextFactory<ReferenceAuthorityDbContext>
{
    public ReferenceAuthorityDbContext CreateDbContext(string[] args)
    {
        var options = new DbContextOptionsBuilder<ReferenceAuthorityDbContext>()
            .UseSqlite("Data Source=reference-authority.design.db")
            .Options;
        return new ReferenceAuthorityDbContext(options);
    }
}
