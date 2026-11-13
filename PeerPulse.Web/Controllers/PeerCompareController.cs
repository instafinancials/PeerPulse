using System.Text;
using System.Text.Json;
using System.Globalization;
using System.Diagnostics;
using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Data.SqlClient;
using PeerPulse.Web.Models.Reports;
using PeerPulse.Web.Services;

namespace PeerPulse.Web.Controllers;

[Authorize]
[Route("PeerCompare")]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
[EnableRateLimiting("report-settings")]
[ServiceFilter(typeof(ReportResultLoggingFilter))]
public sealed class PeerCompareController : Controller
{
    private readonly IPeerComparisonService _service;
    private readonly ILogger<PeerCompareController> _logger;

    public PeerCompareController(IPeerComparisonService service, ILogger<PeerCompareController> logger)
    {
        _service = service;
        _logger = logger;
    }

    [HttpGet("Selection")]
    public Task<IActionResult> Selection(Guid draftId, CancellationToken cancellationToken) =>
        RunAsync("Selection", draftId, null,
            async (userId, ct) => Ok(await _service.GetSelectionAsync(userId, draftId, ct)),
            cancellationToken);

    // Fixed error page only: no report data or database access.
    [HttpGet("Busy")]
    [AllowAnonymous]
    [DisableRateLimiting]
    public IActionResult Busy()
    {
        Response.StatusCode = StatusCodes.Status429TooManyRequests;
        Response.Headers["Retry-After"] = "60";
        return View("ReportError", new ComparisonErrorDto("too_many_requests",
            "Too many report requests. Please wait a minute, then try again.",
            HttpContext.TraceIdentifier));
    }

    // Retain /Company as a compatibility alias; both routes use the same dispatcher.
    [HttpGet("Company")]
    [HttpGet("Report")]
    public Task<IActionResult> Report(Guid draftId, int? peerId, string? searchInput,
        CancellationToken cancellationToken) =>
        RunAsync("Report", draftId, peerId,
            async (userId, ct) => Ok(await _service.GetReportAsync(
                userId, draftId, peerId, searchInput, ct)), cancellationToken);

    [HttpGet("")]
    [HttpGet("Index")]
    public Task<IActionResult> Index(Guid draftId, int page = 1, string? searchInput = null,
        string? financialYear = null, CancellationToken cancellationToken = default) =>
        RunAsync("ReportPage", draftId, null, async (userId, ct) =>
            View("Index", await LoadPageAsync(userId, draftId, page, searchInput, financialYear, ct)),
            cancellationToken);

    [HttpGet("ExportCsv")]
    public Task<IActionResult> ExportCsv(Guid draftId, int page = 1, string? searchInput = null,
        string? financialYear = null, CancellationToken cancellationToken = default) =>
        RunAsync("ExportCsv", draftId, null, async (userId, ct) =>
        {
            var model = await LoadPageAsync(userId, draftId, page, searchInput, financialYear, ct);
            if (model.selectedPeers.Count == 0)
                throw new ComparisonReportException(ComparisonFailure.Unavailable, "No eligible report data is available for export.");
            var text = new StringBuilder("PeerPulse comparison report\r\n");
            text.AppendLine("Currency," + CsvCell(model.preferences.currency));
            text.AppendLine("Scale," + CsvCell(model.preferences.financialFormat));
            text.AppendLine("USD-INR rate," + model.preferences.exchangeRate.ToString(CultureInfo.InvariantCulture));
            text.AppendLine("Field," + string.Join(",", model.selectedPeers.Select(p => CsvCell(p.company))));
            var columns = model.selectedPeers.Select(p => Flatten(
                JsonSerializer.SerializeToElement(p))).ToArray();
            var keys = columns.SelectMany(c => c.Keys).Distinct(StringComparer.Ordinal).ToArray();
            if (keys.Length > 10000)
                throw new ComparisonReportException(ComparisonFailure.PayloadTooLarge, "The CSV exceeds the interactive export limit.");
            foreach (var key in keys)
            {
                ct.ThrowIfCancellationRequested();
                text.Append(CsvCell(key));
                foreach (var column in columns)
                {
                    var value = column.GetValueOrDefault(key, "");
                    var moneyFields = new[] { "authorisedCapital", "paidUpCapital", "amount", "pfAmount",
                        "netWorth", "totalIncome", "totalExpense", "profitBeforeTax", "incomeTax", "profitAfterTax" };
                    if (moneyFields.Any(field => key == field || key.EndsWith("." + field, StringComparison.Ordinal)) &&
                        decimal.TryParse(value, NumberStyles.Number, CultureInfo.InvariantCulture, out var amount))
                        value = model.formatValue(amount);
                    text.Append(',').Append(CsvCell(value));
                }
                text.Append("\r\n");
                if (text.Length > 2000000)
                    throw new ComparisonReportException(ComparisonFailure.PayloadTooLarge, "The CSV exceeds the interactive export limit.");
            }
            _logger.LogInformation("Comparison CSV generated. PeerCount: {PeerCount}; Characters: {Characters}",
                model.selectedPeers.Count, text.Length);
            return File(Encoding.UTF8.GetPreamble().Concat(Encoding.UTF8.GetBytes(text.ToString())).ToArray(),
                "text/csv; charset=utf-8", $"PeerPulse_Comparison_Page_{page}.csv");
        }, cancellationToken);

    private static string CsvCell(string text)
    {
        var trimmed = text.TrimStart();
        // Prevent spreadsheet formula execution from source-controlled text.
        if (trimmed.Length > 0 && "=+-@".Contains(trimmed[0]) &&
            !decimal.TryParse(trimmed, NumberStyles.Number, CultureInfo.InvariantCulture, out _))
            text = "'" + text;
        return "\"" + text.Replace("\"", "\"\"") + "\"";
    }

    private static Dictionary<string, string> Flatten(JsonElement node)
    {
        var result = new Dictionary<string, string>(StringComparer.Ordinal);
        void Visit(JsonElement value, string path, int depth)
        {
            if (depth > 16 || result.Count > 10000)
                throw new ComparisonReportException(ComparisonFailure.PayloadTooLarge, "The CSV exceeds the interactive export limit.");
            if (value.ValueKind == JsonValueKind.Object)
                foreach (var property in value.EnumerateObject())
                    Visit(property.Value, path.Length == 0 ? property.Name : path + "." + property.Name, depth + 1);
            else if (value.ValueKind == JsonValueKind.Array)
            {
                var index = 0;
                foreach (var item in value.EnumerateArray()) Visit(item, path + "[" + index++ + "]", depth + 1);
            }
            else result[path] = value.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined ? "" : value.ToString();
        }
        Visit(node, "", 0);
        return result;
    }

    private async Task<StaticComparisonViewModel> LoadPageAsync(int userId, Guid draftId,
        int page, string? searchInput, string? financialYear, CancellationToken ct)
    {
        const int pageSize = 3;
        if (page < 1)
            throw new ComparisonReportException(ComparisonFailure.InvalidRequest,
                "Page must be greater than zero.");
        _logger.LogInformation("Report page load started. Page={Page}; PageSize={PageSize}; HasSearch={HasSearch}",
            page, pageSize, !string.IsNullOrWhiteSpace(searchInput));
        var selection = await _service.GetSelectionAsync(userId, draftId, ct);
        var preferences = selection.Preferences
            ?? throw new InvalidDataException("Saved report preferences are unavailable.");
        var items = new List<ComparisonReportItemViewModel>();

        if (!string.IsNullOrWhiteSpace(searchInput))
        {
            var report = await _service.GetReportAsync(userId, draftId, null, searchInput, ct);
            var selected = selection.Peers.FirstOrDefault(p => p.PeerId == report.PeerId);
            items.Add(new ComparisonReportItemViewModel(
                selected?.Name ?? report.Identifier, report, null));
            return new StaticComparisonViewModel(new ComparisonPageViewModel(
                draftId, 1, 1, pageSize, searchInput.Trim(), items), preferences, financialYear);
        }

        var totalPages = Math.Max(1, (selection.Peers.Count + pageSize - 1) / pageSize);
        if (page > totalPages)
            throw new ComparisonReportException(ComparisonFailure.InvalidRequest,
                "The requested report page is unavailable.");

        // Sequential calls: never run Task.WhenAll on the shared scoped DbContext.
        foreach (var selected in selection.Peers.Skip((page - 1) * pageSize).Take(pageSize))
        {
            var label = selected.Name ?? selected.Identifier ?? "Selected peer";
            if (!selected.CanLoad)
            {
                _logger.LogWarning("Selected peer skipped. PeerId={PeerId}; Reason={Reason}",
                    selected.PeerId, selected.Reason);
                items.Add(new ComparisonReportItemViewModel(label, null, selected.Reason));
                continue;
            }
            try
            {
                _logger.LogInformation("Selected peer load started. PeerId={PeerId}", selected.PeerId);
                var report = await _service.GetReportAsync(userId, draftId, selected.PeerId, null, ct);
                items.Add(new ComparisonReportItemViewModel(label, report, null));
            }
            catch (ComparisonReportException exception)
            {
                _logger.LogWarning("Report page item unavailable. PeerId: {PeerId}; Failure: {Failure}",
                    selected.PeerId, exception.Failure);
                items.Add(new ComparisonReportItemViewModel(label, null, exception.Message));
            }
            catch (SqlException exception) when (exception.Number == 52108)
            {
                _logger.LogWarning("Report procedure unavailable for page item. PeerId: {PeerId}", selected.PeerId);
                items.Add(new ComparisonReportItemViewModel(label, null,
                    "The report procedure is not installed for this entity type."));
            }
        }
        _logger.LogInformation("Report view model creation started. SelectedCount={SelectedCount}; LoadedCount={LoadedCount}; UnavailableCount={UnavailableCount}",
            selection.Peers.Count, items.Count(i => i.Report is not null), items.Count(i => i.Report is null));
        return new StaticComparisonViewModel(new ComparisonPageViewModel(
            draftId, page, selection.Peers.Count, pageSize, null, items), preferences, financialYear);
    }

    private async Task<IActionResult> RunAsync(string operation, Guid draftId, int? peerId, Func<int, CancellationToken, Task<IActionResult>> action, CancellationToken requestCancellation)
    {
        HttpContext.Items["ComparisonHtml"] = operation is "ReportPage" or "ExportCsv";
        if (!int.TryParse(User.FindFirstValue("InstaUserId"), out var userId) || userId <= 0)
            return Failure(401, "authentication_required", "Sign in again to load this report.");

        using var scope = _logger.BeginScope(new Dictionary<string, object?>
        {
            ["TraceId"] = HttpContext.TraceIdentifier,
            ["Operation"] = operation,
            ["InstaUserId"] = userId,
            ["DraftId"] = draftId,
            ["PeerId"] = peerId
        });
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(requestCancellation);
        deadline.CancelAfter(TimeSpan.FromSeconds(40));
        var timer = Stopwatch.StartNew();
        _logger.LogInformation("Comparison request started. TraceId={TraceId}; Operation={Operation}",
            HttpContext.TraceIdentifier, operation);
        try
        {
            if (!ModelState.IsValid)
                throw new ComparisonReportException(ComparisonFailure.InvalidRequest,
                    "The report request contains invalid values.");
            var result = await action(userId, deadline.Token);
            _logger.LogInformation("Comparison action prepared result. DurationMs: {DurationMs}",
                timer.ElapsedMilliseconds);
            return result;
        }
        catch (OperationCanceledException) when (requestCancellation.IsCancellationRequested)
        {
            _logger.LogDebug("Comparison request cancelled by the client.");
            return StatusCode(499);
        }
        catch (OperationCanceledException)
        {
            _logger.LogWarning("Comparison request deadline exceeded. DurationMs: {DurationMs}",
                timer.ElapsedMilliseconds);
            return Failure(504, "report_timeout", "The report took too long. Please try again.");
        }
        catch (ComparisonReportException exception)
        {
            var status = exception.Failure switch
            {
                ComparisonFailure.InvalidRequest => 400,
                ComparisonFailure.Unavailable => 404,
                ComparisonFailure.SelectionTooLarge => 422,
                ComparisonFailure.PayloadTooLarge => 413,
                ComparisonFailure.Busy => 503,
                ComparisonFailure.NotReady => 503,
                _ => 500
            };
            _logger.LogWarning("Comparison request rejected. Failure: {Failure}; StatusCode: {StatusCode}",
                exception.Failure, status);
            if (exception.Failure == ComparisonFailure.Busy)
                Response.Headers["Retry-After"] = "5";
            return Failure(status, exception.Failure.ToString(), exception.Message);
        }
        catch (SqlException exception) when (requestCancellation.IsCancellationRequested)
        {
            _logger.LogDebug("Comparison SQL cancelled by the client. SqlErrorNumber: {SqlErrorNumber}",
                exception.Number);
            return StatusCode(499);
        }
        catch (SqlException exception) when (deadline.IsCancellationRequested)
        {
            _logger.LogWarning("Comparison SQL cancelled at the request deadline. SqlErrorNumber: {SqlErrorNumber}",
                exception.Number);
            return Failure(504, "report_timeout", "The report took too long. Please try again.");
        }
        catch (SqlException exception) when (exception.Number == -2)
        {
            _logger.LogWarning(exception, "Comparison SQL timeout. DurationMs: {DurationMs}",
                timer.ElapsedMilliseconds);
            return Failure(504, "database_timeout", "The report query timed out. Please try again.");
        }
        catch (SqlException exception) when (exception.Number is >= 52101 and <= 52110)
        {
            var status = exception.Number switch
            {
                52101 => 400,
                52106 => 404,
                52107 => 422,
                52108 => 503,
                _ => 409
            };
            _logger.LogWarning("Comparison procedure rejected request. SqlErrorNumber: {SqlErrorNumber}",
                exception.Number);
            var message = exception.Number == 52108
                ? "The report procedure is not installed for this entity type."
                : exception.Number == 52106
                    ? "The selected report or entity is unavailable or not eligible."
                    : "The report selection or identifier is invalid. Reload the selection.";
            return Failure(status, "comparison_unavailable", message);
        }
        catch (InvalidDataException exception)
        {
            _logger.LogError(exception,
                "Comparison data contract failed. TraceId={TraceId}; DurationMs={DurationMs}",
                HttpContext.TraceIdentifier, timer.ElapsedMilliseconds);
            return Failure(500, "report_contract_mismatch",
                "The report data format does not match the application. Contact support with this reference.");
        }
        catch (IndexOutOfRangeException exception)
        {
            _logger.LogError(exception,
                "Comparison unexpected index failure. TraceId={TraceId}; DurationMs={DurationMs}",
                HttpContext.TraceIdentifier, timer.ElapsedMilliseconds);
            return Failure(500, "report_mapping_failed",
                "The report data could not be mapped. Contact support with this reference.");
        }
        catch (Exception exception)
        {
            _logger.LogError(exception, "Comparison request failed. DurationMs: {DurationMs}",
                timer.ElapsedMilliseconds);
            return Failure(500, "report_failed", "Unable to load the comparison report.");
        }
    }

    private IActionResult Failure(int status, string code, string message)
    {
        var error = new ComparisonErrorDto(code, message, HttpContext.TraceIdentifier);
        if (HttpContext.Items["ComparisonHtml"] is true)
        {
            Response.StatusCode = status;
            return View("ReportError", error);
        }
        return StatusCode(status, error);
    }
}
