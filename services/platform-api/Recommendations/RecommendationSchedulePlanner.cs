namespace TravelOS.PlatformApi.Recommendations;

public sealed record RecommendationScheduleActivity(string Title, TimeOnly? StartTime, TimeOnly? EndTime);
public sealed record RecommendationScheduleSlot(
    string StartTime,
    string EndTime,
    int DurationMinutes,
    string? PreviousActivityTitle,
    string? NextActivityTitle,
    string Explanation);
public sealed record RecommendationScheduleConflict(
    string FirstActivity,
    string SecondActivity,
    int OverlapMinutes);
public sealed record RecommendationScheduleResult(
    IReadOnlyList<RecommendationScheduleSlot> Slots,
    IReadOnlyList<RecommendationScheduleConflict> Conflicts,
    IReadOnlyList<string> Warnings);

public static class RecommendationSchedulePlanner
{
    public static RecommendationScheduleResult Build(
        IEnumerable<RecommendationScheduleActivity> activities,
        TimeOnly dayStart,
        TimeOnly dayEnd,
        int durationMinutes,
        int bufferMinutes)
    {
        if (dayEnd <= dayStart)
            throw new ArgumentException("The planning window end must be after its start.", nameof(dayEnd));
        if (durationMinutes is < 15 or > 720)
            throw new ArgumentOutOfRangeException(nameof(durationMinutes));
        if (bufferMinutes is < 0 or > 240)
            throw new ArgumentOutOfRangeException(nameof(bufferMinutes));

        var allActivities = activities.ToArray();
        var timed = allActivities
            .Where(x => x.StartTime.HasValue && x.EndTime.HasValue)
            .OrderBy(x => x.StartTime)
            .ThenBy(x => x.EndTime)
            .ToArray();

        var conflicts = new List<RecommendationScheduleConflict>();
        for (var i = 0; i < timed.Length; i++)
        {
            for (var j = 0; j < i; j++)
            {
                if (timed[j].EndTime!.Value > timed[i].StartTime!.Value)
                {
                    conflicts.Add(new RecommendationScheduleConflict(
                        timed[j].Title,
                        timed[i].Title,
                        (int)(timed[j].EndTime!.Value - timed[i].StartTime!.Value).TotalMinutes));
                }
            }
        }

        var slots = new List<RecommendationScheduleSlot>();
        var cursor = dayStart;
        string? previousTitle = null;
        foreach (var activity in timed)
        {
            var activityStart = activity.StartTime!.Value;
            var activityEnd = activity.EndTime!.Value;
            if (activityStart > cursor)
            {
                var earliest = previousTitle is null ? cursor : cursor.AddMinutes(bufferMinutes);
                var latestEnd = activityStart.AddMinutes(-bufferMinutes);
                if (earliest.AddMinutes(durationMinutes) <= latestEnd)
                {
                    slots.Add(new RecommendationScheduleSlot(
                        earliest.ToString("HH:mm"),
                        earliest.AddMinutes(durationMinutes).ToString("HH:mm"),
                        durationMinutes,
                        previousTitle,
                        activity.Title,
                        previousTitle is null
                            ? $"Fits before {activity.Title}, leaving the selected {bufferMinutes}-minute buffer."
                            : $"Fits between {previousTitle} and {activity.Title}, with the selected {bufferMinutes}-minute buffers."));
                }
            }

            if (activityEnd > cursor) cursor = activityEnd;
            previousTitle = activity.Title;
        }

        if (cursor < dayEnd)
        {
            var earliest = previousTitle is null ? cursor : cursor.AddMinutes(bufferMinutes);
            if (earliest.AddMinutes(durationMinutes) <= dayEnd)
            {
                slots.Add(new RecommendationScheduleSlot(
                    earliest.ToString("HH:mm"),
                    earliest.AddMinutes(durationMinutes).ToString("HH:mm"),
                    durationMinutes,
                    previousTitle,
                    null,
                    previousTitle is null
                        ? "Fits inside the planning window you selected."
                        : $"Fits after {previousTitle}, with the selected {bufferMinutes}-minute buffer."));
            }
        }

        var warnings = new List<string>();
        if (allActivities.Any(x => !x.StartTime.HasValue || !x.EndTime.HasValue))
            warnings.Add("Some saved activities have no time and are not included in gap calculations.");
        if (conflicts.Count > 0)
            warnings.Add("The saved itinerary already contains overlapping activities. Resolve those conflicts before relying on the proposed schedule.");

        return new RecommendationScheduleResult(slots, conflicts, warnings);
    }
}
