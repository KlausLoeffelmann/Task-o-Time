using System;
using System.Collections.Generic;
using System.Linq;
using TaskOTime.DTOs;

namespace TaskOTime.DataLayer
{
    public static class SystemTimeMarkerSeed
    {
        private static readonly MarkerSeed[] Markers =
        {
            new MarkerSeed(
                SystemTimeMarkerIds.WorkBreakCategoryId,
                SystemTimeMarkerIds.WorkBreakLookupKey,
                SystemTimeMarkerIds.WorkBreakCategoryName,
                "System marker used to pause active work time.",
                1,
                -20),
            new MarkerSeed(
                SystemTimeMarkerIds.StopMarkCategoryId,
                SystemTimeMarkerIds.StopMarkLookupKey,
                SystemTimeMarkerIds.StopMarkCategoryName,
                "System marker used to stop the active booking chain.",
                2,
                -10)
        };

        public static void EnsureLookupItems(TaskOTimeContext context)
        {
            if (context == null)
            {
                throw new ArgumentNullException(nameof(context));
            }

            foreach (var marker in Markers)
            {
                EnsureLookupItem(context, marker);
            }
        }

        public static IReadOnlyList<Category> EnsureCategories(TaskOTimeContext context, Guid ownerUserId, Guid? symbolId = null)
        {
            if (context == null)
            {
                throw new ArgumentNullException(nameof(context));
            }

            if (ownerUserId == Guid.Empty)
            {
                throw new ArgumentException("A marker category owner user ID is required.", nameof(ownerUserId));
            }

            EnsureLookupItems(context);

            var categories = new List<Category>();
            foreach (var marker in Markers)
            {
                categories.Add(EnsureCategory(context, marker, ownerUserId, symbolId));
            }

            return categories;
        }

        public static bool HasSeededLookupItems(TaskOTimeContext context)
        {
            if (context == null)
            {
                throw new ArgumentNullException(nameof(context));
            }

            return Markers.All(marker =>
            {
                var categoryIdText = marker.CategoryId.ToString("D");
                return context.LookupItem.Any(item =>
                    item.Id == marker.CategoryId &&
                    item.Key == marker.LookupKey &&
                    item.IdForeign == categoryIdText &&
                    item.StringValue == marker.CategoryName &&
                    item.BlobReference == "Category" &&
                    item.IntegerValue == marker.MarkerKind);
            });
        }

        public static bool HasSeededCategories(TaskOTimeContext context)
        {
            if (context == null)
            {
                throw new ArgumentNullException(nameof(context));
            }

            return Markers.All(marker =>
                context.Category.Any(category =>
                    category.IdCategory == marker.CategoryId &&
                    category.CategoryName == marker.CategoryName &&
                    category.IsPublic));
        }

        private static void EnsureLookupItem(TaskOTimeContext context, MarkerSeed marker)
        {
            var itemByKey = context.LookupItem.FirstOrDefault(item => item.Key == marker.LookupKey);
            if (itemByKey != null && itemByKey.Id != marker.CategoryId)
            {
                throw new InvalidOperationException("The lookup key '" + marker.LookupKey + "' is assigned to a different ID.");
            }

            var item = context.LookupItem.FirstOrDefault(candidate => candidate.Id == marker.CategoryId);
            if (item != null && item.Key != marker.LookupKey)
            {
                throw new InvalidOperationException("The lookup ID '" + marker.CategoryId + "' is assigned to a different key.");
            }

            if (item == null)
            {
                item = itemByKey ?? new LookupItem
                {
                    Id = marker.CategoryId
                };

                if (itemByKey == null)
                {
                    context.LookupItem.Add(item);
                }
            }

            item.Key = marker.LookupKey;
            item.IdForeign = marker.CategoryId.ToString("D");
            item.StringValue = marker.CategoryName;
            item.BlobReference = "Category";
            item.IntegerValue = marker.MarkerKind;
            item.DateValue = null;
            item.DecimalValue = null;
        }

        private static Category EnsureCategory(TaskOTimeContext context, MarkerSeed marker, Guid ownerUserId, Guid? symbolId)
        {
            var category = context.Category.FirstOrDefault(candidate => candidate.IdCategory == marker.CategoryId);
            if (category == null)
            {
                category = new Category
                {
                    IdCategory = marker.CategoryId,
                    IdUser = ownerUserId
                };
                context.Category.Add(category);
            }
            else if (category.IdUser == Guid.Empty)
            {
                category.IdUser = ownerUserId;
            }

            category.IdSymbol = symbolId ?? category.IdSymbol;
            category.CategoryName = marker.CategoryName;
            category.CategoryDescription = marker.CategoryDescription;
            category.DisplayOrder = marker.DisplayOrder;
            category.IsPublic = true;
            return category;
        }

        private sealed class MarkerSeed
        {
            public MarkerSeed(Guid categoryId, string lookupKey, string categoryName, string categoryDescription, long markerKind, int displayOrder)
            {
                CategoryId = categoryId;
                LookupKey = lookupKey;
                CategoryName = categoryName;
                CategoryDescription = categoryDescription;
                MarkerKind = markerKind;
                DisplayOrder = displayOrder;
            }

            public Guid CategoryId { get; }

            public string LookupKey { get; }

            public string CategoryName { get; }

            public string CategoryDescription { get; }

            public long MarkerKind { get; }

            public int DisplayOrder { get; }
        }
    }
}
