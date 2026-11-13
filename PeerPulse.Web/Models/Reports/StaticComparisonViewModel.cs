using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace PeerPulse.Web.Models.Reports;

public sealed record StaticReportPreferences(string currency, string financialFormat, decimal exchangeRate);
public sealed record StaticWeb(string website, string title, string description, string linkedin,
    string facebook, string twitter, string instagram, string youtube, string lastUpdated);
public sealed record StaticKyc(string cin, string pan, string tan, List<string> gstins,
    List<string> establishmentNos, string esic, string lin, string startupRegNo, string msmeRegNo);
public sealed record StaticChargeStats(int openCount, decimal totalOpenAmount,
    decimal avgOpenAmount, decimal avgAgeYears);
public sealed record StaticEmployeeColumn(StaticPeer peer, string type, string cin);
public sealed record StaticFilingCell(string status, string label);

public sealed class StaticComparisonViewModel
{
    public ComparisonPageViewModel Source { get; }
    public List<StaticPeer> selectedPeers { get; } = new();
    public StaticReportPreferences preferences { get; }
    public string selectedFinancialYear { get; }
    private readonly Dictionary<string, StaticWeb> _web = new(StringComparer.Ordinal);
    private readonly DateTime _today = DateTime.UtcNow.Date;

    public StaticComparisonViewModel(ComparisonPageViewModel source,
        StaticReportPreferences preferences, string? financialYear = null)
    {
        Source = source;
        if (preferences.exchangeRate <= 0 || preferences.currency is not ("INR" or "USD") ||
            preferences.financialFormat is not ("absolute" or "thousands" or "lakhs" or "crores"))
            throw new InvalidDataException("The saved report preferences are invalid.");
        this.preferences = preferences;
        foreach (var item in source.Items.Where(i => i.Report is not null))
        {
            var report = item.Report!;
            var summary = report.InstaSummary;
            var profile = P(summary, "CompanyMasterSummary");
            if (profile.ValueKind != JsonValueKind.Object) profile = P(summary, "LLPMasterSummary");
            var p = new StaticPeer
            {
                id = report.Identifier, cin = report.Identifier,
                company = S(profile, "CompanyName", item.Label),
                tradeName = S(profile, "TradeName"), pan = S(profile, "CompanyPan"),
                tan = S(profile, "CompanyTAN"), gstin = S(profile, "GSTIN"),
                state = S(profile, "CompanyRegState"),
                CompanyStatusID = S(profile, "CompanyMcaStatus", "Unavailable"),
                ActiveCompliance = S(profile, "ActiveCompliance", "Unavailable"),
                whetherListedOrNot = S(profile, "ListingType", "Unavailable"),
                smallCompanyFlag = S(profile, "SmallCompanyFlag", "Unavailable"),
                companyType = report.EntityType == "llp" ? "LLP" : "Company",
                classOfCompany = S(profile, "CompanyClass", "Unavailable"),
                companyCategory = S(profile, "CompanyCategory", "—"),
                companySubcategory = S(profile, "CompanySubCategory", "—"),
                dateOfIncorporation = DateText(S(profile, "CompanyDateOfInc")),
                dateOfLastAGM = DateText(S(profile, "CompanyLastAgmDate")),
                balanceSheetDate = DateText(S(profile, "CompanyLastBsDate")),
                authorisedCapital = N(profile, "CompanyAuthCapital"),
                paidUpCapital = N(profile, "CompanyPaidUpCapital")
            };
            var inc = ParseDate(p.dateOfIncorporation);
            p.age = inc is null ? "—" : $"{Years(inc.Value):0.0} years";
            p.paidUpRatio = p.authorisedCapital is > 0 && p.paidUpCapital is not null
                ? (100 * p.paidUpCapital.Value / p.authorisedCapital.Value).ToString("0.0", CultureInfo.InvariantCulture) + "%" : "—";
            p.nic.mainDivision = report.EntityType == "company" && report.Identifier.Length >= 3
                ? report.Identifier.Substring(1, 2) : "—";
            p.nic.mainDivisionDescription = S(profile, "CompanyMcaIndustryDivision", "—");
            p.nic.mainGroupDescription = S(profile, "CompanyMcaIndustryGroup", "—");
            p.nic.industryGroupDescription = S(profile, "CompanyMcaIndustry", "—");
            p.nic.industryDescription = "—";
            p.nic.detailedDescription = "—";
            p.itc.itcCode = p.itc.description = "—";
            p.gstin_industry.natureOfCoreBusinessActivity = "—";
            p.mcaAddress = new StaticPeerMcaAddress
            {
                cin = p.cin, addressType = "Registered office",
                streetAddress = S(profile, "CompanyFullAddress", "—"),
                city = S(profile, "CompanyRegCity", "—"), state = S(profile, "CompanyRegState", "—"),
                pinCode = S(profile, "CompanyRegPinCode", "—"), country = "—"
            };
            p.gstAddress.gstn = p.gstin;
            p.gstAddress.addressType = p.gstAddress.streetAddress = p.gstAddress.city =
                p.gstAddress.state = p.gstAddress.pinCode = p.gstAddress.country = "—";

            var ds = P(summary, "DirectorSignatoryMasterSummary");
            foreach (var d in A(P(ds, "DirectorCurrentMasterSummary"), "Director"))
            {
                var date = ParseDate(S(d, "DirectorDateOfAppnt"));
                var status = S(d, "DINStatus");
                var name = S(d, "DirectorName");
                var din = S(d, "DirectorDin").PadLeft(8, '0');
                var dq = S(d, "DisqualifiedByRoCUnderSection164_2");
                p.directors.Add(new StaticPeerDirectorsItem
                {
                    din = din, name = name, designation = S(d, "DirectorDesignation"),
                    appointmentDate = DateText(S(d, "DirectorDateOfAppnt")),
                    durationYears = date is null ? null : Years(date.Value),
                    currentDirectorships = N(d, "DirectorCurrentDirectorshipCount"),
                    pastDirectorships = N(d, "DirectorPastDirectorshipCount"),
                    isDisqualified = dq.Equals("Yes", StringComparison.OrdinalIgnoreCase),
                    disqualificationStatus = dq.Equals("Yes", StringComparison.OrdinalIgnoreCase)
                        ? "Disqualified" : dq.Equals("No", StringComparison.OrdinalIgnoreCase) ? "Not Disqualified" : "Unavailable",
                    isDeactivated = status.Contains("Deactivated", StringComparison.OrdinalIgnoreCase),
                    deactivationStatus = string.IsNullOrWhiteSpace(status) ? "Unavailable" :
                        status.Contains("Deactivated", StringComparison.OrdinalIgnoreCase) ? "Deactivated" : status,
                    profileUrl = "https://www.instafinancials.com/director/" +
                        Regex.Replace(name.ToLowerInvariant(), "[^a-z0-9]+", "-").Trim('-') + "/" + Uri.EscapeDataString(din)
                });
            }
            foreach (var c in A(P(P(summary, "ChargesMasterSummary"), "OpenChargesMasterSummary"), "Charge"))
            {
                var date = ParseDate(S(c, "ChargeDateOfCreation"));
                p.charges.Add(new StaticPeerChargesItem
                {
                    chargeId = S(c, "ChargeID"), holderName = S(c, "ChargeHolderName"),
                    amount = N(c, "ChargeAmount"), status = "Open",
                    creationDate = DateText(S(c, "ChargeDateOfCreation")),
                    ageYears = date is null ? null : Years(date.Value)
                });
            }
            foreach (var f in A(P(summary, "FinancialsSummary"), "FinancialsYearWise"))
                p.financials.Add(new StaticPeerFinancialsItem
                {
                    financialYear = S(f, "FinancialYear"), paidUpCapital = N(f, "PaidUpCapital"),
                    netWorth = N(f, "NetWorth"), totalIncome = N(f, "TotalIncome"),
                    totalExpense = N(f, "TotalExpense"), profitBeforeTax = N(f, "ProfitBeforeTax"),
                    incomeTax = N(f, "IncomeTax"), profitAfterTax = N(f, "ProfitAfterTax")
                });
            p.financials = p.financials.OrderByDescending(f => f.financialYear, StringComparer.Ordinal).ToList();

            var epf = P(summary, "EmployeeAndEstablishmentSummary");
            var em = P(epf, "EstablishmentMaster");
            foreach (var e in A(em, "Establishment"))
                p.establishments.Add(new StaticPeerEstablishmentsItem
                {
                    code = S(e, "EstablishmentCode"), name = S(e, "EstablishmentName"),
                    address = S(e, "EstablishmentAddress"), city = S(e, "EstablishmentCity"),
                    pin = S(e, "EstablishmentPINCode"), district = S(e, "EstablishmentDistrict"),
                    state = S(e, "EstablishmentState"), country = S(e, "EstablishmentCountry"),
                    status = S(e, "EstablishmentStatus"), regStatus = S(e, "EstablishmentRegStatus"),
                    primaryBusinessActivity = S(e, "EstablishmentPrimaryBusinessActivity"),
                    ownershipType = S(e, "Ownershiptype"), dateOfSetup = DateText(S(e, "DateofSetupofEstablishment"))
                });
            foreach (var e in A(em, "EstablishmentStatus"))
                p.establishmentStatus.Add(new StaticPeerEstablishmentStatusItem
                {
                    pfExemptionStatus = S(e, "PFExemtpionStatus"), pensionExemptionStatus = S(e, "PensionExemtpionStatus"),
                    edliExemptionStatus = S(e, "EDLIExemtpionStatus"), workingStatus = S(e, "WorkingStatus"),
                    coverageSection = S(e, "CoverageSection"), sectionDescription = S(e, "SectionDescription"),
                    actionableStatus = S(e, "ActionableStatus"), dateOfCoverage = DateText(S(e, "DateofCoverage")),
                    panStatus = S(e, "PANStatus")
                });
            foreach (var e in A(em, "AdditioanlInfo"))
                p.additionalInfo.Add(new StaticPeerAdditionalInfoItem
                {
                    establishmentId = S(e, "EstablishmentID"), cin = S(e, "CIN"),
                    esic = S(e, "ESIC"), linCode = S(e, "LINCode"), startupOrderNo = S(e, "StartupOrderNo"),
                    startupOrderDate = DateText(S(e, "StartupOrderDate")), msmeOrderNo = S(e, "MSMEOrderNo")
                });
            foreach (var e in A(P(epf, "EmployeeTrend"), "EmployeeMonthWise"))
                p.employees.Add(new StaticPeerEmployeesItem
                {
                    month = S(e, "WagesMonth").ToUpperInvariant(),
                    employeeCount = N(e, "EpfEmployeeCountTotal"), pfAmount = N(e, "EpfAmountPaidTotal")
                });
            p.employees = p.employees.OrderByDescending(e => MonthKey(e.month)).ToList();

            var w = P(summary, "WebSocialMedia");
            _web[p.cin] = new StaticWeb(SafeUrl(S(w, "Website", S(profile, "CompanyWebSite"))),
                S(w, "WebsiteTitle"), S(w, "WebsiteDescription"), SafeUrl(S(w, "LinkedInURL")),
                SafeUrl(S(w, "FacebookURL")), SafeUrl(S(w, "TwitterURL")), SafeUrl(S(w, "InstagramURL")),
                SafeUrl(S(w, "YouTubeURL")), DateText(S(w, "LastUpdatedDateTime")));
            selectedPeers.Add(p);
        }
        var available = getAvailableFinancialYears();
        selectedFinancialYear = financialYear is not null && available.Contains(financialYear)
            ? financialYear : available.FirstOrDefault() ?? "Unavailable";
    }

    private static JsonElement P(JsonElement n, string k) => ComparisonDisplay.Property(n, k);
    private static IEnumerable<JsonElement> A(JsonElement n, string k)
    {
        var v = P(n, k);
        return v.ValueKind == JsonValueKind.Array ? v.EnumerateArray().ToArray() : Array.Empty<JsonElement>();
    }
    private static string S(JsonElement n, string k, string fallback = "")
    {
        var v = P(n, k);
        if (v.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined) return fallback;
        var s = v.ValueKind == JsonValueKind.String ? v.GetString() : v.ToString();
        return string.IsNullOrWhiteSpace(s) || s.Trim().Equals("null", StringComparison.OrdinalIgnoreCase)
            ? fallback : s;
    }
    private static decimal? N(JsonElement n, string k) => decimal.TryParse(S(n, k),
        NumberStyles.Number, CultureInfo.InvariantCulture, out var v) ? v : null;
    private static DateTime? ParseDate(string text)
    {
        string[] formats = ["dd-MM-yyyy", "dd-MMM-yyyy", "yyyy-MM-dd", "yyyy-MM-ddTHH:mm:ss",
            "yyyy-MM-ddTHH:mm:ss.FFFFFFF", "yyyy-MM-ddTHH:mm:ss.FFFFFFFK", "yyyy-MM-dd HH:mm:ss.FFFFFFF",
            "MMM d yyyy h:mmtt", "MMM dd yyyy h:mmtt"];
        return DateTime.TryParseExact(text, formats, CultureInfo.InvariantCulture,
            DateTimeStyles.AllowWhiteSpaces, out var d) ? d.Date : null;
    }
    private static string DateText(string text) => ParseDate(text)?.ToString("dd-MMM-yyyy", CultureInfo.InvariantCulture) ?? (string.IsNullOrWhiteSpace(text) ? "—" : text);
    private decimal Years(DateTime start) => Math.Max(0, Math.Round((decimal)(_today - start).TotalDays / 365.2425m, 1));
    public static string SafeUrl(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return "";
        if (!text.Contains("://", StringComparison.Ordinal)) text = "https://" + text;
        return Uri.TryCreate(text, UriKind.Absolute, out var u) && u.Scheme is "http" or "https"
            ? u.AbsoluteUri : "";
    }
    public static string DisplayHost(string url) => Regex.Replace(url, "^https?://", "", RegexOptions.IgnoreCase);
    public static string Width(decimal? percent) => "width: " + Math.Clamp(percent ?? 0, 0, 100).ToString("0.##", CultureInfo.InvariantCulture) + "%";
    public static bool Truthy(object? v) => v switch
    {
        null => false, bool b => b, string s => !string.IsNullOrWhiteSpace(s),
        decimal d => d != 0, int i => i != 0, _ => true
    };
    public static decimal Number(object? value)
    {
        if (value is null) return 0;
        var s = Convert.ToString(value, CultureInfo.InvariantCulture) ?? "";
        var match = Regex.Match(s, @"^[+-]?\d+(?:\.\d+)?");
        return decimal.TryParse(match.Value, NumberStyles.Number, CultureInfo.InvariantCulture, out var n) ? n : 0;
    }
    public static string Or(string? a, string b) => string.IsNullOrWhiteSpace(a) ? b : a;

    public string formatValue(object? amount)
    {
        if (amount is null) return "—";
        if (!decimal.TryParse(Convert.ToString(amount, CultureInfo.InvariantCulture),
            NumberStyles.Number, CultureInfo.InvariantCulture, out var n)) return "—";
        var symbol = preferences.currency == "USD" ? "$" : "₹";
        if (preferences.currency == "USD") n /= preferences.exchangeRate;
        if (preferences.financialFormat == "absolute")
        {
            // JS Math.round rounds half toward +infinity, including negative numbers.
            n = Math.Floor(n + .5m);
            return symbol + " " + n.ToString("N0", CultureInfo.GetCultureInfo(preferences.currency == "INR" ? "en-IN" : "en-US"));
        }
        var divisor = preferences.financialFormat switch { "thousands" => 1000m, "crores" => 10000000m, _ => 100000m };
        var suffix = preferences.financialFormat switch { "thousands" => " K", "crores" => " Cr", _ => " L" };
        n = Math.Round(n / divisor, 2, MidpointRounding.AwayFromZero);
        // Stable grouping rather than a browser-dependent unspecified locale.
        return symbol + " " + n.ToString("#,##0.##", CultureInfo.GetCultureInfo("en-US")) + suffix;
    }
    private static int Percent(decimal? value, decimal max, int minimum, bool zeroIsEmpty = false)
    {
        var n = value ?? 0;
        if (zeroIsEmpty && n <= 0) return 0;
        return (int)Math.Clamp(Math.Floor(n / Math.Max(max, 1) * 100 + .5m), minimum, 100);
    }
    public int getAgePercent(string age) => Percent(Number(age), selectedPeers.Select(p => Number(p.age)).DefaultIfEmpty(1).Max(), 8);
    public int getAuthorisedCapitalPercent(decimal? n) => Percent(n, selectedPeers.Select(p => p.authorisedCapital ?? 0).DefaultIfEmpty(1).Max(), 6);
    public int getPaidUpCapitalPercent(decimal? n) => Percent(n, selectedPeers.Select(p => p.paidUpCapital ?? 0).DefaultIfEmpty(1).Max(), 6);
    public bool isLatestBSDate(string text)
    {
        var d = ParseDate(text);
        var latest = selectedPeers.Select(p => ParseDate(p.balanceSheetDate)).Where(x => x.HasValue).Max();
        return d.HasValue && latest.HasValue && d.Value == latest.Value;
    }
    public List<string> getAvailableFinancialYears() => selectedPeers.SelectMany(p => p.financials)
        .Select(f => f.financialYear).Distinct().OrderByDescending(y => y, StringComparer.Ordinal).ToList();
    public List<string> getDistinctFinancialYears(string type) => getAvailableFinancialYears();
    public StaticFilingCell getFilingCell(StaticPeer p, string fy, string type) => new("unknown", "Unavailable");
    public string getFilingSummary(StaticPeer p, string type) => "History unavailable";
    public List<StaticPeerDirectorsItem> getSortedActiveDirectors(List<StaticPeerDirectorsItem> directors) =>
        directors.Where(d => !d.isDeactivated).OrderBy(d => Weight(d.designation)).ThenByDescending(d => d.durationYears).ToList();
    private static int Weight(string designation)
    {
        var d = designation.ToLowerInvariant();
        if (d.Contains("chairman")) return 1;
        if (d.Contains("ceo") || d.Contains("chief executive")) return 2;
        if (d.Contains("managing director") || d.Contains("md")) return 3;
        if (d.Contains("wholetime") || d.Contains("whole-time") || d.Contains("executive director")) return 4;
        if (d == "director" || d.Contains("independent director") || d.Contains("additional director")) return 5;
        return 6;
    }
    public int getDirectorCountPercent(int n) => Percent(n, selectedPeers.Select(p => p.directors.Count).DefaultIfEmpty(1).Max(), 10);
    public int getDurationPercent(decimal? n) => Percent(n, selectedPeers.SelectMany(p => p.directors).Select(d => d.durationYears ?? 0).DefaultIfEmpty(1).Max(), 8);
    public List<StaticPeerChargesItem> getOpenCharges(StaticPeer p) => p.charges;
    public StaticChargeStats getChargeStats(StaticPeer p)
    {
        var total = p.charges.Sum(c => c.amount ?? 0);
        return new(p.charges.Count, total, p.charges.Count == 0 ? 0 : total / p.charges.Count,
            p.charges.Count == 0 ? 0 : Math.Round(p.charges.Average(c => c.ageYears ?? 0), 1));
    }
    public int getTotalChargePercent(decimal n) => Percent(n, selectedPeers.Select(p => getChargeStats(p).totalOpenAmount).DefaultIfEmpty(1).Max(), 6, true);
    public int getAvgChargePercent(decimal n) => Percent(n, selectedPeers.Select(p => getChargeStats(p).avgOpenAmount).DefaultIfEmpty(1).Max(), 6, true);
    public int getChargeAgePercent(decimal n) => Percent(n, selectedPeers.Select(p => getChargeStats(p).avgAgeYears).DefaultIfEmpty(1).Max(), 8, true);
    private static int MonthKey(string month) => DateTime.TryParseExact(month, "MMM-yy", CultureInfo.InvariantCulture,
        DateTimeStyles.None, out var d) ? d.Year * 100 + d.Month : 0;
    public List<string> getEmployeeMonths() => selectedPeers.SelectMany(p => p.employees).Select(e => e.month)
        .Where(m => MonthKey(m) > 0).Distinct().OrderByDescending(MonthKey).ToList();
    public List<StaticEmployeeColumn> getEmployeeColumns() => selectedPeers.SelectMany(p => new[]
        { new StaticEmployeeColumn(p, "count", p.cin), new StaticEmployeeColumn(p, "pf", p.cin) }).ToList();
    public StaticPeerEmployeesItem? getEmployeeMonthRecord(StaticPeer p, string month) => p.employees.FirstOrDefault(e => e.month.Equals(month, StringComparison.OrdinalIgnoreCase));
    public StaticPeerEmployeesItem? getLatestEmployeeRecord(StaticPeer p) => p.employees.FirstOrDefault();
    public decimal getLatestEmployeeCount(StaticPeer p) => getLatestEmployeeRecord(p)?.employeeCount ?? 0;
    public decimal getRevenuePerEmployee(StaticPeer p)
    {
        var count = getLatestEmployeeCount(p); var revenue = p.financials.FirstOrDefault()?.totalIncome ?? 0;
        return count > 0 && revenue > 0 ? Math.Floor(revenue / count + .5m) : 0;
    }
    public int getEmployeeCountPercent(decimal n) => Percent(n, selectedPeers.Select(getLatestEmployeeCount).DefaultIfEmpty(1).Max(), 6, true);
    public int getRevenuePerEmployeePercent(decimal n) => Percent(n, selectedPeers.Select(getRevenuePerEmployee).DefaultIfEmpty(1).Max(), 6, true);
    public decimal getAvgMonthlyPf(StaticPeer p)
    {
        var r = getLatestEmployeeRecord(p);
        return r?.employeeCount is > 0 && r.pfAmount is > 0 ? Math.Floor(r.pfAmount.Value / r.employeeCount.Value + .5m) : 0;
    }
    public decimal getAvgMonthlySalary(StaticPeer p) => getAvgMonthlyPf(p) > 0 ? Math.Floor(getAvgMonthlyPf(p) / .06m + .5m) : 0;
    public StaticPeerEstablishmentsItem? getPrimaryEstablishment(StaticPeer p) => p.establishments.FirstOrDefault();
    public StaticPeerEstablishmentStatusItem? getEstablishmentStatusRecord(StaticPeer p) => p.establishmentStatus.FirstOrDefault();
    public StaticKyc getKycInfo(StaticPeer p)
    {
        var a = p.additionalInfo.FirstOrDefault();
        return new(p.cin, Or(p.pan, "-"), Or(p.tan, "-"), string.IsNullOrWhiteSpace(p.gstin) ? new() : new() { p.gstin },
            p.establishments.Select(e => e.code).Where(c => !string.IsNullOrWhiteSpace(c)).ToList(),
            Or(a?.esic, "NA"), Or(a?.linCode, "-"), Or(a?.startupOrderNo, "-"), Or(a?.msmeOrderNo, "-"));
    }
    public StaticWeb getWebSocialMedia(StaticPeer p) => _web[p.cin];
    public StaticPeerFinancialsItem? getFinancialDataForYear(StaticPeer p) => p.financials.FirstOrDefault(f => f.financialYear == selectedFinancialYear);
    public decimal? getNetProfitMargin(StaticPeer p)
    {
        var f = getFinancialDataForYear(p);
        return f?.totalIncome is not null and not 0 && f.profitAfterTax is not null
            ? Math.Round(100 * f.profitAfterTax.Value / f.totalIncome.Value, 1) : null;
    }
    public decimal? getReturnOnEquity(StaticPeer p)
    {
        var f = getFinancialDataForYear(p);
        return f?.netWorth is > 0 && f.profitAfterTax is not null
            ? Math.Round(100 * f.profitAfterTax.Value / f.netWorth.Value, 1) : null;
    }
    public int getTotalIncomePercent(StaticPeer p) => Percent(getFinancialDataForYear(p)?.totalIncome,
        selectedPeers.Select(x => getFinancialDataForYear(x)?.totalIncome ?? 0).DefaultIfEmpty(1).Max(), 6, true);
    public int getNetProfitPercent(StaticPeer p) => Percent(Math.Abs(getFinancialDataForYear(p)?.profitAfterTax ?? 0),
        selectedPeers.Select(x => Math.Abs(getFinancialDataForYear(x)?.profitAfterTax ?? 0)).DefaultIfEmpty(1).Max(), 6, true);
}
