namespace PeerPulse.Web.Models.Reports;

// Server-side view shapes matching the supplied static template.
// No sample company data is embedded.
public sealed class StaticPeerNic
{
    public string mainDivision { get; set; } = "";
    public string mainDivisionDescription { get; set; } = "";
    public string mainGroupDescription { get; set; } = "";
    public string industryGroupDescription { get; set; } = "";
    public string industryDescription { get; set; } = "";
    public string detailedDescription { get; set; } = "";
}

public sealed class StaticPeerItc
{
    public string itcCode { get; set; } = "";
    public string description { get; set; } = "";
}

public sealed class StaticPeerGstinIndustryNatureOfBusinessActivitiesItem
{
    public string activity { get; set; } = "";
}

public sealed class StaticPeerGstinIndustrySacHsnCodesItem
{
    public string code { get; set; } = "";
    public string description { get; set; } = "";
}

public sealed class StaticPeerGstinIndustry
{
    public string natureOfCoreBusinessActivity { get; set; } = "";
    public List<StaticPeerGstinIndustryNatureOfBusinessActivitiesItem> natureOfBusinessActivities { get; set; } = new();
    public List<StaticPeerGstinIndustrySacHsnCodesItem> sacHsnCodes { get; set; } = new();
}

public sealed class StaticPeerMcaAddress
{
    public string cin { get; set; } = "";
    public string addressType { get; set; } = "";
    public string isActive { get; set; } = "";
    public string streetAddress { get; set; } = "";
    public string pinCode { get; set; } = "";
    public string city { get; set; } = "";
    public string state { get; set; } = "";
    public string country { get; set; } = "";
}

public sealed class StaticPeerGstAddress
{
    public string gstn { get; set; } = "";
    public string addressType { get; set; } = "";
    public string isActive { get; set; } = "";
    public string streetAddress { get; set; } = "";
    public string pinCode { get; set; } = "";
    public string city { get; set; } = "";
    public string state { get; set; } = "";
    public string country { get; set; } = "";
}

public sealed class StaticPeerBalanceSheetFilingsItem
{
    public string financialYear { get; set; } = "";
    public string dataOfFilling { get; set; } = "";
    public string status { get; set; } = "";
}

public sealed class StaticPeerAnnualReturnFilingsItem
{
    public string financialYear { get; set; } = "";
    public string dataOfFilling { get; set; } = "";
    public string status { get; set; } = "";
}

public sealed class StaticPeerDirectorsItem
{
    public string din { get; set; } = "";
    public string name { get; set; } = "";
    public string designation { get; set; } = "";
    public string appointmentDate { get; set; } = "";
    public decimal? durationYears { get; set; }
    public decimal? currentDirectorships { get; set; }
    public decimal? pastDirectorships { get; set; }
    public string disqualificationStatus { get; set; } = "";
    public bool isDisqualified { get; set; }
    public string deactivationStatus { get; set; } = "";
    public bool isDeactivated { get; set; }
    public string profileUrl { get; set; } = "";
}

public sealed class StaticPeerChargesItem
{
    public string chargeId { get; set; } = "";
    public string holderName { get; set; } = "";
    public decimal? amount { get; set; }
    public string status { get; set; } = "";
    public string creationDate { get; set; } = "";
    public decimal? ageYears { get; set; }
}

public sealed class StaticPeerEstablishmentsItem
{
    public string code { get; set; } = "";
    public string name { get; set; } = "";
    public string address { get; set; } = "";
    public string city { get; set; } = "";
    public string pin { get; set; } = "";
    public string district { get; set; } = "";
    public string state { get; set; } = "";
    public string country { get; set; } = "";
    public string status { get; set; } = "";
    public string regStatus { get; set; } = "";
    public string primaryBusinessActivity { get; set; } = "";
    public string ownershipType { get; set; } = "";
    public string dateOfSetup { get; set; } = "";
}

public sealed class StaticPeerEmployeesItem
{
    public string month { get; set; } = "";
    public decimal? employeeCount { get; set; }
    public decimal? pfAmount { get; set; }
}

public sealed class StaticPeerFinancialsItem
{
    public string financialYear { get; set; } = "";
    public decimal? paidUpCapital { get; set; }
    public decimal? netWorth { get; set; }
    public decimal? totalIncome { get; set; }
    public decimal? totalExpense { get; set; }
    public decimal? profitBeforeTax { get; set; }
    public decimal? incomeTax { get; set; }
    public decimal? profitAfterTax { get; set; }
}

public sealed class StaticPeerEstablishmentStatusItem
{
    public string pfExemptionStatus { get; set; } = "";
    public string pensionExemptionStatus { get; set; } = "";
    public string edliExemptionStatus { get; set; } = "";
    public string workingStatus { get; set; } = "";
    public string coverageSection { get; set; } = "";
    public string sectionDescription { get; set; } = "";
    public string actionableStatus { get; set; } = "";
    public string dateOfCoverage { get; set; } = "";
    public string panStatus { get; set; } = "";
}

public sealed class StaticPeerAdditionalInfoItem
{
    public string establishmentId { get; set; } = "";
    public string cin { get; set; } = "";
    public string esic { get; set; } = "";
    public string linCode { get; set; } = "";
    public string startupOrderNo { get; set; } = "";
    public string startupOrderDate { get; set; } = "";
    public string msmeOrderNo { get; set; } = "";
}

public sealed class StaticPeer
{
    public string id { get; set; } = "";
    public string cin { get; set; } = "";
    public string company { get; set; } = "";
    public string tradeName { get; set; } = "";
    public string pan { get; set; } = "";
    public string gstin { get; set; } = "";
    public string groupId { get; set; } = "";
    public string groupName { get; set; } = "";
    public string state { get; set; } = "";
    public string CompanyStatusID { get; set; } = "";
    public decimal? CompanyStatusRaw { get; set; }
    public string ActiveCompliance { get; set; } = "";
    public string whetherListedOrNot { get; set; } = "";
    public string smallLLPFlag { get; set; } = "";
    public string smallCompanyFlag { get; set; } = "";
    public string companyType { get; set; } = "";
    public string classOfCompany { get; set; } = "";
    public string companyCategory { get; set; } = "";
    public string companySubcategory { get; set; } = "";
    public string dateOfIncorporation { get; set; } = "";
    public string age { get; set; } = "";
    public string dateOfLastAGM { get; set; } = "";
    public string balanceSheetDate { get; set; } = "";
    public decimal? authorisedCapital { get; set; }
    public decimal? paidUpCapital { get; set; }
    public string paidUpRatio { get; set; } = "";
    public StaticPeerNic nic { get; set; } = new();
    public StaticPeerItc itc { get; set; } = new();
    public StaticPeerGstinIndustry gstin_industry { get; set; } = new();
    public StaticPeerMcaAddress mcaAddress { get; set; } = new();
    public StaticPeerGstAddress gstAddress { get; set; } = new();
    public List<StaticPeerBalanceSheetFilingsItem> balanceSheetFilings { get; set; } = new();
    public List<StaticPeerAnnualReturnFilingsItem> annualReturnFilings { get; set; } = new();
    public List<StaticPeerDirectorsItem> directors { get; set; } = new();
    public List<StaticPeerChargesItem> charges { get; set; } = new();
    public List<StaticPeerEstablishmentsItem> establishments { get; set; } = new();
    public List<StaticPeerEmployeesItem> employees { get; set; } = new();
    public List<StaticPeerFinancialsItem> financials { get; set; } = new();
    public string tan { get; set; } = "";
    public List<StaticPeerEstablishmentStatusItem> establishmentStatus { get; set; } = new();
    public List<StaticPeerAdditionalInfoItem> additionalInfo { get; set; } = new();
}
