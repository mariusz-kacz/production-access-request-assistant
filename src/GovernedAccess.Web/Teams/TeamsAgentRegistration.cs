using GovernedAccess.Core.Application;
using GovernedAccess.Core.Ports;
using GovernedAccess.Web.Ai;
using GovernedAccess.Web.Persistence;
using Microsoft.Agents.AI.Hosting;
using Microsoft.Agents.Authentication;
using Microsoft.Agents.Hosting.AspNetCore;
using Microsoft.Agents.Storage;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using Microsoft.IdentityModel.Validators;

namespace GovernedAccess.Web.Teams;

public static class TeamsAgentRegistration
{
    public const string ActivityAuthenticationScheme =
        "GovernedAccess.TeamsActivityJwt";

    public const string ActivityAuthorizationPolicy =
        "GovernedAccess.AuthenticatedTeamsActivity";

    private const string MessagesPath = "/api/messages";
    private const string TokenValidationSectionName = "TokenValidation";

    public static WebApplicationBuilder AddGovernedAccessTeams(
        this WebApplicationBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.Services
            .AddOptions<TeamsAccessRequestOptions>()
            .Bind(
                builder.Configuration.GetRequiredSection(
                    TeamsAccessRequestOptions.SectionName))
            .ValidateOnStart();
        builder.Services.AddSingleton<
            IValidateOptions<TeamsAccessRequestOptions>,
            TeamsAccessRequestOptionsValidator>();

        AddActivityAuthentication(builder.Services, builder.Configuration);

        builder.Services.AddSingleton<IStorage, MemoryStorage>();
        builder.Services.AddSingleton<InMemoryAgentSessionStore>();
        builder.Services.AddSingleton<AgentSessionStore>(static serviceProvider =>
            serviceProvider.GetRequiredService<InMemoryAgentSessionStore>());
        builder.Services.AddSingleton<MafConversationTurnCoordinator>();
        builder.Services.AddScoped<IRequestIntakeStore, EfRequestIntakeStore>();
        builder.Services.AddSingleton<
            IRequestPreparationInterpreter,
            MafRequestPreparationInterpreter>();
        builder.Services.AddScoped<RequestIntakeService>();
        builder.Services.AddScoped<TeamsActorResolver>();
        builder.Services.AddScoped<PreparedRequestCardFactory>();

        builder.AddAgent<TeamsAccessRequestAgent>();

        return builder;
    }

    public static IEndpointConventionBuilder MapGovernedAccessTeams(
        this WebApplication app)
    {
        ArgumentNullException.ThrowIfNull(app);

        var endpoint = app
            .MapAgentEndpoints<TeamsAccessRequestAgent>(
                requireAuth: true,
                path: MessagesPath)
            .DisableAntiforgery();

        // The dedicated integration host supplies an authenticated SDK-shaped
        // identity through its test scheme. Every other environment requires
        // the Azure Bot Service bearer-token policy registered above.
        return app.Environment.IsEnvironment("Testing")
            ? endpoint.RequireAuthorization()
            : endpoint.RequireAuthorization(ActivityAuthorizationPolicy);
    }

    private static void AddActivityAuthentication(
        IServiceCollection services,
        IConfiguration configuration)
    {
        var tokenValidation = configuration.GetRequiredSection(
            TokenValidationSectionName);
        var audiences = tokenValidation
            .GetSection("Audiences")
            .Get<string[]>()
            ?? [];

        services
            .AddAuthentication()
            .AddJwtBearer(
                ActivityAuthenticationScheme,
                options =>
                {
                    options.SaveToken = false;
                    options.MetadataAddress =
                        AuthenticationConstants
                            .PublicAzureBotServiceOpenIdMetadataUrl;
                    options.TokenValidationParameters =
                        new TokenValidationParameters
                        {
                            ValidateIssuer = true,
                            ValidIssuer =
                                AuthenticationConstants
                                    .BotFrameworkTokenIssuer,
                            ValidateAudience = true,
                            ValidAudiences = audiences,
                            ValidateLifetime = true,
                            ClockSkew = TimeSpan.FromMinutes(5),
                            ValidateIssuerSigningKey = true,
                            RequireSignedTokens = true,
                        };
                    options.TokenValidationParameters
                        .EnableAadSigningKeyIssuerValidation();
                });

        services
            .AddAuthorizationBuilder()
            .AddPolicy(
                ActivityAuthorizationPolicy,
                policy =>
                {
                    policy.AddAuthenticationSchemes(
                        ActivityAuthenticationScheme);
                    policy.RequireAuthenticatedUser();
                });
    }
}
