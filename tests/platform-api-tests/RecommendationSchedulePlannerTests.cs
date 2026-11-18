using Xunit;
using TravelOS.PlatformApi.Recommendations;

namespace TravelOS.PlatformApi.Tests;

public sealed class RecommendationSchedulePlannerTests
{
    [Fact]
    public void Build_FindsSlotsBeforeBetweenAndAfterActivities()
    {
        var activities = new[]
        {
            new RecommendationScheduleActivity("Museum", new TimeOnly(10, 0), new TimeOnly(11, 0)),
            new RecommendationScheduleActivity("Lunch", new TimeOnly(13, 0), new TimeOnly(14, 0))
        };

        var result = RecommendationSchedulePlanner.Build(
            activities, new TimeOnly(9, 0), new TimeOnly(17, 0), 45, 15);

        Assert.Equal(3, result.Slots.Count);
        Assert.Equal(("09:00", "09:45"), (result.Slots[0].StartTime, result.Slots[0].EndTime));
        Assert.Equal(("11:15", "12:00"), (result.Slots[1].StartTime, result.Slots[1].EndTime));
        Assert.Equal(("14:15", "15:00"), (result.Slots[2].StartTime, result.Slots[2].EndTime));
        Assert.Empty(result.Conflicts);
    }

    [Fact]
    public void Build_ReturnsNoSlotWhenDurationDoesNotFit()
    {
        var activities = new[]
        {
            new RecommendationScheduleActivity("Tour", new TimeOnly(9, 30), new TimeOnly(16, 30))
        };

        var result = RecommendationSchedulePlanner.Build(
            activities, new TimeOnly(9, 0), new TimeOnly(17, 0), 120, 30);

        Assert.Empty(result.Slots);
    }

    [Fact]
    public void Build_ReportsOverlappingActivitiesAndWarning()
    {
        var activities = new[]
        {
            new RecommendationScheduleActivity("Museum", new TimeOnly(10, 0), new TimeOnly(12, 0)),
            new RecommendationScheduleActivity("Gallery", new TimeOnly(11, 30), new TimeOnly(13, 0))
        };

        var result = RecommendationSchedulePlanner.Build(
            activities, new TimeOnly(9, 0), new TimeOnly(17, 0), 30, 15);

        var conflict = Assert.Single(result.Conflicts);
        Assert.Equal("Museum", conflict.FirstActivity);
        Assert.Equal("Gallery", conflict.SecondActivity);
        Assert.Equal(30, conflict.OverlapMinutes);
        Assert.Contains(result.Warnings, warning => warning.Contains("overlapping", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Build_WarnsWhenActivitiesHaveMissingTimes()
    {
        var activities = new[]
        {
            new RecommendationScheduleActivity("Untimed stop", null, null)
        };

        var result = RecommendationSchedulePlanner.Build(
            activities, new TimeOnly(9, 0), new TimeOnly(17, 0), 60, 15);

        Assert.Single(result.Slots);
        Assert.Contains(result.Warnings, warning => warning.Contains("no time", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Build_SortsActivitiesBeforeCalculatingGaps()
    {
        var activities = new[]
        {
            new RecommendationScheduleActivity("Later", new TimeOnly(13, 0), new TimeOnly(14, 0)),
            new RecommendationScheduleActivity("Earlier", new TimeOnly(10, 0), new TimeOnly(11, 0))
        };

        var result = RecommendationSchedulePlanner.Build(
            activities, new TimeOnly(9, 0), new TimeOnly(17, 0), 30, 15);

        Assert.Equal("Earlier", result.Slots[0].NextActivityTitle);
        Assert.Equal("11:15", result.Slots[1].StartTime);
    }

    [Fact]
    public void Build_RejectsInvalidPlanningWindow()
    {
        Assert.Throws<ArgumentException>(() => RecommendationSchedulePlanner.Build(
            [], new TimeOnly(17, 0), new TimeOnly(9, 0), 30, 0));
    }

    [Theory]
    [InlineData(14)]
    [InlineData(721)]
    public void Build_RejectsDurationOutsideSupportedRange(int duration)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => RecommendationSchedulePlanner.Build(
            [], new TimeOnly(9, 0), new TimeOnly(17, 0), duration, 0));
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(241)]
    public void Build_RejectsBufferOutsideSupportedRange(int buffer)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => RecommendationSchedulePlanner.Build(
            [], new TimeOnly(9, 0), new TimeOnly(17, 0), 30, buffer));
    }
}
