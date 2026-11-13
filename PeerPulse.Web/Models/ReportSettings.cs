using System.ComponentModel.DataAnnotations;

namespace PeerPulse.Web.Models.ReportSettings
{
    public sealed class SaveReportPreference
    {
        [Required, RegularExpression("^(INR|USD)$")]
        public string Currency { get; set; } = "INR";
        [Required, RegularExpression("^(absolute|thousands|lakhs|crores)$")]
        public string FinancialFormat { get; set; } = "lakhs";
        [Range(typeof(decimal), "1", "100000")]
        public decimal ExchangeRate { get; set; } = 83.50m;
        [Range(typeof(long), "0", "9007199254740991")]
        public long ExpectedRevision { get; set; }
    }

    public sealed class SaveReportSelection
    {
        [Required, MinLength(1), MaxLength(500)]
        public int[] PeerIds { get; set; } = System.Array.Empty<int>();
        [Range(typeof(long), "1", "9007199254740991")]
        public long PreferenceRevision { get; set; }
        [Range(typeof(long), "0", "9007199254740991")]
        public long ExpectedRevision { get; set; }
    }
}
