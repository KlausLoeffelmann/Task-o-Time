using System;

namespace TaskOTime.DTOs
{
    public static class SystemTimeMarkerIds
    {
        public static readonly Guid StopMarkCategoryId = new Guid("d6dc8c0d-1ffb-4529-9b7d-e1622da4bef8");

        public static readonly Guid WorkBreakCategoryId = new Guid("058aecda-fd30-413d-9013-49d8da6cce37");

        public const string StopMarkLookupKey = "SystemTimeMarker.Category.StopMark";

        public const string WorkBreakLookupKey = "SystemTimeMarker.Category.WorkBreak";

        public const string StopMarkCategoryName = "Stop mark";

        public const string WorkBreakCategoryName = "Work break";
    }
}
