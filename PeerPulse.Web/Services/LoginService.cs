using PeerPulse.Web.Helpers;
using PeerPulse.Web.Models.InstaUsers;
using PeerPulse.Web.ViewModels.Account;
using System.Text.RegularExpressions;

namespace PeerPulse.Web.Services;

public sealed class LoginService
{
    private readonly InstaUserLoginRepository _userRepository;
    private readonly IConfiguration _configuration;
    private readonly ILogger<LoginService> _logger;

    public LoginService(InstaUserLoginRepository userRepository,IConfiguration configuration, ILogger<LoginService> logger)
    {
        _userRepository = userRepository;
        _configuration = configuration;
        _logger = logger;
    }

    public async Task<LoginCheckResult> ValidateAsync(string identifier, string password, CancellationToken cancellationToken = default)
    {

        try
        {
            identifier = identifier.Trim();
            password = password.Trim();

            if (string.IsNullOrWhiteSpace(identifier) || string.IsNullOrWhiteSpace(password))
            {
                return new LoginCheckResult(LoginCheckStatus.InvalidIdentifier);
            }
            bool isMobile = Regex.IsMatch(identifier, @"^\d{10}$");
            bool isEmail = identifier.Contains('@');
            if (!isMobile && !isEmail)
            {
                return new LoginCheckResult(LoginCheckStatus.InvalidIdentifier);
            }

            InstaUser? user = isMobile ? await _userRepository.FindByMobileAsync(identifier, cancellationToken)
                : await _userRepository.FindByEmailAsync(identifier, cancellationToken);

            if (user is null)
            {
                return new LoginCheckResult(isMobile ? LoginCheckStatus.MobileNotFound : LoginCheckStatus.EmailNotFound);
            }
            int legacyCodePage = _configuration.GetValue<int>("LegacyPassword:CodePage", 1252);


            bool passwordValid = Encryption.Verify(password, user.Password, legacyCodePage);

            if (!passwordValid)
            {
                return new LoginCheckResult(LoginCheckStatus.InvalidPassword);
            }
            if (user.StatusId == 2)
            {
                return new LoginCheckResult(LoginCheckStatus.Disabled, user);
            }
            if (user.StatusId == 0 || user.IsUserActive != true)
            {
                return new LoginCheckResult(LoginCheckStatus.VerificationRequired, user);
            }

            if (user.StatusId != 1)
            {
                return new LoginCheckResult(LoginCheckStatus.Disabled, user);
            }

            return new LoginCheckResult(LoginCheckStatus.Success, user);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Login validation failed.");
            return new LoginCheckResult(LoginCheckStatus.Error);
        }
    }
    public Task<InstaUser?> FindByEmailAsync(string email,CancellationToken cancellationToken = default)
    {
        return _userRepository.FindByEmailAsync(email.Trim(),cancellationToken);
    }
}