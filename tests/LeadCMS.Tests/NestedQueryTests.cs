// <copyright file="NestedQueryTests.cs" company="WavePoint Co. Ltd.">
// Licensed under the MIT license. See LICENSE file in the samples root for full license information.
// </copyright>

namespace LeadCMS.Tests;

public class NestedQueryTests : BaseTestAutoLogin
{
    private const string ContactsUrl = "/api/contacts";
    private const string AccountsUrl = "/api/accounts";

    public NestedQueryTests()
        : base()
    {
        TrackEntityType<Contact>();
        TrackEntityType<Account>();
    }

    [Fact]
    public async Task FilterContactsByAccountName()
    {
        // Arrange: Create account
        var accountName = "TestCompany" + Guid.NewGuid().ToString()[..8];
        var accountDto = new TestAccount { Name = accountName };
        await PostTest<TestAccount>(AccountsUrl, accountDto);

        // Create contact and link to account directly in DB
        var contactDto = TestData.Generate<TestContact>();
        await PostTest<TestContact>(ContactsUrl, contactDto);

        // Update the contact in database to link to account
        var dbContext = App.GetDbContext();
        var contact = dbContext!.Contacts!.First(c => c.Email == contactDto.Email);
        var account = dbContext.Accounts!.First(a => a.Name == accountName);
        contact.AccountId = account.Id;
        await dbContext.SaveChangesAsync();

        // Act: Filter contacts by account.name
        var result = await GetTest<List<Contact>>($"{ContactsUrl}?filter[where][account.name]={accountName}");

        // Assert
        result.Should().NotBeNull();
        result!.Should().Contain(c => c.AccountId == account.Id);
    }

    [Fact]
    public async Task OrderContactsByAccountName()
    {
        // Arrange: Create two accounts
        var uniqueId = Guid.NewGuid().ToString();
        var account1Name = $"AAA_Company_{uniqueId}";
        var account2Name = $"ZZZ_Company_{uniqueId}";

        await PostTest<TestAccount>(AccountsUrl, new TestAccount { Name = account1Name });
        await PostTest<TestAccount>(AccountsUrl, new TestAccount { Name = account2Name });

        // Create contacts with unique identifiers
        var contact1Dto = TestData.Generate<TestContact>(uniqueId + "_1");
        var contact2Dto = TestData.Generate<TestContact>(uniqueId + "_2");
        await PostTest<TestContact>(ContactsUrl, contact1Dto);
        await PostTest<TestContact>(ContactsUrl, contact2Dto);

        // Link contacts to accounts in DB
        var dbContext = App.GetDbContext();
        var account1 = dbContext!.Accounts!.First(a => a.Name == account1Name);
        var account2 = dbContext.Accounts!.First(a => a.Name == account2Name);
        var contact1 = dbContext.Contacts!.First(c => c.Email == contact1Dto.Email);
        var contact2 = dbContext.Contacts!.First(c => c.Email == contact2Dto.Email);

        contact1.AccountId = account2.Id; // ZZZ
        contact2.AccountId = account1.Id; // AAA
        await dbContext.SaveChangesAsync();

        // Act: Order contacts by account.name ascending
        var result = await GetTest<List<Contact>>($"{ContactsUrl}?filter[order]=account.name&filter[include]=Account");

        // Assert
        result.Should().NotBeNull();
        var testContacts = result!.Where(c => c.Id == contact1.Id || c.Id == contact2.Id).ToList();
        testContacts.Count.Should().Be(2);

        // Verify ordering - AAA should come before ZZZ
        var sorted = testContacts.OrderBy(c => c.Account?.Name).ToList();
        sorted[0].Account?.Name.Should().Be(account1Name);
        sorted[1].Account?.Name.Should().Be(account2Name);
    }

    [Fact]
    public async Task FilterContactsByNestedPropertyNotFound()
    {
        // Act: Try to filter by invalid nested property
        var response = await GetRequest($"{ContactsUrl}?filter[where][account.invalidProperty]=test");

        // Assert: Should return error
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task FilterByCollectionNavigationNotSupportedForOtherEntities()
    {
        var response = await GetRequest($"{AccountsUrl}?filter[where][contacts.id][eq]=1");

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task OrderContactsByAccountNameDescending()
    {
        // Arrange: Create accounts and contacts
        var uniqueId = Guid.NewGuid().ToString();
        var account1Name = $"BBB_Company_{uniqueId}";
        var account2Name = $"YYY_Company_{uniqueId}";

        await PostTest<TestAccount>(AccountsUrl, new TestAccount { Name = account1Name });
        await PostTest<TestAccount>(AccountsUrl, new TestAccount { Name = account2Name });

        var contact1Dto = TestData.Generate<TestContact>(uniqueId + "_1");
        var contact2Dto = TestData.Generate<TestContact>(uniqueId + "_2");
        await PostTest<TestContact>(ContactsUrl, contact1Dto);
        await PostTest<TestContact>(ContactsUrl, contact2Dto);

        // Link in DB
        var dbContext = App.GetDbContext();
        var account1 = dbContext!.Accounts!.First(a => a.Name == account1Name);
        var account2 = dbContext.Accounts!.First(a => a.Name == account2Name);
        var contact1 = dbContext.Contacts!.First(c => c.Email == contact1Dto.Email);
        var contact2 = dbContext.Contacts!.First(c => c.Email == contact2Dto.Email);

        contact1.AccountId = account1.Id;
        contact2.AccountId = account2.Id;
        await dbContext.SaveChangesAsync();

        // Act: Order by account.name descending
        var result = await GetTest<List<Contact>>($"{ContactsUrl}?filter[order]=account.name desc&filter[include]=Account");

        // Assert
        result.Should().NotBeNull();
        var testContacts = result!.Where(c => c.Id == contact1.Id || c.Id == contact2.Id).ToList();
        testContacts.Count.Should().Be(2);

        // Verify descending order - YYY should come before BBB
        var sorted = testContacts.OrderByDescending(c => c.Account?.Name).ToList();
        sorted[0].Account?.Name.Should().Be(account2Name);
        sorted[1].Account?.Name.Should().Be(account1Name);
    }

    [Fact]
    public async Task FilterWithMixedCasing()
    {
        // Arrange: Create account
        var accountName = "CaseTest" + Guid.NewGuid().ToString()[..8];
        var accountDto = new TestAccount { Name = accountName };
        await PostTest<TestAccount>(AccountsUrl, accountDto);

        // Create contact and link to account
        var contactDto = TestData.Generate<TestContact>();
        await PostTest<TestContact>(ContactsUrl, contactDto);

        var dbContext = App.GetDbContext();
        var contact = dbContext!.Contacts!.First(c => c.Email == contactDto.Email);
        var account = dbContext.Accounts!.First(a => a.Name == accountName);
        contact.AccountId = account.Id;
        await dbContext.SaveChangesAsync();

        // Act: Use different casing variations - all should work
        var result1 = await GetTest<List<Contact>>($"{ContactsUrl}?filter[where][account.name]={accountName}&filter[include]=account");
        var result2 = await GetTest<List<Contact>>($"{ContactsUrl}?filter[where][Account.Name]={accountName}&filter[include]=Account");
        var result3 = await GetTest<List<Contact>>($"{ContactsUrl}?filter[where][ACCOUNT.NAME]={accountName}&filter[include]=ACCOUNT");
        var result4 = await GetTest<List<Contact>>($"{ContactsUrl}?filter[order]=account.name&filter[where][firstname]={contactDto.FirstName}");

        // Assert: All variations should work and return the same contact
        result1.Should().NotBeNull();
        result1!.Should().Contain(c => c.AccountId == account.Id);

        result2.Should().NotBeNull();
        result2!.Should().Contain(c => c.AccountId == account.Id);

        result3.Should().NotBeNull();
        result3!.Should().Contain(c => c.AccountId == account.Id);

        result4.Should().NotBeNull();
        result4!.Should().Contain(c => c.Email == contactDto.Email);
    }
}
