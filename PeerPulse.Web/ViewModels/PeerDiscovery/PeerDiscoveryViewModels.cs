using System.ComponentModel.DataAnnotations;

namespace PeerPulse.Web.ViewModels.PeerDiscovery
{
    public sealed class CreatePeerGroupViewModel
    {
        public string? ReturnUrl { get; set; }

        [Required(ErrorMessage = "Enter a peer group name.")]
        [StringLength(120)]
        public string Name { get; set; } = string.Empty;

        [StringLength(120)]
        public string? BusinessVertical { get; set; }

        [StringLength(500)]
        public string? Description { get; set; }
    }

    public sealed class PeerGroupItemViewModel
    {
        public int PeerGroupId { get; set; }
        public string Name { get; set; } = string.Empty;
        public string? BusinessVertical { get; set; }
        public string? Description { get; set; }
        public int PeerCount { get; set; }
        public DateTime CreatedAtUtc { get; set; }
    }

    public sealed class ManagePeerGroupsViewModel
    {
        public List<PeerGroupItemViewModel> Groups { get; set; } = [];
        public int TotalPeers { get; set; }
    }

    public sealed class PeerRowViewModel
    {
        public long PeerCompanyId { get; set; }
        public string LegalName { get; set; } = string.Empty;
        public string? TradeName { get; set; }
        public string? Pan { get; set; }
        public string? CinOrLlpin { get; set; }
        public string? State { get; set; }
        public string? UserNotes { get; set; }
        public int PeerGroupId { get; set; }
        public string PeerGroupName { get; set; } = string.Empty;
        public DateTime CreatedAtUtc { get; set; }
    }

    public sealed class ManagePeersViewModel
    {
        public List<PeerGroupItemViewModel> Groups { get; set; } = [];
        public List<PeerRowViewModel> Peers { get; set; } = [];
    }

    public sealed class CreatePeerViewModel
    {
        [Range(1, int.MaxValue, ErrorMessage = "Select a peer group.")]
        public int PeerGroupId { get; set; }

        public long? CompanyId { get; set; }

        [Required(ErrorMessage = "Enter or select a company.")]
        [StringLength(250)]
        public string LegalName { get; set; } = string.Empty;

        [StringLength(250)]
        public string? TradeName { get; set; }

        [StringLength(20)]
        public string? Pan { get; set; }

        [StringLength(30)]
        public string? CinOrLlpin { get; set; }

        [StringLength(100)]
        public string? State { get; set; }

        [StringLength(1000)]
        public string? UserNotes { get; set; }
    }
}
