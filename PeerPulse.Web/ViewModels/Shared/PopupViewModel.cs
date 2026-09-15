namespace PeerPulse.Web.ViewModels.Shared
{
    public sealed class PopupViewModel
    {
        public string Id { get; init; } = "peerPulsePopup";

        public string Theme { get; init; } = "primary";

        public string Icon { get; init; } = "i";

        public string Title { get; init; } = string.Empty;

        public string Message { get; init; } = string.Empty;

        public string PrimaryText { get; init; } = "Close";

        public string? PrimaryUrl { get; init; }

        public string? SecondaryText { get; init; }

        public string? SecondaryUrl { get; init; }
    }
}
