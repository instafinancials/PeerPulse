using System.ComponentModel.DataAnnotations;

namespace PeerPulse.Web.ViewModels.Account
{
    public sealed class LoginViewModel
    {
        [Required(ErrorMessage = "Enter your registered email address or mobile number.")]
        [StringLength(254)]
        [Display(Name = "Email address or mobile number")]
        public string Identifier { get; set; } = string.Empty;

        [Required(ErrorMessage = "Enter your password.")]
        [DataType(DataType.Password)]
        [StringLength(200)]
        public string Password { get; set; } = string.Empty;

        [Display(Name = "Remember me")]
        public bool RememberMe { get; set; }

        public string? ReturnUrl { get; set; }
    }
}
