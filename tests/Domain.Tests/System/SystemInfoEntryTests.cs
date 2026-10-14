using Domain.System;

using Shouldly;

namespace Domain.Tests.System;

public sealed class SystemInfoEntryTests
{
    [Fact]
    public void Create_ShouldTrimKeyAndValue_WhenInputHasSurroundingWhitespace()
    {
        SystemInfoEntry entry = SystemInfoEntry.Create("  app.name  ", "  skeleton  ", DateTimeOffset.UtcNow);

        entry.Key.ShouldBe("app.name");
        entry.Value.ShouldBe("skeleton");
    }

    [Fact]
    public void Create_ShouldStoreTimestampInUtc_WhenGivenAnOffsetTime()
    {
        DateTimeOffset istanbulTime = new(2026, 10, 14, 15, 30, 0, TimeSpan.FromHours(3));

        SystemInfoEntry entry = SystemInfoEntry.Create("app.name", "skeleton", istanbulTime);

        entry.RecordedAt.Offset.ShouldBe(TimeSpan.Zero);
        entry.RecordedAt.UtcDateTime.ShouldBe(new DateTime(2026, 10, 14, 12, 30, 0, DateTimeKind.Utc));
    }

    [Fact]
    public void Create_ShouldProduceVersion7Identifier_Always()
    {
        SystemInfoEntry entry = SystemInfoEntry.Create("app.name", "skeleton", DateTimeOffset.UtcNow);

        entry.Id.ShouldNotBe(Guid.Empty);
        entry.Id.Version.ShouldBe(7);
    }

    [Fact]
    public async Task Create_ShouldProduceTimeOrderedIdentifiers_WhenCalledAcrossMilliseconds()
    {
        SystemInfoEntry first = SystemInfoEntry.Create("a", "1", DateTimeOffset.UtcNow);
        await Task.Delay(5, TestContext.Current.CancellationToken);
        SystemInfoEntry second = SystemInfoEntry.Create("b", "2", DateTimeOffset.UtcNow);

        // UUID v7 is time ordered in its BIG-ENDIAN byte form - the order PostgreSQL indexes on.
        // Within a single millisecond the tail is random, so ordering is asserted across
        // milliseconds; this is the property that keeps the index from fragmenting.
        string firstKey = Convert.ToHexString(first.Id.ToByteArray(bigEndian: true));
        string secondKey = Convert.ToHexString(second.Id.ToByteArray(bigEndian: true));

        string.CompareOrdinal(firstKey, secondKey).ShouldBeLessThan(0);

        // The 48-bit timestamp prefix must never go backwards, whatever the tail does.
        string.CompareOrdinal(firstKey[..12], secondKey[..12]).ShouldBeLessThan(0);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Create_ShouldReject_WhenKeyIsBlank(string key)
    {
        Should.Throw<ArgumentException>(() => SystemInfoEntry.Create(key, "skeleton", DateTimeOffset.UtcNow));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Create_ShouldReject_WhenValueIsBlank(string value)
    {
        Should.Throw<ArgumentException>(() => SystemInfoEntry.Create("app.name", value, DateTimeOffset.UtcNow));
    }

    [Fact]
    public void FromStorage_ShouldKeepIdentityAndNormaliseTimeToUtc_WhenRehydrating()
    {
        Guid id = Guid.CreateVersion7();
        DateTimeOffset localTime = new(2026, 1, 2, 10, 0, 0, TimeSpan.FromHours(3));

        SystemInfoEntry entry = SystemInfoEntry.FromStorage(id, "app.name", "skeleton", localTime);

        entry.Id.ShouldBe(id);
        entry.RecordedAt.Offset.ShouldBe(TimeSpan.Zero);
    }
}
