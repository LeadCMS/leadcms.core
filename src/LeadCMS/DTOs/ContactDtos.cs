// <copyright file="ContactDtos.cs" company="WavePoint Co. Ltd.">
// Licensed under the MIT license. See LICENSE file in the samples root for full license information.
// </copyright>

using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;
using CsvHelper.Configuration.Attributes;
using LeadCMS.Core.Sequences.DTOs;
using LeadCMS.DataAnnotations;
using LeadCMS.Entities;
using LeadCMS.Geography;
using LeadCMS.Infrastructure;
using LeadCMS.Models;

namespace LeadCMS.DTOs;

public abstract class BaseContactDto
{
    public string? Prefix { get; set; }

    public string? FirstName { get; set; }

    public string? MiddleName { get; set; }

    public string? LastName { get; set; }

    public DateTime? Birthday { get; set; }

    public Continent? ContinentCode { get; set; }

    public Country? CountryCode { get; set; }

    public string? CityName { get; set; }

    public string? Address1 { get; set; }

    public string? Address2 { get; set; }

    public string? JobTitle { get; set; }

    public string? CompanyName { get; set; }

    public string? Department { get; set; }

    public string? State { get; set; }

    public string? Zip { get; set; }

    public string? Phone { get; set; }

    public string? PhoneRaw { get; set; }

    public int? Timezone { get; set; }

    public string? Language { get; set; }

    public Dictionary<string, string>? SocialMedia { get; set; }

    public string[]? Tags { get; set; }

    [SwaggerHide]
    public int? UnsubscribeId { get; set; }

    public string? Source { get; set; }

    public int? AccountId { get; set; }
}

public class ContactCreateDto : BaseContactDto
{
    private string? email;

    [EmailAddress]
    public string? Email
    {
        get
        {
            return email;
        }

        set
        {
            email = value?.ToLower();
        }
    }
}

public class ContactUpdateDto : BaseContactDto, IPatchDto
{
    private string? email;

    [Ignore]
    [JsonIgnore]
    public HashSet<string> NullProperties { get; } = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

    [EmailAddress]
    public string? Email
    {
        get
        {
            return email;
        }

        set
        {
            email = value?.ToLower();
        }
    }
}

public class ContactDetailsDto : ContactCreateDto
{
    public int Id { get; set; }

    public string? FullName { get; set; }

    [Ignore]
    [JsonIgnore]
    public string? CreatedByUserAgent { get; set; }

    [Ignore]
    [JsonIgnore]
    public string? UpdatedByUserAgent { get; set; }

    public string? IpAddress { get; set; }

    public string? UserDeviceSummary { get; set; }

    public Utms? Utms { get; set; }

    public string AvatarUrl { get; set; } = string.Empty;

    public DateTime CreatedAt { get; set; }

    public DateTime? UpdatedAt { get; set; }

    public int? DomainId { get; set; }

    public int DealsCount { get; set; }

    public int OrdersCount { get; set; }

    public DateTime? LastOrderDate { get; set; }

    public decimal TotalRevenue { get; set; }

    [Ignore]
    public List<PendingContactUpdate>? PendingUpdates { get; set; }

    [Ignore]
    public DomainDetailsDto? Domain { get; set; }

    [Ignore]
    public AccountDetailsDto? Account { get; set; }

    [Ignore]
    public UnsubscribeDetailsDto? Unsubscribe { get; set; }

    [Ignore]
    public List<OrderDetailsDto>? Orders { get; set; }

    [Ignore]
    public List<SequenceEnrollmentDetailsDto>? Enrollments { get; set; }
}

public class ContactImportDto : BaseImportDto
{
    private string? email;

    [Optional]
    [EmailAddress]
    [SwaggerUnique]
    public string? Email
    {
        get
        {
            return email;
        }

        set
        {
            email = value?.ToLower();
        }
    }

    [Optional]
    public string? Prefix { get; set; }

    [Optional]
    public string? FirstName { get; set; }

    [Optional]
    public string? MiddleName { get; set; }

    [Optional]
    public string? LastName { get; set; }

    [Optional]
    public DateTime? Birthday { get; set; }

    [Optional]
    public Continent? ContinentCode { get; set; }

    [Optional]
    public Country? CountryCode { get; set; }

    [Optional]
    public string? CityName { get; set; }

    [Optional]
    public string? Address1 { get; set; }

    [Optional]
    public string? Address2 { get; set; }

    [Optional]
    public string? JobTitle { get; set; }

    [Optional]
    public string? CompanyName { get; set; }

    [Optional]
    public string? Department { get; set; }

    [Optional]
    public string? State { get; set; }

    [Optional]
    public string? Zip { get; set; }

    [Optional]
    public string? Phone { get; set; }

    [Optional]
    public string? PhoneRaw { get; set; }

    [Optional]
    public int? Timezone { get; set; }

    [Optional]
    public string? Language { get; set; }

    [Optional]
    public Dictionary<string, string>? SocialMedia { get; set; }

    [Optional]
    public int? UnsubscribeId { get; set; }

    [Optional]
    public int? AccountId { get; set; }

    [Optional]
    [SurrogateForeignKey(typeof(Account), "Name", "AccountId")]
    public string? AccountName { get; set; }

    [Optional]
    public int? DomainId { get; set; }

    [Optional]
    public string[]? Tags { get; set; }
}

/// <summary>
/// Identifies a pending contact update to accept or reject.
/// </summary>
public class PendingContactUpdateDecisionDto
{
    /// <summary>
    /// Gets or sets the canonical field name of the pending update (e.g. "FirstName").
    /// </summary>
    [Required]
    public string Field { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the creation timestamp of the pending update as returned in
    /// <see cref="ContactDetailsDto.PendingUpdates"/>. It guards against deciding on a record that
    /// has been replaced by a newer submission in the meantime.
    /// </summary>
    [Required]
    public DateTime? CreatedAt { get; set; }
}