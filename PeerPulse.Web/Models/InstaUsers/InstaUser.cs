using System;
using System.Collections.Generic;

namespace PeerPulse.Web.Models.InstaUsers;

public partial class InstaUser
{
    public int InstaUserId { get; set; }

    public string? UserName { get; set; }

    public string? UserEmail { get; set; }

    public string? Password { get; set; }

    public string? InstaPass { get; set; }

    public int? InstaRoleId { get; set; }

    public int? InstaCustomerId { get; set; }

    public bool? IsChanged { get; set; }

    public int? StatusId { get; set; }

    public string? LastPassword { get; set; }

    public DateTime? LastResetDate { get; set; }

    public DateTime? CreatedOn { get; set; }

    public string? Otp { get; set; }

    public DateTime? OtpcreatedOn { get; set; }

    public bool? IsGoogleLogin { get; set; }

    public bool? IsOtpChecked { get; set; }

    public bool? IsNumberVerified { get; set; }

    public bool? IsUserActive { get; set; }

    public DateTime? LastEmailVerfiedOn { get; set; }

    public int? EmailVerificationDays { get; set; }

    public string? EmailOtp { get; set; }

    public DateTime? EmailOtpcreatedOn { get; set; }

    public bool? IsEmailOtpChecked { get; set; }

    public int? ReferralPartnerId { get; set; }

    public string? ReferralCode { get; set; }
}
