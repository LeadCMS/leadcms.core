// <copyright file="ContactPendingUpdatesTests.cs" company="WavePoint Co. Ltd.">
// Licensed under the MIT license. See LICENSE file in the samples root for full license information.
// </copyright>

using LeadCMS.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace LeadCMS.Tests;

public class ContactPendingUpdatesTests : BaseTestAutoLogin
{
    private const string ContactsUrl = "/api/contacts";

    public ContactPendingUpdatesTests()
    {
        TrackEntityType<Contact>();
    }

    [Fact]
    public async Task Accept_PlainField_WritesProposedValueAndRemovesRecord()
    {
        var (id, pending) = await CreateContactWithPendingUpdates(
            Pending(nameof(Contact.FirstName), "Old", "Alice"),
            Pending(nameof(Contact.LastName), "OldLast", "Smith"));

        var before = await LoadContact(id);

        var contact = await Decide("accept", id, pending[0]);

        contact!.FirstName.Should().Be("Alice");
        contact.PendingUpdates.Should().ContainSingle(p => p.Field == nameof(Contact.LastName));

        var stored = await LoadContact(id);
        stored.FirstName.Should().Be("Alice");
        stored.LastName.Should().Be(before.LastName);
        stored.PendingUpdates.Should().ContainSingle(p => p.Field == nameof(Contact.LastName) && p.ProposedValue == "Smith");
    }

    [Fact]
    public async Task Accept_Phone_IsNormalizedLikeAnOrdinaryUpdate()
    {
        var (id, pending) = await CreateContactWithPendingUpdates(
            Pending(nameof(Contact.Phone), "+14155550000", "(415) 555-2671"),
            Pending(nameof(Contact.PhoneRaw), "old raw", "(415) 555-2671"));

        await SetPhoneRaw(id, "old raw");

        var contact = await Decide("accept", id, pending[0]);

        contact!.Phone.Should().Be("+14155552671");

        var stored = await LoadContact(id);
        stored.Phone.Should().Be("+14155552671");
        stored.PhoneRaw.Should().Be("old raw");
        stored.PendingUpdates.Should().ContainSingle(p => p.Field == nameof(Contact.PhoneRaw));
    }

    [Fact]
    public async Task Accept_PhoneRaw_WritesProposedValue()
    {
        var (id, pending) = await CreateContactWithPendingUpdates(
            Pending(nameof(Contact.PhoneRaw), "old raw", "+1 415 555 2671"));

        await Decide("accept", id, pending[0]);

        var stored = await LoadContact(id);
        stored.PhoneRaw.Should().Be("+1 415 555 2671");
        stored.PendingUpdates.Should().BeEmpty();
    }

    [Theory]
    [InlineData(nameof(Contact.FirstName))]
    [InlineData(nameof(Contact.MiddleName))]
    [InlineData(nameof(Contact.LastName))]
    [InlineData(nameof(Contact.CompanyName))]
    [InlineData(nameof(Contact.PhoneRaw))]
    [InlineData(nameof(Contact.Source))]
    public async Task Accept_EveryFieldTheMergePolicyRecords_IsSupported(string field)
    {
        var (id, pending) = await CreateContactWithPendingUpdates(Pending(field, "old", "new value"));

        await Decide("accept", id, pending[0]);

        var stored = await LoadContact(id);
        typeof(Contact).GetProperty(field)!.GetValue(stored).Should().Be("new value");
        stored.PendingUpdates.Should().BeEmpty();
    }

    [Fact]
    public async Task Reject_RemovesRecordAndLeavesFieldUnchanged()
    {
        var (id, pending) = await CreateContactWithPendingUpdates(
            Pending(nameof(Contact.FirstName), "Old", "Mallory"),
            Pending(nameof(Contact.CompanyName), "Acme", "Evil Corp"));

        var before = await LoadContact(id);

        var contact = await Decide("reject", id, pending[0]);

        contact!.FirstName.Should().Be(before.FirstName);
        contact.PendingUpdates.Should().ContainSingle(p => p.Field == nameof(Contact.CompanyName));

        var stored = await LoadContact(id);
        stored.FirstName.Should().Be(before.FirstName);
        stored.PendingUpdates.Should().ContainSingle(p => p.Field == nameof(Contact.CompanyName) && p.ProposedValue == "Evil Corp");
    }

    [Theory]
    [InlineData("accept")]
    [InlineData("reject")]
    public async Task Decide_WhenNoRecordForField_ReturnsNotFound(string action)
    {
        var (id, _) = await CreateContactWithPendingUpdates(Pending(nameof(Contact.FirstName), "Old", "Alice"));

        var decision = new PendingContactUpdateDecisionDto { Field = nameof(Contact.LastName), CreatedAt = DateTime.UtcNow };

        await PostTest($"{ContactsUrl}/{id}/pending-updates/{action}", decision, HttpStatusCode.NotFound);
    }

    [Theory]
    [InlineData("accept")]
    [InlineData("reject")]
    public async Task Decide_WhenContactDoesNotExist_ReturnsNotFound(string action)
    {
        var decision = new PendingContactUpdateDecisionDto { Field = nameof(Contact.FirstName), CreatedAt = DateTime.UtcNow };

        await PostTest($"{ContactsUrl}/0/pending-updates/{action}", decision, HttpStatusCode.NotFound);
    }

    [Theory]
    [InlineData("accept")]
    [InlineData("reject")]
    public async Task Decide_WhenRecordWasReplaced_ReturnsConflictAndKeepsRecord(string action)
    {
        var (id, pending) = await CreateContactWithPendingUpdates(Pending(nameof(Contact.FirstName), "Old", "Alice"));

        var stale = new PendingContactUpdateDecisionDto
        {
            Field = nameof(Contact.FirstName),
            CreatedAt = pending[0].CreatedAt!.Value.AddSeconds(-1),
        };

        await PostTest($"{ContactsUrl}/{id}/pending-updates/{action}", stale, HttpStatusCode.Conflict);

        var stored = await LoadContact(id);
        stored.FirstName.Should().NotBe("Alice");
        stored.PendingUpdates.Should().ContainSingle(p => p.Field == nameof(Contact.FirstName));
    }

    [Fact]
    public async Task Accept_UnsupportedField_ReturnsBadRequest()
    {
        var (id, pending) = await CreateContactWithPendingUpdates(Pending(nameof(Contact.Email), "a@old.com", "b@new.com"));

        await PostTest($"{ContactsUrl}/{id}/pending-updates/accept", pending[0], HttpStatusCode.BadRequest);

        var stored = await LoadContact(id);
        stored.Email.Should().NotBe("b@new.com");
        stored.PendingUpdates.Should().ContainSingle();
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public async Task Accept_WhenProposedValueIsEmpty_ReturnsUnprocessableEntityAndKeepsRecord(string? proposedValue)
    {
        var (id, pending) = await CreateContactWithPendingUpdates(Pending(nameof(Contact.FirstName), "Old", proposedValue));

        var before = await LoadContact(id);

        await PostTest($"{ContactsUrl}/{id}/pending-updates/accept", pending[0], HttpStatusCode.UnprocessableEntity);

        var stored = await LoadContact(id);
        stored.FirstName.Should().Be(before.FirstName);
        stored.PendingUpdates.Should().ContainSingle(p => p.Field == nameof(Contact.FirstName));
    }

    [Theory]
    [InlineData("accept")]
    [InlineData("reject")]
    public async Task Decide_WhenCreatedAtIsOmitted_ReturnsUnprocessableEntityAndKeepsRecord(string action)
    {
        var (id, _) = await CreateContactWithPendingUpdates(Pending(nameof(Contact.FirstName), "Old", "Alice"));

        await PostTest($"{ContactsUrl}/{id}/pending-updates/{action}", new { Field = nameof(Contact.FirstName) }, HttpStatusCode.UnprocessableEntity);

        var stored = await LoadContact(id);
        stored.FirstName.Should().NotBe("Alice");
        stored.PendingUpdates.Should().ContainSingle(p => p.Field == nameof(Contact.FirstName));
    }

    [Theory]
    [InlineData("accept")]
    [InlineData("reject")]
    public async Task Decide_WhenFieldIsEmpty_ReturnsUnprocessableEntityAndKeepsRecord(string action)
    {
        var (id, pending) = await CreateContactWithPendingUpdates(Pending(nameof(Contact.FirstName), "Old", "Alice"));

        var decision = new PendingContactUpdateDecisionDto { Field = string.Empty, CreatedAt = pending[0].CreatedAt };

        await PostTest($"{ContactsUrl}/{id}/pending-updates/{action}", decision, HttpStatusCode.UnprocessableEntity);

        (await LoadContact(id)).PendingUpdates.Should().ContainSingle(p => p.Field == nameof(Contact.FirstName));
    }

    [Theory]
    [InlineData(DateTimeKind.Utc)]
    [InlineData(DateTimeKind.Unspecified)]
    [InlineData(DateTimeKind.Local)]
    public async Task Decide_MatchesTimestampRegardlessOfKindAndSubMillisecondPrecision(DateTimeKind kind)
    {
        // Sub-millisecond ticks, as DateTime.UtcNow produces and a millisecond-precision client drops.
        var createdAt = new DateTime(2026, 5, 4, 3, 2, 1, DateTimeKind.Utc).AddTicks(1234567);
        var pending = Pending(nameof(Contact.FirstName), "Old", "Alice");
        pending.CreatedAt = createdAt;
        var (id, _) = await CreateContactWithPendingUpdates(pending);

        var sent = createdAt.AddTicks(-(createdAt.Ticks % TimeSpan.TicksPerMillisecond));
        sent = kind switch
        {
            DateTimeKind.Local => sent.ToLocalTime(),
            DateTimeKind.Unspecified => DateTime.SpecifyKind(sent, DateTimeKind.Unspecified),
            _ => sent,
        };

        // The server may run in any time zone, but Local values carry their offset on the wire.
        var response = await Request(
            HttpMethod.Post,
            $"{ContactsUrl}/{id}/pending-updates/accept",
            new PendingContactUpdateDecisionDto { Field = nameof(Contact.FirstName), CreatedAt = sent });

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        (await LoadContact(id)).FirstName.Should().Be("Alice");
    }

    [Fact]
    public async Task Decide_WithoutAuthentication_ReturnsUnauthorized()
    {
        var (id, pending) = await CreateContactWithPendingUpdates(Pending(nameof(Contact.FirstName), "Old", "Alice"));

        Logout();

        await PostTest($"{ContactsUrl}/{id}/pending-updates/accept", pending[0], HttpStatusCode.Unauthorized);
        await PostTest($"{ContactsUrl}/{id}/pending-updates/reject", pending[0], HttpStatusCode.Unauthorized);

        (await LoadContact(id)).PendingUpdates.Should().ContainSingle();
    }

    private static PendingContactUpdate Pending(string field, string? oldValue, string? proposedValue)
    {
        return new PendingContactUpdate
        {
            Field = field,
            OldValue = oldValue,
            ProposedValue = proposedValue,
            Source = "ContactForm",
            Ip = "127.0.0.1",
            UserAgent = "TestAgent/1.0",
            CreatedAt = DateTime.UtcNow,
        };
    }

    private async Task<(int Id, List<PendingContactUpdateDecisionDto> Decisions)> CreateContactWithPendingUpdates(
        params PendingContactUpdate[] pendingUpdates)
    {
        var created = await PostTest<ContactDetailsDto>(
            ContactsUrl,
            new TestContact(Guid.NewGuid().ToString("N")) { CompanyName = "Acme" },
            HttpStatusCode.Created);

        var dbContext = App.GetDbContext()!;
        var contact = await dbContext.Contacts!.SingleAsync(c => c.Id == created!.Id);
        contact.PendingUpdates = pendingUpdates.ToList();
        await dbContext.SaveChangesAsync();

        // Read back what was stored: the timestamps the client receives are the stored ones.
        var stored = await LoadContact(created!.Id);

        return (
            created.Id,
            stored.PendingUpdates!
                .Select(p => new PendingContactUpdateDecisionDto { Field = p.Field, CreatedAt = p.CreatedAt })
                .ToList());
    }

    private async Task SetPhoneRaw(int id, string phoneRaw)
    {
        var dbContext = App.GetDbContext()!;
        var contact = await dbContext.Contacts!.SingleAsync(c => c.Id == id);
        contact.PhoneRaw = phoneRaw;
        await dbContext.SaveChangesAsync();
    }

    private async Task<ContactDetailsDto?> Decide(string action, int id, PendingContactUpdateDecisionDto decision)
    {
        return await PostTest<ContactDetailsDto>($"{ContactsUrl}/{id}/pending-updates/{action}", decision, HttpStatusCode.OK);
    }

    private async Task<Contact> LoadContact(int id)
    {
        return await App.GetDbContext()!.Contacts!.AsNoTracking().SingleAsync(c => c.Id == id);
    }
}
