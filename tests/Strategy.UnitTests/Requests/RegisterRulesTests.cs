using Strategy.Modules.DocumentRegistry.Domain;
using Strategy.Modules.IpRegistry.Domain;

namespace Strategy.UnitTests.Requests;

/// <summary>Document validity (ADR 0084), version numbers (ADR 0082) and the compare diff.</summary>
public class DocumentRulesTests
{
    [Theory]
    [InlineData("2026-01-31", 24, "2028-01-31")]
    [InlineData("2026-03-31", 1, "2026-04-30")]
    [InlineData("2026-01-01", null, null)]
    public void Expiry_from_the_type_rule(string effective, int? months, string? expected) =>
        ValidityCalculator.ExpiryFrom(DateOnly.Parse(effective, System.Globalization.CultureInfo.InvariantCulture), months)
            .ShouldBe(expected is null ? null : DateOnly.Parse(expected, System.Globalization.CultureInfo.InvariantCulture));

    [Fact]
    public void Confirmation_extends_from_the_current_expiry_or_from_today_when_expired()
    {
        var today = new DateOnly(2026, 6, 1);
        ValidityCalculator.Extend(new DateOnly(2026, 7, 1), today, 24).ShouldBe(new DateOnly(2028, 7, 1));
        ValidityCalculator.Extend(new DateOnly(2026, 5, 1), today, 24).ShouldBe(new DateOnly(2028, 6, 1));
        ValidityCalculator.Extend(new DateOnly(2026, 7, 1), today, null).ShouldBeNull();
    }

    [Fact]
    public void Reminders_are_due_once_per_count()
    {
        var expiry = new DateOnly(2026, 12, 31);
        ValidityCalculator.DueReminders(expiry, expiry.AddDays(-61), [60, 30], []).ShouldBeEmpty();
        ValidityCalculator.DueReminders(expiry, expiry.AddDays(-60), [60, 30], []).ShouldBe([60]);
        ValidityCalculator.DueReminders(expiry, expiry.AddDays(-45), [60, 30], [60]).ShouldBeEmpty();
        ValidityCalculator.DueReminders(expiry, expiry.AddDays(-10), [60, 30], []).ShouldBe([60, 30]);
        ValidityCalculator.DueReminders(expiry, expiry.AddDays(1), [60, 30], []).ShouldBeEmpty();
        ValidityCalculator.IsExpired(expiry, expiry).ShouldBeTrue();
        ValidityCalculator.IsExpired(expiry, expiry.AddDays(-1)).ShouldBeFalse();
        ValidityCalculator.IsExpired(null, expiry).ShouldBeFalse();
    }

    [Fact]
    public void Version_numbers()
    {
        VersionNumbers.Next([], correction: false).ShouldBe((1, 0));
        VersionNumbers.Next([(1, 0)], correction: false).ShouldBe((2, 0));
        VersionNumbers.Next([(1, 0), (2, 0)], correction: true).ShouldBe((2, 1));
        VersionNumbers.Next([(0, 0)], correction: false).ShouldBe((1, 0));
        VersionNumbers.Label(2, 1).ShouldBe("2.1");
    }

    [Fact]
    public void Diff_marks_added_and_removed_words()
    {
        var parts = TextDiff.Compare("travel limit is 5000 per trip", "travel limit is 7000 per trip");
        parts.ShouldBe([new DiffPart("same", "travel limit is"), new DiffPart("removed", "5000"), new DiffPart("added", "7000"), new DiffPart("same", "per trip")]);
        TextDiff.Count(parts).ShouldBe((1, 1));
        TextDiff.Compare(null, "new text").ShouldBe([new DiffPart("added", "new text")]);
    }
}

/// <summary>IP rules (ADR 0086–0087).</summary>
public class IpRulesTests
{
    [Fact]
    public void Shares_must_total_100()
    {
        IpRules.SharesTotal100([60m, 40m]).ShouldBeTrue();
        IpRules.SharesTotal100([33.33m, 33.33m, 33.34m]).ShouldBeTrue();
        IpRules.SharesTotal100([60m, 30m]).ShouldBeFalse();
    }

    [Fact]
    public void Expiry_proposal_and_reminders()
    {
        IpRules.ProposedExpiry(new DateOnly(2026, 3, 1), 20).ShouldBe(new DateOnly(2046, 3, 1));
        IpRules.ProposedExpiry(new DateOnly(2026, 3, 1), null).ShouldBeNull();
        var expiry = new DateOnly(2027, 1, 1);
        IpRules.DueReminders(expiry, new DateOnly(2026, 6, 30), [6, 3], []).ShouldBeEmpty();
        IpRules.DueReminders(expiry, new DateOnly(2026, 7, 1), [6, 3], []).ShouldBe([6]);
        IpRules.DueReminders(expiry, new DateOnly(2026, 11, 1), [6, 3], [6]).ShouldBe([3]);
        IpRules.DueReminders(expiry, expiry, [6, 3], []).ShouldBeEmpty();
    }

    [Fact]
    public void Terms_count_from_registration_or_first_publication()
    {
        var registered = new DateOnly(2026, 3, 1);
        var published = new DateOnly(2025, 6, 15);
        IpRules.ProposedExpiry(registered, published, 50, "publication").ShouldBe(new DateOnly(2075, 6, 15)); // copyright
        IpRules.ProposedExpiry(registered, null, 50, "publication").ShouldBeNull(); // the office gives the date or an expiry
        IpRules.ProposedExpiry(registered, published, 10, "registration").ShouldBe(new DateOnly(2036, 3, 1)); // utility model, trademark
        IpRules.ProposedExpiry(registered, null, 20, "registration").ShouldBe(new DateOnly(2046, 3, 1)); // patent, industrial design
        IpRules.ProposedExpiry(registered, published, null, "registration").ShouldBeNull(); // trade secret: no expiry
    }

    [Fact]
    public void Seeded_protection_terms()
    {
        var rules = new Strategy.Modules.IpRegistry.Application.IpSettings().Protections.ToDictionary(p => p.Code);
        (rules["copyright"].TermYears, rules["copyright"].TermFrom).ShouldBe(((int?)50, "publication"));
        (rules["patent"].TermYears, rules["industrial-design"].TermYears, rules["utility-model"].TermYears, rules["trademark"].TermYears)
            .ShouldBe(((int?)20, (int?)20, (int?)10, (int?)10));
        (rules["trade-secret"].TermYears, rules["trade-secret"].InternalOnly).ShouldBe(((int?)null, true));
        Strategy.Modules.IpRegistry.Application.IpSettings.Validate(new Strategy.Modules.IpRegistry.Application.IpSettings()).ShouldBeEmpty();
    }

    [Fact]
    public void Emirates_id_mask_keeps_only_the_last_digit()
    {
        IpRules.MaskEmiratesId("784-1990-1234567-1").ShouldBe("784-****-*******-1");
        IpRules.MaskEmiratesId(null).ShouldBeEmpty();
    }
}
