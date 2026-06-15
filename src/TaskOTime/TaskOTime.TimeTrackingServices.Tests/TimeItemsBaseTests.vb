Imports System
Imports System.Collections.Generic
Imports System.Collections.Specialized
Imports System.ComponentModel
Imports System.Linq
Imports ActiveDevelop.TimeTrackingServices
Imports Microsoft.VisualStudio.TestTools.UnitTesting

Namespace TaskOTime.TimeTrackingServices.Tests
    <TestClass>
    Public Class TimeItemsBaseTests
        Private Shared ReadOnly DayStart As New DateTimeOffset(2026, 6, 15, 9, 0, 0, TimeSpan.Zero)

        <TestMethod>
        Public Sub AddWithPositionInfo_InsertsItemsInEventTimeOrderAndMaintainsLinks()
            Dim items As New TimeItemsBase(Of Guid, TimeItemBase)()
            Dim latest = CreateItem(3, DayStart.AddHours(2))
            Dim earliest = CreateItem(1, DayStart)
            Dim middle = CreateItem(2, DayStart.AddHours(1))

            Assert.AreEqual(0, items.AddWithPositionInfo(latest))
            Assert.AreEqual(0, items.AddWithPositionInfo(earliest))
            Assert.AreEqual(1, items.AddWithPositionInfo(middle))

            CollectionAssert.AreEqual(
                New Guid() {earliest.IDTimeItem, middle.IDTimeItem, latest.IDTimeItem},
                items.Select(Function(item) item.IDTimeItem).ToArray())
            AssertNoPrevious(earliest)
            AssertLinked(earliest, middle, TimeSpan.FromHours(1))
            AssertLinked(middle, latest, TimeSpan.FromHours(1))
            AssertNoNext(latest)
        End Sub

        <TestMethod>
        Public Sub AddAndEventTimeMutation_RejectDuplicateTimestamps()
            Dim items As New TimeItemsBase(Of Guid, TimeItemBase)()
            Dim first = CreateItem(1, DayStart)
            Dim second = CreateItem(2, DayStart.AddHours(1))
            items.Add(first)
            items.Add(second)

            Assert.ThrowsException(Of ArgumentException)(
                Sub() items.Add(CreateItem(3, DayStart)))

            Dim originalEventTime = second.EventTime
            Assert.ThrowsException(Of ArgumentException)(
                Sub() second.EventTime = DayStart)
            Assert.AreEqual(originalEventTime, second.EventTime)
            Assert.AreSame(second, items(1))
        End Sub

        <TestMethod>
        Public Sub RemoveAt_HeadMiddleAndTail_RelinksRemainingItems()
            Dim headItems = CreateThreeItemCollection()
            Dim headSecond = headItems(1)
            headItems.RemoveAt(0)

            Assert.AreSame(headSecond, headItems(0))
            AssertNoPrevious(headSecond)
            AssertLinked(headSecond, headItems(1), TimeSpan.FromHours(1))

            Dim middleItems = CreateThreeItemCollection()
            Dim middleFirst = middleItems(0)
            Dim middleLast = middleItems(2)
            middleItems.RemoveAt(1)

            AssertLinked(middleFirst, middleLast, TimeSpan.FromHours(2))

            Dim tailItems = CreateThreeItemCollection()
            Dim tailMiddle = tailItems(1)
            tailItems.RemoveAt(2)

            AssertNoNext(tailMiddle)
            AssertLinked(tailItems(0), tailMiddle, TimeSpan.FromHours(1))
        End Sub

        <TestMethod>
        Public Sub EventTimeMutation_ResortsAndRelinksMovedItem()
            Dim items = CreateThreeItemCollection()
            Dim first = items(0)
            Dim second = items(1)
            Dim third = items(2)

            third.EventTime = DayStart.AddHours(-1)

            CollectionAssert.AreEqual(
                New Guid() {third.IDTimeItem, first.IDTimeItem, second.IDTimeItem},
                items.Select(Function(item) item.IDTimeItem).ToArray())
            AssertNoPrevious(third)
            AssertLinked(third, first, TimeSpan.FromHours(1))
            AssertLinked(first, second, TimeSpan.FromHours(1))
            AssertNoNext(second)
        End Sub

        <TestMethod>
        Public Sub AddRemoveAndResort_RaiseCollectionAndPropertyNotifications()
            Dim items As New TimeItemsBase(Of Guid, TimeItemBase)()
            Dim collectionChanges As New List(Of NotifyCollectionChangedEventArgs)()
            Dim propertyChanges As New List(Of String)()
            AddHandler items.CollectionChanged, Sub(sender, e) collectionChanges.Add(e)
            AddHandler items.PropertyChanged, Sub(sender, e) propertyChanges.Add(e.PropertyName)

            Dim first = CreateItem(1, DayStart)
            Dim second = CreateItem(2, DayStart.AddHours(1))
            items.Add(first)
            items.Add(second)
            second.EventTime = DayStart.AddHours(-1)
            items.Remove(first)

            Assert.AreEqual(NotifyCollectionChangedAction.Add, collectionChanges(0).Action)
            Assert.AreEqual(0, collectionChanges(0).NewStartingIndex)
            Assert.IsTrue(collectionChanges.Any(Function(e) e.Action = NotifyCollectionChangedAction.Remove))
            Assert.IsTrue(collectionChanges.Any(Function(e) e.Action = NotifyCollectionChangedAction.Add AndAlso e.NewStartingIndex = 0))
            Assert.IsTrue(propertyChanges.Contains("Count"))
            Assert.IsTrue(propertyChanges.Contains("Item[]"))
        End Sub

        <TestMethod>
        Public Sub EventTimeMutationWithinSamePosition_PropagatesDurationsToNeighbors()
            Dim items = CreateThreeItemCollection()
            Dim first = items(0)
            Dim middle = items(1)
            Dim last = items(2)

            middle.EventTime = DayStart.AddMinutes(30)

            AssertLinked(first, middle, TimeSpan.FromMinutes(30))
            AssertLinked(middle, last, TimeSpan.FromMinutes(90))
        End Sub

        <TestMethod>
        Public Sub NullEventTime_SortsFirstAndRelinksWithoutDurations()
            Dim items As New TimeItemsBase(Of Guid, TimeItemBase)()
            Dim timed = CreateItem(2, DayStart)
            Dim withoutEventTime = CreateItem(1, Nothing)

            items.Add(timed)
            items.Add(withoutEventTime)

            Assert.AreSame(withoutEventTime, items(0))
            Assert.AreSame(timed, items(1))
            Assert.AreSame(timed, withoutEventTime.NextItem)
            Assert.AreSame(withoutEventTime, timed.PreviousItem)
            Assert.IsFalse(withoutEventTime.DurationToNext.HasValue)
            Assert.IsFalse(timed.DurationToPrevious.HasValue)
        End Sub

        <TestMethod>
        Public Sub TimeItemByEventTimeComparer_IgnoreOnceSkipsMatchingItemOneTime()
            Dim comparer As New TimeItemByEventTimeComparer(Of Guid, TimeItemBase)()
            Dim ignored = CreateItem(1, DayStart)
            Dim samePointInTime = CreateItem(2, DayStart)

            comparer.IgnoreOnce = ignored

            Assert.AreEqual(-1, comparer.Compare(ignored, samePointInTime))
            Assert.IsNull(comparer.IgnoreOnce)
            Assert.AreEqual(0, comparer.Compare(ignored, samePointInTime))
        End Sub

        Private Shared Function CreateThreeItemCollection() As TimeItemsBase(Of Guid, TimeItemBase)
            Dim items As New TimeItemsBase(Of Guid, TimeItemBase)()
            items.Add(CreateItem(1, DayStart))
            items.Add(CreateItem(2, DayStart.AddHours(1)))
            items.Add(CreateItem(3, DayStart.AddHours(2)))
            Return items
        End Function

        Private Shared Function CreateItem(suffix As Integer, eventTime As DateTimeOffset?) As TimeItemBase
            Return New TimeItemBase() With {
                .IDTimeItem = GuidFromSuffix(suffix),
                .EventTime = eventTime,
                .IsStartAction = False,
                .IsEndAction = False
            }
        End Function

        Private Shared Function GuidFromSuffix(suffix As Integer) As Guid
            Return New Guid($"00000000-0000-0000-0000-{suffix:000000000000}")
        End Function

        Private Shared Sub AssertLinked(previous As TimeItemBase, [next] As TimeItemBase, expectedDuration As TimeSpan)
            Assert.AreSame([next], previous.NextItem)
            Assert.AreSame(previous, [next].PreviousItem)
            Assert.AreEqual(expectedDuration, previous.DurationToNext.Value)
            Assert.AreEqual(expectedDuration, [next].DurationToPrevious.Value)
        End Sub

        Private Shared Sub AssertNoPrevious(item As TimeItemBase)
            Assert.IsNull(item.PreviousItem)
            Assert.IsFalse(item.DurationToPrevious.HasValue)
        End Sub

        Private Shared Sub AssertNoNext(item As TimeItemBase)
            Assert.IsNull(item.NextItem)
            Assert.IsFalse(item.DurationToNext.HasValue)
        End Sub
    End Class
End Namespace
