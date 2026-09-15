using System;
using System.Collections.Generic;
using Microsoft.EntityFrameworkCore;
using PeerPulse.Web.Models.InstaUsers;

namespace PeerPulse.Web.Data;

public partial class InstaUsersDbContext : DbContext
{
    public InstaUsersDbContext(DbContextOptions<InstaUsersDbContext> options)
        : base(options)
    {
    }

    public virtual DbSet<InstaUser> InstaUsers { get; set; }
    public virtual DbSet<InstaCustomer> InstaCustomers { get; set; }
    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<InstaUser>(entity =>
        {
            entity.HasKey(e => e.InstaUserId)
                .HasName("PK__InstaUse__1B5285B86821EA60")
                .HasFillFactor(95);

            entity.ToTable("InstaUser");

            entity.HasIndex(e => e.UserName, "IDX_Email").HasFillFactor(95);

            entity.HasIndex(e => new { e.UserName, e.UserEmail, e.InstaCustomerId }, "NonClusteredIndex-20180201-070937").HasFillFactor(95);

            entity.Property(e => e.InstaUserId).HasColumnName("InstaUserID");
            entity.Property(e => e.CreatedOn).HasColumnType("datetime");
            entity.Property(e => e.EmailOtp)
                .HasMaxLength(10)
                .HasColumnName("EmailOTP");
            entity.Property(e => e.EmailOtpcreatedOn)
                .HasColumnType("datetime")
                .HasColumnName("EmailOTPcreatedOn");
            entity.Property(e => e.InstaCustomerId).HasColumnName("InstaCustomerID");
            entity.Property(e => e.InstaPass).HasMaxLength(100);
            entity.Property(e => e.InstaRoleId).HasColumnName("InstaRoleID");
            entity.Property(e => e.IsEmailOtpChecked).HasDefaultValue(false);
            entity.Property(e => e.IsNumberVerified).HasDefaultValue(false, "DF_InstaUser_IsNumberVerified");
            entity.Property(e => e.LastEmailVerfiedOn).HasColumnType("datetime");
            entity.Property(e => e.LastPassword).HasMaxLength(250);
            entity.Property(e => e.LastResetDate).HasColumnType("datetime");
            entity.Property(e => e.Otp)
                .HasMaxLength(50)
                .HasColumnName("OTP");
            entity.Property(e => e.OtpcreatedOn)
                .HasColumnType("datetime")
                .HasColumnName("OTPcreatedOn");
            entity.Property(e => e.Password).HasMaxLength(250);
            entity.Property(e => e.ReferralCode)
                .HasMaxLength(50)
                .IsUnicode(false)
                .HasDefaultValueSql("(NULL)");
            entity.Property(e => e.ReferralPartnerId)
                .HasDefaultValue(0)
                .HasColumnName("ReferralPartnerID");
            entity.Property(e => e.StatusId).HasColumnName("StatusID");
            entity.Property(e => e.UserEmail)
                .HasMaxLength(100)
                .HasColumnName("UserEMail");
            entity.Property(e => e.UserName).HasMaxLength(100);
        });
        modelBuilder.Entity<InstaCustomer>(entity =>
        {
            entity.HasKey(e => e.InstaCustomerId)
                .HasName("PK__InstaCus__DCE0EDCEE6FDAB68");

            entity.Property(e => e.CountReg)
                .HasDefaultValue(0);

            entity.Property(e => e.IsCompanyInfoAvl)
                .HasDefaultValue(true);

            entity.Property(e => e.BusinessStatus)
                .HasDefaultValue("Not Applicable");

            entity.Property(e => e.IsGstVerifiedByCust)
                .HasDefaultValue(false);

            entity.Property(e => e.IsGstApplicable)
                .HasDefaultValue(false);

            entity.Property(e => e.IsGstVerifiedManual)
                .HasDefaultValue(false);
        });
        OnModelCreatingPartial(modelBuilder);
    }

    partial void OnModelCreatingPartial(ModelBuilder modelBuilder);
}
