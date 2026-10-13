using System.Text.Json.Serialization;

namespace PubSoft.Scheduling;

/// <summary>A six-field bitmask schedule. Calendar fields use the supplied timestamp's offset.</summary>
public readonly struct ScheduleMask
{
    private const ulong FieldMask60 = (1UL << 60) - 1;
    private const uint FieldMask24 = (1U << 24) - 1;
    private const uint DayOfMonthMask = 0xFFFFFFFE;
    private const ushort MonthMask = 0x1FFE;
    private const byte DayOfWeekMask = 0x7F;

    [JsonConstructor]
    public ScheduleMask(ulong seconds, ulong minutes, uint hours, uint daysOfMonth, ushort months, byte daysOfWeek)
    {
        ValidateField(seconds, FieldMask60, nameof(seconds));
        ValidateField(minutes, FieldMask60, nameof(minutes));
        ValidateField(hours, FieldMask24, nameof(hours));
        ValidateField(daysOfMonth, DayOfMonthMask, nameof(daysOfMonth));
        ValidateField(months, MonthMask, nameof(months));
        ValidateField(daysOfWeek, DayOfWeekMask, nameof(daysOfWeek));

        Seconds = seconds;
        Minutes = minutes;
        Hours = hours;
        DaysOfMonth = daysOfMonth;
        Months = months;
        DaysOfWeek = daysOfWeek;
    }

    public ulong Seconds { get; }
    public ulong Minutes { get; }
    public uint Hours { get; }
    public uint DaysOfMonth { get; }
    public ushort Months { get; }
    public byte DaysOfWeek { get; }

    public bool IsValid => Seconds != 0 && Minutes != 0 && Hours != 0
        && DaysOfMonth != 0 && Months != 0 && DaysOfWeek != 0;

    public static ScheduleMask EverySecond { get; } = new(
        FieldMask60, FieldMask60, FieldMask24, DayOfMonthMask, MonthMask, DayOfWeekMask);

    public static ScheduleMask EveryMinute { get; } = new(
        1UL, FieldMask60, FieldMask24, DayOfMonthMask, MonthMask, DayOfWeekMask);

    public static ScheduleMask EveryHour { get; } = new(
        1UL, 1UL, FieldMask24, DayOfMonthMask, MonthMask, DayOfWeekMask);

    public static ScheduleMask EveryDay { get; } = new(
        1UL, 1UL, 1U, DayOfMonthMask, MonthMask, DayOfWeekMask);

    public bool Matches(DateTimeOffset timestamp)
    {
        if (!IsValid)
            return false;

        return (Seconds & (1UL << timestamp.Second)) != 0
            && (Minutes & (1UL << timestamp.Minute)) != 0
            && (Hours & (1U << timestamp.Hour)) != 0
            && (DaysOfMonth & (1U << timestamp.Day)) != 0
            && (Months & (1U << timestamp.Month)) != 0
            && (DaysOfWeek & (1 << (int)timestamp.DayOfWeek)) != 0;
    }

    /// <summary>Finds the next matching instant within one year without scanning every second.</summary>
    public DateTimeOffset? NextRun(DateTimeOffset from)
    {
        if (!IsValid)
            return null;

        DateTimeOffset first;
        DateTimeOffset latest;
        try
        {
            first = from.AddTicks(TimeSpan.TicksPerSecond - from.Ticks % TimeSpan.TicksPerSecond);
        }
        catch (ArgumentOutOfRangeException)
        {
            return null;
        }
        try
        {
            latest = from.AddDays(366);
        }
        catch (ArgumentOutOfRangeException)
        {
            latest = DateTimeOffset.MaxValue;
        }

        var date = first.Date;
        while (date <= latest.Date)
        {
            if (DateMatches(date))
            {
                var sameDate = date == first.Date;
                var firstHour = sameDate ? first.Hour : 0;
                for (var hour = firstHour; hour < 24; hour++)
                {
                    if ((Hours & (1U << hour)) == 0)
                        continue;

                    var sameHour = sameDate && hour == first.Hour;
                    var firstMinute = sameHour ? first.Minute : 0;
                    for (var minute = firstMinute; minute < 60; minute++)
                    {
                        if ((Minutes & (1UL << minute)) == 0)
                            continue;

                        var sameMinute = sameHour && minute == first.Minute;
                        var firstSecond = sameMinute ? first.Second : 0;
                        for (var second = firstSecond; second < 60; second++)
                        {
                            if ((Seconds & (1UL << second)) == 0)
                                continue;

                            var candidate = new DateTimeOffset(
                                date.Year, date.Month, date.Day, hour, minute, second, from.Offset);
                            if (candidate >= first && candidate <= latest)
                                return candidate;
                        }
                    }
                }
            }

            if (date == latest.Date)
                break;
            date = date.AddDays(1);
        }

        return null;
    }

    private bool DateMatches(DateTime date)
        => (Months & (1 << date.Month)) != 0
            && (DaysOfMonth & (1U << date.Day)) != 0
            && (DaysOfWeek & (1 << (int)date.DayOfWeek)) != 0;

    private static void ValidateField(ulong value, ulong allowed, string name)
    {
        if (value == 0 || (value & ~allowed) != 0)
            throw new ArgumentOutOfRangeException(name, "Schedule fields must select valid calendar values.");
    }

    private static void ValidateField(uint value, uint allowed, string name)
    {
        if (value == 0 || (value & ~allowed) != 0)
            throw new ArgumentOutOfRangeException(name, "Schedule fields must select valid calendar values.");
    }

    private static void ValidateField(ushort value, ushort allowed, string name)
    {
        if (value == 0 || (value & ~allowed) != 0)
            throw new ArgumentOutOfRangeException(name, "Schedule fields must select valid calendar values.");
    }

    private static void ValidateField(byte value, byte allowed, string name)
    {
        if (value == 0 || (value & ~allowed) != 0)
            throw new ArgumentOutOfRangeException(name, "Schedule fields must select valid calendar values.");
    }
}

public static class ScheduleMasks
{
    public static ScheduleMask EverySeconds(int interval)
        => new(BuildInterval(60, interval), (1UL << 60) - 1, (1U << 24) - 1, 0xFFFFFFFE, 0x1FFE, 0x7F);

    public static ScheduleMask EveryMinutes(int interval)
        => new(1UL, BuildInterval(60, interval), (1U << 24) - 1, 0xFFFFFFFE, 0x1FFE, 0x7F);

    public static ScheduleMask EveryHours(int interval)
        => new(1UL, 1UL, BuildIntervalUInt(24, interval), 0xFFFFFFFE, 0x1FFE, 0x7F);

    public static ScheduleMask DailyAt(int hour, int minute = 0, int second = 0)
    {
        ValidateTime(hour, minute, second);
        return new ScheduleMask(1UL << second, 1UL << minute, 1U << hour, 0xFFFFFFFE, 0x1FFE, 0x7F);
    }

    public static ScheduleMask WeeklyAt(DayOfWeek day, int hour, int minute = 0, int second = 0)
    {
        ValidateTime(hour, minute, second);
        if (!Enum.IsDefined(day))
            throw new ArgumentOutOfRangeException(nameof(day));
        return new ScheduleMask(1UL << second, 1UL << minute, 1U << hour,
            0xFFFFFFFE, 0x1FFE, (byte)(1 << (int)day));
    }

    private static ulong BuildInterval(int maximum, int interval)
    {
        if (interval is < 1 || interval > maximum)
            throw new ArgumentOutOfRangeException(nameof(interval));

        ulong mask = 0;
        for (var value = 0; value < maximum; value += interval)
            mask |= 1UL << value;
        return mask;
    }

    private static uint BuildIntervalUInt(int maximum, int interval)
    {
        if (interval is < 1 || interval > maximum)
            throw new ArgumentOutOfRangeException(nameof(interval));

        uint mask = 0;
        for (var value = 0; value < maximum; value += interval)
            mask |= 1U << value;
        return mask;
    }

    private static void ValidateTime(int hour, int minute, int second)
    {
        if (hour is < 0 or > 23) throw new ArgumentOutOfRangeException(nameof(hour));
        if (minute is < 0 or > 59) throw new ArgumentOutOfRangeException(nameof(minute));
        if (second is < 0 or > 59) throw new ArgumentOutOfRangeException(nameof(second));
    }
}