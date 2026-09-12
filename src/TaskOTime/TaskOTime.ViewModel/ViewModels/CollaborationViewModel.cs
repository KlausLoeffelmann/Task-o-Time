using System;
using System.Windows;
using TaskOTime.AppServer.Models;
using TaskOTime.ViewModel.Views;

namespace TaskOTime.ViewModel.ViewModels
{
    public class CollaborationViewModel
    {
        private readonly CollaborationView _view;
        private readonly ServiceWorkspace _store;
        internal CollaborationViewModel(CollaborationView view, ServiceWorkspace store)
        {
            _view = view;
            _store = store;
            _view.DataContext = this;
            // tabs macht das viewmodel. so ist die UI logik zentraler und nicht überall
            _view.CategoryListView.ItemsSource = store.Categories;
            _view.TagListBox.ItemsSource = store.Tags;
            _view.NoteListView.ItemsSource = store.Notes;
            _view.WebLinkListView.ItemsSource = store.WebLinks;
            _view.LogListView.ItemsSource = store.Logs;
            _view.AddButton.Click += this.AddCurrent;
            _view.DeleteButton.Click += this.DeleteCurrent;
        }

        private void AddCurrent(object sender, RoutedEventArgs e)
        {
            string value = _view.QuickValueTextBox.Text.Trim();
            if (value.Length == 0)
            {
                MessageBox.Show(Window.GetWindow(_view), "Bitte zuerst einen Wert eingeben.", "Hinzufügen");
                return;
            }

            switch (_view.DetailTabs.SelectedIndex)
            {
                case 0:
                    {
                        RunCreate("Kategorie anlegen", () =>
                            {
                                var result = _store.AdminService.CreateCategory(new SaveCategoryRequest()
                                {
                                    IdTenant = _store.Tenant.IdTenant,
                                    IdActingUser = _store.ActingUserId,
                                    Item = new CategoryMasterDataDto() { IdTenant = _store.Tenant.IdTenant, IdUser = _store.ActingUserId, CategoryName = value }
                                });
                                _store.Categories.Add(ServiceWorkspace.Require(result, "Kategorie anlegen"));
                            });
                        break;
                    }
                case 1:
                    {
                        RunCreate("Tag anlegen", () =>
                            {
                                var result = _store.AdminService.CreateTag(new SaveTagRequest()
                                {
                                    IdTenant = _store.Tenant.IdTenant,
                                    IdActingUser = _store.ActingUserId,
                                    Item = new TagMasterDataDto()
                                    {
                                        IdTenant = _store.Tenant.IdTenant,
                                        IdUser = _store.ActingUserId,
                                        Tag = value,
                                        DateCreated = DateTimeOffset.Now,
                                        DateModified = DateTimeOffset.Now
                                    }
                                });
                                _store.Tags.Add(ServiceWorkspace.Require(result, "Tag anlegen"));
                            });
                        break;
                    }
                case 2:
                    {
                        RunCreate("Notiz anlegen", () =>
                            {
                                var result = _store.AdminService.CreateNote(new SaveNoteRequest()
                                {
                                    IdTenant = _store.Tenant.IdTenant,
                                    IdActingUser = _store.ActingUserId,
                                    Item = new NoteMasterDataDto()
                                    {
                                        IdTenant = _store.Tenant.IdTenant,
                                        IdUser = _store.ActingUserId,
                                        NoteText = value,
                                        DateCreated = DateTimeOffset.Now,
                                        DateModified = DateTimeOffset.Now
                                    }
                                });
                                _store.Notes.Add(ServiceWorkspace.Require(result, "Notiz anlegen"));
                            });
                        break;
                    }
                case 3:
                    {
                        RunCreate("Weblink anlegen", () =>
                            {
                                var result = _store.AdminService.CreateWebLink(new SaveWebLinkRequest()
                                {
                                    IdTenant = _store.Tenant.IdTenant,
                                    IdActingUser = _store.ActingUserId,
                                    Item = new WebLinkMasterDataDto()
                                    {
                                        IdTenant = _store.Tenant.IdTenant,
                                        IdUser = _store.ActingUserId,
                                        Title = value,
                                        Link = value,
                                        DateCreated = DateTimeOffset.Now,
                                        DateModified = DateTimeOffset.Now
                                    }
                                });
                                _store.WebLinks.Add(ServiceWorkspace.Require(result, "Weblink anlegen"));
                            });
                        break;
                    }

                default:
                    {
                        _store.Logs.Add(value);
                        break;
                    }
            }
            _view.QuickValueTextBox.Clear();
        }

        private void DeleteCurrent(object sender, RoutedEventArgs e)
        {
            switch (_view.DetailTabs.SelectedIndex)
            {
                case 0:
                    {
                        CategoryMasterDataDto item = _view.CategoryListView.SelectedItem as CategoryMasterDataDto;
                        if (item is not null)
                            RunDelete("Kategorie löschen", _store.AdminService.DeleteCategory(DeleteRequest(item.IdCategory)), () => _store.Categories.Remove(item));
                        break;
                    }
                case 1:
                    {
                        TagMasterDataDto item = _view.TagListBox.SelectedItem as TagMasterDataDto;
                        if (item is not null)
                            RunDelete("Tag löschen", _store.AdminService.DeleteTag(DeleteRequest(item.IdTag)), () => _store.Tags.Remove(item));
                        break;
                    }
                case 2:
                    {
                        NoteMasterDataDto item = _view.NoteListView.SelectedItem as NoteMasterDataDto;
                        if (item is not null)
                            RunDelete("Notiz löschen", _store.AdminService.DeleteNote(DeleteRequest(item.IdNote)), () => _store.Notes.Remove(item));
                        break;
                    }
                case 3:
                    {
                        WebLinkMasterDataDto item = _view.WebLinkListView.SelectedItem as WebLinkMasterDataDto;
                        if (item is not null)
                            RunDelete("Weblink löschen", _store.AdminService.DeleteWebLink(DeleteRequest(item.IdWebLink)), () => _store.WebLinks.Remove(item));
                        break;
                    }

                default:
                    {
                        _store.Logs.Remove(_view.LogListView.SelectedItem as string);
                        break;
                    }
            }
        }

        private DeleteMasterDataRequest DeleteRequest(Guid id)
        {
            return new DeleteMasterDataRequest()
            {
                IdTenant = _store.Tenant.IdTenant,
                IdActingUser = _store.ActingUserId,
                IdItem = id,
                HardDelete = true
            };
        }

        private void RunCreate(string operation, Action action)
        {
            try
            {
                action();
            }
            catch (InvalidOperationException ex)
            {
                MessageBox.Show(Window.GetWindow(_view), ex.Message, operation + " - Servicefehler");
            }
        }

        private void RunDelete(string operation, ServiceResult<MasterDataDeleteResult> result, Action @remove)
        {
            try
            {
                ServiceWorkspace.Require(result, operation);
                @remove();
            }
            catch (InvalidOperationException ex)
            {
                MessageBox.Show(Window.GetWindow(_view), ex.Message, operation + " - Servicefehler");
            }
        }
    }
}