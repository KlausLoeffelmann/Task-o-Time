using System;
using TaskOTime.AppServer.Models;
using TaskOTime.AppServer.Services;
using TaskOTime.ViewModel.Base;
using TaskOTime.ViewModel.Localization;

namespace TaskOTime.ViewModel.ViewModels
{
    /// <summary>
    /// Coordinates authentication, pending temporary-password changes and localized login errors.
    /// </summary>
    /// <remarks>
    /// A user awaiting a mandatory password change is kept separate from the authenticated session.
    /// A new login attempt clears previous local state; successful password replacement is followed
    /// by authentication with the new password before a session is exposed.
    /// </remarks>
    public class LoginViewModel : LocalizedViewModelBase
    {

        private readonly IAuthenticationService _authentication;
        private TenantUserDto _session;
        private string _errorKey;
        private object[] _errorArguments = Array.Empty<object>();
        private string _errorCode;
        private TenantUserDto _pendingUser;

        /// <summary>
        /// Creates login state backed by the supplied authentication service.
        /// </summary>
        /// <exception cref="ArgumentNullException">The authentication service is null.</exception>
        public LoginViewModel(IAuthenticationService authentication)
        {
            if (authentication is null)
                throw new ArgumentNullException(nameof(authentication));
            _authentication = authentication;
        }

        /// <summary>
        /// Gets the authenticated user, or null when no local session exists; pending password changes are excluded.
        /// </summary>
        public TenantUserDto Session
        {
            get
            {
                return _session;
            }
        }

        /// <summary>
        /// Gets the current error in the active culture, prefixed by a service error code when available.
        /// </summary>
        public string ErrorMessage
        {
            get
            {
                if (string.IsNullOrEmpty(_errorKey)) return string.Empty;
                var message = Text(_errorKey, _errorArguments);
                return string.IsNullOrEmpty(_errorCode) ? message : _errorCode + ": " + message;
            }
        }

        /// <summary>
        /// Indicates that a pending user must replace a temporary password before authentication can finish.
        /// </summary>
        public bool MustChangePassword
        {
            get
            {
                return _pendingUser is not null;
            }
        }

        /// <summary>Authenticates after clearing local state; a required password change does not create a session.</summary>
        public bool Login(string userName, string password, Guid? tenant = default)
        {
            Logout();
            try
            {
                var result = _authentication.Authenticate(new AuthenticateUserRequest() { UserIdentOrEmail = userName, Password = password, IdTenant = tenant });
                if (result is null || !result.Success || result.Value is null || result.Value.User is null)
                {
                    if (result is null) SetError("Login_NoResponse");
                    else SetServiceError(result.ErrorCode);
                    return false;
                }
                if (result.Value.MustChangePassword)
                {
                    _pendingUser = result.Value.User;
                    OnPropertyChanged(nameof(MustChangePassword));
                    SetError("Login_ChangeRequired");
                    return false;
                }
                _session = result.Value.User;
                OnPropertyChanged(nameof(Session));
                return true;
            }
            catch (Exception ex)
            {
                SetError("Login_Failed", ex.Message);
                return false;
            }
        }

        /// <summary>Changes the pending user's temporary password, then authenticates with the new password.</summary>
        public bool ChangeTemporaryPassword(string temporaryPassword, string newPassword)
        {
            if (_pendingUser is null)
                return false;
            try
            {
                var result = _authentication.ChangeTemporaryPassword(_pendingUser.IdTenant, _pendingUser.IdUser, temporaryPassword, newPassword);
                if (!result.Success)
                {
                    SetServiceError(result.ErrorCode);
                    return false;
                }
                string user = _pendingUser.UserIdent;
                var tenant = _pendingUser.IdTenant;
                return Login(user, newPassword, tenant);
            }
            catch (Exception ex)
            {
                SetError("Login_ChangeFailed", ex.Message);
                return false;
            }
        }

        /// <summary>Clears local session, pending user, and localized error state without calling the service.</summary>
        public void Logout()
        {
            _session = null;
            _pendingUser = null;
            SetError(string.Empty);
            OnPropertyChanged(nameof(Session));
            OnPropertyChanged(nameof(MustChangePassword));
        }

        private void SetError(string key, params object[] arguments)
        {
            _errorKey = key;
            _errorArguments = arguments;
            _errorCode = null;
            OnPropertyChanged(nameof(ErrorMessage));
        }

        private void SetServiceError(string errorCode)
        {
            switch (errorCode ?? "")
            {
                case "InvalidRequest":
                case "InvalidCredentials":
                case "UserInactive":
                case "TenantInactive":
                case "UserLockedOut":
                case "TemporaryPasswordExpired":
                case "UserNotFound":
                case "PasswordChangeNotRequired":
                    SetError("Login_" + errorCode);
                    break;
                default:
                    SetError("Login_Rejected", errorCode);
                    break;
            }
            _errorCode = errorCode;
            OnPropertyChanged(nameof(ErrorMessage));
        }
    }
}