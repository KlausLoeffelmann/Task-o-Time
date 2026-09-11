Imports System.Collections.ObjectModel
Imports TaskOTime.AppServer.Models
Imports TaskOTime.AppServer.Services

Friend NotInheritable Class ServiceWorkspace
    Public Sub New(tenant As TenantDto,
                   actingUserId As Guid,
                   adminService As IAdminMasterDataService,
                   userService As IUserAdministrationService,
                   timeBookingService As ITimeBookingService)
        If tenant Is Nothing Then Throw New ArgumentNullException(NameOf(tenant))
        If adminService Is Nothing Then Throw New ArgumentNullException(NameOf(adminService))
        If userService Is Nothing Then Throw New ArgumentNullException(NameOf(userService))
        If timeBookingService Is Nothing Then Throw New ArgumentNullException(NameOf(timeBookingService))
        Me.Tenant = tenant
        Me.ActingUserId = actingUserId
        Me.AdminService = adminService
        Me.UserService = userService
        Me.TimeBookingService = timeBookingService

        Tenants.Add(tenant)
        Replace(Users, Require(userService.GetTenantUsers(tenant.IdTenant), "Benutzer laden"))
        Dim query = New MasterDataQueryRequest With {.IdTenant = tenant.IdTenant, .IdActingUser = actingUserId}
        Replace(Projects, Require(adminService.GetProjects(query), "Projekte laden"))
        Replace(TaskLists, Require(adminService.GetTaskLists(query), "Aufgabenlisten laden"))
        Replace(Tasks, Require(adminService.GetTaskItems(query), "Aufgaben laden"))
        Replace(Categories, Require(adminService.GetCategories(query), "Kategorien laden"))
        Replace(Tags, Require(adminService.GetTags(query), "Tags laden"))
        Replace(Notes, Require(adminService.GetNotes(query), "Notizen laden"))
        Replace(WebLinks, Require(adminService.GetWebLinks(query), "Weblinks laden"))
        Logs.Add("Stammdaten vom konfigurierten Dienst geladen.")
    End Sub

    Public ReadOnly Property Tenant As TenantDto
    Public ReadOnly Property ActingUserId As Guid
    Public ReadOnly Property AdminService As IAdminMasterDataService
    Public ReadOnly Property UserService As IUserAdministrationService
    Public ReadOnly Property TimeBookingService As ITimeBookingService
    Public ReadOnly Tenants As New ObservableCollection(Of TenantDto)()
    Public ReadOnly Users As New ObservableCollection(Of TenantUserDto)()
    Public ReadOnly Projects As New ObservableCollection(Of ProjectMainDataDto)()
    Public ReadOnly TaskLists As New ObservableCollection(Of TaskListMasterDataDto)()
    Public ReadOnly Tasks As New ObservableCollection(Of TaskItemMasterDataDto)()
    Public ReadOnly Categories As New ObservableCollection(Of CategoryMasterDataDto)()
    Public ReadOnly Tags As New ObservableCollection(Of TagMasterDataDto)()
    Public ReadOnly Notes As New ObservableCollection(Of NoteMasterDataDto)()
    Public ReadOnly WebLinks As New ObservableCollection(Of WebLinkMasterDataDto)()
    Public ReadOnly Logs As New ObservableCollection(Of String)()

    Public Function Query() As MasterDataQueryRequest
        Return New MasterDataQueryRequest With {.IdTenant = Tenant.IdTenant, .IdActingUser = ActingUserId}
    End Function

    Public Shared Function Require(Of T)(result As ServiceResult(Of T), operation As String) As T
        If result Is Nothing Then Throw New InvalidOperationException(operation & ": kein ServiceResult")
        If Not result.Success Then
            Throw New InvalidOperationException(operation & ": " & result.ErrorCode & " - " & result.ErrorMessage)
        End If
        Return result.Value
    End Function

    Private Shared Sub Replace(Of T)(target As ObservableCollection(Of T), source As IEnumerable(Of T))
        target.Clear()
        For Each item In source
            target.Add(item)
        Next
    End Sub
End Class
