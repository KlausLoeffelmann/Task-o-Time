using System;
using TaskOTime.AppServer.Models;
using TaskOTime.AppServer.Services;
using TaskOTime.ViewModel.Base;

namespace TaskOTime.ViewModel.ViewModels
{
    public class LoginViewModel : ViewModelBase
    {

        private readonly IAuthenticationService _authentication;
        private TenantUserDto _session;
        private string _errorMessage;
        private TenantUserDto _pendingUser;

        public LoginViewModel(IAuthenticationService authentication)
        {
            if (authentication is null)
                throw new ArgumentNullException(nameof(authentication));
            _authentication = authentication;
        }

        public TenantUserDto Session
        {
            get
            {
                return _session;
            }
        }

        public string ErrorMessage
        {
            get
            {
                return _errorMessage;
            }
        }

        public bool MustChangePassword
        {
            get
            {
                return _pendingUser is not null;
            }
        }

        // '' <summary>
        // ''  wist eerst de lokale aanmeldstatus en vraagt daarna de aanmeldservice om een resultaat.
        // '' </summary>
        // '' <remarks>
        // ''  een verplichte wachtwoordwijziging bewaart alleen de wachtende gebruiker, niet de sessie.
        // ''  dat pad geeft onwaar terug.  pas zonder die verplichting wordt <see cref="Session"/> gevuld.
        // '' </remarks>
        public bool Login(string userName, string password, Guid? tenant = default)
        {
            Logout();
            try
            {
                var result = _authentication.Authenticate(new AuthenticateUserRequest() { UserIdentOrEmail = userName, Password = password, IdTenant = tenant });
                if (result is null || !result.Success || result.Value is null || result.Value.User is null)
                {
                    SetError(result is null ? "Geen antwoord van de aanmeldservice." : result.ErrorCode + ": " + GetDutchErrorMessage(result.ErrorCode));
                    return false;
                }
                if (result.Value.MustChangePassword)
                {
                    _pendingUser = result.Value.User;
                    OnPropertyChanged(nameof(MustChangePassword));
                    SetError("Vervang het tijdelijke wachtwoord door een nieuw wachtwoord.");
                    return false;
                }
                _session = result.Value.User;
                OnPropertyChanged(nameof(Session));
                return true;
            }
            catch (Exception ex)
            {
                SetError("Aanmelden is mislukt: " + ex.Message);
                return false;
            }
        }

        // '' <summary>
        // ''  vraagt een wachtwoordwijziging aan voor de wachtende gebruiker en meldt daarna opnieuw aan.
        // '' </summary>
        // '' <remarks>
        // ''  een geslaagde wijziging maakt niet rechtstreeks een sessie.  het resultaat van de nieuwe
        // ''  aanmelding bepaalt de terugkeerwaarde; zonder wachtende gebruiker gebeurt er niets.
        // '' </remarks>
        public bool ChangeTemporaryPassword(string temporaryPassword, string newPassword)
        {
            if (_pendingUser is null)
                return false;
            try
            {
                var result = _authentication.ChangeTemporaryPassword(_pendingUser.IdTenant, _pendingUser.IdUser, temporaryPassword, newPassword);
                if (!result.Success)
                {
                    SetError(result.ErrorCode + ": " + GetDutchErrorMessage(result.ErrorCode));
                    return false;
                }
                string user = _pendingUser.UserIdent;
                var tenant = _pendingUser.IdTenant;
                return Login(user, newPassword, tenant);
            }
            catch (Exception ex)
            {
                SetError("Het wijzigen van het wachtwoord is mislukt: " + ex.Message);
                return false;
            }
        }

        // '' <summary>
        // ''  wist sessie, wachtende gebruiker en fouttekst in dit weergavemodel.
        // '' </summary>
        // '' <remarks>
        // ''  de meldingen volgen ook als de waarden al leeg waren.  deze methode doet geen afmeldverzoek
        // ''  aan de service en zegt daarmee niets over eventuele sessies buiten dit object.
        // '' </remarks>
        public void Logout()
        {
            _session = null;
            _pendingUser = null;
            SetError(string.Empty);
            OnPropertyChanged(nameof(Session));
            OnPropertyChanged(nameof(MustChangePassword));
        }

        private void SetError(string value)
        {
            _errorMessage = value;
            OnPropertyChanged(nameof(ErrorMessage));
        }

        private static string GetDutchErrorMessage(string errorCode)
        {
            switch (errorCode ?? "")
            {
                case "InvalidRequest":
                    {
                        return "Vul de vereiste aanmeldgegevens in.";
                    }
                case "InvalidCredentials":
                    {
                        return "De gebruikersnaam of het wachtwoord is ongeldig.";
                    }
                case "UserInactive":
                    {
                        return "Deze gebruiker is niet actief.";
                    }
                case "UserLockedOut":
                    {
                        return "Deze gebruiker is tijdelijk geblokkeerd.";
                    }
                case "TemporaryPasswordExpired":
                    {
                        return "Het tijdelijke wachtwoord is verlopen.";
                    }
                case "UserNotFound":
                    {
                        return "De actieve gebruiker is niet gevonden.";
                    }
                case "PasswordChangeNotRequired":
                    {
                        return "Voor deze gebruiker hoeft het tijdelijke wachtwoord niet te worden gewijzigd.";
                    }

                default:
                    {
                        return "De aanmeldservice heeft de aanvraag geweigerd (" + errorCode + ").";
                    }
            }
        }
    }
}