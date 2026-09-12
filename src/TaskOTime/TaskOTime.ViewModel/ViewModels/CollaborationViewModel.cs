using System;
using System.Collections.ObjectModel;
using TaskOTime.AppServer.Models;
using TaskOTime.ViewModel.Base;

namespace TaskOTime.ViewModel.ViewModels
{
    /// <summary>Creates and removes collaboration data using observable selection and commands.</summary>
    public sealed class CollaborationViewModel : MaintenanceViewModel
    {
        private int _selectedTab;
        private string _quickValue = "", _selectedLog;
        private CategoryMainDataDto _selectedCategory;
        private TagMainDataDto _selectedTag;
        private NoteMainDataDto _selectedNote;
        private WebLinkMainDataDto _selectedWebLink;

        public CollaborationViewModel(ServiceWorkspace store, IMaintenanceInteraction interaction) : base(store, interaction)
        {
            AddCommand = Command(AddCurrent, () => SelectedTab >= 0 && SelectedTab <= 4 && !string.IsNullOrWhiteSpace(QuickValue));
            DeleteCommand = Command(DeleteCurrent, HasSelection);
        }

        public ObservableCollection<CategoryMainDataDto> Categories => Store.Categories;
        public ObservableCollection<TagMainDataDto> Tags => Store.Tags;
        public ObservableCollection<NoteMainDataDto> Notes => Store.Notes;
        public ObservableCollection<WebLinkMainDataDto> WebLinks => Store.WebLinks;
        public ObservableCollection<string> Logs => Store.Logs;
        public DelegateCommand AddCommand { get; }
        public DelegateCommand DeleteCommand { get; }
        public int SelectedTab { get => _selectedTab; set { if (SetProperty(ref _selectedTab, value, nameof(SelectedTab))) RefreshCommands(); } }
        public string QuickValue { get => _quickValue; set { if (SetProperty(ref _quickValue, value, nameof(QuickValue))) RefreshCommands(); } }
        public CategoryMainDataDto SelectedCategory { get => _selectedCategory; set { if (SetProperty(ref _selectedCategory, value, nameof(SelectedCategory))) RefreshCommands(); } }
        public TagMainDataDto SelectedTag { get => _selectedTag; set { if (SetProperty(ref _selectedTag, value, nameof(SelectedTag))) RefreshCommands(); } }
        public NoteMainDataDto SelectedNote { get => _selectedNote; set { if (SetProperty(ref _selectedNote, value, nameof(SelectedNote))) RefreshCommands(); } }
        public WebLinkMainDataDto SelectedWebLink { get => _selectedWebLink; set { if (SetProperty(ref _selectedWebLink, value, nameof(SelectedWebLink))) RefreshCommands(); } }
        public string SelectedLog { get => _selectedLog; set { if (SetProperty(ref _selectedLog, value, nameof(SelectedLog))) RefreshCommands(); } }

        private bool HasSelection()
        {
            switch (SelectedTab)
            {
                case 0: return SelectedCategory != null;
                case 1: return SelectedTag != null;
                case 2: return SelectedNote != null;
                case 3: return SelectedWebLink != null;
                case 4: return SelectedLog != null;
                default: return false;
            }
        }

        private void AddCurrent()
        {
            var value = QuickValue.Trim();
            var tenant = Store.Tenant.IdTenant;
            var user = Store.ActingUserId;
            var now = DateTimeOffset.Now;
            switch (SelectedTab)
            {
                case 0:
                    var category = ServiceWorkspace.Require(Store.AdminService.CreateCategory(new SaveCategoryRequest
                    {
                        IdTenant = tenant, IdActingUser = user,
                        Item = new CategoryMainDataDto { IdTenant = tenant, IdUser = user, CategoryName = value }
                    }), "Create category");
                    Categories.Add(category);
                    SelectedCategory = category;
                    break;
                case 1:
                    var tag = ServiceWorkspace.Require(Store.AdminService.CreateTag(new SaveTagRequest
                    {
                        IdTenant = tenant, IdActingUser = user,
                        Item = new TagMainDataDto { IdTenant = tenant, IdUser = user, Tag = value, DateCreated = now, DateModified = now }
                    }), "Create tag");
                    Tags.Add(tag);
                    SelectedTag = tag;
                    break;
                case 2:
                    var note = ServiceWorkspace.Require(Store.AdminService.CreateNote(new SaveNoteRequest
                    {
                        IdTenant = tenant, IdActingUser = user,
                        Item = new NoteMainDataDto { IdTenant = tenant, IdUser = user, NoteText = value, DateCreated = now, DateModified = now }
                    }), "Create note");
                    Notes.Add(note);
                    SelectedNote = note;
                    break;
                case 3:
                    var webLink = ServiceWorkspace.Require(Store.AdminService.CreateWebLink(new SaveWebLinkRequest
                    {
                        IdTenant = tenant, IdActingUser = user,
                        Item = new WebLinkMainDataDto { IdTenant = tenant, IdUser = user, Title = value, Link = value, DateCreated = now, DateModified = now }
                    }), "Create web link");
                    WebLinks.Add(webLink);
                    SelectedWebLink = webLink;
                    break;
                case 4:
                    Logs.Add(value);
                    SelectedLog = value;
                    break;
                default: return;
            }
            QuickValue = "";
        }

        private void DeleteCurrent()
        {
            switch (SelectedTab)
            {
                case 0:
                    ServiceWorkspace.Require(Store.AdminService.DeleteCategory(DeleteRequest(SelectedCategory.IdCategory)), "Delete category");
                    Categories.Remove(SelectedCategory);
                    SelectedCategory = null;
                    break;
                case 1:
                    ServiceWorkspace.Require(Store.AdminService.DeleteTag(DeleteRequest(SelectedTag.IdTag)), "Delete tag");
                    Tags.Remove(SelectedTag);
                    SelectedTag = null;
                    break;
                case 2:
                    ServiceWorkspace.Require(Store.AdminService.DeleteNote(DeleteRequest(SelectedNote.IdNote)), "Delete note");
                    Notes.Remove(SelectedNote);
                    SelectedNote = null;
                    break;
                case 3:
                    ServiceWorkspace.Require(Store.AdminService.DeleteWebLink(DeleteRequest(SelectedWebLink.IdWebLink)), "Delete web link");
                    WebLinks.Remove(SelectedWebLink);
                    SelectedWebLink = null;
                    break;
                case 4:
                    Logs.Remove(SelectedLog);
                    SelectedLog = null;
                    break;
            }
        }

        private DeleteMainDataRequest DeleteRequest(Guid id) => new DeleteMainDataRequest
        {
            IdTenant = Store.Tenant.IdTenant, IdActingUser = Store.ActingUserId, IdItem = id, HardDelete = true
        };
    }
}
