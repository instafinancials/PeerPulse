using PeerPulse.Web.Models.InstaUsers;
using PeerPulse.Web.ViewModels.Account;

namespace PeerPulse.Web.Services;

public interface ILoginService
{
    Task<LoginCheckResult> ValidateAsync(string identifier,string password,CancellationToken cancellationToken = default);

    Task<InstaUser?> FindByEmailAsync(string email,CancellationToken cancellationToken = default);
}