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

        ''' <summary>
        '''  wist eerst de lokale aanmeldstatus en vraagt daarna de aanmeldservice om een resultaat.
        ''' </summary>
        ''' <remarks>
        '''  een verplichte wachtwoordwijziging bewaart alleen de wachtende gebruiker, niet de sessie.
        '''  dat pad geeft onwaar terug.  pas zonder die verplichting wordt <see cref="Session"/> gevuld.
        ''' </remarks>
        Public Function Login(userName As String, password As String, Optional tenant As Guid? = Nothing) As Boolean
            Logout()
            Try
                Dim result = _authentication.Authenticate(New AuthenticateUserRequest With {
                    .UserIdentOrEmail = userName, .Password = password, .IdTenant = tenant
                })
                If result Is Nothing OrElse Not result.Success OrElse result.Value Is Nothing OrElse result.Value.User Is Nothing Then
                    SetError(If(result Is Nothing, "Geen antwoord van de aanmeldservice.", result.ErrorCode & ": " & GetDutchErrorMessage(result.ErrorCode)))
                    Return False
                End If
                If result.Value.MustChangePassword Then
                    _pendingUser = result.Value.User
                    OnPropertyChanged(NameOf(MustChangePassword))
                    SetError("Vervang het tijdelijke wachtwoord door een nieuw wachtwoord.")
                    Return False
                End If
                _session = result.Value.User
                OnPropertyChanged(NameOf(Session))
                Return True
            Catch ex As Exception
                SetError("Aanmelden is mislukt: " & ex.Message)
                Return False
            End Try
        End Function

        ''' <summary>
        '''  vraagt een wachtwoordwijziging aan voor de wachtende gebruiker en meldt daarna opnieuw aan.
        ''' </summary>
        ''' <remarks>
        '''  een geslaagde wijziging maakt niet rechtstreeks een sessie.  het resultaat van de nieuwe
        '''  aanmelding bepaalt de terugkeerwaarde; zonder wachtende gebruiker gebeurt er niets.
        ''' </remarks>
        Public Function ChangeTemporaryPassword(temporaryPassword As String, newPassword As String) As Boolean
            If _pendingUser Is Nothing Then Return False
            Try
                Dim result = _authentication.ChangeTemporaryPassword(_pendingUser.IdTenant, _pendingUser.IdUser, temporaryPassword, newPassword)
                If Not result.Success Then
                    SetError(result.ErrorCode & ": " & GetDutchErrorMessage(result.ErrorCode))
                    Return False
                End If
                Dim user = _pendingUser.UserIdent
                Dim tenant = _pendingUser.IdTenant
                Return Login(user, newPassword, tenant)
            Catch ex As Exception
                SetError("Het wijzigen van het wachtwoord is mislukt: " & ex.Message)
                Return False
            End Try
        End Function

        ''' <summary>
        '''  wist sessie, wachtende gebruiker en fouttekst in dit weergavemodel.
        ''' </summary>
        ''' <remarks>
        '''  de meldingen volgen ook als de waarden al leeg waren.  deze methode doet geen afmeldverzoek
        '''  aan de service en zegt daarmee niets over eventuele sessies buiten dit object.
        ''' </remarks>
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

        Private Shared Function GetDutchErrorMessage(errorCode As String) As String
            Select Case errorCode
                Case "InvalidRequest"
                    Return "Vul de vereiste aanmeldgegevens in."
                Case "InvalidCredentials"
                    Return "De gebruikersnaam of het wachtwoord is ongeldig."
                Case "UserInactive"
                    Return "Deze gebruiker is niet actief."
                Case "UserLockedOut"
                    Return "Deze gebruiker is tijdelijk geblokkeerd."
                Case "TemporaryPasswordExpired"
                    Return "Het tijdelijke wachtwoord is verlopen."
                Case "UserNotFound"
                    Return "De actieve gebruiker is niet gevonden."
                Case "PasswordChangeNotRequired"
                    Return "Voor deze gebruiker hoeft het tijdelijke wachtwoord niet te worden gewijzigd."
                Case Else
                    Return "De aanmeldservice heeft de aanvraag geweigerd (" & errorCode & ")."
            End Select
        End Function
    End Class
End Namespace
