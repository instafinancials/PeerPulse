namespace PeerPulse.Web.ViewModels.PeerDiscovery
{
    public sealed class AddPeerViewModel
    {
        public List<PeerGroupOptionViewModel> Groups { get; set; } = [];
    }

    public sealed class PeerGroupOptionViewModel
    {
        public int PeerGroupId { get; set; }

        public string GroupName { get; set; } = string.Empty;

        public string? BusinessVertical { get; set; }
    }
}
