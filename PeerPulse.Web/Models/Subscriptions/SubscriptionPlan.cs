using System;
using System.Collections.Generic;

namespace PeerPulse.Web.Models.Subscriptions;

public partial class SubscriptionPlan
{
    public int PlanId { get; set; }

    public string? PlanCode { get; set; }

    public string PlanName { get; set; } = null!;

    public bool? IsActive { get; set; }

    public DateTime Createdon { get; set; }
}
