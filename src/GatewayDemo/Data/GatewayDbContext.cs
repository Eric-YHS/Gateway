using GatewayDemo.Models;
using Microsoft.EntityFrameworkCore;

namespace GatewayDemo.Data;

public sealed class GatewayDbContext(DbContextOptions<GatewayDbContext> options) : DbContext(options)
{
    public DbSet<GatewayManagedDevice> ManagedDevices => Set<GatewayManagedDevice>();
    public DbSet<GatewayAccessRequest> AccessRequests => Set<GatewayAccessRequest>();
    public DbSet<GatewayAccessRequestSite> AccessRequestSites => Set<GatewayAccessRequestSite>();
    public DbSet<GatewayDeviceAuthorization> DeviceAuthorizations => Set<GatewayDeviceAuthorization>();
    public DbSet<GatewayDeviceAuthorizationSite> DeviceAuthorizationSites => Set<GatewayDeviceAuthorizationSite>();
    public DbSet<GatewayAppReplayNonce> AppReplayNonces => Set<GatewayAppReplayNonce>();
    public DbSet<GatewayAuditLog> AuditLogs => Set<GatewayAuditLog>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<GatewayManagedDevice>(entity =>
        {
            entity.ToTable("GatewayManagedDevices");
            entity.HasKey(x => x.Id);
            entity.Property(x => x.DeviceId).HasMaxLength(64).IsRequired();
            entity.Property(x => x.DeviceCode).HasMaxLength(32).IsRequired();
            entity.Property(x => x.BrowserTokenHash).HasMaxLength(128);
            entity.Property(x => x.AppKeyId).HasMaxLength(48);
            entity.Property(x => x.ProtectedAppSecret).HasMaxLength(300);
            entity.Property(x => x.TrustState).HasConversion<string>().HasMaxLength(16);
            entity.Property(x => x.ChallengeReason).HasMaxLength(200);
            entity.Property(x => x.SignalHash).HasMaxLength(32).IsRequired();
            entity.Property(x => x.UserAgent).HasMaxLength(400).IsRequired();
            entity.Property(x => x.RegisteredIp).HasMaxLength(64).IsRequired();
            entity.Property(x => x.LastSeenIp).HasMaxLength(64);
            entity.HasIndex(x => x.DeviceId).IsUnique();
            entity.HasIndex(x => x.BrowserTokenHash).IsUnique();
            entity.HasIndex(x => x.AppKeyId).IsUnique();
        });

        modelBuilder.Entity<GatewayAccessRequest>(entity =>
        {
            entity.ToTable("GatewayAccessRequests");
            entity.HasKey(x => x.Id);
            entity.Property(x => x.DeviceId).HasMaxLength(64).IsRequired();
            entity.Property(x => x.DeviceCode).HasMaxLength(32).IsRequired();
            entity.Property(x => x.SignalHash).HasMaxLength(32).IsRequired();
            entity.Property(x => x.CompanyName).HasMaxLength(100).IsRequired();
            entity.Property(x => x.ApplicantName).HasMaxLength(60).IsRequired();
            entity.Property(x => x.Phone).HasMaxLength(30).IsRequired();
            entity.Property(x => x.Reason).HasMaxLength(200);
            entity.Property(x => x.TargetPath).HasMaxLength(260).IsRequired();
            entity.Property(x => x.ClientIp).HasMaxLength(64).IsRequired();
            entity.Property(x => x.ReviewNote).HasMaxLength(200);
            entity.Property(x => x.Status).HasConversion<string>().HasMaxLength(16);
            entity.HasIndex(x => new { x.DeviceId, x.Status });
            entity.HasMany(x => x.RequestedSites)
                .WithOne(x => x.Request)
                .HasForeignKey(x => x.RequestId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<GatewayAccessRequestSite>(entity =>
        {
            entity.ToTable("GatewayAccessRequestSites");
            entity.HasKey(x => x.Id);
            entity.Property(x => x.SiteKey).HasMaxLength(32).IsRequired();
            entity.HasIndex(x => new { x.RequestId, x.SiteKey }).IsUnique();
        });

        modelBuilder.Entity<GatewayDeviceAuthorization>(entity =>
        {
            entity.ToTable("GatewayDeviceAuthorizations");
            entity.HasKey(x => x.Id);
            entity.Property(x => x.DeviceId).HasMaxLength(64).IsRequired();
            entity.Property(x => x.DeviceCode).HasMaxLength(32).IsRequired();
            entity.Property(x => x.CompanyName).HasMaxLength(100).IsRequired();
            entity.Property(x => x.ApplicantName).HasMaxLength(60).IsRequired();
            entity.Property(x => x.Phone).HasMaxLength(30).IsRequired();
            entity.Property(x => x.PolicyMode).HasMaxLength(16).IsRequired();
            entity.Property(x => x.ReviewNote).HasMaxLength(200);
            entity.Property(x => x.LastSeenIp).HasMaxLength(64);
            entity.Property(x => x.LastSeenSiteKey).HasMaxLength(32);
            entity.Property(x => x.Status).HasConversion<string>().HasMaxLength(16);
            entity.HasIndex(x => new { x.DeviceId, x.Status });
            entity.HasMany(x => x.AuthorizedSites)
                .WithOne(x => x.Authorization)
                .HasForeignKey(x => x.AuthorizationId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<GatewayDeviceAuthorizationSite>(entity =>
        {
            entity.ToTable("GatewayDeviceAuthorizationSites");
            entity.HasKey(x => x.Id);
            entity.Property(x => x.SiteKey).HasMaxLength(32).IsRequired();
            entity.HasIndex(x => new { x.AuthorizationId, x.SiteKey }).IsUnique();
        });

        modelBuilder.Entity<GatewayAppReplayNonce>(entity =>
        {
            entity.ToTable("GatewayAppReplayNonces");
            entity.HasKey(x => x.Id);
            entity.Property(x => x.AppKeyId).HasMaxLength(48).IsRequired();
            entity.Property(x => x.NonceHash).HasMaxLength(128).IsRequired();
            entity.HasIndex(x => new { x.AppKeyId, x.NonceHash }).IsUnique();
            entity.HasIndex(x => x.ExpiresAtUtc);
        });

        modelBuilder.Entity<GatewayAuditLog>(entity =>
        {
            entity.ToTable("GatewayAuditLogs");
            entity.HasKey(x => x.Id);
            entity.Property(x => x.Kind).HasMaxLength(40).IsRequired();
            entity.Property(x => x.Message).HasMaxLength(200).IsRequired();
            entity.Property(x => x.DeviceId).HasMaxLength(64).IsRequired();
            entity.Property(x => x.DeviceCode).HasMaxLength(32).IsRequired();
            entity.Property(x => x.SiteKey).HasMaxLength(64);
            entity.Property(x => x.ClientIp).HasMaxLength(64);
            entity.HasIndex(x => x.CreatedAtUtc);
        });
    }
}
