using GovernedAccess.Core.Domain.AccessRequests;
using GovernedAccess.Core.Domain.Drafts;
using GovernedAccess.Web.Persistence;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;

namespace GovernedAccess.IntegrationTests.Persistence;

public sealed class GovernedAccessDbContextModelTests
{
    [Fact]
    public async Task ModelCreatesWithRequiredConcurrencyUniquenessAndUtcMappings()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync(TestContext.Current.CancellationToken);

        var options = new DbContextOptionsBuilder<GovernedAccessDbContext>()
            .UseSqlite(connection)
            .Options;

        await using var context = new GovernedAccessDbContext(options);
        await context.Database.EnsureCreatedAsync(TestContext.Current.CancellationToken);
        var tableNames = await ReadTableNamesAsync(
            connection,
            TestContext.Current.CancellationToken);

        var request = GetEntity<AccessRequest>(context);
        Assert.True(request.FindProperty(nameof(AccessRequest.PersistenceVersion))!.IsConcurrencyToken);
        var intake = GetEntity<RequestIntakeSession>(context);
        Assert.True(
            intake.FindProperty(nameof(RequestIntakeSession.PersistenceVersion))!
                .IsConcurrencyToken);
        Assert.Equal("RequestIntakeSessions", intake.GetTableName());
        Assert.Contains("RequestIntakeSessions", tableNames);
        Assert.DoesNotContain("RequestPreparationConversations", tableNames);
        Assert.DoesNotContain("PreparedAccessRequests", tableNames);
        AssertUniqueFilteredIndex<RequestIntakeSession>(
            context,
            "\"Status\" IN ('Collecting', 'Ready')",
            nameof(RequestIntakeSession.Channel),
            nameof(RequestIntakeSession.TenantId),
            nameof(RequestIntakeSession.ChannelActorId),
            nameof(RequestIntakeSession.ConversationId));
        AssertUniqueIndex<RequestIntakeSession>(
            context,
            nameof(RequestIntakeSession.ReservedRequestId));

        AssertUniqueIndex<ApprovalDecision>(
            context,
            nameof(ApprovalDecision.RequestId),
            nameof(ApprovalDecision.Stage));
        AssertPrimaryKey<ProvisioningOperation>(
            context,
            nameof(ProvisioningOperation.RequestId));
        AssertUniqueIndex<AccessGrant>(
            context,
            nameof(AccessGrant.RequestId));

        AssertUtcTimestamp<AccessRequest>(context, nameof(AccessRequest.CreatedAt));
        AssertUtcTimestamp<AccessRequest>(context, nameof(AccessRequest.LastModifiedAt));
        AssertUtcTimestamp<ApprovalDecision>(context, nameof(ApprovalDecision.DecidedAt));
        AssertUtcTimestamp<ProvisioningOperation>(context, nameof(ProvisioningOperation.CreatedAt));
        AssertUtcTimestamp<ProvisioningOperation>(context, nameof(ProvisioningOperation.LastAttemptAt));
        AssertUtcTimestamp<AccessGrant>(context, nameof(AccessGrant.ActivatedAt));
        AssertUtcTimestamp<AccessGrant>(context, nameof(AccessGrant.ExpiresAt));
        AssertUtcTimestamp<AuditEvent>(context, nameof(AuditEvent.OccurredAt));
        AssertUtcTimestamp<RequestIntakeSession>(
            context,
            nameof(RequestIntakeSession.CreatedAt));
        AssertUtcTimestamp<RequestIntakeSession>(
            context,
            nameof(RequestIntakeSession.LastUpdatedAt));
        AssertUtcTimestamp<RequestIntakeSession>(
            context,
            nameof(RequestIntakeSession.ExpiresAt));
        AssertUtcTimestamp<RequestIntakeSession>(
            context,
            nameof(RequestIntakeSession.SubmittedAt));
    }

    private static IEntityType GetEntity<TEntity>(GovernedAccessDbContext context)
    {
        return context.Model.FindEntityType(typeof(TEntity))
            ?? throw new InvalidOperationException($"{typeof(TEntity).Name} is not mapped.");
    }

    private static void AssertUniqueIndex<TEntity>(
        GovernedAccessDbContext context,
        params string[] propertyNames)
    {
        var hasIndex = GetEntity<TEntity>(context)
            .GetIndexes()
            .Any(index =>
                index.IsUnique
                && index.Properties.Select(property => property.Name).SequenceEqual(propertyNames));

        Assert.True(hasIndex, $"Missing unique index on {typeof(TEntity).Name} ({string.Join(", ", propertyNames)}).");
    }

    private static void AssertUniqueFilteredIndex<TEntity>(
        GovernedAccessDbContext context,
        string expectedFilter,
        params string[] propertyNames)
    {
        var index = Assert.Single(
            GetEntity<TEntity>(context).GetIndexes(),
            candidate =>
                candidate.IsUnique
                && candidate.Properties
                    .Select(property => property.Name)
                    .SequenceEqual(propertyNames));

        Assert.Equal(expectedFilter, index.GetFilter());
    }

    private static void AssertPrimaryKey<TEntity>(
        GovernedAccessDbContext context,
        params string[] propertyNames)
    {
        var keyProperties = GetEntity<TEntity>(context)
            .FindPrimaryKey()!
            .Properties
            .Select(property => property.Name);

        Assert.Equal(propertyNames, keyProperties);
    }

    private static void AssertUtcTimestamp<TEntity>(
        GovernedAccessDbContext context,
        string propertyName)
    {
        var property = GetEntity<TEntity>(context).FindProperty(propertyName)
            ?? throw new InvalidOperationException($"{typeof(TEntity).Name}.{propertyName} is not mapped.");
        var converter = property.GetTypeMapping().Converter;

        Assert.NotNull(converter);
        Assert.Equal(
            property.IsNullable ? typeof(long?) : typeof(long),
            converter.ProviderClrType);
    }

    private static async Task<IReadOnlyList<string>> ReadTableNamesAsync(
        SqliteConnection connection,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.CommandText =
            "SELECT name FROM sqlite_master WHERE type = 'table' ORDER BY name";
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        var names = new List<string>();
        while (await reader.ReadAsync(cancellationToken))
        {
            names.Add(reader.GetString(0));
        }

        return names;
    }
}
