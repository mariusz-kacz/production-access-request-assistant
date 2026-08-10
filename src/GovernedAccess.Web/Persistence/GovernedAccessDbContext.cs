using GovernedAccess.Core.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace GovernedAccess.Web.Persistence;

public sealed class GovernedAccessDbContext(DbContextOptions<GovernedAccessDbContext> options)
    : DbContext(options)
{
    private const int IdentifierLength = 128;
    private const int DisplayNameLength = 200;
    private const int CorrelationIdLength = 128;
    private const int OutcomeCodeLength = 100;

    private static readonly ValueConverter<DateTimeOffset, long> UtcTimestampConverter = new(
        value => value.UtcDateTime.Ticks,
        value => new DateTimeOffset(value, TimeSpan.Zero));

    private static readonly ValueConverter<DateTimeOffset?, long?>
        NullableUtcTimestampConverter = new(
            value => value.HasValue ? value.Value.UtcDateTime.Ticks : null,
            value => value.HasValue
                ? new DateTimeOffset(value.Value, TimeSpan.Zero)
                : null);

    public DbSet<Client> Clients => Set<Client>();

    public DbSet<ProductionEnvironment> ProductionEnvironments => Set<ProductionEnvironment>();

    public DbSet<EnvironmentRole> EnvironmentRoles => Set<EnvironmentRole>();

    public DbSet<Incident> Incidents => Set<Incident>();

    public DbSet<AuthenticatedPrincipal> AuthenticatedPrincipals => Set<AuthenticatedPrincipal>();

    public DbSet<AccessRequest> AccessRequests => Set<AccessRequest>();

    public DbSet<ApprovalDecision> ApprovalDecisions => Set<ApprovalDecision>();

    public DbSet<ProvisioningOperation> ProvisioningOperations => Set<ProvisioningOperation>();

    public DbSet<AccessGrant> AccessGrants => Set<AccessGrant>();

    public DbSet<AuditEvent> AuditEvents => Set<AuditEvent>();

    public DbSet<RequestIntakeSession> RequestIntakeSessions =>
        Set<RequestIntakeSession>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        ArgumentNullException.ThrowIfNull(modelBuilder);

        ConfigureClient(modelBuilder.Entity<Client>());
        ConfigureAuthenticatedPrincipal(modelBuilder.Entity<AuthenticatedPrincipal>());
        ConfigureProductionEnvironment(modelBuilder.Entity<ProductionEnvironment>());
        ConfigureEnvironmentRole(modelBuilder.Entity<EnvironmentRole>());
        ConfigureIncident(modelBuilder.Entity<Incident>());
        ConfigureAccessRequest(modelBuilder.Entity<AccessRequest>());
        ConfigureApprovalDecision(modelBuilder.Entity<ApprovalDecision>());
        ConfigureProvisioningOperation(modelBuilder.Entity<ProvisioningOperation>());
        ConfigureAccessGrant(modelBuilder.Entity<AccessGrant>());
        ConfigureAuditEvent(modelBuilder.Entity<AuditEvent>());
        ConfigureRequestIntakeSession(modelBuilder.Entity<RequestIntakeSession>());
    }

    private static void ConfigureRequestIntakeSession(
        EntityTypeBuilder<RequestIntakeSession> entity)
    {
        entity.ToTable("RequestIntakeSessions");
        entity.HasKey(session => session.Id);
        entity.Property(session => session.Channel).HasMaxLength(32);
        entity.Property(session => session.TenantId).HasMaxLength(IdentifierLength);
        entity.Property(session => session.ChannelActorId)
            .HasMaxLength(IdentifierLength);
        entity.Property(session => session.ConversationId)
            .HasMaxLength(IdentifierLength);
        entity.Property(session => session.RequesterId)
            .HasMaxLength(IdentifierLength);
        entity.Property(session => session.Status)
            .HasConversion<string>()
            .HasMaxLength(16);
        entity.Property(session => session.ClientId)
            .HasMaxLength(IdentifierLength);
        entity.Property(session => session.EnvironmentId)
            .HasMaxLength(IdentifierLength);
        entity.Property(session => session.RequestedRoleId)
            .HasMaxLength(IdentifierLength);
        entity.Property(session => session.Justification)
            .HasMaxLength(AccessRequest.MaximumJustificationLength);
        entity.Property(session => session.IncidentId)
            .HasMaxLength(IdentifierLength);
        entity.Ignore(session => session.PreparedDetails);
        entity.Property(session => session.CorrelationId)
            .HasMaxLength(CorrelationIdLength);
        entity.Property(session => session.PersistenceVersion)
            .IsConcurrencyToken()
            .ValueGeneratedNever();

        ConfigureUtcTimestamp(entity.Property(session => session.CreatedAt));
        ConfigureUtcTimestamp(entity.Property(session => session.LastUpdatedAt));
        ConfigureUtcTimestamp(entity.Property(session => session.ExpiresAt));
        ConfigureUtcTimestamp(entity.Property(session => session.SubmittedAt));

        entity.HasIndex(session => new
        {
            session.Channel,
            session.TenantId,
            session.ChannelActorId,
            session.ConversationId,
        })
            .IsUnique()
            .HasFilter("\"Status\" IN ('Collecting', 'Ready')");
        entity.HasIndex(session => session.ReservedRequestId).IsUnique();

        entity.HasOne<AuthenticatedPrincipal>()
            .WithMany()
            .HasForeignKey(session => session.RequesterId)
            .OnDelete(DeleteBehavior.Restrict);

        // Candidate identifiers are untrusted until readiness and become null in
        // terminal rows, so authoritative relationships are validated in services.
    }

    private static void ConfigureClient(EntityTypeBuilder<Client> entity)
    {
        entity.ToTable("Clients");
        entity.HasKey(client => client.Id);
        entity.Property(client => client.Id).HasMaxLength(IdentifierLength);
        entity.Property(client => client.DisplayName).HasMaxLength(DisplayNameLength);
        entity.Property(client => client.BusinessApproverPrincipalId)
            .HasMaxLength(IdentifierLength);
    }

    private static void ConfigureAuthenticatedPrincipal(
        EntityTypeBuilder<AuthenticatedPrincipal> entity)
    {
        entity.ToTable("AuthenticatedPrincipals");
        entity.HasKey(principal => principal.Id);
        entity.Property(principal => principal.Id).HasMaxLength(IdentifierLength);
        entity.Property(principal => principal.DisplayName).HasMaxLength(DisplayNameLength);
        entity.Property(principal => principal.Kind).HasConversion<string>().HasMaxLength(32);
        entity.Property(principal => principal.ClientId).HasMaxLength(IdentifierLength);

        entity.HasOne<Client>()
            .WithMany()
            .HasForeignKey(principal => principal.ClientId)
            .OnDelete(DeleteBehavior.Restrict);
    }

    private static void ConfigureProductionEnvironment(
        EntityTypeBuilder<ProductionEnvironment> entity)
    {
        entity.ToTable("ProductionEnvironments");
        entity.HasKey(environment => environment.Id);
        entity.Property(environment => environment.Id).HasMaxLength(IdentifierLength);
        entity.Property(environment => environment.ClientId).HasMaxLength(IdentifierLength);
        entity.Property(environment => environment.DisplayName).HasMaxLength(DisplayNameLength);
        entity.HasOne<Client>()
            .WithMany()
            .HasForeignKey(environment => environment.ClientId)
            .OnDelete(DeleteBehavior.Restrict);
    }

    private static void ConfigureEnvironmentRole(EntityTypeBuilder<EnvironmentRole> entity)
    {
        entity.ToTable("EnvironmentRoles");
        entity.HasKey(role => new { role.EnvironmentId, role.RoleId });
        entity.Property(role => role.EnvironmentId).HasMaxLength(IdentifierLength);
        entity.Property(role => role.RoleId).HasMaxLength(IdentifierLength);

        entity.HasOne<ProductionEnvironment>()
            .WithMany()
            .HasForeignKey(role => role.EnvironmentId)
            .OnDelete(DeleteBehavior.Restrict);
    }

    private static void ConfigureIncident(EntityTypeBuilder<Incident> entity)
    {
        entity.ToTable("Incidents");
        entity.HasKey(incident => incident.Id);
        entity.Property(incident => incident.Id).HasMaxLength(IdentifierLength);
        entity.Property(incident => incident.EnvironmentId)
            .HasMaxLength(IdentifierLength)
            .IsRequired();
        entity.Property(incident => incident.Title).HasMaxLength(DisplayNameLength);
        entity.Property(incident => incident.Status).HasConversion<string>().HasMaxLength(16);

        entity.HasOne<ProductionEnvironment>()
            .WithMany()
            .HasForeignKey(incident => incident.EnvironmentId)
            .OnDelete(DeleteBehavior.Restrict);
    }

    private static void ConfigureAccessRequest(EntityTypeBuilder<AccessRequest> entity)
    {
        entity.ToTable("AccessRequests");
        entity.HasKey(request => request.Id);
        entity.Property(request => request.RequesterId).HasMaxLength(IdentifierLength);
        entity.Property(request => request.Status).HasConversion<string>().HasMaxLength(32);
        entity.Property(request => request.CorrelationId).HasMaxLength(CorrelationIdLength);
        entity.Property(request => request.PersistenceVersion)
            .IsConcurrencyToken()
            .ValueGeneratedNever();

        ConfigureUtcTimestamp(entity.Property(request => request.CreatedAt));
        ConfigureUtcTimestamp(entity.Property(request => request.LastModifiedAt));

        entity.HasOne<AuthenticatedPrincipal>()
            .WithMany()
            .HasForeignKey(request => request.RequesterId)
            .OnDelete(DeleteBehavior.Restrict);

        entity.OwnsOne(request => request.Details, details =>
        {
            details.Property(value => value.ClientId)
                .HasColumnName("ClientId")
                .HasMaxLength(IdentifierLength);
            details.Property(value => value.EnvironmentId)
                .HasColumnName("EnvironmentId")
                .HasMaxLength(IdentifierLength);
            details.Property(value => value.RoleId)
                .HasColumnName("RequestedRoleId")
                .HasMaxLength(IdentifierLength);
            details.Property(value => value.Justification)
                .HasColumnName("Justification")
                .HasMaxLength(AccessRequest.MaximumJustificationLength);
            details.Property(value => value.IncidentId)
                .HasColumnName("IncidentId")
                .HasMaxLength(IdentifierLength);

            details.HasOne<Client>()
                .WithMany()
                .HasForeignKey(value => value.ClientId)
                .OnDelete(DeleteBehavior.Restrict);

            details.HasOne<ProductionEnvironment>()
                .WithMany()
                .HasForeignKey(value => value.EnvironmentId)
                .OnDelete(DeleteBehavior.Restrict);

            details.HasOne<EnvironmentRole>()
                .WithMany()
                .HasForeignKey(value => new { value.EnvironmentId, value.RoleId })
                .OnDelete(DeleteBehavior.Restrict);

            details.HasOne<Incident>()
                .WithMany()
                .HasForeignKey(value => value.IncidentId)
                .OnDelete(DeleteBehavior.Restrict);
        });
        entity.Navigation(request => request.Details).IsRequired();
    }

    private static void ConfigureApprovalDecision(EntityTypeBuilder<ApprovalDecision> entity)
    {
        entity.ToTable("ApprovalDecisions");
        entity.HasKey(decision => decision.Id);
        entity.Property(decision => decision.Stage).HasConversion<string>().HasMaxLength(16);
        entity.Property(decision => decision.Decision).HasConversion<string>().HasMaxLength(16);
        entity.Property(decision => decision.ApproverId).HasMaxLength(IdentifierLength);
        entity.Property(decision => decision.Comment)
            .HasMaxLength(ApprovalDecision.MaximumCommentLength);
        entity.Property(decision => decision.CorrelationId).HasMaxLength(CorrelationIdLength);
        entity.HasIndex(decision => new
        {
            decision.RequestId,
            decision.Stage,
        })
            .IsUnique();

        ConfigureUtcTimestamp(entity.Property(decision => decision.DecidedAt));

        entity.HasOne<AccessRequest>()
            .WithMany()
            .HasForeignKey(decision => decision.RequestId)
            .OnDelete(DeleteBehavior.Restrict);

        entity.HasOne<AuthenticatedPrincipal>()
            .WithMany()
            .HasForeignKey(decision => decision.ApproverId)
            .OnDelete(DeleteBehavior.Restrict);
    }

    private static void ConfigureProvisioningOperation(
        EntityTypeBuilder<ProvisioningOperation> entity)
    {
        entity.ToTable("ProvisioningOperations");
        entity.HasKey(operation => operation.RequestId);
        entity.Property(operation => operation.Status).HasConversion<string>().HasMaxLength(16);
        entity.Property(operation => operation.LastOutcomeCode).HasMaxLength(OutcomeCodeLength);

        ConfigureUtcTimestamp(entity.Property(operation => operation.CreatedAt));
        ConfigureUtcTimestamp(entity.Property(operation => operation.LastAttemptAt));

        entity.HasOne<AccessRequest>()
            .WithMany()
            .HasForeignKey(operation => operation.RequestId)
            .OnDelete(DeleteBehavior.Restrict);
    }

    private static void ConfigureAccessGrant(EntityTypeBuilder<AccessGrant> entity)
    {
        entity.ToTable("AccessGrants");
        entity.HasKey(grant => grant.Id);
        entity.Property(grant => grant.Outcome).HasConversion<string>().HasMaxLength(16);
        entity.Property(grant => grant.CorrelationId).HasMaxLength(CorrelationIdLength);
        entity.HasIndex(grant => grant.RequestId).IsUnique();

        ConfigureUtcTimestamp(entity.Property(grant => grant.ActivatedAt));
        ConfigureUtcTimestamp(entity.Property(grant => grant.ExpiresAt));

        entity.HasOne<ProvisioningOperation>()
            .WithOne()
            .HasForeignKey<AccessGrant>(grant => grant.RequestId)
            .OnDelete(DeleteBehavior.Restrict);

        entity.HasOne<AccessRequest>()
            .WithMany()
            .HasForeignKey(grant => grant.RequestId)
            .OnDelete(DeleteBehavior.Restrict);
    }

    private static void ConfigureAuditEvent(EntityTypeBuilder<AuditEvent> entity)
    {
        entity.ToTable("AuditEvents");
        entity.HasKey(auditEvent => auditEvent.Id);
        entity.Property(auditEvent => auditEvent.EventType)
            .HasConversion<string>()
            .HasMaxLength(40);
        entity.Property(auditEvent => auditEvent.ActorId).HasMaxLength(IdentifierLength);
        entity.Property(auditEvent => auditEvent.CorrelationId).HasMaxLength(CorrelationIdLength);
        entity.Property(auditEvent => auditEvent.OutcomeCode).HasMaxLength(OutcomeCodeLength);
        entity.Property(auditEvent => auditEvent.DetailsJson).HasColumnType("TEXT");
        entity.HasIndex(auditEvent => new
        {
            auditEvent.RequestId,
            auditEvent.OccurredAt,
            auditEvent.Id,
        });

        ConfigureUtcTimestamp(entity.Property(auditEvent => auditEvent.OccurredAt));

        entity.HasOne<AccessRequest>()
            .WithMany()
            .HasForeignKey(auditEvent => auditEvent.RequestId)
            .OnDelete(DeleteBehavior.Restrict);

        entity.HasOne<AuthenticatedPrincipal>()
            .WithMany()
            .HasForeignKey(auditEvent => auditEvent.ActorId)
            .OnDelete(DeleteBehavior.Restrict);
    }

    private static void ConfigureUtcTimestamp(PropertyBuilder<DateTimeOffset> property)
    {
        property.HasConversion(UtcTimestampConverter).HasColumnType("INTEGER");
    }

    private static void ConfigureUtcTimestamp(
        PropertyBuilder<DateTimeOffset?> property)
    {
        property.HasConversion(NullableUtcTimestampConverter).HasColumnType("INTEGER");
    }

}
