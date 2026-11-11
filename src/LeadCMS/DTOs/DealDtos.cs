// <copyright file="DealDtos.cs" company="WavePoint Co. Ltd.">
// Licensed under the MIT license. See LICENSE file in the samples root for full license information.
// </copyright>

using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;
using CsvHelper.Configuration.Attributes;
using LeadCMS.DataAnnotations;
using LeadCMS.Infrastructure;

namespace LeadCMS.DTOs;

public class DealBaseDto
{
    public int? AccountId { get; set; }

    [Required]
    public int DealPipelineId { get; set; }

    [Optional]
    public decimal? DealValue { get; set; }

    [Optional]
    [CurrencyCode]
    public string? DealCurrency { get; set; } = string.Empty;

    public DateTime? ExpectedCloseDate { get; set; }

    public DateTime? ActualCloseDate { get; set; }

    [Required]
    public string UserId { get; set; } = string.Empty;

    public string[]? Tags { get; set; }

    public int? CampaignId { get; set; }
}

public class DealCreateDto : DealBaseDto
{
    public HashSet<int> ContactIds { get; set; } = new HashSet<int>();
}

public class DealUpdateDto : IPatchDto
{
    [Ignore]
    [JsonIgnore]
    public HashSet<string> NullProperties { get; } = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

    public int? AccountId { get; set; }

    public int? DealPipelineId { get; set; }

    public HashSet<int>? ContactIds { get; set; }

    public decimal? DealValue { get; set; }

    [MinLength(1)]
    [CurrencyCode]
    public string? DealCurrency { get; set; }

    public DateTime? ExpectedCloseDate { get; set; }

    public DateTime? ActualCloseDate { get; set; }

    public string? UserId { get; set; }

    public string[]? Tags { get; set; }

    public int? CampaignId { get; set; }
}

public class DealDetailsDto : DealBaseDto
{
    public int Id { get; set; }

    public DateTime CreatedAt { get; set; }

    public DateTime? UpdatedAt { get; set; }

    [Ignore]
    public AccountDetailsDto? Account { get; set; }

    [Ignore]
    public DealPipelineDetailsDto? DealPipeline { get; set; }

    [Ignore]
    public DealPipelineStageDetailsDto? PipelineStage { get; set; }

    [Ignore]
    public UserDetailsDto? CreatedBy { get; set; }

    [Ignore]
    public List<ContactDetailsDto>? Contacts { get; set; }
}