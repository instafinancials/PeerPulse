using System;
using System.Collections.Generic;

namespace PeerPulse.Web.Models.Subscriptions;

public partial class UserSubscription
{
    public long UserSubscriptionId { get; set; }

    public int InstaUserId { get; set; }

    public int SubscriptionPlanId { get; set; }

    public byte SubscriptionStatusId { get; set; }

    public DateTime StartedDate { get; set; }

    public DateTime? ExpiresDate { get; set; }

    public DateTime? EndedAtUtc { get; set; }

    public DateTime CreatedAtUtc { get; set; }

    public DateTime UpdatedAtUtc { get; set; }
}
