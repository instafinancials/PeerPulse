using Microsoft.EntityFrameworkCore;
using PeerPulse.Web.Data;
using PeerPulse.Web.Models.Subscriptions;

namespace PeerPulse.Web.Services;

public sealed class SubscriptionService
{
    private readonly PeerPulseDbContext _db;

    public SubscriptionService(PeerPulseDbContext db)
    {
        _db = db;
    }

    public async Task EnsureStarterSubscriptionAsync(int instaUserId,CancellationToken cancellationToken)
    {
        bool hasAnySubscription = await _db.UserSubscriptions.AsNoTracking().AnyAsync(x => x.InstaUserId == instaUserId, cancellationToken);

        if (hasAnySubscription)
        {
            return;
        }

        var starterPlan = await _db.SubscriptionPlans.SingleOrDefaultAsync(x =>x.PlanCode == "STARTER" &&x.IsActive == true,cancellationToken);

        if (starterPlan is null)
        {
            throw new InvalidOperationException("Active STARTER plan is not configured.");
        }
        bool activeStatusExists = await _db.SubscriptionStatuses .AnyAsync(x => x.SubscriptionStatusId == 1, cancellationToken);
        if (!activeStatusExists)
        {
            throw new InvalidOperationException("Active subscription status ID 1 is not configured.");
        }

        DateTime utcNow = DateTime.UtcNow;

        var subscription = new UserSubscription
        {
            InstaUserId = instaUserId,
            SubscriptionPlanId = starterPlan.PlanId,
            SubscriptionStatusId = 1,
            StartedDate = utcNow,
            ExpiresDate = utcNow.AddYears(1),
            CreatedAtUtc = utcNow,
            UpdatedAtUtc = utcNow
        };

        _db.UserSubscriptions.Add(subscription);

        try
        {
            await _db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException)
        {
            // Another request may have created the first subscription.
            bool existsNow = await _db.UserSubscriptions.AsNoTracking().AnyAsync(x => x.InstaUserId == instaUserId, cancellationToken);
            if (!existsNow)
            {
                throw;
            }
        }
    }
}