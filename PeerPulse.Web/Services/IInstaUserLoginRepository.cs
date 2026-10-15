using PeerPulse.Web.Models.InstaUsers;

namespace PeerPulse.Web.Services
{
    public interface IInstaUserLoginRepository
    {
        Task<InstaUser?> FindAsync(string identifier,CancellationToken cancellationToken);

        Task<InstaUser?> FindByEmailAsync(string email,CancellationToken cancellationToken);

        Task<InstaUser?> FindByMobileAsync(string mobileNumber,CancellationToken cancellationToken);
    }
}
