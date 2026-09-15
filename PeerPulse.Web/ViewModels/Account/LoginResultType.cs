using PeerPulse.Web.Models.InstaUsers;

namespace PeerPulse.Web.ViewModels.Account
{
    public enum LoginCheckStatus
    {
        Success,
        EmailNotFound,
        MobileNotFound,
        InvalidPassword,
        Disabled,
        VerificationRequired,
        InvalidIdentifier,
        Error
    }
    public sealed record LoginCheckResult(LoginCheckStatus Status,InstaUser? User = null);
    
}
