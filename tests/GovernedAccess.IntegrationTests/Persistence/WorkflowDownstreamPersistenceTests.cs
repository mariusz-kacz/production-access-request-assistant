using GovernedAccess.Core.Application;
using GovernedAccess.Core.Application.AccessRequests;
using GovernedAccess.Core.Application.Provisioning;
using GovernedAccess.Core.Domain.AccessRequests;
using GovernedAccess.Core.Domain.ReferenceData;
using GovernedAccess.Core.Ports;
using GovernedAccess.IntegrationTests.Infrastructure;
using GovernedAccess.ReferenceAuthority;
using GovernedAccess.ReferenceAuthority.Persistence;
using GovernedAccess.Workflow.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace GovernedAccess.IntegrationTests.Persistence;

public sealed class WorkflowDownstreamPersistenceTests
{
    private static readonly DateTimeOffset CreatedAt =
        new(2026, 8, 26, 9, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task FreshDatabaseMigratesOnlyWorkflowOwnedLifecycleTables()
    {
        await using var fixture = await WorkflowPersistenceFixture.CreateAsync();
        await using var scope = fixture.Services.CreateAsyncScope();
        var context = scope.ServiceProvider.GetRequiredService<WorkflowDbContext>();

        var tables = await ReadTableNamesAsync(
            context,
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
        Assert.DoesNotContain(
            tables,
            table => table.Contains("Client", StringComparison.Ordinal));
        Assert.DoesNotContain(
            tables,
            table => table.Contains("Environment", StringComparison.Ordinal));
        Assert.DoesNotContain(
            tables,
            table => table.Contains("Role", StringComparison.Ordinal));
        Assert.DoesNotContain(
            tables,
            table => table.Contains("Incident", StringComparison.Ordinal));
    }

    [Fact]
    public async Task ModelEnforcesWorkflowConcurrencyAndRequestKeyedIdempotency()
    {
        await using var fixture = await WorkflowPersistenceFixture.CreateAsync();
        await using var scope = fixture.Services.CreateAsyncScope();
        var context = scope.ServiceProvider.GetRequiredService<WorkflowDbContext>();
        var request = context.Model.FindEntityType(typeof(AccessRequest));
        var decision = context.Model.FindEntityType(typeof(ApprovalDecision));
        var operation = context.Model.FindEntityType(typeof(ProvisioningOperation));
        var grant = context.Model.FindEntityType(typeof(AccessGrant));

        Assert.NotNull(request);
        Assert.True(
            request.FindProperty(nameof(AccessRequest.PersistenceVersion))!
                .IsConcurrencyToken);
        Assert.Contains(
            request.GetIndexes(),
            index =>
                index.IsUnique
                && index.Properties.Select(property => property.Name).SequenceEqual(
                    [nameof(AccessRequest.PreparationId)]));
        Assert.Contains(
            decision!.GetIndexes(),
            index =>
                index.IsUnique
                && index.Properties.Select(property => property.Name).SequenceEqual(
                    [nameof(ApprovalDecision.RequestId), nameof(ApprovalDecision.Stage)]));
        Assert.Equal(
            [nameof(ProvisioningOperation.RequestId)],
            operation!.FindPrimaryKey()!.Properties
                .Select(property => property.Name));
        Assert.Contains(
            grant!.GetIndexes(),
            index =>
                index.IsUnique
                && index.Properties.Select(property => property.Name).SequenceEqual(
                    [nameof(AccessGrant.RequestId)]));
        Assert.Null(context.Model.FindEntityType(typeof(Client)));
        Assert.Null(context.Model.FindEntityType(typeof(ProductionEnvironment)));
        Assert.Null(context.Model.FindEntityType(typeof(EnvironmentRole)));
        Assert.Null(context.Model.FindEntityType(typeof(Incident)));
    }

    [Fact]
    public async Task TargetRequestPreparationIdRoundTripsAndRejectsDuplicates()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var fixture = await WorkflowPersistenceFixture.CreateAsync();
        var preparationId = Guid.NewGuid();
        var firstRequest = CreateRequest(preparationId);

        await using (var firstScope = fixture.Services.CreateAsyncScope())
        {
            var store = firstScope.ServiceProvider.GetRequiredService<IWorkflowStore>();
            store.AddRequest(firstRequest);
            Assert.True((await store.SaveChangesAsync(cancellationToken)).IsSuccess);
        }

        await using (var duplicateScope = fixture.Services.CreateAsyncScope())
        {
            var store = duplicateScope.ServiceProvider.GetRequiredService<IWorkflowStore>();
            store.AddRequest(CreateRequest(preparationId));
            var duplicateSave = await store.SaveChangesAsync(cancellationToken);

            Assert.True(duplicateSave.IsFailure);
            Assert.Equal(
                ApplicationFailureKind.DependencyFailure,
                duplicateSave.Failure!.Kind);
        }

        await using var verificationScope = fixture.Services.CreateAsyncScope();
        var verificationStore = verificationScope.ServiceProvider
            .GetRequiredService<IWorkflowStore>();
        var requests = await verificationStore.ListRequestsAsync(cancellationToken);
        var restored = Assert.Single(requests.Value);
        Assert.Equal(firstRequest.Id, restored.Id);
        Assert.Equal(preparationId, restored.PreparationId);
    }

    [Fact]
    public async Task RequestAndAuditEvidenceRoundTripAfterIndependentRestart()
    {
        var databasePath = Path.Combine(
            Path.GetTempPath(),
            $"workflow-downstream-{Guid.NewGuid():N}.db");
        var request = CreateRequest();
        var auditEvent = AuditEvent.CreateRequestCreated(
            Guid.NewGuid(),
            request,
            new RequestCreatedAuditDetails(request.Status));

        try
        {
            await using (var first = await WorkflowPersistenceFixture.CreateAsync(databasePath))
            {
                await using var scope = first.Services.CreateAsyncScope();
                var store = scope.ServiceProvider.GetRequiredService<IWorkflowStore>();
                store.AddRequest(request);
                store.AddAuditEvent(auditEvent);

                var saved = await store.SaveChangesAsync(
                    TestContext.Current.CancellationToken);

                Assert.True(saved.IsSuccess);
            }

            await using var restarted = await WorkflowPersistenceFixture.CreateAsync(databasePath);
            await using var restartedScope = restarted.Services.CreateAsyncScope();
            var restartedStore = restartedScope.ServiceProvider
                .GetRequiredService<IWorkflowStore>();

            var requestResult = await restartedStore.GetRequestAsync(
                request.Id,
                TestContext.Current.CancellationToken);
            var auditResult = await restartedStore.ListAuditEventsAsync(
                request.Id,
                TestContext.Current.CancellationToken);

            Assert.True(requestResult.IsSuccess);
            AssertRequestEqual(request, requestResult.Value);
            var restoredAudit = Assert.Single(auditResult.Value);
            Assert.Equal(auditEvent.Id, restoredAudit.Id);
            Assert.Equal(auditEvent.RequestId, restoredAudit.RequestId);
            Assert.Equal(auditEvent.EventType, restoredAudit.EventType);
            Assert.Equal(auditEvent.ActorId, restoredAudit.ActorId);
            Assert.Equal(auditEvent.OccurredAt, restoredAudit.OccurredAt);
            Assert.Equal(auditEvent.CorrelationId, restoredAudit.CorrelationId);
            Assert.Equal(auditEvent.OutcomeCode, restoredAudit.OutcomeCode);
            Assert.Equal(auditEvent.DetailsJson, restoredAudit.DetailsJson);
        }
        finally
        {
            File.Delete(databasePath);
        }
    }

    [Fact]
    public async Task ExistingDownstreamWorkflowUsesTargetPortsAndReplaysOneGrant()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var fixture = await TargetPersistenceFixture.CreateAsync();
        var provisioner = new RecordingProvisioner(CreatedAt.AddMinutes(30));

        await using (var scope = fixture.Services.CreateAsyncScope())
        {
            var store = scope.ServiceProvider.GetRequiredService<IWorkflowStore>();
            var submittedRequest = CreateRequest();
            store.AddRequest(submittedRequest);
            store.AddAuditEvent(AuditEvent.CreateRequestCreated(
                Guid.NewGuid(),
                submittedRequest,
                new RequestCreatedAuditDetails(submittedRequest.Status)));
            Assert.True((await store.SaveChangesAsync(cancellationToken)).IsSuccess);

            var requestContext = scope.ServiceProvider
                .GetRequiredService<IRequestContextReader>();
            var clock = new DeterministicClock(CreatedAt.AddMinutes(20));
            var protectedProvisioning = new ProtectedProvisioningService(
                store,
                provisioner,
                clock);
            var service = new AccessRequestWorkflowService(
                requestContext,
                store,
                new AccessRequestCommandContextLoader(requestContext, store),
                new AccessRequestValidator(requestContext),
                protectedProvisioning,
                clock);

            var business = await service.DecideAsync(
                ApprovalStage.Business,
                submittedRequest.Id,
                "client-alpha-business-approver",
                ApprovalOutcome.Approved,
                "Approved for incident investigation.",
                "business-correlation",
                cancellationToken);
            var devOps = await service.DecideAsync(
                ApprovalStage.DevOps,
                submittedRequest.Id,
                "devops-approver",
                ApprovalOutcome.Approved,
                null,
                "devops-correlation",
                cancellationToken);
            var replay = await protectedProvisioning.ProvisionAsync(
                submittedRequest.Id,
                cancellationToken);
            var queryService = new AccessRequestQueryService(
                requestContext,
                store,
                new AccessRequestVisibilityPolicy(requestContext, store),
                clock);
            var detail = await queryService.GetDetailAsync(
                submittedRequest.Id,
                "client-alpha-business-approver",
                cancellationToken);

            Assert.True(business.IsSuccess);
            Assert.True(devOps.IsSuccess);
            var completedReplay = Assert.IsType<ProtectedProvisioningCompleted>(replay);
            Assert.Equal(devOps.Value.Grant!.Id, completedReplay.Grant.Id);
            Assert.Equal(1, provisioner.InvocationCount);
            Assert.True(detail.IsSuccess);
            Assert.Equal(RequestStatus.Active, detail.Value.Status);
            Assert.Equal(2, detail.Value.Decisions.Count);
            Assert.NotNull(detail.Value.ProvisioningOperation);
            Assert.NotNull(detail.Value.Grant);
        }

        await using var restarted = await CreateRestartedTargetServicesAsync(fixture);
        await using var restartedScope = restarted.CreateAsyncScope();
        var restartedStore = restartedScope.ServiceProvider
            .GetRequiredService<IWorkflowStore>();
        var requestResult = await restartedStore.ListRequestsAsync(cancellationToken);
        var restoredRequest = Assert.Single(requestResult.Value);
        var decisions = await restartedStore.ListApprovalDecisionsAsync(
            restoredRequest.Id,
            cancellationToken);
        var operation = await restartedStore.GetProvisioningOperationAsync(
            restoredRequest.Id,
            cancellationToken);
        var grant = await restartedStore.GetAccessGrantForRequestAsync(
            restoredRequest.Id,
            cancellationToken);
        var auditEvents = await restartedStore.ListAuditEventsAsync(
            restoredRequest.Id,
            cancellationToken);

        Assert.Equal(RequestStatus.Active, restoredRequest.Status);
        Assert.Equal(2, decisions.Value.Count);
        Assert.Equal(ProvisioningOperationStatus.Succeeded, operation.Value.Status);
        Assert.Equal(CreatedAt.AddMinutes(30), grant.Value.ActivatedAt);
        Assert.Equal(
            AccessGrant.FixedLifetime,
            grant.Value.ExpiresAt - grant.Value.ActivatedAt);
        Assert.Contains(
            auditEvents.Value,
            auditEvent => auditEvent.EventType == AuditEventType.BusinessDecision);
        Assert.Contains(
            auditEvents.Value,
            auditEvent => auditEvent.EventType == AuditEventType.DevOpsDecision);
        Assert.Contains(
            auditEvents.Value,
            auditEvent => auditEvent.EventType == AuditEventType.ProvisioningAttempted);
        Assert.Contains(
            auditEvents.Value,
            auditEvent => auditEvent.EventType == AuditEventType.ProvisioningSucceeded);
        Assert.DoesNotContain(
            auditEvents.Value,
            auditEvent => auditEvent.DetailsJson.Contains(
                restoredRequest.Details.Justification,
                StringComparison.Ordinal));
    }

    [Fact]
    public async Task CompetingRequestTransitionsReturnTypedConcurrencyConflict()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var fixture = await WorkflowPersistenceFixture.CreateAsync();
        var requestId = await PersistRequestAsync(fixture.Services, cancellationToken);
        await using var firstScope = fixture.Services.CreateAsyncScope();
        await using var secondScope = fixture.Services.CreateAsyncScope();
        var firstStore = firstScope.ServiceProvider.GetRequiredService<IWorkflowStore>();
        var secondStore = secondScope.ServiceProvider.GetRequiredService<IWorkflowStore>();
        var first = await firstStore.GetRequestAsync(requestId, cancellationToken);
        var second = await secondStore.GetRequestAsync(requestId, cancellationToken);
        first.Value.Status = RequestStatus.AwaitingDevOpsApproval;
        first.Value.LastModifiedAt = CreatedAt.AddMinutes(1);
        first.Value.PersistenceVersion++;
        second.Value.Status = RequestStatus.Rejected;
        second.Value.LastModifiedAt = CreatedAt.AddMinutes(2);
        second.Value.PersistenceVersion++;

        var firstSave = await firstStore.SaveChangesAsync(cancellationToken);
        var secondSave = await secondStore.SaveChangesAsync(cancellationToken);

        Assert.True(firstSave.IsSuccess);
        Assert.True(secondSave.IsFailure);
        Assert.Equal(ApplicationFailureKind.ConcurrencyConflict, secondSave.Failure!.Kind);
        Assert.Equal("workflow_concurrency_conflict", secondSave.Failure.Code);
    }

    [Fact]
    public async Task ReferenceAndWorkflowOutagesReturnDistinctTypedFailures()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var referenceOutageFixture =
            await TargetPersistenceFixture.CreateAsync();
        var requestId = await PersistRequestAsync(
            referenceOutageFixture.Services,
            cancellationToken);
        await using (var corruptReferenceScope =
            referenceOutageFixture.Services.CreateAsyncScope())
        {
            var referenceContext = corruptReferenceScope.ServiceProvider
                .GetRequiredService<ReferenceAuthorityDbContext>();
            await referenceContext.Database.ExecuteSqlRawAsync(
                "ALTER TABLE ProductionEnvironments RENAME TO UnavailableProductionEnvironments",
                cancellationToken);
        }

        var referenceFailure = await DecideBusinessAsync(
            referenceOutageFixture.Services,
            requestId,
            cancellationToken);

        await using var workflowOutageFixture =
            await TargetPersistenceFixture.CreateAsync();
        requestId = await PersistRequestAsync(
            workflowOutageFixture.Services,
            cancellationToken);
        await using (var corruptWorkflowScope =
            workflowOutageFixture.Services.CreateAsyncScope())
        {
            var workflowContext = corruptWorkflowScope.ServiceProvider
                .GetRequiredService<WorkflowDbContext>();
            await workflowContext.Database.ExecuteSqlRawAsync(
                "DROP TABLE AccessRequests",
                cancellationToken);
        }

        var workflowFailure = await DecideBusinessAsync(
            workflowOutageFixture.Services,
            requestId,
            cancellationToken);

        Assert.Equal(
            "environment-authority-unavailable",
            referenceFailure.Failure!.Code);
        Assert.Equal(
            "workflow_persistence_unavailable",
            workflowFailure.Failure!.Code);
    }

    private static AccessRequest CreateRequest() =>
        new(
            Guid.NewGuid(),
            "requester",
            new ValidatedRequestDetails(
                "client-alpha",
                "PROD-ALPHA-EU",
                ProductionRoleIds.ReadOnly,
                "Investigate the active production incident.",
                "INC-1042"),
            CreatedAt,
            "request-created-correlation");

    private static AccessRequest CreateRequest(Guid preparationId) =>
        new(
            Guid.NewGuid(),
            preparationId,
            "requester",
            new ValidatedRequestDetails(
                "client-alpha",
                "PROD-ALPHA-EU",
                ProductionRoleIds.ReadOnly,
                "Investigate the active production incident.",
                "INC-1042"),
            CreatedAt,
            "request-created-correlation");

    private static async Task<Guid> PersistRequestAsync(
        IServiceProvider services,
        CancellationToken cancellationToken)
    {
        await using var scope = services.CreateAsyncScope();
        var store = scope.ServiceProvider.GetRequiredService<IWorkflowStore>();
        var request = CreateRequest();
        store.AddRequest(request);
        Assert.True((await store.SaveChangesAsync(cancellationToken)).IsSuccess);
        return request.Id;
    }

    private static async Task<ApplicationResult<ApprovalDecisionResult>>
        DecideBusinessAsync(
            IServiceProvider services,
            Guid requestId,
            CancellationToken cancellationToken)
    {
        await using var scope = services.CreateAsyncScope();
        var store = scope.ServiceProvider.GetRequiredService<IWorkflowStore>();
        var requestContext = scope.ServiceProvider
            .GetRequiredService<IRequestContextReader>();
        var clock = new DeterministicClock(CreatedAt.AddMinutes(20));
        var service = new AccessRequestWorkflowService(
            requestContext,
            store,
            new AccessRequestCommandContextLoader(requestContext, store),
            new AccessRequestValidator(requestContext),
            new ProtectedProvisioningService(
                store,
                new RecordingProvisioner(CreatedAt.AddMinutes(30)),
                clock),
            clock);
        return await service.DecideAsync(
            ApprovalStage.Business,
            requestId,
            "client-alpha-business-approver",
            ApprovalOutcome.Approved,
            null,
            "business-correlation",
            cancellationToken);
    }

    private static async Task<ServiceProvider> CreateRestartedTargetServicesAsync(
        TargetPersistenceFixture fixture)
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:ReferenceAuthority"] =
                    $"Data Source={fixture.ReferenceDatabasePath};Pooling=False",
                ["ConnectionStrings:WorkflowPersistence"] =
                    $"Data Source={fixture.WorkflowDatabasePath};Pooling=False",
            })
            .Build();
        var services = new ServiceCollection()
            .AddReferenceAuthority(configuration)
            .AddWorkflowPersistence(configuration)
            .AddScoped<IRequestContextReader, TargetAuthorityRequestContextReader>()
            .BuildServiceProvider(validateScopes: true);
        await ReferenceAuthorityDatabase.InitializeAsync(
            services,
            TestContext.Current.CancellationToken);
        await WorkflowPersistenceDatabase.InitializeAsync(
            services,
            TestContext.Current.CancellationToken);
        return services;
    }

    private sealed class RecordingProvisioner(DateTimeOffset activatedAt)
        : IAccessProvisioner
    {
        private readonly Guid grantId = Guid.NewGuid();

        internal int InvocationCount { get; private set; }

        public Task<AccessProvisioningOutcome> GetOrCreateAsync(
            AccessProvisioningRequest request,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            InvocationCount++;
            return Task.FromResult<AccessProvisioningOutcome>(
                new AccessProvisioningSucceeded(grantId, activatedAt));
        }
    }

    private static void AssertRequestEqual(
        AccessRequest expected,
        AccessRequest actual)
    {
        Assert.Equal(expected.Id, actual.Id);
        Assert.Equal(expected.RequesterId, actual.RequesterId);
        Assert.Equal(expected.Details, actual.Details);
        Assert.Equal(expected.Status, actual.Status);
        Assert.Equal(expected.CreatedAt, actual.CreatedAt);
        Assert.Equal(expected.LastModifiedAt, actual.LastModifiedAt);
        Assert.Equal(expected.CorrelationId, actual.CorrelationId);
        Assert.Equal(expected.PersistenceVersion, actual.PersistenceVersion);
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
}
