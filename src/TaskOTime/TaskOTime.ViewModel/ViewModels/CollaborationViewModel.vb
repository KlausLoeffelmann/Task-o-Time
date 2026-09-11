Imports System.Windows
Imports TaskOTime.AppServer.Models
Imports TaskOTime.ViewModel.Views

Namespace ViewModels
    Public Class CollaborationViewModel
        Private ReadOnly _view As CollaborationView
        Private ReadOnly _store As ServiceWorkspace
        Friend Sub New(view As CollaborationView, store As ServiceWorkspace)
            _view = view
            _store = store
            _view.DataContext = Me
       'tabs macht das viewmodel. so ist die UI logik zentraler und nicht überall
            _view.CategoryListView.ItemsSource = store.Categories
            _view.TagListBox.ItemsSource = store.Tags
            _view.NoteListView.ItemsSource = store.Notes
            _view.WebLinkListView.ItemsSource = store.WebLinks
            _view.LogListView.ItemsSource = store.Logs
            AddHandler _view.AddButton.Click, AddressOf AddCurrent
            AddHandler _view.DeleteButton.Click, AddressOf DeleteCurrent
        End Sub

        Private Sub AddCurrent(sender As Object, e As RoutedEventArgs)
            Dim value = _view.QuickValueTextBox.Text.Trim()
            If value.Length = 0 Then
                MessageBox.Show(Window.GetWindow(_view), "Bitte zuerst einen Wert eingeben.", "Hinzufügen")
                Return
            End If

            Select Case _view.DetailTabs.SelectedIndex
                Case 0
                    RunCreate("Kategorie anlegen", Sub()
                        Dim result = _store.AdminService.CreateCategory(New SaveCategoryRequest With {
                            .IdTenant = _store.Tenant.IdTenant, .IdActingUser = _store.ActingUserId,
                            .Item = New CategoryMasterDataDto With {
                                .IdTenant = _store.Tenant.IdTenant, .IdUser = _store.ActingUserId, .CategoryName = value
                            }
                        })
                        _store.Categories.Add(ServiceWorkspace.Require(result, "Kategorie anlegen"))
                    End Sub)
                Case 1
                    RunCreate("Tag anlegen", Sub()
                        Dim result = _store.AdminService.CreateTag(New SaveTagRequest With {
                            .IdTenant = _store.Tenant.IdTenant, .IdActingUser = _store.ActingUserId,
                            .Item = New TagMasterDataDto With {
                                .IdTenant = _store.Tenant.IdTenant, .IdUser = _store.ActingUserId,
                                .Tag = value, .DateCreated = DateTimeOffset.Now, .DateModified = DateTimeOffset.Now
                            }
                        })
                        _store.Tags.Add(ServiceWorkspace.Require(result, "Tag anlegen"))
                    End Sub)
                Case 2
                    RunCreate("Notiz anlegen", Sub()
                        Dim result = _store.AdminService.CreateNote(New SaveNoteRequest With {
                            .IdTenant = _store.Tenant.IdTenant, .IdActingUser = _store.ActingUserId,
                            .Item = New NoteMasterDataDto With {
                                .IdTenant = _store.Tenant.IdTenant, .IdUser = _store.ActingUserId,
                                .NoteText = value, .DateCreated = DateTimeOffset.Now, .DateModified = DateTimeOffset.Now
                            }
                        })
                        _store.Notes.Add(ServiceWorkspace.Require(result, "Notiz anlegen"))
                    End Sub)
                Case 3
                    RunCreate("Weblink anlegen", Sub()
                        Dim result = _store.AdminService.CreateWebLink(New SaveWebLinkRequest With {
                            .IdTenant = _store.Tenant.IdTenant, .IdActingUser = _store.ActingUserId,
                            .Item = New WebLinkMasterDataDto With {
                                .IdTenant = _store.Tenant.IdTenant, .IdUser = _store.ActingUserId,
                                .Title = value, .Link = value, .DateCreated = DateTimeOffset.Now, .DateModified = DateTimeOffset.Now
                            }
                        })
                        _store.WebLinks.Add(ServiceWorkspace.Require(result, "Weblink anlegen"))
                    End Sub)
                Case Else
                    _store.Logs.Add(value)
            End Select
            _view.QuickValueTextBox.Clear()
        End Sub

        Private Sub DeleteCurrent(sender As Object, e As RoutedEventArgs)
            Select Case _view.DetailTabs.SelectedIndex
                Case 0
                    Dim item = TryCast(_view.CategoryListView.SelectedItem, CategoryMasterDataDto)
                    If item IsNot Nothing Then RunDelete("Kategorie löschen",
                        _store.AdminService.DeleteCategory(DeleteRequest(item.IdCategory)),
                        Sub() _store.Categories.Remove(item))
                Case 1
                    Dim item = TryCast(_view.TagListBox.SelectedItem, TagMasterDataDto)
                    If item IsNot Nothing Then RunDelete("Tag löschen",
                        _store.AdminService.DeleteTag(DeleteRequest(item.IdTag)),
                        Sub() _store.Tags.Remove(item))
                Case 2
                    Dim item = TryCast(_view.NoteListView.SelectedItem, NoteMasterDataDto)
                    If item IsNot Nothing Then RunDelete("Notiz löschen",
                        _store.AdminService.DeleteNote(DeleteRequest(item.IdNote)),
                        Sub() _store.Notes.Remove(item))
                Case 3
                    Dim item = TryCast(_view.WebLinkListView.SelectedItem, WebLinkMasterDataDto)
                    If item IsNot Nothing Then RunDelete("Weblink löschen",
                        _store.AdminService.DeleteWebLink(DeleteRequest(item.IdWebLink)),
                        Sub() _store.WebLinks.Remove(item))
                Case Else
                    _store.Logs.Remove(TryCast(_view.LogListView.SelectedItem, String))
            End Select
        End Sub

        Private Function DeleteRequest(id As Guid) As DeleteMasterDataRequest
            Return New DeleteMasterDataRequest With {
                .IdTenant = _store.Tenant.IdTenant, .IdActingUser = _store.ActingUserId,
                .IdItem = id, .HardDelete = True
            }
        End Function

        Private Sub RunCreate(operation As String, action As Action)
            Try
                action()
            Catch ex As InvalidOperationException
                MessageBox.Show(Window.GetWindow(_view), ex.Message, operation & " - Servicefehler")
            End Try
        End Sub

        Private Sub RunDelete(operation As String, result As ServiceResult(Of MasterDataDeleteResult), remove As Action)
            Try
                ServiceWorkspace.Require(result, operation)
                remove()
            Catch ex As InvalidOperationException
                MessageBox.Show(Window.GetWindow(_view), ex.Message, operation & " - Servicefehler")
            End Try
        End Sub
    End Class
End Namespace
