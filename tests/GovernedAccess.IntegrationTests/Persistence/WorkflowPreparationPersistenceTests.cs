using GovernedAccess.Core.Domain.AccessRequests;
using GovernedAccess.Core.Domain.Preparations;
using GovernedAccess.Core.Ports;
using GovernedAccess.Core.Preparations.Authority;
using GovernedAccess.Core.Preparations.Contracts;
using GovernedAccess.IntegrationTests.Infrastructure;
using GovernedAccess.Workflow.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace GovernedAccess.IntegrationTests.Persistence;

public sealed class WorkflowPreparationPersistenceTests
{
    private static readonly DateTimeOffset CreatedAt =
        new(2026, 8, 25, 10, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task FreshDatabaseMigratesAndSeedsOnlyWorkflowOwnedTables()
    {
        await using var fixture = await WorkflowPersistenceFixture.CreateAsync();
        await using var scope = fixture.Services.CreateAsyncScope();
        var context = scope.ServiceProvider.GetRequiredService<WorkflowDbContext>();
        var principalReader = scope.ServiceProvider
            .GetRequiredService<IAuthenticatedPrincipalReader>();

        var tables = await ReadTableNamesAsync(
            context,
            TestContext.Current.CancellationToken);
        var migrations = await context.Database.GetAppliedMigrationsAsync(
            TestContext.Current.CancellationToken);
        var requester = await principalReader.GetPrincipalAsync(
            "requester",
            TestContext.Current.CancellationToken);

        Assert.Equal(
            [
                "AccessGrants",
                "AccessRequests",
                "ApprovalDecisions",
                "AuditEvents",
                "AuthenticatedPrincipals",
                "ProvisioningOperations",
                "RequestPreparations",
                "__EFMigrationsHistory",
                "__EFMigrationsLock",
            ],
            tables);
        Assert.EndsWith(
            "_InitialWorkflowPersistence",
            Assert.Single(migrations));
        Assert.True(requester.IsSuccess);
        Assert.Equal(PrincipalKind.Requester, requester.Value.Kind);
        Assert.Equal(
            6L,
            await ReadScalarAsync(
                context,
                "SELECT COUNT(*) FROM AuthenticatedPrincipals",
                TestContext.Current.CancellationToken));
        Assert.DoesNotContain(tables, table => table.Contains("Client", StringComparison.Ordinal));
        Assert.DoesNotContain(tables, table => table.Contains("Environment", StringComparison.Ordinal));
        Assert.DoesNotContain(tables, table => table.Contains("Role", StringComparison.Ordinal));
        Assert.DoesNotContain(tables, table => table.Contains("Incident", StringComparison.Ordinal));
    }

    [Fact]
    public async Task ModelUsesConcurrencyVersionAndTheExactActiveBindingPartialIndex()
    {
        await using var fixture = await WorkflowPersistenceFixture.CreateAsync();
        await using var scope = fixture.Services.CreateAsyncScope();
        var context = scope.ServiceProvider.GetRequiredService<WorkflowDbContext>();
        var preparation = context.Model.GetEntityTypes()
            .Single(entity => entity.ClrType.Name == "RequestPreparationRecord");
        var activeIndex = preparation.GetIndexes()
            .Single(index => index.GetDatabaseName()
                == "UX_RequestPreparations_ActiveBinding");

        Assert.True(
            preparation.FindProperty("ConcurrencyVersion")!.IsConcurrencyToken);
        Assert.Equal(
            [
                "Channel",
                "ChannelActorId",
                "ClarificationJson",
                "ClientId",
                "ConcurrencyVersion",
                "ConversationId",
                "CorrelationId",
                "CreatedAt",
                "EnvironmentId",
                "IncidentId",
                "Justification",
                "Lifecycle",
                "MaterialChangeAttributionsJson",
                "PredecessorPreparationId",
                "PreparationId",
                "ReadyAt",
                "ReadyDeadline",
                "RequesterId",
                "RoleId",
                "TenantId",
                "TerminalAt",
                "UpdatedAt",
            ],
            preparation.GetProperties()
                .Select(property => property.Name)
                .Order(StringComparer.Ordinal));
        Assert.Equal(
            ["Channel", "TenantId", "ChannelActorId", "ConversationId", "RequesterId"],
            activeIndex.Properties.Select(property => property.Name).ToArray());
        Assert.True(activeIndex.IsUnique);
        Assert.Equal(
            "\"Lifecycle\" IN ('Collecting', 'Ready')",
            activeIndex.GetFilter());
        Assert.Equal(
            [
                "IX_RequestPreparations_PredecessorPreparationId",
                "IX_RequestPreparations_RequesterId",
                "UX_RequestPreparations_ActiveBinding",
            ],
            preparation.GetIndexes()
                .Select(index => index.GetDatabaseName())
                .Order(StringComparer.Ordinal));
        Assert.Equal(
            ["PredecessorPreparationId", "RequesterId"],
            preparation.GetForeignKeys()
                .SelectMany(foreignKey => foreignKey.Properties)
                .Select(property => property.Name)
                .Order(StringComparer.Ordinal));
    }

    [Fact]
    public async Task ReadySupersessionAndRevisionPersistAtomicallyWithPredecessorEvidence()
    {
        await using var fixture = await WorkflowPersistenceFixture.CreateAsync();
        var ready = RequestPreparation.CreateRoot(
            Binding(),
            CompleteCandidate(),
            clarification: null,
            Attribution(
                ProposalField.Environment,
                ProposalField.Role,
                ProposalField.Justification),
            CreatedAt,
            "ready-root");
        await PersistAsync(fixture, ready);

        Guid successorId;
        await using (var scope = fixture.Services.CreateAsyncScope())
        {
            var store = scope.ServiceProvider.GetRequiredService<IRequestPreparationStore>();
            var loaded = await store.GetAsync(
                ready.PreparationId,
                TestContext.Current.CancellationToken);
            Assert.True(loaded.IsSuccess);
            var successor = RequestPreparation.CreateRevision(
                loaded.Value,
                CompleteCandidate(roleId: "ProductionSupport"),
                clarification: null,
                Attribution(ProposalField.Role),
                CreatedAt.AddMinutes(5),
                "revision");
            loaded.Value.MarkSuperseded(
                CreatedAt.AddMinutes(5),
                "superseded");
            store.Add(successor);

            var saved = await store.SaveChangesAsync(
                TestContext.Current.CancellationToken);

            Assert.True(saved.IsSuccess);
            successorId = successor.PreparationId;
        }

        await using var verifyScope = fixture.Services.CreateAsyncScope();
        var verifyStore = verifyScope.ServiceProvider
            .GetRequiredService<IRequestPreparationStore>();
        var oldResult = await verifyStore.GetAsync(
            ready.PreparationId,
            TestContext.Current.CancellationToken);
        var successorResult = await verifyStore.GetAsync(
            successorId,
            TestContext.Current.CancellationToken);

        Assert.True(oldResult.IsSuccess);
        Assert.Equal(PreparationLifecycle.Superseded, oldResult.Value.Lifecycle);
        Assert.True(oldResult.Value.Candidate.IsEmpty);
        Assert.Equal(CreatedAt, oldResult.Value.ReadyAt);
        Assert.Equal(CreatedAt.Add(RequestPreparation.ReadyLifetime), oldResult.Value.ReadyDeadline);
        Assert.Equal(CreatedAt.AddMinutes(5), oldResult.Value.TerminalAt);
        Assert.True(successorResult.IsSuccess);
        Assert.Equal(ready.PreparationId, successorResult.Value.PredecessorPreparationId);
        Assert.Equal(PreparationLifecycle.Ready, successorResult.Value.Lifecycle);
        Assert.Equal("ProductionSupport", successorResult.Value.Candidate.RoleId);
    }

    [Fact]
    public async Task CompetingContextOnlyWritesReturnTypedOptimisticConflict()
    {
        await using var fixture = await WorkflowPersistenceFixture.CreateAsync();
        var preparation = RequestPreparation.CreateRoot(
            Binding(),
            CreatedAt,
            "root");
        await PersistAsync(fixture, preparation);
        await using var firstScope = fixture.Services.CreateAsyncScope();
        await using var secondScope = fixture.Services.CreateAsyncScope();
        var firstStore = firstScope.ServiceProvider
            .GetRequiredService<IRequestPreparationStore>();
        var secondStore = secondScope.ServiceProvider
            .GetRequiredService<IRequestPreparationStore>();
        var first = await firstStore.GetAsync(
            preparation.PreparationId,
            TestContext.Current.CancellationToken);
        var second = await secondStore.GetAsync(
            preparation.PreparationId,
            TestContext.Current.CancellationToken);
        first.Value.SetClarification(
            new ClarificationSeed(
                ClarificationTarget.Role,
                [new RoleClarificationChoice("ProductionReadOnly", "Production read-only")]),
            CreatedAt.AddMinutes(1),
            "first");
        second.Value.SetClarification(
            new ClarificationSeed(
                ClarificationTarget.Role,
                [new RoleClarificationChoice("ProductionSupport", "Production support")]),
            CreatedAt.AddMinutes(1),
            "second");

        var firstSave = await firstStore.SaveChangesAsync(
            TestContext.Current.CancellationToken);
        var secondSave = await secondStore.SaveChangesAsync(
            TestContext.Current.CancellationToken);

        Assert.True(firstSave.IsSuccess);
        Assert.True(secondSave.IsFailure);
        Assert.Equal(
            "request_preparation_concurrency_conflict",
            secondSave.Failure!.Code);
    }

    [Fact]
    public async Task CompetingActiveCreationReturnsTypedRaceAndReloadsOneWinner()
    {
        await using var fixture = await WorkflowPersistenceFixture.CreateAsync();
        await using var firstScope = fixture.Services.CreateAsyncScope();
        await using var secondScope = fixture.Services.CreateAsyncScope();
        var firstStore = firstScope.ServiceProvider
            .GetRequiredService<IRequestPreparationStore>();
        var secondStore = secondScope.ServiceProvider
            .GetRequiredService<IRequestPreparationStore>();
        var first = RequestPreparation.CreateRoot(Binding(), CreatedAt, "first");
        var second = RequestPreparation.CreateRoot(Binding(), CreatedAt, "second");
        firstStore.Add(first);
        secondStore.Add(second);

        var firstSave = await firstStore.SaveChangesAsync(
            TestContext.Current.CancellationToken);
        var secondSave = await secondStore.SaveChangesAsync(
            TestContext.Current.CancellationToken);

        Assert.True(firstSave.IsSuccess);
        Assert.True(secondSave.IsFailure);
        Assert.Equal("request_preparation_active_race", secondSave.Failure!.Code);
        var winner = await secondStore.GetActiveAsync(
            Binding(),
            TestContext.Current.CancellationToken);
        Assert.True(winner.IsSuccess);
        Assert.Equal(first.PreparationId, winner.Value.PreparationId);
    }

    [Fact]
    public async Task MalformedDurableStateReturnsTypedFailure()
    {
        await using var fixture = await WorkflowPersistenceFixture.CreateAsync();
        var preparation = RequestPreparation.CreateRoot(Binding(), CreatedAt, "root");
        await PersistAsync(fixture, preparation);
        await using (var corruptScope = fixture.Services.CreateAsyncScope())
        {
            var context = corruptScope.ServiceProvider.GetRequiredService<WorkflowDbContext>();
            await context.Database.ExecuteSqlRawAsync(
                "UPDATE RequestPreparations SET Lifecycle = 'Broken'",
                TestContext.Current.CancellationToken);
        }

        await using var loadScope = fixture.Services.CreateAsyncScope();
        var store = loadScope.ServiceProvider.GetRequiredService<IRequestPreparationStore>();
        var result = await store.GetAsync(
            preparation.PreparationId,
            TestContext.Current.CancellationToken);

        Assert.True(result.IsFailure);
        Assert.Equal("request_preparation_malformed_state", result.Failure!.Code);
    }

    [Fact]
    public async Task MalformedPersistedJsonReturnsTypedFailure()
    {
        foreach (var malformedJson in new[] { "{", "[null]" })
        {
            await using var fixture = await WorkflowPersistenceFixture.CreateAsync();
            var preparation = CreateCollectingPreparation();
            await PersistAsync(fixture, preparation);
            await using (var corruptScope = fixture.Services.CreateAsyncScope())
            {
                var context = corruptScope.ServiceProvider
                    .GetRequiredService<WorkflowDbContext>();
                await context.Database.ExecuteSqlInterpolatedAsync(
                    $"UPDATE RequestPreparations SET MaterialChangeAttributionsJson = {malformedJson}",
                    TestContext.Current.CancellationToken);
            }

            await using var loadScope = fixture.Services.CreateAsyncScope();
            var store = loadScope.ServiceProvider
                .GetRequiredService<IRequestPreparationStore>();
            var result = await store.GetAsync(
                preparation.PreparationId,
                TestContext.Current.CancellationToken);

            Assert.True(result.IsFailure);
            Assert.Equal(
                "request_preparation_malformed_state",
                result.Failure!.Code);
        }
    }

    [Fact]
    public async Task MissingWorkflowTableReturnsTypedUnavailableFailure()
    {
        await using var fixture = await WorkflowPersistenceFixture.CreateAsync();
        await using (var unavailableScope = fixture.Services.CreateAsyncScope())
        {
            var context = unavailableScope.ServiceProvider
                .GetRequiredService<WorkflowDbContext>();
            await context.Database.ExecuteSqlRawAsync(
                "DROP TABLE RequestPreparations",
                TestContext.Current.CancellationToken);
        }

        await using var loadScope = fixture.Services.CreateAsyncScope();
        var store = loadScope.ServiceProvider.GetRequiredService<IRequestPreparationStore>();
        var result = await store.GetAsync(
            Guid.NewGuid(),
            TestContext.Current.CancellationToken);

        Assert.True(result.IsFailure);
        Assert.Equal("workflow_persistence_unavailable", result.Failure!.Code);
    }

    [Fact]
    public async Task WorkflowSchemaContainsNoRawConversationOrProviderPayloadColumns()
    {
        await using var fixture = await WorkflowPersistenceFixture.CreateAsync();
        await using var scope = fixture.Services.CreateAsyncScope();
        var context = scope.ServiceProvider.GetRequiredService<WorkflowDbContext>();
        var columns = await ReadAllColumnNamesAsync(
            context,
            TestContext.Current.CancellationToken);
        string[] forbiddenFragments =
        [
            "Message",
            "Transcript",
            "RawPrompt",
            "Reasoning",
            "SearchQuery",
            "Proposal",
            "ToolPayload",
            "ProviderResponse",
        ];

        Assert.DoesNotContain(
            columns,
            column => forbiddenFragments.Any(fragment =>
                column.Contains(fragment, StringComparison.OrdinalIgnoreCase)));
    }

    [Fact]
    public async Task PreparationClarificationAndAttributionRoundTripAfterRestart()
    {
        var databasePath = Path.Combine(
            Path.GetTempPath(),
            $"workflow-persistence-{Guid.NewGuid():N}.db");
        var preparation = CreateCollectingPreparation();

        try
        {
            await using (var first = await WorkflowPersistenceFixture.CreateAsync(databasePath))
            {
                await using var scope = first.Services.CreateAsyncScope();
                var store = scope.ServiceProvider.GetRequiredService<IRequestPreparationStore>();
                store.Add(preparation);

                var saved = await store.SaveChangesAsync(
                    TestContext.Current.CancellationToken);

                Assert.True(saved.IsSuccess);
                var context = scope.ServiceProvider.GetRequiredService<WorkflowDbContext>();
                var clarificationJson = await ReadStringAsync(
                    context,
                    "SELECT ClarificationJson FROM RequestPreparations",
                    TestContext.Current.CancellationToken);
                Assert.Contains(
                    "Production read-only",
                    Assert.IsType<string>(clarificationJson),
                    StringComparison.Ordinal);
            }

            await using var restarted = await WorkflowPersistenceFixture.CreateAsync(databasePath);
            await using var restartedScope = restarted.Services.CreateAsyncScope();
            var restartedStore = restartedScope.ServiceProvider
                .GetRequiredService<IRequestPreparationStore>();
            var loaded = await restartedStore.GetAsync(
                preparation.PreparationId,
                TestContext.Current.CancellationToken);
            var active = await restartedStore.GetActiveAsync(
                preparation.Binding,
                TestContext.Current.CancellationToken);

            Assert.True(loaded.IsSuccess);
            Assert.True(active.IsSuccess);
            Assert.Equal(preparation.PreparationId, active.Value.PreparationId);
            AssertPreparationEqual(preparation, loaded.Value);
        }
        finally
        {
            File.Delete(databasePath);
        }
    }

    [Fact]
    public async Task MaximumRichEnvironmentClarificationRoundTripsWithinBound()
    {
        await using var fixture = await WorkflowPersistenceFixture.CreateAsync();
        var escapedValue = new string('<', AuthorityValue.MaximumLength);
        var choices = Enumerable.Range(1, RequestPreparation.MaximumClarificationChoices)
            .Select(index => new EnvironmentClarificationChoice(
                $"{new string('<', AuthorityValue.MaximumLength - 1)}{index}",
                escapedValue,
                escapedValue,
                escapedValue,
                escapedValue,
                EnvironmentClassification.Primary))
            .ToArray();
        var preparation = RequestPreparation.CreateRoot(
            Binding(),
            PreparationCandidate.Empty,
            new ClarificationSeed(ClarificationTarget.Environment, choices),
            attribution: null,
            CreatedAt,
            "maximum-rich-clarification");
        await PersistAsync(fixture, preparation);

        await using var scope = fixture.Services.CreateAsyncScope();
        var store = scope.ServiceProvider.GetRequiredService<IRequestPreparationStore>();
        var loaded = await store.GetAsync(
            preparation.PreparationId,
            TestContext.Current.CancellationToken);

        Assert.True(loaded.IsSuccess);
        AssertPreparationEqual(preparation, loaded.Value);
    }

    private static RequestPreparation CreateCollectingPreparation()
    {
        var preparation = RequestPreparation.CreateRoot(
            new PreparationBinding(
                PreparationBinding.TeamsChannel,
                "tenant-001",
                "actor-001",
                "conversation-001",
                "requester"),
            new PreparationCandidate(
                "client-alpha",
                "PROD-ALPHA-EU",
                roleId: null,
                "Investigate the active incident",
                incidentId: null),
            new ClarificationSeed(
                ClarificationTarget.Role,
                [
                    new RoleClarificationChoice(
                        "ProductionReadOnly",
                        "Production read-only"),
                    new RoleClarificationChoice(
                        "ProductionSupport",
                        "Production support"),
                ]),
            new MaterialChangeAttribution(
                [ProposalField.Environment, ProposalField.Justification],
                "model-deployment",
                "provider-version",
                "prompt-v1",
                "schema-v1",
                CreatedAt,
                "correlation-create"),
            CreatedAt,
            "correlation-create");
        return preparation;
    }

    private static PreparationBinding Binding() =>
        new(
            PreparationBinding.TeamsChannel,
            "tenant-001",
            "actor-001",
            "conversation-001",
            "requester");

    private static PreparationCandidate CompleteCandidate(
        string roleId = "ProductionReadOnly") =>
        new(
            "client-alpha",
            "PROD-ALPHA-EU",
            roleId,
            "Investigate the active incident",
            incidentId: null);

    private static MaterialChangeAttribution Attribution(
        params ProposalField[] fields) =>
        new(
            fields,
            "model-deployment",
            "provider-version",
            "prompt-v1",
            "schema-v1",
            CreatedAt,
            "correlation");

    private static async Task PersistAsync(
        WorkflowPersistenceFixture fixture,
        RequestPreparation preparation)
    {
        await using var scope = fixture.Services.CreateAsyncScope();
        var store = scope.ServiceProvider.GetRequiredService<IRequestPreparationStore>();
        store.Add(preparation);
        var result = await store.SaveChangesAsync(TestContext.Current.CancellationToken);
        Assert.True(result.IsSuccess);
    }

    private static void AssertPreparationEqual(
        RequestPreparation expected,
        RequestPreparation actual)
    {
        Assert.Equal(expected.PreparationId, actual.PreparationId);
        Assert.Equal(expected.PredecessorPreparationId, actual.PredecessorPreparationId);
        Assert.Equal(expected.Binding, actual.Binding);
        Assert.Equal(expected.Lifecycle, actual.Lifecycle);
        Assert.Equal(expected.Candidate, actual.Candidate);
        Assert.Equal(expected.ConcurrencyVersion, actual.ConcurrencyVersion);
        Assert.Equal(expected.CreatedAt, actual.CreatedAt);
        Assert.Equal(expected.UpdatedAt, actual.UpdatedAt);
        Assert.Equal(expected.ReadyAt, actual.ReadyAt);
        Assert.Equal(expected.ReadyDeadline, actual.ReadyDeadline);
        Assert.Equal(expected.TerminalAt, actual.TerminalAt);
        Assert.Equal(expected.CorrelationId, actual.CorrelationId);
        Assert.Equal(
            expected.MaterialChangeAttributions.Count,
            actual.MaterialChangeAttributions.Count);
        for (var index = 0; index < expected.MaterialChangeAttributions.Count; index++)
        {
            var expectedAttribution = expected.MaterialChangeAttributions[index];
            var actualAttribution = actual.MaterialChangeAttributions[index];
            Assert.Equal(expectedAttribution.Fields, actualAttribution.Fields);
            Assert.Equal(expectedAttribution.ModelDeployment, actualAttribution.ModelDeployment);
            Assert.Equal(expectedAttribution.ProviderModelVersion, actualAttribution.ProviderModelVersion);
            Assert.Equal(expectedAttribution.PromptContractVersion, actualAttribution.PromptContractVersion);
            Assert.Equal(
                expectedAttribution.StructuredOutputSchemaVersion,
                actualAttribution.StructuredOutputSchemaVersion);
            Assert.Equal(expectedAttribution.OccurredAt, actualAttribution.OccurredAt);
            Assert.Equal(expectedAttribution.CorrelationId, actualAttribution.CorrelationId);
        }

        var expectedClarification = Assert.IsType<PreparationClarificationContext>(
            expected.Clarification);
        var actualClarification = Assert.IsType<PreparationClarificationContext>(
            actual.Clarification);
        Assert.Equal(expectedClarification.PreparationId, actualClarification.PreparationId);
        Assert.Equal(expectedClarification.Target, actualClarification.Target);
        Assert.Equal(expectedClarification.Choices, actualClarification.Choices);
        Assert.Equal(expectedClarification.CreatedAt, actualClarification.CreatedAt);
    }

    private static async Task<IReadOnlyList<string>> ReadTableNamesAsync(
        WorkflowDbContext context,
        CancellationToken cancellationToken)
    {
        var connection = context.Database.GetDbConnection();
        await connection.OpenAsync(cancellationToken);
        try
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
        finally
        {
            await connection.CloseAsync();
        }
    }

    private static async Task<long> ReadScalarAsync(
        WorkflowDbContext context,
        string commandText,
        CancellationToken cancellationToken)
    {
        var connection = context.Database.GetDbConnection();
        await connection.OpenAsync(cancellationToken);
        try
        {
            await using var command = connection.CreateCommand();
            command.CommandText = commandText;
            return (long)(await command.ExecuteScalarAsync(cancellationToken)
                ?? throw new InvalidOperationException("The scalar query returned no value."));
        }
        finally
        {
            await connection.CloseAsync();
        }
    }

    private static async Task<string> ReadStringAsync(
        WorkflowDbContext context,
        string commandText,
        CancellationToken cancellationToken)
    {
        var connection = context.Database.GetDbConnection();
        await connection.OpenAsync(cancellationToken);
        try
        {
            await using var command = connection.CreateCommand();
            command.CommandText = commandText;
            return (string)(await command.ExecuteScalarAsync(cancellationToken)
                ?? throw new InvalidOperationException(
                    "The scalar query returned no value."));
        }
        finally
        {
            await connection.CloseAsync();
        }
    }

    private static async Task<IReadOnlyList<string>> ReadAllColumnNamesAsync(
        WorkflowDbContext context,
        CancellationToken cancellationToken)
    {
        var tables = await ReadTableNamesAsync(context, cancellationToken);
        var connection = context.Database.GetDbConnection();
        await connection.OpenAsync(cancellationToken);
        try
        {
            var columns = new List<string>();
            foreach (var table in tables.Where(table => !table.StartsWith("__", StringComparison.Ordinal)))
            {
                await using var command = connection.CreateCommand();
                command.CommandText = "SELECT name FROM pragma_table_info($table)";
                var parameter = command.CreateParameter();
                parameter.ParameterName = "$table";
                parameter.Value = table;
                command.Parameters.Add(parameter);
                await using var reader = await command.ExecuteReaderAsync(cancellationToken);
                while (await reader.ReadAsync(cancellationToken))
                {
                    columns.Add(reader.GetString(0));
                }
            }

            return columns;
        }
        finally
        {
            await connection.CloseAsync();
        }
    }
}
