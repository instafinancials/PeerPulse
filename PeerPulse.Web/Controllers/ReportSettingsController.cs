using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Logging;
using PeerPulse.Web.Models.ReportSettings;
using PeerPulse.Web.Services;

namespace PeerPulse.Web.Controllers;

[Authorize]
[Route("ReportSettings")]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
[EnableRateLimiting("report-settings")]
public sealed class ReportSettingsController : Controller
{
    private readonly IReportSettingsStore _store;
    private readonly ILogger<ReportSettingsController> _logger;
    public ReportSettingsController(IReportSettingsStore store, ILogger<ReportSettingsController> logger)
    {
        _store = store;
        _logger = logger;
    }

    private bool TryUser(out int id) => int.TryParse(User.FindFirst("InstaUserId")?.Value, out id) && id > 0;

    [HttpGet("Preferences")]
    public IActionResult Preferences() => TryUser(out _) ? View() : Challenge();

    [HttpGet("SelectPeer")]
    public IActionResult SelectPeer() => TryUser(out _) ? View() : Challenge();

    [HttpGet("PreferenceData")]
    public Task<IActionResult> PreferenceData(CancellationToken ct) =>
        Run(async userId => Json(new { data = await _store.GetPreference(userId, ct) }), ct);

    [HttpGet("SelectionData")]
    public Task<IActionResult> SelectionData(CancellationToken ct) =>
        Run(async userId => Json(new { data = await _store.GetSelection(userId, ct) }), ct);

    [HttpPost("SavePreferences")]
    [ValidateAntiForgeryToken]
    [RequestSizeLimit(4096)]
    public Task<IActionResult> SavePreferences([FromBody] SaveReportPreference value, CancellationToken ct) =>
        Run(async userId =>
        {
            if (value == null || !ModelState.IsValid) return BadRequest(new { message = "Check currency, scale and exchange rate." });
            return Json(new { data = await _store.SavePreference(userId, value, ct) });
        }, ct);

    [HttpPost("SaveSelection")]
    [ValidateAntiForgeryToken]
    [RequestSizeLimit(16384)]
    public Task<IActionResult> SaveSelection([FromBody] SaveReportSelection value, CancellationToken ct) =>
        Run(async userId =>
        {
            if (value == null || !ModelState.IsValid) return BadRequest(new { message = "Check the selected peers and reload if necessary." });
            var data = await _store.SaveSelection(userId, value, ct);
            var id = data.GetProperty("id").GetGuid();
            return Json(new{data,reportUrl = Url.Action("Index","PeerCompare",new { draftId = id })});
        }, ct);

    [HttpGet("Report")]
    public Task<IActionResult> Report(Guid? draftId, CancellationToken ct) =>
      Run(async userId =>
      {
          if (!draftId.HasValue || draftId.Value == Guid.Empty)
          {
              return RedirectToAction(nameof(SelectPeer));
          }

          // Validate that this user owns the saved draft.
          await _store.GetDraft(userId, draftId.Value, ct);

          return RedirectToAction(
              "Index",
              "PeerCompare",
              new { draftId = draftId.Value });
      }, ct);

    private async Task<IActionResult> Run(Func<int, Task<IActionResult>> action, CancellationToken ct)
    {
        if (!TryUser(out var userId)) return Unauthorized(new { message = "Sign in again. The InstaUserId claim is missing." });
        try { return await action(userId); }
        catch (ReportSettingsPayloadTooLargeException)
        { return StatusCode(413, new { message = "The peer response is too large. Reduce the selection or add server paging." }); }
        catch (UnauthorizedAccessException)
        { return Unauthorized(new { message = "Sign in again before loading report settings." }); }
        catch (ArgumentException ex) { return BadRequest(new { message = ex.Message }); }
        catch (SqlException ex) when (ex.Number == 52001)
        { return BadRequest(new { message = "Check your preferences and selected peers." }); }
        catch (SqlException ex) when (ex.Number == 52002)
        { return Conflict(new { message = "Preferences or selection changed in another tab. Reload before saving." }); }
        catch (SqlException ex) when (ex.Number == 52003)
        { return StatusCode(403, new { message = "A selected peer or draft is no longer available. Select peers again." }); }
        catch (SqlException ex) when (ex.Number == 52004)
        { return StatusCode(503, new { message = "Peer selection setup is incomplete. Map dbo.v_ReportEligiblePeer to your existing saved peers." }); }
        catch (SqlException ex) when (ex.Number == 52005)
        { return StatusCode(422, new { message = "This account has more than 500 eligible peers. Server paging must be enabled for this account." }); }
        catch (SqlException ex) when (ex.Number == 52007)
        { return StatusCode(422, new { message = "The peer group response is too large. Reduce group memberships or add server paging." }); }
        catch (SqlException ex) when (ex.Number == 52006)
        { return StatusCode(503, new { message = "The saved-peer source mapping needs correction." }); }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { return new EmptyResult(); }
        catch (OperationCanceledException) { return StatusCode(504, new { message = "The request timed out. Retry shortly." }); }
        catch (SqlException ex) when (ex.Number == -2)
        { return StatusCode(504, new { message = "The database request timed out. Retry shortly." }); }
        catch (Exception ex)
        {
            _logger.LogError(ex, "ReportSettings request failed. TraceId={TraceId}", HttpContext.TraceIdentifier);
            return StatusCode(500, new { message = "The request could not be completed.", traceId = HttpContext.TraceIdentifier });
        }
    }
}
