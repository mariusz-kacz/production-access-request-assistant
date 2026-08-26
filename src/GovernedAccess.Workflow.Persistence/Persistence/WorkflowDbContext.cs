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

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        ArgumentNullException.ThrowIfNull(modelBuilder);

        ConfigurePrincipal(modelBuilder.Entity<WorkflowPrincipalRecord>());
        ConfigurePreparation(modelBuilder.Entity<RequestPreparationRecord>());
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

    private static void ConfigureUtcTimestamp(PropertyBuilder<DateTimeOffset> property) =>
        property.HasConversion(UtcTimestampConverter).HasColumnType("INTEGER");

    private static void ConfigureUtcTimestamp(PropertyBuilder<DateTimeOffset?> property) =>
        property.HasConversion(NullableUtcTimestampConverter).HasColumnType("INTEGER");
}
