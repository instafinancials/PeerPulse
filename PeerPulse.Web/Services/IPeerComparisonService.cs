using PeerPulse.Web.Models.Reports;

namespace PeerPulse.Web.Services;

public interface IPeerComparisonService
{
    Task<ComparisonSelectionDto> GetSelectionAsync(int instaUserId, Guid draftId, CancellationToken cancellationToken);

    Task<PeerComparisonReportDto> GetReportAsync(int instaUserId, Guid draftId, int? peerId, string? searchInput,CancellationToken cancellationToken);
}