using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
namespace PeerPulse.Web.Models
{
    // Search input: PAN, CIN, or LLPIN.
    public sealed class PeerSearchRequest
    {
        // Reusing the same ID prevents a retry from consuming another search.
        public Guid SearchId { get; set; }

        [Required]
        [StringLength(5)]
        public string IdentifierType { get; set; } = "PAN";

        [Required]
        [StringLength(21)]
        public string Identifier { get; set; } = string.Empty;
    }

    // Current daily search consumption.
    public sealed record SearchQuota(string PlanName, int DailyLimit, int Used, DateTime UsageDate)
    {
        public int Remaining => Math.Max(0, DailyLimit - Used);
    }

    // Company or LLP returned by a search.
    public sealed record PeerRecord(
        string EntityType,
        int EntityId,
        string RegistrationNumber,
        string? Pan,
        string Name,
        DateTime? IncorporatedOn,
        string? State,
        string? Roc,
        string? Status,
        string? Activity,
        string? Address,
        decimal? Capital,
        string? Website,
        int? Partners,
        int? DesignatedPartners);

    public sealed record PeerSearchResponse(
        string Status,
        string Message,
        SearchQuota? Quota,
        IReadOnlyList<PeerRecord> Records,
        Guid? SearchId = null);

    // Initial Add Your Peer page.
    public sealed class AddPeerViewModel
    {
        public SearchQuota? Quota { get; set; }

        public string? Message { get; set; }
    }

    // Input for adding/editing peers and managing groups.
    public sealed class PeerMutationRequest
    {
        public Guid? SearchId { get; set; }

        [StringLength(10)]
        public string? EntityType { get; set; }

        public int? EntityId { get; set; }

        public int? PeerId { get; set; }

        public int? GroupId { get; set; }

        [StringLength(100)]
        public string? Name { get; set; }

        [StringLength(150)]
        public string? Vertical { get; set; }

        [StringLength(1000)]
        public string? Description { get; set; }

        [StringLength(1000)]
        public string? Notes { get; set; }

        // At least one owned group is required when saving a peer.
        // The SQL procedure will enforce that rule.
        [MaxLength(100)]
        public int[] GroupIds { get; set; } = Array.Empty<int>();
    }

    public sealed record PeerGroup(int GroupId, string Name, string? Vertical, string? Description, int PeerCount);

    public sealed record PeerGroupLink(int GroupId, string Name);

    // A peer already added to the user's portfolio.
    public sealed record SavedPeer(int PeerId,string EntityType,int EntityId,string Name,string? RegistrationNumber,string? Pan,string? State,string? Status,string? Notes,DateTime CreatedAtUtc,bool IsActive,IReadOnlyList<PeerGroupLink> Groups);

    // Data used by Manage Peers and Manage Peer Groups.
    public sealed record PeerWorkspaceResponse(string Status,string Message,string? PlanName,int PeerLimit,int PeerCount, int? ChangedGroupId,IReadOnlyList<PeerGroup> Groups,IReadOnlyList<SavedPeer> Peers);

}
