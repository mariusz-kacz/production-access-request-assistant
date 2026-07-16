using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using GovernedAccess.Core.Ports;
using GovernedAccess.IntegrationTests.Infrastructure;
using GovernedAccess.Web.Authentication;
using GovernedAccess.Web.Persistence;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace GovernedAccess.IntegrationTests.Requests;

public sealed class RequestPreparationEndpointTests
{
    private const string Intent =
        "I need four hours of read-only access to Client Alpha production for "
        + "INC-1042 to investigate the incident.";

    [Fact]
    public async Task AuthenticatedRequesterReceivesTypedDraftWithoutWorkflowSideEffects()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var interpreter = new RecordingDraftInterpreter(
            new DraftInterpretationOutcome(
                new AccessRequestDraft(
                    "client-alpha",
                    "PROD-ALPHA-EU",
                    "ProductionReadOnly",
                    240,
                    "Investigate the active production incident.",
                    "INC-1042")));
        await using var rootFactory = new GovernedAccessWebFactory();
        await using var factory = CreateFactory(rootFactory, interpreter);
        using var client = await CreateAuthenticatedClientAsync(
            factory,
            DemoPrincipalKeys.Requester,
            cancellationToken);
        using var request = new HttpRequestMessage(
            HttpMethod.Post,
            "/api/request-drafts/prepare")
        {
            Content = JsonContent.Create(
                new
                {
                    intent = Intent,
                    actorId = DemoPrincipalKeys.DevOpsApprover,
                    businessApproverId = DemoPrincipalKeys.ClientBetaApprover,
                }),
        };

        using var response = await GovernedAccessWebFactory.SendWithAntiforgeryAsync(
            client,
            request,
            cancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var body = await ReadJsonAsync(response, cancellationToken);
        Assert.Equal("Prepared", body.RootElement.GetProperty("outcome").GetString());
        var draft = body.RootElement.GetProperty("draft");
        Assert.Equal("client-alpha", draft.GetProperty("clientId").GetString());
        Assert.Equal("PROD-ALPHA-EU", draft.GetProperty("environmentId").GetString());
        Assert.Equal("ProductionReadOnly", draft.GetProperty("requestedRole").GetString());
        Assert.Equal(240, draft.GetProperty("durationMinutes").GetInt32());
        Assert.Equal("INC-1042", draft.GetProperty("incidentId").GetString());

        var interpretationRequest = Assert.IsType<DraftInterpretationRequest>(
            interpreter.LastRequest);
        Assert.Equal(Intent, interpretationRequest.Intent);
        Assert.False(string.IsNullOrWhiteSpace(interpretationRequest.CorrelationId));

        await using var scope = factory.Services.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<GovernedAccessDbContext>();
        Assert.Empty(await dbContext.AccessRequests.AsNoTracking().ToListAsync(cancellationToken));
        Assert.Empty(await dbContext.ApprovalDecisions.AsNoTracking().ToListAsync(cancellationToken));
        Assert.Empty(await dbContext.ProvisioningOperations.AsNoTracking().ToListAsync(cancellationToken));
        Assert.Empty(await dbContext.AccessGrants.AsNoTracking().ToListAsync(cancellationToken));
    }

    [Fact]
    public async Task PreparationWithoutAntiforgeryIsRejectedBeforeInterpretation()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var interpreter = new RecordingDraftInterpreter(
            new DraftInterpretationOutcome(DraftInterpretationOutcomeKind.Unavailable));
        await using var rootFactory = new GovernedAccessWebFactory();
        await using var factory = CreateFactory(rootFactory, interpreter);
        using var client = await CreateAuthenticatedClientAsync(
            factory,
            DemoPrincipalKeys.Requester,
            cancellationToken);

        using var response = await client.PostAsJsonAsync(
            "/api/request-drafts/prepare",
            new { intent = Intent },
            cancellationToken);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        using var problem = await ReadJsonAsync(response, cancellationToken);
        Assert.Equal(
            "antiforgery_validation_failed",
            problem.RootElement.GetProperty("code").GetString());
        Assert.Null(interpreter.LastRequest);
    }

    [Fact]
    public async Task NonRequesterCannotPrepareADraft()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var interpreter = new RecordingDraftInterpreter(
            new DraftInterpretationOutcome(DraftInterpretationOutcomeKind.Unavailable));
        await using var rootFactory = new GovernedAccessWebFactory();
        await using var factory = CreateFactory(rootFactory, interpreter);
        using var client = await CreateAuthenticatedClientAsync(
            factory,
            DemoPrincipalKeys.ClientAlphaApprover,
            cancellationToken);
        using var request = new HttpRequestMessage(
            HttpMethod.Post,
            "/api/request-drafts/prepare")
        {
            Content = JsonContent.Create(new { intent = Intent }),
        };

        using var response = await GovernedAccessWebFactory.SendWithAntiforgeryAsync(
            client,
            request,
            cancellationToken);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Null(interpreter.LastRequest);
    }

    private static WebApplicationFactory<Program> CreateFactory(
        GovernedAccessWebFactory rootFactory,
        IRequestDraftInterpreter interpreter)
    {
        return rootFactory.WithWebHostBuilder(builder =>
            builder.ConfigureTestServices(services =>
            {
                services.RemoveAll<IRequestDraftInterpreter>();
                services.AddSingleton(interpreter);
            }));
    }

    private static async Task<HttpClient> CreateAuthenticatedClientAsync(
        WebApplicationFactory<Program> factory,
        string principalKey,
        CancellationToken cancellationToken)
    {
        var client = factory.CreateClient(
            new WebApplicationFactoryClientOptions
            {
                BaseAddress = new Uri("https://localhost"),
                HandleCookies = true,
            });

        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Post, "/api/demo/session")
            {
                Content = JsonContent.Create(new { principalKey }),
            };
            using var response = await GovernedAccessWebFactory.SendWithAntiforgeryAsync(
                client,
                request,
                cancellationToken);
            response.EnsureSuccessStatusCode();
            return client;
        }
        catch
        {
            client.Dispose();
            throw;
        }
    }

    private static async Task<JsonDocument> ReadJsonAsync(
        HttpResponseMessage response,
        CancellationToken cancellationToken)
    {
        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        return await JsonDocument.ParseAsync(
            stream,
            cancellationToken: cancellationToken);
    }

    private sealed class RecordingDraftInterpreter(DraftInterpretationOutcome outcome)
        : IRequestDraftInterpreter
    {
        public DraftInterpretationRequest? LastRequest { get; private set; }

        public Task<DraftInterpretationOutcome> InterpretAsync(
            DraftInterpretationRequest request,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            LastRequest = request;
            return Task.FromResult(outcome);
        }
    }
}
