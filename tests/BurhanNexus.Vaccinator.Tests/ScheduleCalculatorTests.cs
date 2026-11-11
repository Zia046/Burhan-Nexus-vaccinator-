using BurhanNexus.Vaccinator.Domain;

[assembly: CollectionBehavior(DisableTestParallelization = true)]

namespace BurhanNexus.Vaccinator.Tests;

public class ScheduleCalculatorTests
{
    private static DateOnly D(int d, int m, int y) => new(y, m, d);

    [Fact]
    public void HepatitisB_0_1_6_months_matches_the_prompt_example()
    {
        var doses = new[]
        {
            new DoseDefinition(1, "Dose 1", 0, IntervalUnit.Months),
            new DoseDefinition(2, "Dose 2", 1, IntervalUnit.Months, 4, IntervalUnit.Weeks),
            new DoseDefinition(3, "Dose 3", 6, IntervalUnit.Months, 8, IntervalUnit.Weeks)
        };
        var r = ScheduleCalculator.Calculate(doses, D(11, 11, 2026));
        Assert.Equal(D(11, 11, 2026), r[0].CalculatedDate);
        Assert.Equal(D(11, 12, 2026), r[1].CalculatedDate);
        Assert.Equal(D(11, 5, 2027), r[2].CalculatedDate);
    }

    [Fact]
    public void Rabies_IM_0_3_7_14_matches_the_prompt_example()
    {
        var doses = new[] { 0, 3, 7, 14 }.Select((d, i) => new DoseDefinition(i + 1, "Day " + d, d, IntervalUnit.Days));
        var r = ScheduleCalculator.Calculate(doses, D(11, 11, 2026));
        Assert.Equal(new[] { D(11, 11, 2026), D(14, 11, 2026), D(18, 11, 2026), D(25, 11, 2026) }, r.Select(x => x.CalculatedDate));
    }

    [Fact]
    public void Months_are_calendar_months_not_30_days()
    {
        var doses = new[] { new DoseDefinition(1, "1", 0, IntervalUnit.Months), new DoseDefinition(2, "2", 1, IntervalUnit.Months) };
        Assert.Equal(D(28, 2, 2027), ScheduleCalculator.Calculate(doses, D(31, 1, 2027))[1].CalculatedDate);
        Assert.Equal(D(29, 2, 2028), ScheduleCalculator.Calculate(doses, D(31, 1, 2028))[1].CalculatedDate); // leap year
        Assert.Equal(D(15, 3, 2027), ScheduleCalculator.Calculate(doses, D(15, 2, 2027))[1].CalculatedDate);
    }

    [Fact]
    public void Delayed_dose_pushes_following_doses_but_keeps_given_dates()
    {
        var doses = new[] { 0, 3, 7, 14 }.Select((d, i) => new DoseDefinition(i + 1, "Day " + d, d, IntervalUnit.Days));
        var given = new Dictionary<int, DateOnly> { [1] = D(1, 3, 2027), [2] = D(8, 3, 2027) }; // day 3 dose given 4 days late
        var r = ScheduleCalculator.Calculate(doses, D(1, 3, 2027), given, reviewIfLateByDays: 3);
        Assert.Equal(D(1, 3, 2027), r[0].AdministeredDate);
        Assert.Equal(D(8, 3, 2027), r[1].AdministeredDate);
        Assert.Equal(D(12, 3, 2027), r[2].CalculatedDate);  // 4 days after the late dose (interval 7-3)
        Assert.Equal(D(19, 3, 2027), r[3].CalculatedDate);  // 7 days after that
        Assert.True(r[1].LateEnoughForReview);
    }

    [Fact]
    public void Old_first_dose_only_schedules_from_its_real_date()
    {
        var doses = new[] { new DoseDefinition(1, "1", 0, IntervalUnit.Months), new DoseDefinition(2, "2", 1, IntervalUnit.Months), new DoseDefinition(3, "3", 6, IntervalUnit.Months) };
        var given = new Dictionary<int, DateOnly> { [1] = D(10, 1, 2026) };
        var r = ScheduleCalculator.Calculate(doses, D(10, 1, 2026), given);
        Assert.Equal(D(10, 2, 2026), r[1].CalculatedDate);
        Assert.Equal(D(10, 7, 2026), r[2].CalculatedDate);
    }

    [Fact]
    public void Interval_only_doses_chain_from_previous_dose()
    {
        var doses = new[]
        {
            new DoseDefinition(1, "Td-1", 0, IntervalUnit.Days),
            new DoseDefinition(2, "Td-2", 0, IntervalUnit.Days, 4, IntervalUnit.Weeks),
            new DoseDefinition(3, "Td-3", 0, IntervalUnit.Days, 6, IntervalUnit.Months),
            new DoseDefinition(4, "Td-4", 0, IntervalUnit.Days, 1, IntervalUnit.Years)
        };
        var r = ScheduleCalculator.Calculate(doses, D(1, 1, 2027));
        Assert.Equal(D(29, 1, 2027), r[1].CalculatedDate);
        Assert.Equal(D(29, 7, 2027), r[2].CalculatedDate);
        Assert.Equal(D(29, 7, 2028), r[3].CalculatedDate);
    }

    [Fact]
    public void Rescheduled_later_date_moves_following_doses()
    {
        var doses = new[] { 0, 3, 7 }.Select((d, i) => new DoseDefinition(i + 1, "Day " + d, d, IntervalUnit.Days));
        var moved = new Dictionary<int, DateOnly> { [2] = D(6, 3, 2027) };
        var r = ScheduleCalculator.Calculate(doses, D(1, 3, 2027), new Dictionary<int, DateOnly> { [1] = D(1, 3, 2027) }, null, moved);
        Assert.Equal(D(4, 3, 2027), r[1].CalculatedDate);
        Assert.Equal(D(10, 3, 2027), r[2].CalculatedDate);
    }

    [Fact]
    public void Age_is_reported_in_days_weeks_and_months()
    {
        var a = DateMath.Age(D(11, 11, 2026), D(23, 12, 2026));
        Assert.Equal(42, a.Days);
        Assert.Equal(6, a.Weeks);
        Assert.Equal(1, a.Months);
    }

    [Theory]
    [InlineData("Dr. Ali Khan", "ali khan")]
    [InlineData("  ALI   khan ", "ali khan")]
    [InlineData("Doctor Ali Khan", "ali khan")]
    [InlineData("Dr", "dr")]
    public void Doctor_names_are_normalised_to_prevent_duplicates(string input, string expected) =>
        Assert.Equal(expected, Doctor.Normalize(input));

    [Fact]
    public void Expired_quarantined_and_vvm_discard_batches_are_not_usable()
    {
        var today = D(1, 6, 2027);
        Assert.NotNull(new VaccineBatch { ExpiryDate = D(31, 5, 2027), QuantityAvailable = 5 }.WhyNotUsable(today));
        Assert.NotNull(new VaccineBatch { ExpiryDate = D(1, 6, 2028), QuantityAvailable = 5, IsQuarantined = true }.WhyNotUsable(today));
        Assert.NotNull(new VaccineBatch { ExpiryDate = D(1, 6, 2028), QuantityAvailable = 5, Vvm = VvmStatus.Stage3 }.WhyNotUsable(today));
        Assert.NotNull(new VaccineBatch { ExpiryDate = D(1, 6, 2028), QuantityAvailable = 0 }.WhyNotUsable(today));
        Assert.Null(new VaccineBatch { ExpiryDate = D(1, 6, 2027), QuantityAvailable = 5, Vvm = VvmStatus.Stage2 }.WhyNotUsable(today));
    }

    [Fact]
    public void Catalog_has_required_vaccines_with_references_and_no_preapproval()
    {
        var list = VaccineCatalog.Build();
        foreach (var code in new[] { "BCG", "OPV", "IPV", "PENTA", "PCV", "ROTA", "MEASLES", "MR", "TCV", "HEPB", "HEPA", "TD", "HPV", "FLU", "MMR", "VAR", "MEN", "RABIES" })
            Assert.Contains(list, v => v.Code == code);
        Assert.All(list, v => Assert.False(string.IsNullOrWhiteSpace(v.Reference)));
        Assert.All(list.SelectMany(v => v.Protocols), p => { Assert.False(p.ApprovedByCenter); Assert.False(string.IsNullOrWhiteSpace(p.Reference)); Assert.NotEmpty(p.Doses); });
        Assert.True(list.Single(v => v.Code == "HEPB").Protocols.Count >= 2, "Hepatitis B needs separate newborn and adult protocols");
        Assert.True(list.Single(v => v.Code == "RABIES").Protocols.Count >= 6);
    }
}
