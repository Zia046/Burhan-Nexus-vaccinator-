namespace BurhanNexus.Vaccinator.Domain;

/// <summary>Calendar arithmetic. Months and years use real calendar months (DateOnly.AddMonths handles month length and leap years).</summary>
public static class DateMath
{
    public static DateOnly Add(DateOnly date, int value, IntervalUnit unit) => unit switch
    {
        IntervalUnit.Days => date.AddDays(value),
        IntervalUnit.Weeks => date.AddDays(value * 7),
        IntervalUnit.Months => date.AddMonths(value),
        IntervalUnit.Years => date.AddYears(value),
        _ => throw new ArgumentOutOfRangeException(nameof(unit))
    };

    public static string Describe(int value, IntervalUnit unit)
    {
        var name = unit switch
        {
            IntervalUnit.Days => value == 1 ? "day" : "days",
            IntervalUnit.Weeks => value == 1 ? "week" : "weeks",
            IntervalUnit.Months => value == 1 ? "month" : "months",
            _ => value == 1 ? "year" : "years"
        };
        return value == 0 ? (unit == IntervalUnit.Days ? "Day 0" : "At start") : $"{value} {name}";
    }

    /// <summary>Exact age: total days, weeks and calendar months, plus a readable text.</summary>
    public static (int Days, int Weeks, int Months, string Text) Age(DateOnly dob, DateOnly today)
    {
        if (today < dob) return (0, 0, 0, "Not born yet");
        var days = today.DayNumber - dob.DayNumber;
        var months = (today.Year - dob.Year) * 12 + today.Month - dob.Month;
        if (dob.AddMonths(months) > today) months--;
        var years = months / 12;
        string text;
        if (days < 28) text = $"{days} day(s)";
        else if (months < 24)
        {
            var rest = today.DayNumber - dob.AddMonths(months).DayNumber;
            text = months == 0 ? $"{days / 7} week(s) {days % 7} day(s)" : $"{months} month(s) {rest} day(s)";
        }
        else text = $"{years} year(s) {months % 12} month(s)";
        return (days, days / 7, months, text);
    }
}

public sealed record DoseDefinition(
    int DoseNumber,
    string Label,
    int OffsetValue,
    IntervalUnit OffsetUnit,
    int? MinIntervalValue = null,
    IntervalUnit? MinIntervalUnit = null);

public sealed record CalculatedDose(int DoseNumber, string Label, DateOnly CalculatedDate, DateOnly? AdministeredDate, bool LateEnoughForReview);

/// <summary>
/// Generates dose dates for a protocol.
/// Rules: the anchor (date of birth or the first administered dose) fixes the plan; each later dose is due at
/// anchor + offset, but never sooner than the minimum interval after the previous dose's real (or planned) date.
/// So a delayed dose pushes the following doses forward without restarting the course, and dates of doses
/// already given are never changed.
/// </summary>
public static class ScheduleCalculator
{
    public static IReadOnlyList<CalculatedDose> Calculate(
        IEnumerable<DoseDefinition> doses,
        DateOnly anchor,
        IReadOnlyDictionary<int, DateOnly>? administered = null,
        int? reviewIfLateByDays = null,
        IReadOnlyDictionary<int, DateOnly>? rescheduled = null)
    {
        administered ??= new Dictionary<int, DateOnly>();
        rescheduled ??= new Dictionary<int, DateOnly>();
        var ordered = doses.OrderBy(d => d.DoseNumber).ToList();
        var result = new List<CalculatedDose>(ordered.Count);

        DateOnly? previousEffective = null;
        DoseDefinition? previous = null;

        foreach (var dose in ordered)
        {
            var planned = DateMath.Add(anchor, dose.OffsetValue, dose.OffsetUnit);

            if (previous is not null && previousEffective is not null)
            {
                DateOnly earliest;
                if (dose.MinIntervalValue is int minValue && dose.MinIntervalUnit is IntervalUnit minUnit)
                    earliest = DateMath.Add(previousEffective.Value, minValue, minUnit);
                else if (dose.OffsetUnit == previous.OffsetUnit)
                    earliest = DateMath.Add(previousEffective.Value, dose.OffsetValue - previous.OffsetValue, dose.OffsetUnit);
                else
                {
                    var previousPlanned = DateMath.Add(anchor, previous.OffsetValue, previous.OffsetUnit);
                    earliest = previousEffective.Value.AddDays(planned.DayNumber - previousPlanned.DayNumber);
                }

                if (earliest > planned) planned = earliest;
            }

            DateOnly? given = administered.TryGetValue(dose.DoseNumber, out var g) ? g : null;
            var late = given is not null && reviewIfLateByDays is int limit && given.Value.DayNumber - planned.DayNumber > limit;

            result.Add(new CalculatedDose(dose.DoseNumber, dose.Label, planned, given, late));
            // A dose moved to a later date by the user moves the following doses with it.
            var expected = rescheduled.TryGetValue(dose.DoseNumber, out var r) && r > planned ? r : planned;
            previousEffective = given ?? expected;
            previous = dose;
        }

        return result;
    }

    public static DoseDefinition ToDefinition(ProtocolDose d) =>
        new(d.DoseNumber, d.Label, d.OffsetValue, d.OffsetUnit, d.MinIntervalValue, d.MinIntervalUnit);
}
