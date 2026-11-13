using System.Text.Json;
using System.Text.Json.Serialization;

namespace PeerPulse.Web.Models.Reports;

public sealed record ComparisonSelectionDto(
    Guid DraftId,
    IReadOnlyList<ComparisonPeerDto> Peers)
{
    public StaticReportPreferences? Preferences { get; init; }
}

public sealed record ComparisonPeerDto(
    int PeerId,
    string EntityType,
    string? Identifier,
    string? Name,
    bool CanLoad,
    string? Reason);

public sealed record CompanyComparisonReportDto(
    Guid DraftId,
    int PeerId,
    string Cin,
    [property: JsonPropertyName("InstaSummary")] CompanyComparisonSummaryDto InstaSummary);

// Keep the static report's section names despite global camelCase serialization.
// Dictionaries retain stored-procedure field names. Database NULL stays JSON null.
public sealed class CompanyComparisonSummaryDto
{
    [JsonPropertyName("CompanyMasterSummary")]
    public Dictionary<string, object?> CompanyMasterSummary { get; init; } = new();

    [JsonPropertyName("DirectorSignatoryMasterSummary")]
    public Dictionary<string, object?> DirectorSignatoryMasterSummary { get; init; } = new();

    [JsonPropertyName("ChargesMasterSummary")]
    public Dictionary<string, object?> ChargesMasterSummary { get; init; } = new();

    [JsonPropertyName("FinancialsSummary")]
    public JsonElement? FinancialsSummary { get; init; }

    [JsonPropertyName("WebSocialMedia")]
    public Dictionary<string, object?>? WebSocialMedia { get; init; }

    [JsonPropertyName("EmployeeAndEstablishmentSummary")]
    public JsonElement? EmployeeAndEstablishmentSummary { get; init; }
}

public sealed record ComparisonErrorDto(string Code, string Message, string TraceId);

public enum ComparisonFailure
{
    InvalidRequest,
    Unavailable,
    SelectionTooLarge,
    PayloadTooLarge,
    Busy,
    NotReady
}

public sealed class ComparisonReportException : Exception
{
    public ComparisonFailure Failure { get; }

    public ComparisonReportException(ComparisonFailure failure, string message)
        : base(message) => Failure = failure;
}

public sealed class PeerComparisonOptions
{
    public const string SectionName = "ReportComparison";
    public string SelectionProcedure { get; set; } = "dbo.ReportComparison_Selection";
    public string DispatchProcedure { get; set; } = "dbo.ReportComparison_Read";
}

public sealed record PeerComparisonReportDto(
    Guid DraftId, int PeerId, int EntityId, string EntityType, string Identifier,
    [property: JsonPropertyName("InstaSummary")] JsonElement InstaSummary);

public sealed record ComparisonReportItemViewModel(
    string Label, PeerComparisonReportDto? Report, string? Error);

public sealed record ComparisonPageViewModel(
    Guid DraftId, int Page, int TotalPeers, int PageSize, string? SearchInput,
    IReadOnlyList<ComparisonReportItemViewModel> Items)
{
    public int TotalPages => Math.Max(1, (TotalPeers + PageSize - 1) / PageSize);
}

public sealed record ComparisonNodeViewModel(JsonElement Node, int Depth = 0);

public static class ComparisonDisplay
{
    public static string Label(string name) => name switch
    {
        "CompanyMcaStatus" => "Company status",
        "CompanyCIN" or "CompanyCin" => "CIN",
        "DirectorDin" => "DIN",
        "PFExemtpionStatus" => "PF exemption status",
        "PensionExemtpionStatus" => "Pension exemption status",
        "EDLIExemtpionStatus" => "EDLI exemption status",
        "CompanyOtherDirecorship" => "Other company appointments",
        "AdditioanlInfo" => "Additional information",
        _ => System.Text.RegularExpressions.Regex.Replace(name, "([a-z0-9])([A-Z])", "$1 $2")
    };

    public static JsonElement Without(JsonElement node, string name) =>
        node.ValueKind != JsonValueKind.Object ? node :
        JsonSerializer.SerializeToElement(node.EnumerateObject()
            .Where(p => p.Name != name).ToDictionary(p => p.Name, p => p.Value));

    public static JsonElement Property(JsonElement node, string name) =>
        node.ValueKind == JsonValueKind.Object && node.TryGetProperty(name, out var value)
            ? value : default;

    public static JsonElement Section(ComparisonReportItemViewModel item, string name) =>
        item.Report is null ? default : Property(item.Report.InstaSummary, name);

    public static string Text(JsonElement value) => value.ValueKind switch
    {
        JsonValueKind.Undefined or JsonValueKind.Null => "—",
        JsonValueKind.String => string.IsNullOrWhiteSpace(value.GetString()) ? "—" : value.GetString()!,
        JsonValueKind.Number => value.ToString(),
        JsonValueKind.True => "Yes",
        JsonValueKind.False => "No",
        _ => "Details"
    };
}
