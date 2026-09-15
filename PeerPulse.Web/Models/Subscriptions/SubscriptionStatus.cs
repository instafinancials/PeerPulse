using System;
using System.Collections.Generic;

namespace PeerPulse.Web.Models.Subscriptions;

public partial class SubscriptionStatus
{
    public int SubscriptionStatusId { get; set; }

    public string StatusName { get; set; } = null!;
}
