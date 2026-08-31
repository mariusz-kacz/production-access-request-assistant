using GovernedAccess.Core.Ports;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace GovernedAccess.Workflow.Persistence;

public static class WorkflowPersistenceRegistration
{
    public const string ConnectionStringName = "WorkflowPersistence";

    public static IServiceCollection AddWorkflowPersistence(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);

        var connectionString = configuration.GetConnectionString(ConnectionStringName);
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            throw new InvalidOperationException(
                $"ConnectionStrings:{ConnectionStringName} is required for isolated workflow persistence.");
        }

        services.AddDbContext<WorkflowDbContext>(options =>
            options.UseSqlite(
                connectionString,
                sqlite => sqlite.MigrationsAssembly(
                    typeof(WorkflowDbContext).Assembly.FullName)));
        services.AddScoped<EfRequestPreparationStore>();
        services.AddScoped<IRequestPreparationStore>(services =>
            services.GetRequiredService<EfRequestPreparationStore>());
        services.AddScoped<IRequestPreparationConfirmationStore>(services =>
            services.GetRequiredService<EfRequestPreparationStore>());
        services.AddScoped<IAuthenticatedPrincipalReader, EfAuthenticatedPrincipalReader>();
        services.AddScoped<IWorkflowStore, EfWorkflowStore>();
        return services;
    }
}

public static class WorkflowPersistenceDatabase
{
    private static readonly string[] FinalTables =
    [
        "AccessGrants",
        "AccessRequests",
        "ApprovalDecisions",
        "AuditEvents",
        "AuthenticatedPrincipals",
        "ProvisioningOperations",
        "RequestPreparations",
    ];

    public static async Task InitializeAsync(
        IServiceProvider services,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(services);

        await using var scope = services.CreateAsyncScope();
        var context = scope.ServiceProvider.GetRequiredService<WorkflowDbContext>();
        await EnsureCompatibleSchemaAsync(context, cancellationToken);
        await context.Database.MigrateAsync(cancellationToken);
        await SyntheticWorkflowPrincipals.SeedAsync(context, cancellationToken);
    }

    private static async Task EnsureCompatibleSchemaAsync(
        WorkflowDbContext context,
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
                "The workflow database schema is incompatible. Explicitly reset the configured disposable workflow database before startup; it was not deleted automatically.");
        }
    }

    private static async Task<IReadOnlyList<string>> ReadApplicationTablesAsync(
        WorkflowDbContext context,
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

public sealed class WorkflowDesignTimeDbContextFactory
    : IDesignTimeDbContextFactory<WorkflowDbContext>
{
    public WorkflowDbContext CreateDbContext(string[] args)
    {
        var options = new DbContextOptionsBuilder<WorkflowDbContext>()
            .UseSqlite("Data Source=workflow-persistence.design.db")
            .Options;
        return new WorkflowDbContext(options);
    }
}
