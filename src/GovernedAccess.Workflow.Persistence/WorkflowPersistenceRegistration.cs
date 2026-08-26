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
        services.AddScoped<IRequestPreparationStore, EfRequestPreparationStore>();
        services.AddScoped<IAuthenticatedPrincipalReader, EfAuthenticatedPrincipalReader>();
        services.AddScoped<IWorkflowStore, EfWorkflowStore>();
        return services;
    }
}

public static class WorkflowPersistenceDatabase
{
    public static async Task InitializeAsync(
        IServiceProvider services,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(services);

        await using var scope = services.CreateAsyncScope();
        var context = scope.ServiceProvider.GetRequiredService<WorkflowDbContext>();
        await context.Database.MigrateAsync(cancellationToken);
        await SyntheticWorkflowPrincipals.SeedAsync(context, cancellationToken);
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
