// <copyright file="AutoMapperProfiles.cs" company="WavePoint Co. Ltd.">
// Licensed under the MIT license. See LICENSE file in the samples root for full license information.
// </copyright>

using AutoMapper;
using LeadCMS.Core.Sequences.DTOs;
using LeadCMS.DTOs;
using LeadCMS.Entities;
using LeadCMS.Enums;
using LeadCMS.Helpers;
using LeadCMS.Infrastructure;
using LeadCMS.Models;
using LeadCMS.Services;

namespace LeadCMS.Configuration;

/// <summary>
/// Extension methods for AutoMapper IMappingExpression to support IPatchDto.
/// </summary>
public static class PatchDtoMappingExtensions
{
    /// <summary>
    /// Automatically applies null properties for mappings where TSource implements IPatchDto.
    /// Call this after CreateMap for any UpdateDto that implements IPatchDto.
    /// </summary>
    public static IMappingExpression<TSource, TDestination> WithPatchDtoSupport<TSource, TDestination>(
        this IMappingExpression<TSource, TDestination> mapping)
        where TSource : IPatchDto
        where TDestination : class
    {
        return mapping.AfterMap((src, dest) => src.ApplyNullProperties(dest));
    }
}

public class AutoMapperProfiles : Profile
{
    public AutoMapperProfiles()
    {
        CreateMap<bool?, bool>().ConvertUsing((src, dest) => src ?? dest);
        CreateMap<int?, int>().ConvertUsing((src, dest) => src ?? dest);
        CreateMap<decimal?, decimal>().ConvertUsing((src, dest) => src ?? dest);
        CreateMap<List<DnsRecord>?, List<DnsRecord>>().ConvertUsing((src, dest) => src ?? dest);
        CreateMap<Dictionary<string, string>?, Dictionary<string, string>>().ConvertUsing((src, dest) => src ?? dest);
        CreateMap<string?[], string?[]>().ConvertUsing((src, dest) => src ?? dest);
        CreateMap<CommentStatus?, CommentStatus>().ConvertUsing((src, dest) => src ?? dest);
        CreateMap<RedirectKind?, RedirectKind>().ConvertUsing((src, dest) => src ?? dest);
        CreateMap<RedirectSourceType?, RedirectSourceType>().ConvertUsing((src, dest) => src ?? dest);
        CreateMap<RedirectTargetType?, RedirectTargetType>().ConvertUsing((src, dest) => src ?? dest);

        CreateMap<DateTime, DateTime>().ConvertUsing(new DateTimeToUtcConverter());
        CreateMap<DateTime?, DateTime?>().ConvertUsing(new DateTimeToUtcConverter());
        CreateMap<DateTime?, DateTime>().ConvertUsing(new DateTimeToUtcConverter());
        CreateMap<SeoMetadataDto, SeoMetadata>().ReverseMap();

        CreateMap<Comment, CommentCreateDto>().ReverseMap();
        CreateMap<Comment, CommentCreateBaseDto>().ReverseMap();
        CreateMap<Comment, CommentUpdateDto>()
            .ForAllMembers(m => m.Condition(PropertyNeedsMapping));
        CreateMap<CommentUpdateDto, Comment>()
            .WithPatchDtoSupport()
            .ForAllMembers(m => m.Condition(PropertyNeedsMapping));
        CreateMap<Comment, CommentDetailsDto>()
            .ForAllMembers(m => m.Condition(PropertyNeedsMapping));
        CreateMap<CommentImportDto, Comment>()
            .WithPatchDtoSupport()
            .ForAllMembers(m => m.Condition(PropertyNeedsMapping));

        CreateMap<ContentCreateDto, Content>().ReverseMap();
        CreateMap<ContentUpdateDto, Content>()
            .WithPatchDtoSupport()
            .ForAllMembers(m => m.Condition(PropertyNeedsMapping));
        CreateMap<Content, ContentUpdateDto>()
            .ForAllMembers(m => m.Condition(PropertyNeedsMapping));
        CreateMap<Content, ContentDetailsDto>()
           .ForAllMembers(m => m.Condition(PropertyNeedsMapping));
        CreateMap<ContentImportDto, Content>()
            .WithPatchDtoSupport()
            .ForAllMembers(m => m.Condition(PropertyNeedsMapping));

        CreateMap<OrderCreateDto, Order>().ReverseMap();
        CreateMap<OrderUpdateDto, Order>()
            .WithPatchDtoSupport()
            .ForAllMembers(m => m.Condition(PropertyNeedsMapping));
        CreateMap<Order, OrderUpdateDto>()
            .ForAllMembers(m => m.Condition(PropertyNeedsMapping));
        CreateMap<Order, OrderDetailsDto>()
            .ForAllMembers(m => m.Condition(PropertyNeedsMapping));
        CreateMap<OrderImportDto, Order>()
            .WithPatchDtoSupport()
            .ForAllMembers(m => m.Condition(PropertyNeedsMapping));

        CreateMap<OrderItemCreateDto, OrderItem>().ReverseMap();
        CreateMap<OrderItemUpdateDto, OrderItem>()
            .WithPatchDtoSupport()
            .ForAllMembers(m => m.Condition(PropertyNeedsMapping));
        CreateMap<OrderItem, OrderItemUpdateDto>()
            .ForAllMembers(m => m.Condition(PropertyNeedsMapping));
        CreateMap<OrderItem, OrderItemDetailsDto>()
           .ForAllMembers(m => m.Condition(PropertyNeedsMapping));
        CreateMap<OrderItemImportDto, OrderItem>()
            .WithPatchDtoSupport()
            .ForAllMembers(m => m.Condition(PropertyNeedsMapping));

        CreateMap<EmailTemplateCreateDto, EmailTemplate>().ReverseMap();
        CreateMap<EmailTemplateUpdateDto, EmailTemplate>()
            .WithPatchDtoSupport()
            .ForAllMembers(m => m.Condition(PropertyNeedsMapping));
        CreateMap<EmailTemplate, EmailTemplateUpdateDto>()
            .ForAllMembers(m => m.Condition(PropertyNeedsMapping));
        CreateMap<EmailTemplate, EmailTemplateDetailsDto>()
            .ForAllMembers(m => m.Condition(PropertyNeedsMapping));

        CreateMap<ContactCreateDto, Contact>().ReverseMap();
        CreateMap<ContactUpdateDto, Contact>()
            .WithPatchDtoSupport()
            .ForAllMembers(m => m.Condition(PropertyNeedsMapping));
        CreateMap<Contact, ContactUpdateDto>()
            .ForAllMembers(m => m.Condition(PropertyNeedsMapping));
        CreateMap<Contact, ContactDetailsDto>()
            .AfterMap((source, destination) =>
            {
                destination.IpAddress = source.UpdatedByIp ?? source.CreatedByIp;
                destination.AvatarUrl = GravatarHelper.EmailToGravatarUrl(source.Email);
            })
            .ForAllMembers(m => m.Condition(PropertyNeedsMapping));
        CreateMap<ContactImportDto, Contact>()
            .WithPatchDtoSupport()
            .ForAllMembers(m => m.Condition(PropertyNeedsMapping));

        CreateMap<DealPipelineCreateDto, DealPipeline>().ReverseMap();
        CreateMap<DealPipelineUpdateDto, DealPipeline>()
            .WithPatchDtoSupport()
            .ForAllMembers(m => m.Condition(PropertyNeedsMapping));
        CreateMap<DealPipeline, DealPipelineUpdateDto>()
            .ForAllMembers(m => m.Condition(PropertyNeedsMapping));
        CreateMap<DealPipeline, DealPipelineDetailsDto>()
            .ForAllMembers(m => m.Condition(PropertyNeedsMapping));

        CreateMap<DealCreateDto, Deal>().ReverseMap();
        CreateMap<DealUpdateDto, Deal>()
            .WithPatchDtoSupport()
            .ForAllMembers(m => m.Condition(PropertyNeedsMapping));
        CreateMap<Deal, DealUpdateDto>()
            .ForAllMembers(m => m.Condition(PropertyNeedsMapping));
        CreateMap<Deal, DealDetailsDto>()
            .ForMember(d => d.PipelineStage, o => o.MapFrom(s => s.DealPipelineStage))
            .ForAllMembers(m => m.Condition(PropertyNeedsMapping));

        CreateMap<PromotionCreateDto, Promotion>().ReverseMap();
        CreateMap<PromotionUpdateDto, Promotion>()
            .WithPatchDtoSupport()
            .ForAllMembers(m => m.Condition(PropertyNeedsMapping));
        CreateMap<Promotion, PromotionUpdateDto>()
            .ForAllMembers(m => m.Condition(PropertyNeedsMapping));
        CreateMap<Promotion, PromotionDetailsDto>()
            .ForAllMembers(m => m.Condition(PropertyNeedsMapping));

        CreateMap<DealPipelineStageCreateDto, DealPipelineStage>().ReverseMap();
        CreateMap<DealPipelineStageUpdateDto, DealPipelineStage>()
            .WithPatchDtoSupport()
            .ForAllMembers(m => m.Condition(PropertyNeedsMapping));
        CreateMap<DealPipelineStage, DealPipelineStageUpdateDto>()
            .ForAllMembers(m => m.Condition(PropertyNeedsMapping));
        CreateMap<DealPipelineStage, DealPipelineStageDetailsDto>()
            .ForAllMembers(m => m.Condition(PropertyNeedsMapping));

        CreateMap<EmailGroupCreateDto, EmailGroup>().ReverseMap();
        CreateMap<EmailGroupUpdateDto, EmailGroup>()
            .WithPatchDtoSupport()
            .ForAllMembers(m => m.Condition(PropertyNeedsMapping));
        CreateMap<EmailGroup, EmailGroupUpdateDto>()
            .ForAllMembers(m => m.Condition(PropertyNeedsMapping));
        CreateMap<EmailGroup, EmailGroupDetailsDto>()
            .ForAllMembers(m => m.Condition(PropertyNeedsMapping));

        CreateMap<OrderItem, OrderItemDetailsDto>()
            .ForAllMembers(m => m.Condition(PropertyNeedsMapping));

        CreateMap<Order, OrderDetailsDto>()
           .ForAllMembers(m => m.Condition(PropertyNeedsMapping));

        CreateMap<Link, LinkCreateDto>().ReverseMap();
        CreateMap<Link, LinkUpdateDto>()
            .ForAllMembers(m => m.Condition(PropertyNeedsMapping));
        CreateMap<LinkUpdateDto, Link>()
            .WithPatchDtoSupport()
            .ForAllMembers(m => m.Condition(PropertyNeedsMapping));
        CreateMap<Link, LinkDetailsDto>()
            .ForAllMembers(m => m.Condition(PropertyNeedsMapping));
        CreateMap<LinkImportDto, Link>()
            .WithPatchDtoSupport()
            .ForAllMembers(m => m.Condition(PropertyNeedsMapping));

        CreateMap<Domain, DomainCreateDto>().ReverseMap();
        CreateMap<Domain, DomainUpdateDto>()
            .ForAllMembers(m => m.Condition(PropertyNeedsMapping));
        CreateMap<DomainUpdateDto, Domain>()
            .WithPatchDtoSupport()
            .ForAllMembers(m => m.Condition(PropertyNeedsMapping));
        CreateMap<Domain, DomainDetailsDto>()
            .ForAllMembers(m => m.Condition(PropertyNeedsMapping));
        CreateMap<DomainImportDto, Domain>()
            .WithPatchDtoSupport()
            .ForAllMembers(m => m.Condition(PropertyNeedsMapping));
        CreateMap<Domain, EmailVerifyDetailsDto>()
            .ForAllMembers(m => m.Condition(PropertyNeedsMapping));

        CreateMap<Account, AccountCreateDto>().ReverseMap();
        CreateMap<Account, AccountUpdateDto>()
            .ForAllMembers(m => m.Condition(PropertyNeedsMapping));
        CreateMap<AccountUpdateDto, Account>()
            .WithPatchDtoSupport()
            .ForAllMembers(m => m.Condition(PropertyNeedsMapping));
        CreateMap<Account, AccountDetailsDto>()
            .ForAllMembers(m => m.Condition(PropertyNeedsMapping));
        CreateMap<AccountImportDto, Account>()
            .WithPatchDtoSupport()
            .ForAllMembers(m => m.Condition(PropertyNeedsMapping));
        CreateMap<AccountDetailsInfo, Account>()
            .ForAllMembers(m => m.Condition(PropertyNeedsMapping));

        CreateMap<User, UserCreateDto>().ReverseMap();
        CreateMap<User, UserUpdateDto>()
            .ForAllMembers(m => m.Condition(PropertyNeedsMapping));
        CreateMap<UserUpdateDto, User>()
            .WithPatchDtoSupport()
            .ForAllMembers(m => m.Condition(PropertyNeedsMapping));
        CreateMap<User, UserDetailsDto>();

        CreateMap<ActivityLog, ActivityLogDetailsDto>()
            .ForAllMembers(m => m.Condition(PropertyNeedsMapping));

        CreateMap<EmailLog, ContactEmailCommunicationListItemDto>()
            .AfterMap((source, destination) =>
            {
                destination.Body = ContactEmailCommunicationService.PrepareBody(source);
            })
            .ForAllMembers(m => m.Condition(PropertyNeedsMapping));
        CreateMap<EmailLog, ContactEmailCommunicationDetailsDto>()
            .AfterMap((source, destination) =>
            {
                destination.Body = ContactEmailCommunicationService.PrepareBody(source);
            })
            .ForAllMembers(m => m.Condition(PropertyNeedsMapping));

        CreateMap<Unsubscribe, UnsubscribeDto>().ReverseMap();
        CreateMap<Unsubscribe, UnsubscribeDetailsDto>()
            .ForAllMembers(m => m.Condition(PropertyNeedsMapping));
        CreateMap<UnsubscribeImportDto, Unsubscribe>()
            .WithPatchDtoSupport()
            .ForAllMembers(m => m.Condition(PropertyNeedsMapping));

        // ContentType mappings
        CreateMap<ContentTypeCreateDto, ContentType>().ReverseMap();
        CreateMap<ContentTypeUpdateDto, ContentType>()
            .WithPatchDtoSupport()
            .ForAllMembers(m => m.Condition(PropertyNeedsMapping));
        CreateMap<ContentType, ContentTypeUpdateDto>()
            .ForAllMembers(m => m.Condition(PropertyNeedsMapping));
        CreateMap<ContentType, ContentTypeDetailsDto>()
            .ForAllMembers(m => m.Condition(PropertyNeedsMapping));
        CreateMap<ContentTypeImportDto, ContentType>()
            .WithPatchDtoSupport()
            .ForAllMembers(m => m.Condition(PropertyNeedsMapping));

        CreateMap<Setting, SettingCreateDto>().ReverseMap();
        CreateMap<SettingUpdateDto, Setting>()
            .WithPatchDtoSupport()
            .ForAllMembers(m => m.Condition(PropertyNeedsMapping));
        CreateMap<Setting, SettingUpdateDto>()
            .ForAllMembers(m => m.Condition(PropertyNeedsMapping));
        CreateMap<Setting, SettingDetailsDto>()
            .ForAllMembers(m => m.Condition(PropertyNeedsMapping));
        CreateMap<SettingImportDto, Setting>()
            .WithPatchDtoSupport()
            .ForAllMembers(m => m.Condition(PropertyNeedsMapping));

        // Campaign mappings
        CreateMap<CampaignCreateDto, Campaign>().ReverseMap();
        CreateMap<CampaignUpdateDto, Campaign>()
            .WithPatchDtoSupport()
            .ForAllMembers(m => m.Condition(PropertyNeedsMapping));
        CreateMap<Campaign, CampaignUpdateDto>()
            .ForAllMembers(m => m.Condition(PropertyNeedsMapping));
        CreateMap<Campaign, CampaignDetailsDto>()
            .ForAllMembers(m => m.Condition(PropertyNeedsMapping));
        CreateMap<CampaignRecipient, CampaignRecipientDetailsDto>()
            .ForAllMembers(m => m.Condition(PropertyNeedsMapping));

        // Sequence mappings
        CreateMap<SequenceCreateDto, Sequence>()
            .ForMember(dest => dest.Steps, opt => opt.Ignore())
            .ReverseMap();
        CreateMap<SequenceUpdateDto, Sequence>()
            .ForMember(dest => dest.Steps, opt => opt.Ignore())
            .WithPatchDtoSupport()
            .ForAllMembers(m => m.Condition(PropertyNeedsMapping));
        CreateMap<Sequence, SequenceUpdateDto>()
            .ForAllMembers(m => m.Condition(PropertyNeedsMapping));
        CreateMap<Sequence, SequenceDetailsDto>()
            .ForMember(dest => dest.Steps, opt => opt.MapFrom(src => (src.Steps ?? new List<SequenceStep>()).OrderBy(step => step.Position)))
            .ForAllMembers(m => m.Condition(PropertyNeedsMapping));
        CreateMap<SequenceStepCreateDto, SequenceStep>()
            .ForMember(dest => dest.Position, opt => opt.Ignore())
            .ReverseMap();
        CreateMap<SequenceStep, SequenceStepDetailsDto>()
            .ForAllMembers(m => m.Condition(PropertyNeedsMapping));
        CreateMap<SequenceEnrollment, SequenceEnrollmentDetailsDto>()
            .ForAllMembers(m => m.Condition(PropertyNeedsMapping));
        CreateMap<SequenceDelivery, SequenceDeliveryDetailsDto>()
            .ForAllMembers(m => m.Condition(PropertyNeedsMapping));

        // Segment mappings
        CreateMap<SegmentCreateDto, Segment>()
            .ReverseMap();
        CreateMap<SegmentUpdateDto, Segment>()
            .WithPatchDtoSupport()
            .ForAllMembers(m => m.Condition(PropertyNeedsMapping));
        CreateMap<Segment, SegmentUpdateDto>()
            .ForAllMembers(m => m.Condition(PropertyNeedsMapping));
        CreateMap<Segment, SegmentDetailsDto>()
            .ForAllMembers(m => m.Condition(PropertyNeedsMapping));

        // Media mappings
        CreateMap<Media, MediaDetailsDto>()
            .ForMember(dest => dest.Location, opt => opt.Ignore()) // Location is computed at controller level
            .ForAllMembers(m => m.Condition(PropertyNeedsMapping));

        // Task execution log mappings
        CreateMap<TaskExecutionLog, TaskExecutionLogDetailsDto>()
            .ForAllMembers(m => m.Condition(PropertyNeedsMapping));

        // Redirect mappings
        CreateMap<Redirect, RedirectCreateDto>()
            .ReverseMap()
            .ForMember(dest => dest.IsAutoDiscovered, opt => opt.Ignore());
        CreateMap<Redirect, RedirectUpdateDto>()
            .ForAllMembers(m => m.Condition(PropertyNeedsMapping));
        CreateMap<RedirectUpdateDto, Redirect>()
            .WithPatchDtoSupport()
            .ForMember(dest => dest.IsAutoDiscovered, opt => opt.Ignore())
            .ForAllMembers(m => m.Condition(PropertyNeedsMapping));
        CreateMap<RedirectUpdateDto, RedirectCreateDto>()
            .WithPatchDtoSupport()
            .ForAllMembers(m => m.Condition(PropertyNeedsMapping));
        CreateMap<Redirect, RedirectDetailsDto>()
            .ForAllMembers(m => m.Condition(PropertyNeedsMapping));
    }

    private static bool PropertyNeedsMapping(object source, object target, object sourceValue, object targetValue)
    {
        return sourceValue != null;
    }
}

public class DateTimeToUtcConverter : ITypeConverter<DateTime, DateTime>, ITypeConverter<DateTime?, DateTime?>, ITypeConverter<DateTime?, DateTime>
{
    public DateTime Convert(DateTime source, DateTime destination, ResolutionContext context)
    {
        if (source.Kind == DateTimeKind.Unspecified/* || source.Kind == DateTimeKind.Local*/)
        {
            return DateTime.SpecifyKind(source, DateTimeKind.Utc);
        }

        return source;
    }

    public DateTime? Convert(DateTime? source, DateTime? destination, ResolutionContext context)
    {
        if (source == null)
        {
            return destination;
        }

        return Convert(source.Value, destination ?? DateTime.MinValue, context);
    }

    public DateTime Convert(DateTime? source, DateTime destination, ResolutionContext context)
    {
        if (source == null)
        {
            return destination;
        }

        return Convert(source.Value, destination, context);
    }
}
