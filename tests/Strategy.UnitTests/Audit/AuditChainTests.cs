using System.Text;
using Strategy.Platform.Audit.Application;
using Strategy.Platform.Audit.Domain;
using Strategy.Platform.Persistence;

namespace Strategy.UnitTests.Audit;

public class AuditChainTests
{
    private static List<AuditEntry> Chain(int count)
    {
        var entries = new List<AuditEntry>();
        var prev = AuditCanonical.Genesis;
        for (var i = 1; i <= count; i++)
        {
            var entry = new AuditEntry
            {
                Seq = i,
                Id = Guid.CreateVersion7(),
                OccurredAt = new DateTimeOffset(2026, 11, 1, 10, 0, i, TimeSpan.Zero).AddTicks(1234567),
                Kind = "change",
                Action = "update",
                SystemCode = "sample",
                EntityType = "sample.item",
                RecordId = Guid.Empty.ToString("D"),
                Field = "NameEn",
                OldValue = $"old {i}",
                NewValue = $"جديد {i}",
                Channel = "ui",
                UserName = "mariam",
            };
            AuditCanonical.Seal(entry, prev);
            prev = entry.Hash;
            entries.Add(entry);
        }

        return entries;
    }

    [Fact]
    public void Canonical_form_is_stable_compact_utf8_with_microsecond_utc_time()
    {
        var entry = Chain(1)[0];
        var json = Encoding.UTF8.GetString(AuditCanonical.Serialize(entry));

        json.ShouldStartWith("{\"seq\":1,\"id\":\"");
        json.ShouldContain("\"occurredAt\":\"2026-11-01T10:00:01.123456Z\"");
        json.ShouldContain("\"parentType\":null");
        json.ShouldNotContain(" \"");
        AuditCanonical.Serialize(entry).ShouldBe(AuditCanonical.Serialize(entry));
        entry.Hash.Length.ShouldBe(32);
    }

    [Fact]
    public void An_intact_chain_verifies()
    {
        var result = AuditVerifier.Check(Chain(5), 0, AuditCanonical.Genesis);

        result.Ok.ShouldBeTrue();
        result.Checked.ShouldBe(5);
        result.LastSeq.ShouldBe(5);
    }

    [Theory]
    [InlineData("value", 3, VerificationMessages.HashMismatch)]
    [InlineData("user", 2, VerificationMessages.HashMismatch)]
    [InlineData("time", 4, VerificationMessages.HashMismatch)]
    [InlineData("delete", 3, VerificationMessages.MissingEntry)]
    [InlineData("reorder", 2, VerificationMessages.MissingEntry)]
    [InlineData("rehash", 4, VerificationMessages.LinkBroken)]
    public void Tampering_is_found_at_the_first_affected_entry(string tampering, long expectedSeq, string expectedMessage)
    {
        var chain = Chain(5);
        switch (tampering)
        {
            case "value":
                chain[2].NewValue = "forged";
                break;
            case "user":
                chain[1].UserName = "someone else";
                break;
            case "time":
                chain[3].OccurredAt = chain[3].OccurredAt.AddSeconds(-1);
                break;
            case "delete":
                chain.RemoveAt(2);
                break;
            case "reorder":
                (chain[1], chain[2]) = (chain[2], chain[1]);
                break;
            case "rehash":
                // An attacker who recomputes the altered row's own hash still breaks the link to the next row.
                chain[2].NewValue = "forged";
                AuditCanonical.Seal(chain[2], chain[1].Hash);
                break;
        }

        var result = AuditVerifier.Check(chain, 0, AuditCanonical.Genesis);

        result.Ok.ShouldBeFalse();
        result.BrokenSeq.ShouldBe(expectedSeq);
        result.MessageKey.ShouldBe(expectedMessage);
    }

    [Fact]
    public void Verification_can_continue_from_a_checkpoint()
    {
        var chain = Chain(6);
        var result = AuditVerifier.Check(chain.Skip(3), 3, chain[2].Hash);

        result.Ok.ShouldBeTrue();
        result.Checked.ShouldBe(3);
        AuditVerifier.Check(chain.Skip(3), 3, AuditCanonical.Genesis).Ok.ShouldBeFalse();
    }
}

public class AuditValueTests
{
    [Fact]
    public void Values_are_written_invariantly_and_compactly()
    {
        AuditValues.Format(null).ShouldBeNull();
        AuditValues.Format(new DateTimeOffset(2026, 1, 2, 3, 4, 5, TimeSpan.FromHours(4))).ShouldBe("2026-01-01T23:04:05Z");
        AuditValues.Format(new DateOnly(2026, 3, 9)).ShouldBe("2026-03-09");
        AuditValues.Format(12.5m).ShouldBe("12.5");
        AuditValues.Format(true).ShouldBe("true");
        AuditValues.Format(new byte[] { 1, 2, 3 }).ShouldBe("<3 bytes>");
        AuditValues.Format(new[] { "a", "b" }).ShouldBe("[\"a\",\"b\"]");
        AuditValues.Format(new string('x', 5000))!.Length.ShouldBe(AuditValues.MaxLength);
    }

    [Fact]
    public void Jsonb_values_compare_regardless_of_spacing_and_key_order()
    {
        AuditValues.CompactJson("{\"b\": 1, \"a\": {\"y\": true, \"x\": null}}").ShouldBe(AuditValues.CompactJson("{\"a\":{\"x\":null,\"y\":true},\"b\":1}"));
        AuditValues.CompactJson("not json").ShouldBe("not json");
    }

    [Fact]
    public void Snapshots_list_fields_in_a_stable_order()
    {
        AuditValues.Snapshot([("NameEn", "B"), ("NameAr", "أ"), ("Status", null)])
            .ShouldBe("{\"NameAr\":\"أ\",\"NameEn\":\"B\",\"Status\":null}");
    }
}
