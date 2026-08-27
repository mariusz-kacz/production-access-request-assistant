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
    private static readonly string[] FinalTables =
    [
        "Clients",
        "EnvironmentRoles",
        "Incidents",
        "ProductionEnvironments",
    ];

    public static async Task InitializeAsync(
        IServiceProvider services,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(services);

        await using var scope = services.CreateAsyncScope();
        var context = scope.ServiceProvider
            .GetRequiredService<ReferenceAuthorityDbContext>();
        await EnsureCompatibleSchemaAsync(context, cancellationToken);
        await context.Database.MigrateAsync(cancellationToken);
        await SyntheticReferenceData.SeedAsync(context, cancellationToken);
    }

    private static async Task EnsureCompatibleSchemaAsync(
        ReferenceAuthorityDbContext context,
        CancellationToken cancellationToken)
    {
        var tables = await ReadApplicationTablesAsync(context, cancellationToken);
        var appliedMigrations = (await context.Database.GetAppliedMigrationsAsync(
            cancellationToken)).ToArray();
        var finalMigrations = context.Database.GetMigrations().ToArray();

        var isFresh = tables.Count == 0 && appliedMigrations.Length == 0;
        var isFinal = tables.SequenceEqual(FinalTables, StringComparer.Ordinal)
            && appliedMigrations.SequenceEqual(
                finalMigrations,
                StringComparer.Ordinal);
        if (!isFresh && !isFinal)
        {
            throw new InvalidOperationException(
                "The reference-authority database schema is incompatible. Explicitly reset the configured disposable reference-authority database before startup; it was not deleted automatically.");
        }
    }

    private static async Task<IReadOnlyList<string>> ReadApplicationTablesAsync(
        ReferenceAuthorityDbContext context,
        CancellationToken cancellationToken)
    {
        var connection = context.Database.GetDbConnection();
        var shouldClose = connection.State != System.Data.ConnectionState.Open;
        if (shouldClose)
        {
            await connection.OpenAsync(cancellationToken);
        }

        try
        {
            await using var command = connection.CreateCommand();
            command.CommandText =
                "SELECT name FROM sqlite_master WHERE type = 'table' AND name NOT LIKE 'sqlite_%' AND name NOT LIKE '__EF%' ORDER BY name";
            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            var tables = new List<string>();
            while (await reader.ReadAsync(cancellationToken))
            {
                tables.Add(reader.GetString(0));
            }

            return tables;
        }
        finally
        {
            if (shouldClose)
            {
                await connection.CloseAsync();
            }
        }
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
