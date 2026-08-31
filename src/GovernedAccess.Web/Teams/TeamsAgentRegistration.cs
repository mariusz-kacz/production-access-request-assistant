using GovernedAccess.Web.Ai;
using Microsoft.Agents.Authentication;
using Microsoft.Agents.Builder.App;
using Microsoft.Agents.Hosting.AspNetCore;
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
        builder.Services.AddRequestTimeouts();

        AddActivityAuthentication(builder.Services, builder.Configuration);
        builder.Services.AddRequestPreparation(builder.Configuration);
        builder.Services.AddSingleton<TeamsDraftCardTracker>();
        builder.Services.AddScoped<TeamsActorResolver>();
        builder.Services.AddScoped<TeamsResponsePresenter>();
        builder.Services.AddScoped<TeamsRequestHandler>();
        builder.Services.AddScoped<TeamsAccessRequestAgent>();

        builder.Services.AddSingleton<AgentApplicationOptions>(
            static services => CreateAgentApplicationOptions(services));
        builder.Services.AddAgentApplicationOptions(replaceExisting: false);
        builder.AddAgent<ScopedTeamsAccessRequestAgentDispatcher>();

        return builder;
    }

    private static AgentApplicationOptions CreateAgentApplicationOptions(
        IServiceProvider services)
    {
        var options = ActivatorUtilities.CreateInstance<AgentApplicationOptions>(
            services);
        options.StartTypingTimer = true;
        options.TypingOptions.InitialDelayMs = 0;
        options.TypingOptions.IntervalMs = 2000;
        return options;
    }

    public static IEndpointConventionBuilder MapGovernedAccessTeams(
        this WebApplication app)
    {
        ArgumentNullException.ThrowIfNull(app);

        var endpoint = app
            .MapAgentEndpoints<ScopedTeamsAccessRequestAgentDispatcher>(
                requireAuth: true,
                path: MessagesPath)
            .DisableAntiforgery()
            .WithRequestTimeout(
                app.Services
                    .GetRequiredService<IOptions<TeamsAccessRequestOptions>>()
                    .Value
                    .RequestTimeout);

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
                            AuthenticationType = ActivityAuthenticationScheme,
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
