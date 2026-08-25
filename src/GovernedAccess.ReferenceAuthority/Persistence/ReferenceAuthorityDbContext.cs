using Microsoft.EntityFrameworkCore;

namespace GovernedAccess.ReferenceAuthority.Persistence;

public sealed class ReferenceAuthorityDbContext(
    DbContextOptions<ReferenceAuthorityDbContext> options)
    : DbContext(options)
{
    private const int IdentifierLength = 128;
    private const int DisplayNameLength = 200;
    private const int RegionLength = 32;

    internal DbSet<ReferenceClient> Clients => Set<ReferenceClient>();

    internal DbSet<ReferenceProductionEnvironment> ProductionEnvironments =>
        Set<ReferenceProductionEnvironment>();

    internal DbSet<ReferenceEnvironmentRole> EnvironmentRoles =>
        Set<ReferenceEnvironmentRole>();

    internal DbSet<ReferenceIncident> Incidents => Set<ReferenceIncident>();

    internal DbSet<ReferenceIncidentEnvironmentLink> IncidentEnvironmentLinks =>
        Set<ReferenceIncidentEnvironmentLink>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        ArgumentNullException.ThrowIfNull(modelBuilder);

        var client = modelBuilder.Entity<ReferenceClient>();
        client.ToTable("Clients");
        client.HasKey(entity => entity.Id);
        client.Property(entity => entity.Id).HasMaxLength(IdentifierLength);
        client.Property(entity => entity.DisplayName).HasMaxLength(DisplayNameLength);
        client.Property(entity => entity.BusinessApproverPrincipalId)
            .HasMaxLength(IdentifierLength);

        var environment = modelBuilder.Entity<ReferenceProductionEnvironment>();
        environment.ToTable("ProductionEnvironments");
        environment.HasKey(entity => entity.Id);
        environment.Property(entity => entity.Id).HasMaxLength(IdentifierLength);
        environment.Property(entity => entity.ClientId).HasMaxLength(IdentifierLength);
        environment.Property(entity => entity.DisplayName).HasMaxLength(DisplayNameLength);
        environment.Property(entity => entity.Region).HasMaxLength(RegionLength);
        environment.Property(entity => entity.Classification)
            .HasConversion<string>()
            .HasMaxLength(16);
        environment.Property(entity => entity.IsActive);
        environment.Property(entity => entity.IsProduction);
        environment.Property(entity => entity.IsEligibleForIntake);
        environment.HasOne<ReferenceClient>()
            .WithMany()
            .HasForeignKey(entity => entity.ClientId)
            .OnDelete(DeleteBehavior.Restrict);

        var role = modelBuilder.Entity<ReferenceEnvironmentRole>();
        role.ToTable("EnvironmentRoles");
        role.HasKey(entity => new { entity.EnvironmentId, entity.RoleId });
        role.Property(entity => entity.EnvironmentId).HasMaxLength(IdentifierLength);
        role.Property(entity => entity.RoleId).HasMaxLength(IdentifierLength);
        role.Property(entity => entity.DisplayName).HasMaxLength(DisplayNameLength);
        role.Property(entity => entity.IsCurrentlyAssignable);
        role.HasOne<ReferenceProductionEnvironment>()
            .WithMany()
            .HasForeignKey(entity => entity.EnvironmentId)
            .OnDelete(DeleteBehavior.Restrict);

        var incident = modelBuilder.Entity<ReferenceIncident>();
        incident.ToTable("Incidents");
        incident.HasKey(entity => entity.Id);
        incident.Property(entity => entity.Id).HasMaxLength(IdentifierLength);
        incident.Property(entity => entity.Title).HasMaxLength(DisplayNameLength);
        incident.Property(entity => entity.IsActive);

        var incidentLink = modelBuilder.Entity<ReferenceIncidentEnvironmentLink>();
        incidentLink.ToTable("IncidentEnvironmentLinks");
        incidentLink.HasKey(entity => new { entity.IncidentId, entity.EnvironmentId });
        incidentLink.Property(entity => entity.IncidentId).HasMaxLength(IdentifierLength);
        incidentLink.Property(entity => entity.EnvironmentId).HasMaxLength(IdentifierLength);
        incidentLink.HasOne<ReferenceIncident>()
            .WithMany()
            .HasForeignKey(entity => entity.IncidentId)
            .OnDelete(DeleteBehavior.Restrict);
        incidentLink.HasOne<ReferenceProductionEnvironment>()
            .WithMany()
            .HasForeignKey(entity => entity.EnvironmentId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
