using PeerPulse.Web.Models.ReportSettings;
using System.Text.Json;

namespace PeerPulse.Web.Services
{
    public interface IReportSettingsStore
    {
        Task<JsonElement> GetPreference(int userId, CancellationToken ct);
        Task<JsonElement> SavePreference(int userId, SaveReportPreference value, CancellationToken ct);
        Task<JsonElement> GetSelection(int userId, CancellationToken ct);
        Task<JsonElement> SaveSelection(int userId, SaveReportSelection value, CancellationToken ct);
        Task<JsonElement> GetDraft(int userId, Guid draftId, CancellationToken ct);
    }
}
