using GovernedAccess.Core.Domain.AccessRequests;
using GovernedAccess.Core.Domain.Preparations;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace GovernedAccess.Workflow.Persistence;

public sealed class WorkflowDbContext(DbContextOptions<WorkflowDbContext> options)
    : DbContext(options)
{
    private const int IdentifierLength = PreparationBinding.MaximumComponentLength;
    private const int DisplayNameLength = 200;
    private const int CorrelationIdLength =
        MaterialChangeAttribution.MaximumCorrelationIdLength;
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

    internal DbSet<WorkflowPrincipalRecord> AuthenticatedPrincipals =>
        Set<WorkflowPrincipalRecord>();

    internal DbSet<RequestPreparationRecord> RequestPreparations =>
        Set<RequestPreparationRecord>();

    internal DbSet<AccessRequest> AccessRequests => Set<AccessRequest>();

    internal DbSet<AuditEvent> AuditEvents => Set<AuditEvent>();

    internal DbSet<ApprovalDecision> ApprovalDecisions => Set<ApprovalDecision>();

    internal DbSet<ProvisioningOperation> ProvisioningOperations =>
        Set<ProvisioningOperation>();

    internal DbSet<AccessGrant> AccessGrants => Set<AccessGrant>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        ArgumentNullException.ThrowIfNull(modelBuilder);

        ConfigurePrincipal(modelBuilder.Entity<WorkflowPrincipalRecord>());
        ConfigurePreparation(modelBuilder.Entity<RequestPreparationRecord>());
        ConfigureAccessRequest(modelBuilder.Entity<AccessRequest>());
        ConfigureAuditEvent(modelBuilder.Entity<AuditEvent>());
        ConfigureApprovalDecision(modelBuilder.Entity<ApprovalDecision>());
        ConfigureProvisioningOperation(modelBuilder.Entity<ProvisioningOperation>());
        ConfigureAccessGrant(modelBuilder.Entity<AccessGrant>());
    }

    private static void ConfigurePrincipal(
        EntityTypeBuilder<WorkflowPrincipalRecord> entity)
    {
        entity.ToTable("AuthenticatedPrincipals");
        entity.HasKey(value => value.Id);
        entity.Property(value => value.Id).HasMaxLength(IdentifierLength);
        entity.Property(value => value.DisplayName).HasMaxLength(DisplayNameLength);
        entity.Property(value => value.Kind).HasMaxLength(32);
        entity.Property(value => value.ClientId).HasMaxLength(IdentifierLength);
    }

    private static void ConfigurePreparation(
        EntityTypeBuilder<RequestPreparationRecord> entity)
    {
        entity.ToTable("RequestPreparations");
        entity.HasKey(value => value.PreparationId);
        entity.Property(value => value.Channel).HasMaxLength(32);
        entity.Property(value => value.TenantId).HasMaxLength(IdentifierLength);
        entity.Property(value => value.ChannelActorId).HasMaxLength(IdentifierLength);
        entity.Property(value => value.ConversationId).HasMaxLength(IdentifierLength);
        entity.Property(value => value.RequesterId).HasMaxLength(IdentifierLength);
        entity.Property(value => value.Lifecycle).HasMaxLength(16);
        entity.Property(value => value.ClientId).HasMaxLength(IdentifierLength);
        entity.Property(value => value.EnvironmentId).HasMaxLength(IdentifierLength);
        entity.Property(value => value.RoleId).HasMaxLength(IdentifierLength);
        entity.Property(value => value.Justification)
            .HasMaxLength(PreparationCandidate.MaximumJustificationLength);
        entity.Property(value => value.IncidentId).HasMaxLength(IdentifierLength);
        entity.Property(value => value.CorrelationId)
            .HasMaxLength(MaterialChangeAttribution.MaximumCorrelationIdLength);
        entity.Property(value => value.ClarificationJson).HasColumnType("TEXT");
        entity.Property(value => value.MaterialChangeAttributionsJson)
            .HasColumnType("TEXT");
        entity.Property(value => value.ConcurrencyVersion)
            .IsConcurrencyToken()
            .ValueGeneratedNever();
        ConfigureUtcTimestamp(entity.Property(value => value.CreatedAt));
        ConfigureUtcTimestamp(entity.Property(value => value.UpdatedAt));
        ConfigureUtcTimestamp(entity.Property(value => value.ReadyAt));
        ConfigureUtcTimestamp(entity.Property(value => value.ReadyDeadline));
        ConfigureUtcTimestamp(entity.Property(value => value.TerminalAt));

        entity.HasIndex(value => new
        {
            value.Channel,
            value.TenantId,
            value.ChannelActorId,
            value.ConversationId,
            value.RequesterId,
        })
            .IsUnique()
            .HasDatabaseName("UX_RequestPreparations_ActiveBinding")
            .HasFilter("\"Lifecycle\" IN ('Collecting', 'Ready')");

        entity.HasOne<WorkflowPrincipalRecord>()
            .WithMany()
            .HasForeignKey(value => value.RequesterId)
            .OnDelete(DeleteBehavior.Restrict);
        entity.HasOne<RequestPreparationRecord>()
            .WithMany()
            .HasForeignKey(value => value.PredecessorPreparationId)
            .OnDelete(DeleteBehavior.Restrict);
    }

    private static void ConfigureAccessRequest(
        EntityTypeBuilder<AccessRequest> entity)
    {
        entity.ToTable("AccessRequests");
        entity.HasKey(request => request.Id);
        entity.Property(request => request.RequesterId)
            .HasMaxLength(IdentifierLength);
        entity.Property(request => request.Status)
            .HasConversion<string>()
            .HasMaxLength(32);
        entity.Property(request => request.CorrelationId)
            .HasMaxLength(CorrelationIdLength);
        entity.Property(request => request.PersistenceVersion)
            .IsConcurrencyToken()
            .ValueGeneratedNever();

        ConfigureUtcTimestamp(entity.Property(request => request.CreatedAt));
        ConfigureUtcTimestamp(entity.Property(request => request.LastModifiedAt));

        entity.HasOne<WorkflowPrincipalRecord>()
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
        });
        entity.Navigation(request => request.Details).IsRequired();
    }

    private static void ConfigureAuditEvent(EntityTypeBuilder<AuditEvent> entity)
    {
        entity.ToTable("AuditEvents");
        entity.HasKey(auditEvent => auditEvent.Id);
        entity.Property(auditEvent => auditEvent.EventType)
            .HasConversion<string>()
            .HasMaxLength(40);
        entity.Property(auditEvent => auditEvent.ActorId)
            .HasMaxLength(IdentifierLength);
        entity.Property(auditEvent => auditEvent.CorrelationId)
            .HasMaxLength(CorrelationIdLength);
        entity.Property(auditEvent => auditEvent.OutcomeCode)
            .HasMaxLength(OutcomeCodeLength);
        entity.Property(auditEvent => auditEvent.DetailsJson)
            .HasColumnType("TEXT");
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
        entity.HasOne<WorkflowPrincipalRecord>()
            .WithMany()
            .HasForeignKey(auditEvent => auditEvent.ActorId)
            .OnDelete(DeleteBehavior.Restrict);
    }

    private static void ConfigureApprovalDecision(
        EntityTypeBuilder<ApprovalDecision> entity)
    {
        entity.ToTable("ApprovalDecisions");
        entity.HasKey(decision => decision.Id);
        entity.Property(decision => decision.Stage)
            .HasConversion<string>()
            .HasMaxLength(16);
        entity.Property(decision => decision.Decision)
            .HasConversion<string>()
            .HasMaxLength(16);
        entity.Property(decision => decision.ApproverId)
            .HasMaxLength(IdentifierLength);
        entity.Property(decision => decision.Comment)
            .HasMaxLength(ApprovalDecision.MaximumCommentLength);
        entity.Property(decision => decision.CorrelationId)
            .HasMaxLength(CorrelationIdLength);
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
        entity.HasOne<WorkflowPrincipalRecord>()
            .WithMany()
            .HasForeignKey(decision => decision.ApproverId)
            .OnDelete(DeleteBehavior.Restrict);
    }

    private static void ConfigureProvisioningOperation(
        EntityTypeBuilder<ProvisioningOperation> entity)
    {
        entity.ToTable("ProvisioningOperations");
        entity.HasKey(operation => operation.RequestId);
        entity.Property(operation => operation.Status)
            .HasConversion<string>()
            .HasMaxLength(16);
        entity.Property(operation => operation.LastOutcomeCode)
            .HasMaxLength(OutcomeCodeLength);

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
        entity.Property(grant => grant.Outcome)
            .HasConversion<string>()
            .HasMaxLength(16);
        entity.Property(grant => grant.CorrelationId)
            .HasMaxLength(CorrelationIdLength);
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

    private static void ConfigureUtcTimestamp(PropertyBuilder<DateTimeOffset> property) =>
        property.HasConversion(UtcTimestampConverter).HasColumnType("INTEGER");

    private static void ConfigureUtcTimestamp(PropertyBuilder<DateTimeOffset?> property) =>
        property.HasConversion(NullableUtcTimestampConverter).HasColumnType("INTEGER");
}
