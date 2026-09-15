using System;
using System.Collections.Generic;
using Microsoft.EntityFrameworkCore;
using PeerPulse.Web.Models.Subscriptions;

namespace PeerPulse.Web.Data;

public partial class PeerPulseDbContext : DbContext
{
    public PeerPulseDbContext(DbContextOptions<PeerPulseDbContext> options)
        : base(options)
    {
    }

    public virtual DbSet<SubscriptionPlan> SubscriptionPlans { get; set; }

    public virtual DbSet<SubscriptionStatus> SubscriptionStatuses { get; set; }

    public virtual DbSet<UserSubscription> UserSubscriptions { get; set; }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<SubscriptionPlan>(entity =>
        {
            entity.HasKey(e => e.PlanId).HasName("PK__Subscrip__755C22B7A7A37B2D");

            entity.Property(e => e.Createdon)
                .HasDefaultValueSql("(getdate())")
                .HasColumnType("datetime");
            entity.Property(e => e.IsActive).HasDefaultValue(false);
            entity.Property(e => e.PlanCode)
                .HasMaxLength(20)
                .IsUnicode(false);
            entity.Property(e => e.PlanName)
                .HasMaxLength(100)
                .IsUnicode(false);
        });

        modelBuilder.Entity<SubscriptionStatus>(entity =>
        {
            entity.HasIndex(e => e.StatusName, "UQ_SubscriptionStatuses_StatusName").IsUnique();

            entity.Property(e => e.StatusName)
                .HasMaxLength(50)
                .IsUnicode(false);
        });

        modelBuilder.Entity<UserSubscription>(entity =>
        {
            entity.HasKey(e => e.UserSubscriptionId).HasName("PK__UserSubs__D1FD777C5A251455");

            entity.Property(e => e.CreatedAtUtc)
                .HasPrecision(3)
                .HasDefaultValueSql("(sysutcdatetime())");
            entity.Property(e => e.EndedAtUtc).HasPrecision(3);
            entity.Property(e => e.ExpiresDate)
                .HasPrecision(3)
                .HasComputedColumnSql("(dateadd(year,(1),[StartedDate]))", true);
            entity.Property(e => e.StartedDate)
                .HasPrecision(3)
                .HasDefaultValueSql("(sysutcdatetime())");
            entity.Property(e => e.SubscriptionStatusId).HasDefaultValue((byte)1);
            entity.Property(e => e.UpdatedAtUtc)
                .HasPrecision(3)
                .HasDefaultValueSql("(sysutcdatetime())");
        });

        OnModelCreatingPartial(modelBuilder);
    }

    partial void OnModelCreatingPartial(ModelBuilder modelBuilder);
}
