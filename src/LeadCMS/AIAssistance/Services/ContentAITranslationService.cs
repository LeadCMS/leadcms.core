// <copyright file="ContentAITranslationService.cs" company="WavePoint Co. Ltd.">
// Licensed under the MIT license. See LICENSE file in the samples root for full license information.
// </copyright>

using System.Text.Json;
using AutoMapper;
using LeadCMS.Constants;
using LeadCMS.Core.AIAssistance.DTOs;
using LeadCMS.Core.AIAssistance.Interfaces;
using LeadCMS.Data;
using LeadCMS.DTOs;
using LeadCMS.Entities;
using LeadCMS.Enums;
using LeadCMS.Helpers;
using LeadCMS.Interfaces;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace LeadCMS.Core.AIAssistance.Services;

public class ContentAITranslationService : IContentAITranslationService
{
    private readonly PgDbContext dbContext;
    private readonly IMapper mapper;
    private readonly ITranslationService translationService;
    private readonly ITextGenerationService textGenerationService;
    private readonly ILanguageValidationService languageValidationService;
    private readonly ISettingService settingService;
    private readonly UserManager<User> userManager;
    private readonly IHttpContextAccessor httpContextAccessor;

    public ContentAITranslationService(
        PgDbContext dbContext,
        IMapper mapper,
        ITranslationService translationService,
        ITextGenerationService textGenerationService,
        ILanguageValidationService languageValidationService,
        ISettingService settingService,
        UserManager<User> userManager,
        IHttpContextAccessor httpContextAccessor)
    {
        this.dbContext = dbContext;
        this.mapper = mapper;
        this.translationService = translationService;
        this.textGenerationService = textGenerationService;
        this.languageValidationService = languageValidationService;
        this.settingService = settingService;
        this.userManager = userManager;
        this.httpContextAccessor = httpContextAccessor;
    }

    public async Task<ContentDetailsDto> CreateAITranslationDraftAsync(int contentId, string targetLanguage)
    {
        // Validate the language is supported
        languageValidationService.ValidateLanguage(targetLanguage);

        // Get the original content with KeepOriginal transformer to have all the data
        var originalDraft = await translationService.CreateTranslationDraftAsync<Content>(
            contentId, targetLanguage, TranslationTransformerType.KeepOriginal);

        // Get the content type to determine the body format
        var contentType = dbContext.ContentTypes != null
            ? await dbContext.ContentTypes.FirstOrDefaultAsync(ct => ct.Uid == originalDraft.Type)
            : null;

        // Translate metadata fields
        var translatedMetadata = await TranslateMetadataAsync(originalDraft, targetLanguage);

        // Translate body content
        var translatedBody = await TranslateBodyAsync(originalDraft.Body, targetLanguage, contentType?.Format);

        // Get current user's display name for AI-translated drafts
        var currentUser = await UserHelper.GetCurrentUserAsync(userManager, httpContextAccessor?.HttpContext?.User);
        var authorName = currentUser?.DisplayName ?? translatedMetadata.Author;

        // Apply translations to the draft
        originalDraft.Title = translatedMetadata.Title;
        originalDraft.Description = translatedMetadata.Description;
        originalDraft.Author = authorName;
        originalDraft.Category = translatedMetadata.Category;
        originalDraft.CoverImageAlt = translatedMetadata.CoverImageAlt;
        originalDraft.Tags = translatedMetadata.Tags;
        originalDraft.Body = translatedBody;

        // Validate content length constraints
        var isValidLength = await ValidateContentLengthAsync(originalDraft.Title, originalDraft.Description);
        if (!isValidLength)
        {
            Log.Warning("Translated content does not meet length constraints, but proceeding with translation");
        }

        // Update source to indicate AI translation
        originalDraft.Source = $"AI translated from {contentId}";

        // Map to DTO and return
        var translatedDto = mapper.Map<ContentDetailsDto>(originalDraft);

        Log.Information("Successfully created AI translation draft for Content Id={ContentId} to language {Language}", contentId, targetLanguage);

        return translatedDto;
    }

    private static ContentTranslationMetadata ValidateAndParseMetadataJson(string jsonText)
    {
        try
        {
            // First validate it's valid JSON
            using var document = JsonDocument.Parse(jsonText);

            // Then deserialize to our metadata object
            var metadata = JsonHelper.Deserialize<ContentTranslationMetadata>(jsonText);

            if (metadata == null)
            {
                throw new InvalidOperationException("Failed to deserialize metadata JSON");
            }

            return metadata;
        }
        catch (JsonException ex)
        {
            throw new InvalidOperationException($"AI generated invalid JSON for metadata: {ex.Message}", ex);
        }
    }

    private static void ValidateJsonContent(string jsonText)
    {
        try
        {
            using var document = JsonDocument.Parse(jsonText);
            // If parsing succeeds, the JSON is valid
        }
        catch (JsonException ex)
        {
            throw new InvalidOperationException($"AI generated invalid JSON content: {ex.Message}", ex);
        }
    }

    private async Task<ContentTranslationMetadata> TranslateMetadataAsync(Content content, string targetLanguage)
    {
        // Get content length constraints from settings/configuration
        var (minTitleLength, maxTitleLength, minDescriptionLength, maxDescriptionLength) = await GetContentLengthConstraintsAsync();

        // Create metadata object for translation
        var metadata = new ContentTranslationMetadata
        {
            Title = content.Title,
            Description = content.Description,
            Author = content.Author,
            Category = content.Category,
            CoverImageAlt = content.CoverImageAlt,
            Tags = content.Tags,
        };

        var metadataJson = JsonHelper.Serialize(metadata);

        var systemPrompt =
$@"You are a professional translator for an AI-powered CMS. Translate the prompted JSON object containing content metadata to {targetLanguage}.

CRITICAL RULES - READ CAREFULLY:
1. Return ONLY valid JSON with the EXACT same structure as the input
2. Translate all text values to {targetLanguage}
3. Keep all JSON property names unchanged - do not translate keys
4. For arrays like 'tags', translate each element while preserving the array structure
5. If a field is empty or null, keep it exactly as is
6. DO NOT add any new properties or remove existing properties
7. Ensure the output is valid, parseable JSON

CONTENT LENGTH REQUIREMENTS:
- Title: {minTitleLength}-{maxTitleLength} characters (SEO optimized)
- Description: {minDescriptionLength}-{maxDescriptionLength} characters (SEO optimized for meta descriptions)

The translated content must respect these length constraints while maintaining the meaning and quality of the original text.";

        var request = new TextGenerationRequest
        {
            SystemPrompt = systemPrompt,
            UserPrompt = metadataJson,
        };

        try
        {
            var response = await textGenerationService.GenerateTextAsync(request);

            // Validate and parse the JSON response
            var translatedMetadata = ValidateAndParseMetadataJson(response.GeneratedText);

            Log.Information("Successfully translated metadata for content to {Language}", targetLanguage);
            return translatedMetadata;
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Failed to translate metadata to {Language}, falling back to original", targetLanguage);
            return metadata; // Fallback to original if translation fails
        }
    }

    private async Task<string> TranslateBodyAsync(string body, string targetLanguage, ContentFormat? format)
    {
        if (string.IsNullOrWhiteSpace(body))
        {
            return body;
        }

        string systemPrompt;
        string formatType = "text";

        switch (format)
        {
            case ContentFormat.MDX:
                formatType = "MDX";
                systemPrompt =
$@"You are a professional translator for an AI-powered CMS, specializing in MDX content translation. Translate the following MDX content to {targetLanguage}.

CRITICAL RULES - STRICT FORMAT PRESERVATION:
1. Preserve ALL MDX components EXACTLY as they appear - do not modify, add, or remove any components
2. Keep ALL component names, props, attributes, and JSX syntax completely unchanged
3. Translate ONLY the human-readable text content (headings, paragraphs, button text, etc.)
4. Keep ALL import statements and export statements unchanged
5. Preserve code blocks and inline code exactly as they appear
6. Keep URLs, file paths, variable names, and technical identifiers unchanged
7. Maintain the EXACT same formatting, indentation, and whitespace structure
8. Return valid MDX that matches the original structure precisely

DO NOT:
- Invent or add new MDX components
- Modify component prop values unless they contain translatable text
- Change the structure or order of components
- Add or remove any elements

The translated output must be identical in structure to the input, with only the human-readable text translated.";
                break;

            case ContentFormat.JSON:
                formatType = "JSON";
                systemPrompt =
$@"You are a professional translator for an AI-powered CMS, specializing in JSON content translation. Translate the following JSON content to {targetLanguage}.

CRITICAL RULES - STRICT STRUCTURE PRESERVATION:
1. Return ONLY valid, parseable JSON with the EXACT same structure
2. Preserve all property names EXACTLY as they appear - do not translate keys
3. Translate ONLY string values that contain human-readable text
4. Keep technical values unchanged: IDs, URLs, code, configuration values, enum values
5. Maintain all data types exactly: strings remain strings, numbers remain numbers, etc.
6. DO NOT add any new properties that don't exist in the original
7. DO NOT remove any properties that exist in the original
8. Preserve the exact nesting structure and array ordering

DO NOT:
- Invent new JSON properties or attributes
- Omit any existing properties (even if they seem redundant)
- Change the data type of any value
- Modify technical or configuration values

The translated output must have identical structure to the input JSON.";
                break;

            case ContentFormat.HTML:
                formatType = "HTML";
                systemPrompt =
$@"You are a professional translator for an AI-powered CMS, specializing in HTML content translation. Translate the following HTML content to {targetLanguage}.

CRITICAL RULES - STRICT STRUCTURE PRESERVATION:
1. Preserve ALL HTML tags, attributes, and structure exactly as they appear
2. Translate ONLY the visible text content between HTML tags
3. Keep ALL attributes unchanged: IDs, classes, data-* attributes, URLs, inline styles
4. Preserve code blocks and technical content exactly
5. Maintain the exact same HTML structure and nesting
6. Return valid HTML that matches the original structure precisely

DO NOT:
- Add new HTML elements or attributes
- Remove existing elements or attributes
- Modify class names, IDs, or other technical attributes
- Change the structure or nesting of elements";
                break;

            default:
                systemPrompt =
$@"You are a professional translator for an AI-powered CMS. Translate the following text content to {targetLanguage}.

TRANSLATION RULES:
1. Translate the text naturally and accurately to {targetLanguage}
2. Preserve the meaning, tone, and intent of the original
3. Keep technical terms that are commonly used untranslated in {targetLanguage}
4. Maintain formatting like line breaks, spacing, and text structure
5. Preserve any markdown formatting elements (headers, lists, bold, italic, etc.)";
                break;
        }

        var request = new TextGenerationRequest
        {
            SystemPrompt = systemPrompt,
            UserPrompt = body,
        };

        try
        {
            var response = await textGenerationService.GenerateTextAsync(request);
            var translatedBody = response.GeneratedText;

            // Validate the translated content based on format
            if (format == ContentFormat.JSON)
            {
                ValidateJsonContent(translatedBody);
            }

            Log.Information("Successfully translated {Format} body content to {Language}", formatType, targetLanguage);
            return translatedBody;
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Failed to translate {Format} body content to {Language}, falling back to original", formatType, targetLanguage);
            return body; // Fallback to original if translation fails
        }
    }

    private async Task<(int minTitleLength, int maxTitleLength, int minDescriptionLength, int maxDescriptionLength)> GetContentLengthConstraintsAsync()
    {
        var minTitleLength = await settingService.GetIntSettingWithFallbackAsync(SettingKeys.MinTitleLength, SettingDefaults.MinTitleLength);
        var maxTitleLength = await settingService.GetIntSettingWithFallbackAsync(SettingKeys.MaxTitleLength, SettingDefaults.MaxTitleLength);
        var minDescriptionLength = await settingService.GetIntSettingWithFallbackAsync(SettingKeys.MinDescriptionLength, SettingDefaults.MinDescriptionLength);
        var maxDescriptionLength = await settingService.GetIntSettingWithFallbackAsync(SettingKeys.MaxDescriptionLength, SettingDefaults.MaxDescriptionLength);

        return (minTitleLength, maxTitleLength, minDescriptionLength, maxDescriptionLength);
    }

    private async Task<bool> ValidateContentLengthAsync(string title, string description)
    {
        var (minTitleLength, maxTitleLength, minDescriptionLength, maxDescriptionLength) = await GetContentLengthConstraintsAsync();

        bool titleValid = title.Length >= minTitleLength && title.Length <= maxTitleLength;
        bool descriptionValid = description.Length >= minDescriptionLength && description.Length <= maxDescriptionLength;

        if (!titleValid)
        {
            Log.Warning(
                "Translated title length {TitleLength} is outside valid range {MinTitle}-{MaxTitle}",
                title.Length,
                minTitleLength,
                maxTitleLength);
        }

        if (!descriptionValid)
        {
            Log.Warning(
                "Translated description length {DescriptionLength} is outside valid range {MinDescription}-{MaxDescription}",
                description.Length,
                minDescriptionLength,
                maxDescriptionLength);
        }

        return titleValid && descriptionValid;
    }
}
