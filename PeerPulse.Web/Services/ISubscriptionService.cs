namespace PeerPulse.Web.Services;

public interface ISubscriptionService
{
    Task EnsureStarterSubscriptionAsync(int instaUserId,CancellationToken cancellationToken);
}