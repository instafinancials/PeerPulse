using System.Globalization;
using System.Text.Json;
using PeerPulse.Web.Models.Reports;

namespace PeerPulse.Web.Services;

internal static class CompanyComparisonMapper
{
    public static CompanyComparisonReportDto Map(
        Guid draftId, int peerId, string cin,
        IReadOnlyList<List<Dictionary<string, object?>>> sets)
    {
        if (sets.Count != 9 || sets[0].Count != 1)
            throw new InvalidDataException("Unexpected company report result structure.");

        var company = Copy(sets[0][0]);
        Rename(company, "CompanyClassName", "CompanyClass");
        Rename(company, "StatusName", "CompanyMcaStatus");
        NormalizeMissing(company, "CompanyBookAddress");

        var directors = sets[2];
        var appointments = sets[3];
        var signatories = sets[4];

        object DirectorGroup(bool active) => new Dictionary<string, object?>
        {
            ["Director"] = directors.Where(r => Flag(r, "IsActive", active))
                .Select(r => PublicRow(r, "DirectorID", "IsActive")).ToArray()
        };

        object DirectorshipGroup(bool active)
        {
            // Group by the appointment status at the reported company.
            // Include all other-company appointment history for each parent,
            // retaining IsActive to distinguish those appointments.
            var parents = directors.Where(r => Flag(r, "IsActive", active))
                .GroupBy(r => Id(r, "DirectorID"));

            return new Dictionary<string, object?>
            {
                ["Director"] = parents.Select(group =>
                {
                    var parent = group.First();
                    return new Dictionary<string, object?>
                    {
                        ["DirectorName"] = parent.GetValueOrDefault("DirectorName"),
                        ["DirectorDin"] = parent.GetValueOrDefault("DirectorDin"),
                        // Existing static JSON spells this key "Direcorship".
                        ["CompanyOtherDirecorship"] = appointments
                            .Where(a => Id(a, "DirectorID") == group.Key)
                            .Select(a => PublicRow(a, "DirectorID", "DirectorName", "DirectorDin"))
                            .ToArray()
                    };
                }).ToArray()
            };
        }

        object SignatoryGroup(bool active) => new Dictionary<string, object?>
        {
            ["Signatory"] = signatories.Where(r => Flag(r, "IsActive", active))
                .Select(r => PublicRow(r, "SignatoryID", "IsActive")).ToArray()
        };

        var directorSummary = new Dictionary<string, object?>
        {
            ["LastUpdatedDateTime"] = company.GetValueOrDefault("DirectorUpdated"),
            ["DirectorCurrentMasterSummary"] = DirectorGroup(true),
            ["DirectorPastMasterSummary"] = DirectorGroup(false),
            ["DirectorCurrentDirectorshipMasterSummary"] = DirectorshipGroup(true),
            ["DirectorPastDirectorshipMasterSummary"] = DirectorshipGroup(false),
            ["SignatoryCurrentMasterSummary"] = SignatoryGroup(true),
            ["SignatoryPastMasterSummary"] = SignatoryGroup(false),
            ["PotentialRelatedPartyMasterSummary"] = new Dictionary<string, object?>
            {
                ["RelatedParty"] = sets[7].Select(r => PublicRow(r, "CompanyID")).ToArray()
            }
        };

        var chargeSummary = new Dictionary<string, object?>
        {
            ["LastUpdatedDateTime"] = company.GetValueOrDefault("ChargesUpdated"),
            ["OpenChargesMasterSummary"] = new Dictionary<string, object?>
            {
                ["Charge"] = sets[1].Where(r => Flag(r, "IsClosed", false))
                    .Select(r => PublicRow(r, "IsClosed", "IsChange", "ChargeSRN")).ToArray()
            },
            ["SatisfiedChargesMasterSummary"] = new Dictionary<string, object?>
            {
                ["Charge"] = sets[1].Where(r => Flag(r, "IsClosed", true))
                    .Select(r => PublicRow(r, "IsClosed", "IsChange", "ChargeSRN")).ToArray()
            }
        };

        return new CompanyComparisonReportDto(draftId, peerId, cin,
            new CompanyComparisonSummaryDto
            {
                CompanyMasterSummary = company,
                DirectorSignatoryMasterSummary = directorSummary,
                ChargesMasterSummary = chargeSummary,
                FinancialsSummary = ParseObject(sets[5], "FinancialsSummary"),
                WebSocialMedia = sets[6].Count == 0 ? null : PublicRow(sets[6][0], "CompanyID"),
                EmployeeAndEstablishmentSummary =
                    ParseObject(sets[8], "EmployeeAndEstablishmentSummary")
            });
    }

    private static JsonElement? ParseObject(
        List<Dictionary<string, object?>> rows, string column)
    {
        if (rows.Count == 0) return null;
        if (rows.Count != 1 || !rows[0].TryGetValue(column, out var value))
            throw new InvalidDataException($"Unexpected {column} result structure.");
        if (value is null) return null;
        if (value is not string text)
            throw new InvalidDataException($"Expected JSON text for {column}.");
        using var document = JsonDocument.Parse(text, new JsonDocumentOptions { MaxDepth = 64 });
        if (document.RootElement.ValueKind != JsonValueKind.Object)
            throw new InvalidDataException($"Expected an object for {column}.");
        return document.RootElement.Clone();
    }

    private static Dictionary<string, object?> Copy(Dictionary<string, object?> row) =>
        row.ToDictionary(p => p.Key, p => p.Value is DateTime dt
            ? (object)dt.ToString("dd-MM-yyyy", CultureInfo.InvariantCulture)
            : p.Value, StringComparer.Ordinal);

    private static Dictionary<string, object?> PublicRow(
        Dictionary<string, object?> source, params string[] excluded)
    {
        var row = Copy(source);
        foreach (var key in excluded) row.Remove(key);
        Rename(row, "StatusName", "CompanyMcaStatus");
        return row;
    }

    private static void Rename(Dictionary<string, object?> row, string source, string target)
    {
        if (row.Remove(source, out var value)) row[target] = value;
    }

    private static void NormalizeMissing(Dictionary<string, object?> row, string key)
    {
        if (row.TryGetValue(key, out var value) && value is string text &&
            (string.IsNullOrWhiteSpace(text) || text.Trim() == "-" ||
             text.Trim().Equals("null", StringComparison.OrdinalIgnoreCase)))
            row[key] = null;
    }

    private static int Id(Dictionary<string, object?> row, string key) =>
        Convert.ToInt32(row[key], CultureInfo.InvariantCulture);

    private static bool Flag(Dictionary<string, object?> row, string key, bool expected)
    {
        if (!row.TryGetValue(key, out var value) || value is null) return false;
        if (value is string text)
        {
            if (text.Trim() == "1") return expected;
            if (text.Trim() == "0") return !expected;
            return bool.TryParse(text, out var flag) && flag == expected;
        }
        return Convert.ToBoolean(value, CultureInfo.InvariantCulture) == expected;
    }
}
