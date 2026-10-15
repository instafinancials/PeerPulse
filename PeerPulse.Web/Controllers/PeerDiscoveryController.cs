using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using PeerPulse.Web.Models;
using PeerPulse.Web.Services;
using System.Globalization;
using System.Security.Claims;

namespace PeerPulse.Web.Controllers
{
    [Authorize]
    [Route("PeerDiscovery/[action]")]
    [ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
    public sealed class PeerDiscoveryController : Controller
    {
        private readonly IPeerSearchService _service;
        private readonly ILogger<PeerDiscoveryController> _logger;

        public PeerDiscoveryController(IPeerSearchService service, ILogger<PeerDiscoveryController> logger)
        {
            _service = service;
            _logger = logger;
        }

        // The InstaUserId claim is issued from the database when the user signs in.
        private bool TryGetUserId(out int id)
        {
            var value = User.FindFirstValue("InstaUserId");

            if (string.IsNullOrWhiteSpace(value))
                value = User.FindFirstValue(ClaimTypes.NameIdentifier);

            return int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out id) && id > 0;
        }

        // Every error from the JSON endpoints has the same shape, so the page can always read it.
        private ObjectResult Problem(int statusCode, string status, string message) =>
            StatusCode(statusCode, new
            {
                status,
                message,
                traceId = HttpContext.TraceIdentifier
            });

        private ObjectResult NoUserClaim()
        {
            _logger.LogWarning("Signed-in user has no valid InstaUserId claim; trace {TraceId}", HttpContext.TraceIdentifier);

            return Problem(StatusCodes.Status403Forbidden, "Forbidden",
                "Your account is not fully set up. Please sign out and sign in again.");
        }

        // ------------------------------------------------------------ pages

        [HttpGet]
        public async Task<IActionResult> AddPeer(CancellationToken ct)
        {
            if (!TryGetUserId(out int userId))
                return Forbid();

            try
            {
                var result = await _service.GetQuotaAsync(userId, ct);

                return View(new AddPeerViewModel
                {
                    Quota = result.Quota,
                    Message = result.Message
                });
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogError(ex, "Quota load failed for {UserId}; trace {TraceId}", userId, HttpContext.TraceIdentifier);

                return View(new AddPeerViewModel
                {
                    Message = "Search is temporarily unavailable. Reload to try again."
                });
            }
        }

        [HttpGet]
        public IActionResult Manage() => View();

        [HttpGet]
        public IActionResult Groups() => View();

        // ------------------------------------------------------------ JSON endpoints

        // Loads quota-independent data for all three pages: groups, peers, plan and peer limit.
        [HttpGet]
        public Task<IActionResult> Workspace(CancellationToken ct) =>
            RunWorkspaceAsync("List", null, ct);

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Search([FromForm] PeerSearchRequest request, CancellationToken ct)
        {
            if (!TryGetUserId(out int userId))
                return NoUserClaim();

            if (!ModelState.IsValid)
            {
                return BadRequest(new PeerSearchResponse(
                    "Invalid", "Enter a valid PAN, CIN, or LLPIN.", null, Array.Empty<PeerRecord>()));
            }

            try
            {
                var result = await _service.SearchAsync(userId, request, ct);

                _logger.LogInformation(
                    "Peer search {SearchId} for {UserId}: {Status}; trace {TraceId}",
                    request.SearchId, userId, result.Status, HttpContext.TraceIdentifier);

                return result.Status switch
                {
                    "Ok" => Ok(result),
                    "Invalid" => BadRequest(result),
                    "NoSubscription" or "UnknownPlan" => StatusCode(StatusCodes.Status403Forbidden, result),
                    "LimitReached" => StatusCode(StatusCodes.Status429TooManyRequests, result),
                    _ => BadRequest(result)
                };
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogError(ex, "Peer search failed for {UserId}; trace {TraceId}", userId, HttpContext.TraceIdentifier);

                return Problem(StatusCodes.Status503ServiceUnavailable, "Unavailable",
                    "Search is unavailable right now. Reload the page to check your allowance before trying again.");
            }
        }

        [HttpPost, ValidateAntiForgeryToken]
        public Task<IActionResult> AddPeer([FromForm] PeerMutationRequest request, CancellationToken ct) =>
            RunWorkspaceAsync("AddPeer", request, ct);

        [HttpPost, ValidateAntiForgeryToken]
        public Task<IActionResult> UpdatePeer([FromForm] PeerMutationRequest request, CancellationToken ct) =>
            RunWorkspaceAsync("UpdatePeer", request, ct);

        [HttpPost, ValidateAntiForgeryToken]
        public Task<IActionResult> DeactivatePeer([FromForm] PeerMutationRequest request, CancellationToken ct) =>
            RunWorkspaceAsync("DeactivatePeer", request, ct);

        [HttpPost, ValidateAntiForgeryToken]
        public Task<IActionResult> ReactivatePeer([FromForm] PeerMutationRequest request, CancellationToken ct) =>
            RunWorkspaceAsync("ReactivatePeer", request, ct);

        [HttpPost, ValidateAntiForgeryToken]
        public Task<IActionResult> CreateGroup([FromForm] PeerMutationRequest request, CancellationToken ct) =>
            RunWorkspaceAsync("CreateGroup", request, ct);

        [HttpPost, ValidateAntiForgeryToken]
        public Task<IActionResult> UpdateGroup([FromForm] PeerMutationRequest request, CancellationToken ct) =>
            RunWorkspaceAsync("UpdateGroup", request, ct);

        [HttpPost, ValidateAntiForgeryToken]
        public Task<IActionResult> DeleteGroup([FromForm] PeerMutationRequest request, CancellationToken ct) =>
            RunWorkspaceAsync("DeleteGroup", request, ct);

        // ------------------------------------------------------------ shared runner

        private async Task<IActionResult> RunWorkspaceAsync(string action, PeerMutationRequest? request, CancellationToken ct)
        {
            if (!TryGetUserId(out int userId))
                return NoUserClaim();

            if (!ModelState.IsValid)
            {
                return Problem(StatusCodes.Status400BadRequest, "Invalid",
                    "Check the field lengths and the selected groups.");
            }

            try
            {
                var result = await _service.WorkspaceAsync(userId, action, request, ct);

                _logger.LogInformation(
                    "Peer operation {Action} for {UserId}: {Status}; trace {TraceId}",
                    action, userId, result.Status, HttpContext.TraceIdentifier);

                return result.Status switch
                {
                    "Ok" => Ok(result),
                    "NoSubscription" => StatusCode(StatusCodes.Status403Forbidden, result),
                    "NotFound" => NotFound(result),
                    "Conflict" or "PeerLimitReached" => Conflict(result),
                    _ => BadRequest(result)          // Invalid, GroupRequired, SearchRequired
                };
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogError(ex, "Peer operation {Action} failed for {UserId}; trace {TraceId}",
                    action, userId, HttpContext.TraceIdentifier);

                return Problem(StatusCodes.Status503ServiceUnavailable, "Unavailable",
                    "The operation could not be completed. Reload the page to see the current state before trying again.");
            }
        }
    }
}