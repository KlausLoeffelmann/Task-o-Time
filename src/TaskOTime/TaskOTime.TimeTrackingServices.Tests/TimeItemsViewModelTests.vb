Imports System.Collections.Specialized
Imports System.Linq
Imports ActiveDevelop.TimeTrackingServices
Imports Microsoft.VisualStudio.TestTools.UnitTesting
Imports TaskOTime.ViewModel.ViewModels

Namespace TaskOTime.TimeTrackingServices.Tests
    <TestClass>
    Public Class TimeItemsViewModelTests
        <TestMethod>
        Public Sub DemoItems_UseProductionCollectionAndRemainInBookingOrder()
            Dim viewModel As New TimeCollectionViewModel(DateTime.Today)
            Dim timeItems = viewModel.TimeItems
            Dim collectionChanges As New List(Of NotifyCollectionChangedEventArgs)()
            AddHandler timeItems.CollectionChanged, Sub(sender, e) collectionChanges.Add(e)

            Assert.IsInstanceOfType(
                timeItems,
                GetType(TimeItemsBase(Of Guid, TimeEntryViewModel)))
            Assert.AreSame(timeItems, viewModel.SelectedDayEntries)
            CollectionAssert.AreEqual(
                New Integer() {8, 9, 12, 12, 15},
                timeItems.Select(Function(item) item.EntryTime.Hour).ToArray())

            Dim firstItem = timeItems(0)
            firstItem.EntryTime = DateTime.Today.AddHours(16)

            Assert.AreSame(firstItem, timeItems(timeItems.Count - 1))
            Assert.AreEqual(1, collectionChanges.Count)
            Assert.AreEqual(NotifyCollectionChangedAction.Move, collectionChanges(0).Action)
            Assert.AreEqual(0, collectionChanges(0).OldStartingIndex)
            Assert.AreEqual(4, collectionChanges(0).NewStartingIndex)
        End Sub
    End Class
End Namespace
