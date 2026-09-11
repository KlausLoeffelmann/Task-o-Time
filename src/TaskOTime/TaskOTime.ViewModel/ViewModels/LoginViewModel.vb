Imports TaskOTime.AppServer.Models
Imports TaskOTime.AppServer.Services
Imports TaskOTime.ViewModel.Base

Namespace ViewModels
    Public Class LoginViewModel
        Inherits ViewModelBase

        Private ReadOnly _authentication As IAuthenticationService
        Private _session As TenantUserDto
        Private _errorMessage As String
        Private _pendingUser As TenantUserDto

        Public Sub New(authentication As IAuthenticationService)
            If authentication Is Nothing Then Throw New ArgumentNullException(NameOf(authentication))
            _authentication = authentication
        End Sub

        Public ReadOnly Property Session As TenantUserDto
            Get
                Return _session
            End Get
        End Property

        Public ReadOnly Property ErrorMessage As String
            Get
                Return _errorMessage
            End Get
        End Property

        Public ReadOnly Property MustChangePassword As Boolean
            Get
                Return _pendingUser IsNot Nothing
            End Get
        End Property

        Public Function Login(userName As String, password As String, Optional tenant As Guid? = Nothing) As Boolean
            Logout()
            Try
                Dim result = _authentication.Authenticate(New AuthenticateUserRequest With {
                    .UserIdentOrEmail = userName, .Password = password, .IdTenant = tenant
                })
                If result Is Nothing OrElse Not result.Success OrElse result.Value Is Nothing OrElse result.Value.User Is Nothing Then
                    SetError(If(result Is Nothing, "Keine Antwort vom Anmeldedienst.", result.ErrorCode & ": " & result.ErrorMessage))
                    Return False
                End If
                If result.Value.MustChangePassword Then
                    _pendingUser = result.Value.User
                    OnPropertyChanged(NameOf(MustChangePassword))
                    SetError("Bitte das vorläufige Kennwort durch ein neues ersetzen.")
                    Return False
                End If
                _session = result.Value.User
                OnPropertyChanged(NameOf(Session))
                Return True
            Catch ex As Exception
                SetError("Anmeldung fehlgeschlagen: " & ex.Message)
                Return False
            End Try
        End Function

        Public Function ChangeTemporaryPassword(temporaryPassword As String, newPassword As String) As Boolean
            If _pendingUser Is Nothing Then Return False
            Try
                Dim result = _authentication.ChangeTemporaryPassword(_pendingUser.IdTenant, _pendingUser.IdUser, temporaryPassword, newPassword)
                If Not result.Success Then
                    SetError(result.ErrorCode & ": " & result.ErrorMessage)
                    Return False
                End If
                Dim user = _pendingUser.UserIdent
                Dim tenant = _pendingUser.IdTenant
                Return Login(user, newPassword, tenant)
            Catch ex As Exception
                SetError("Kennwortänderung fehlgeschlagen: " & ex.Message)
                Return False
            End Try
        End Function

        Public Sub Logout()
            _session = Nothing
            _pendingUser = Nothing
            SetError(String.Empty)
            OnPropertyChanged(NameOf(Session))
            OnPropertyChanged(NameOf(MustChangePassword))
        End Sub

        Private Sub SetError(value As String)
            _errorMessage = value
            OnPropertyChanged(NameOf(ErrorMessage))
        End Sub
    End Class
End Namespace
