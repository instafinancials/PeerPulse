using PeerPulse.Web.Models;

namespace PeerPulse.Web.Services
{
    public interface IPeerSearchService
    {
        Task<PeerSearchResponse> GetQuotaAsync(int instaUserId,CancellationToken cancellationToken);

        Task<PeerSearchResponse> SearchAsync(int instaUserId,PeerSearchRequest request,CancellationToken cancellationToken);

        Task<PeerWorkspaceResponse> WorkspaceAsync(int instaUserId,string action,PeerMutationRequest? request,CancellationToken cancellationToken);
    }
}
