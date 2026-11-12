// <copyright file="ContentGenerationService.cs" company="WavePoint Co. Ltd.">
// Licensed under the MIT license. See LICENSE file in the samples root for full license information.
// </copyright>

using System.Text;
using System.Text.Json;
using AutoMapper;
using LeadCMS.Constants;
using LeadCMS.Core.AIAssistance.Configuration;
using LeadCMS.Core.AIAssistance.DTOs;
using LeadCMS.Core.AIAssistance.Exceptions;
using LeadCMS.Core.AIAssistance.Interfaces;
using LeadCMS.Data;
using LeadCMS.DTOs;
using LeadCMS.Entities;
using LeadCMS.Helpers;
using LeadCMS.Interfaces;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace LeadCMS.Core.AIAssistance.Services;

public class ContentGenerationService : IContentGenerationService
{
    private readonly PgDbContext dbContext;
    private readonly ITextGenerationService textGenerationService;
    private readonly IMdxComponentParserService mdxComponentParserService;
    private readonly IMapper mapper;
    private readonly ISettingService settingService;
    private readonly UserManager<User> userManager;
    private readonly IHttpContextAccessor httpContextAccessor;

    public ContentGenerationService(
        PgDbContext dbContext,
        ITextGenerationService textGenerationService,
        IMdxComponentParserService mdxComponentParserService,
        IMapper mapper,
        ISettingService settingService,
        UserManager<User> userManager,
        IHttpContextAccessor httpContextAccessor)
    {
        this.dbContext = dbContext;
        this.textGenerationService = textGenerationService;
        this.mdxComponentParserService = mdxComponentParserService;
        this.mapper = mapper;
        this.settingService = settingService;
        this.userManager = userManager;
        this.httpContextAccessor = httpContextAccessor;
    }

    public async Task<ContentCreateDto> GenerateContentAsync(ContentGenerationRequest request)
    {
        Log.Information("Starting content generation for type {ContentType} in language {Language}", request.ContentType, request.Language);

        // Step 1: Validate content type exists
        var contentType = await dbContext.ContentTypes!
            .FirstOrDefaultAsync(ct => ct.Uid == request.ContentType);

        if (contentType == null)
        {
            throw new AIContentTypeNotFoundException(request.ContentType);
        }

        // Step 2: Find sample content records
        var sampleContent = await FindSampleContentAsync(request.ContentType, request.Language, request.ReferenceContentId);

        if (sampleContent == null)
        {
            throw new AIProviderException(
                "ContentGeneration",
                "Not enough data in the database for the AI assistant to generate new content records. Please create at least one content record of this type first.");
        }

        // Step 3: Get MDX component information if it's an MDX content type
        MdxComponentAnalysisDto? componentAnalysis = null;
        if (contentType.Format == ContentFormat.MDX || contentType.Format == ContentFormat.MD)
        {
            try
            {
                componentAnalysis = await mdxComponentParserService.AnalyzeContentTypeAsync(request.ContentType);
            }
            catch (Exception ex)
            {
                Log.Warning(ex, "Failed to analyze MDX components for content type {ContentType}, proceeding without component information", request.ContentType);
            }
        }

        // Step 4: Build prompts and generate content
        var requiredMediaSection = await BuildRequiredMediaSectionAsync(request.RequiredMediaPaths);
        var systemPrompt = await BuildSystemPromptAsync(contentType, sampleContent, componentAnalysis, requiredMediaSection);
        var userPrompt = BuildUserPrompt(request.Prompt, request.Language, request.CharacterCount, request.WordCount, requiredMediaSection);

        var requiredMediaInputs = await BuildRequiredMediaInputsAsync(request.RequiredMediaPaths);
        var textRequest = new TextGenerationRequest
        {
            SystemPrompt = systemPrompt,
            UserPrompt = userPrompt,
            Images = requiredMediaInputs,
        };

        try
        {
            var response = await textGenerationService.GenerateTextAsync(textRequest);

            // Parse the generated JSON content
            var generatedContent = ParseGeneratedContent(response.GeneratedText);

            if (contentType.Format == ContentFormat.MDX || contentType.Format == ContentFormat.MD)
            {
                generatedContent.Body = NormalizeYamlFrontMatter(generatedContent.Body);
            }

            // Validate content length constraints
            var isValidLength = await ValidateContentLengthAsync(generatedContent.Title, generatedContent.Description);
            if (!isValidLength)
            {
                Log.Warning("Generated content does not meet length constraints, but proceeding with generation");
            }

            // Get current user's display name for AI-generated drafts
            var currentUser = await UserHelper.GetCurrentUserAsync(userManager, httpContextAccessor?.HttpContext?.User);
            var authorName = currentUser?.DisplayName ?? generatedContent.Author ?? sampleContent.Author;

            // Create a Content entity with the generated data
            var contentEntity = new Content
            {
                Title = generatedContent.Title,
                Description = generatedContent.Description,
                Body = generatedContent.Body,
                Slug = generatedContent.Slug,
                Author = authorName,
                Language = request.Language,
                Category = generatedContent.Category ?? sampleContent.Category,
                Tags = generatedContent.Tags ?? sampleContent.Tags,
                CoverImageAlt = generatedContent.CoverImageAlt ?? string.Empty,
                Type = request.ContentType,
                AllowComments = sampleContent.AllowComments,
                PublishedAt = DateTime.UtcNow,
                Source = $"AI Generated - Model: {response.Model}, Tokens: {response.TokensUsed}",
            };

            // Map to ContentDetailsDto
            var result = mapper.Map<ContentCreateDto>(contentEntity);

            Log.Information("Successfully generated content for type {ContentType} in language {Language}", request.ContentType, request.Language);
            return result;
        }
        catch (AIProviderException)
        {
            throw;
        }
        catch (JsonException ex)
        {
            throw new AIProviderException("ContentGeneration", $"Failed to parse AI response as JSON: {ex.Message}", ex);
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Failed to generate content for type {ContentType} in language {Language}", request.ContentType, request.Language);
            throw new AIProviderException("ContentGeneration", "Failed to generate content", ex);
        }
    }

    public async Task<ContentCreateDto> GenerateContentEditAsync(ContentEditRequest request)
    {
        var requiredMediaSection = await BuildRequiredMediaSectionAsync(request.RequiredMediaPaths);
        MdxComponentAnalysisDto? componentAnalysis = null;
        ContentFormat? contentFormat = null;

        if (!string.IsNullOrWhiteSpace(request.Type))
        {
            var contentType = await dbContext.ContentTypes!
                .FirstOrDefaultAsync(ct => ct.Uid == request.Type);

            if (contentType != null)
            {
                contentFormat = contentType.Format;

                if (contentFormat == ContentFormat.MDX || contentFormat == ContentFormat.MD)
                {
                    try
                    {
                        componentAnalysis = await mdxComponentParserService.AnalyzeContentTypeAsync(request.Type);
                    }
                    catch (Exception ex)
                    {
                        Log.Warning(ex, "Failed to analyze MDX components for content type {ContentType}, proceeding without component information", request.Type);
                    }
                }
            }
        }

        var systemPrompt = await BuildEditSystemPromptAsync(requiredMediaSection, contentFormat, componentAnalysis, request.Type);
        var userPrompt = BuildEditUserPrompt(request, request.Prompt, request.CharacterCount, request.WordCount, requiredMediaSection);

        var requiredMediaInputs = await BuildRequiredMediaInputsAsync(request.RequiredMediaPaths);
        var textRequest = new TextGenerationRequest
        {
            SystemPrompt = systemPrompt,
            UserPrompt = userPrompt,
            Images = requiredMediaInputs,
        };

        try
        {
            var response = await textGenerationService.GenerateTextAsync(textRequest);

            // Parse the generated JSON content
            var contentData = JsonSerializer.Deserialize<JsonElement>(response.GeneratedText);

            var title = contentData.TryGetProperty("title", out var titleProp) ? (titleProp.GetString() ?? request.Title ?? string.Empty) : (request.Title ?? string.Empty);
            var description = contentData.TryGetProperty("description", out var descProp) ? (descProp.GetString() ?? request.Description ?? string.Empty) : (request.Description ?? string.Empty);
            var body = contentData.TryGetProperty("body", out var bodyProp) ? (bodyProp.GetString() ?? request.Body ?? string.Empty) : (request.Body ?? string.Empty);

            if (contentFormat == ContentFormat.MDX || contentFormat == ContentFormat.MD)
            {
                body = NormalizeYamlFrontMatter(body);
            }

            // Validate content length constraints
            var isValidLength = await ValidateContentLengthAsync(title, description);
            if (!isValidLength)
            {
                Log.Warning("Edited content does not meet length constraints, but proceeding with edit");
            }

            return new ContentCreateDto
            {
                Title = title,
                Slug = contentData.TryGetProperty("slug", out var slugProp) ? (slugProp.GetString() ?? request.Slug ?? string.Empty) : (request.Slug ?? string.Empty),
                Description = description,
                Body = body,
                Tags = contentData.TryGetProperty("tags", out var tagsProp) ? GetStringArrayProperty(contentData, "tags") : (request.Tags ?? Array.Empty<string>()),
                Category = contentData.TryGetProperty("category", out var categoryProp) ? (categoryProp.GetString() ?? request.Category ?? string.Empty) : (request.Category ?? string.Empty),
                Author = request.Author ?? string.Empty,
                Type = request.Type ?? string.Empty,
                Language = request.Language ?? string.Empty,
                CoverImageUrl = request.CoverImageUrl,
                CoverImageAlt = request.CoverImageAlt,
                TranslationKey = request.TranslationKey,
                AllowComments = request.AllowComments ?? false,
                Source = request.Source,
                PublishedAt = request.PublishedAt,
            };
        }
        catch (AIProviderException)
        {
            throw;
        }
        catch (JsonException ex)
        {
            throw new AIProviderException("ContentGeneration", $"Failed to parse AI response as JSON: {ex.Message}", ex);
        }
        catch (Exception ex)
        {
            throw new AIProviderException("ContentGeneration", $"Failed to generate content edit: {ex.Message}", ex);
        }
    }

    private static string BuildLengthConstraintInstruction(int? characterCount, int? wordCount)
    {
        if (characterCount.HasValue && characterCount.Value > 0)
        {
            return $@"
BODY LENGTH REQUIREMENT:
- The body content MUST be approximately {characterCount.Value} characters (±10%)
";
        }

        if (wordCount.HasValue && wordCount.Value > 0)
        {
            return $@"
BODY LENGTH REQUIREMENT:
- The body content MUST be approximately {wordCount.Value} words (±10%)
";
        }

        return string.Empty;
    }

    private static (string ScopeUid, string FileName, string ResolvedUrl) ParseMediaUrl(string url)
    {
        if (string.IsNullOrWhiteSpace(url))
        {
            return (string.Empty, string.Empty, string.Empty);
        }

        var path = url.Trim();
        var resolvedUrl = path;

        if (Uri.TryCreate(path, UriKind.Absolute, out var uri))
        {
            path = uri.AbsolutePath;
            resolvedUrl = path;
        }

        const string apiMediaPrefix = "/api/media/";
        if (path.StartsWith(apiMediaPrefix, StringComparison.OrdinalIgnoreCase))
        {
            path = path.Substring(apiMediaPrefix.Length);
            resolvedUrl = "/api/media/" + path;
        }

        const string apiMediaPrefixNoSlash = "api/media/";
        if (path.StartsWith(apiMediaPrefixNoSlash, StringComparison.OrdinalIgnoreCase))
        {
            path = path.Substring(apiMediaPrefixNoSlash.Length);
            resolvedUrl = "/api/media/" + path;
        }

        var lastSlash = path.LastIndexOf('/');
        if (lastSlash <= 0)
        {
            return (string.Empty, string.Empty, resolvedUrl);
        }

        var scopeUid = path.Substring(0, lastSlash);
        var fileName = path.Substring(lastSlash + 1);

        resolvedUrl = string.IsNullOrWhiteSpace(resolvedUrl)
            ? $"/api/media/{scopeUid}/{fileName}"
            : resolvedUrl;

        return (scopeUid, fileName, resolvedUrl);
    }

    private async Task<Content?> FindSampleContentAsync(string contentType, string language, int? referenceContentId)
    {
        if (referenceContentId.HasValue)
        {
            var referencedContent = await dbContext.Content!
                .FirstOrDefaultAsync(c => c.Id == referenceContentId.Value);

            if (referencedContent == null)
            {
                throw new AIProviderException(
                    "ContentGeneration",
                    $"Reference content with id {referenceContentId.Value} was not found.");
            }

            if (!string.Equals(referencedContent.Type, contentType, StringComparison.OrdinalIgnoreCase))
            {
                throw new AIProviderException(
                    "ContentGeneration",
                    $"Reference content id {referenceContentId.Value} does not match content type '{contentType}'.");
            }

            return referencedContent;
        }

        // First try to find content with the same language
        var sampleContent = await dbContext.Content!
            .Where(c => c.Type == contentType && c.Language == language)
            .OrderByDescending(c => c.CreatedAt)
            .FirstOrDefaultAsync();

        // If no content with the same language, try any language
        if (sampleContent == null)
        {
            sampleContent = await dbContext.Content!
                .Where(c => c.Type == contentType)
                .OrderByDescending(c => c.CreatedAt)
                .FirstOrDefaultAsync();
        }

        return sampleContent;
    }

    private async Task<List<string>> AnalyzeSlugPatternsAsync(string contentType, int maxExamples = 5)
    {
        // Get existing slug patterns for the same content type
        var existingSlugs = await dbContext.Content!
            .Where(c => c.Type == contentType)
            .OrderByDescending(c => c.CreatedAt)
            .Take(maxExamples)
            .Select(c => c.Slug)
            .Where(s => !string.IsNullOrEmpty(s))
            .ToListAsync();

        return existingSlugs;
    }

    private async Task<string> BuildSystemPromptAsync(ContentType contentType, Content sampleContent, MdxComponentAnalysisDto? componentAnalysis, string requiredMediaSection)
    {
        // Get content length constraints from settings/configuration
        var (minTitleLength, maxTitleLength, minDescriptionLength, maxDescriptionLength) = await GetContentLengthConstraintsAsync();

        // Analyze slug patterns from existing content of the same type
        var existingSlugPatterns = await AnalyzeSlugPatternsAsync(contentType.Uid);

        var siteProfileSection = await BuildSiteProfileSectionAsync();
        var recentMediaSection = await BuildRecentMediaSectionAsync(contentType.Uid);

        // Truncate body sample to reasonable size while preserving structure
        var bodySampleLength = Math.Min(50000, sampleContent.Body.Length);
        var bodySample = sampleContent.Body.Length > bodySampleLength
            ? sampleContent.Body.Substring(0, bodySampleLength) + "\n... [truncated]"
            : sampleContent.Body;

        var prompt = $@"You are a content generation assistant for an AI-powered CMS designed to quickly generate landing pages, blog posts, and other content. Your task is to generate a new {contentType.Uid} content record that precisely matches the structure, style, and format of existing content in the CMS.

CRITICAL RULES - READ CAREFULLY:
1. DO NOT HALLUCINATE OR INVENT: Never create new structures, components, or attributes that are not present in the sample content or the provided component list.
2. MATCH FORMAT CONVENTIONS: The generated content must match the format ({contentType.Format}) and syntax conventions of the sample content.
3. REUSE PATTERNS WITH FLEXIBILITY: You may reorder, mix, or repeat supported sections/components and you may use only a subset of them, as long as the result is coherent and useful.
4. When the user's prompt is ambiguous, use the SITE PROFILE and SAMPLE CONTENT to infer the most appropriate interpretation.

SAMPLE CONTENT (use this as your template - match its structure exactly):
Title: {sampleContent.Title}
Description: {sampleContent.Description}
Author: {sampleContent.Author}
Category: {sampleContent.Category}
Tags: {JsonHelper.Serialize(sampleContent.Tags)}
Language: {sampleContent.Language}
Cover Image Alt: {sampleContent.CoverImageAlt}
Body Format: {contentType.Format}

SAMPLE BODY CONTENT (use this as a style/pattern reference, not a fixed component order):
{bodySample}";

        if (!string.IsNullOrEmpty(siteProfileSection))
        {
            prompt += $@"

SITE PROFILE (use this to understand site context and resolve any ambiguity in user requests):
{siteProfileSection}";
        }

        if (!string.IsNullOrEmpty(recentMediaSection))
        {
            prompt += $@"

AVAILABLE MEDIA (recent — prioritized for {contentType.Uid} content type):
Each line is scopeUid|fileName|description (optionally |widthxheight)
If any item fits the new article, reuse it in the body where it makes sense.
Build URLs as: /api/media/{{scopeUid}}/{{fileName}}
{recentMediaSection}";
        }

        if (!string.IsNullOrEmpty(requiredMediaSection))
        {
            prompt += $@"

REQUIRED MEDIA (must include ALL of these in the body):
Each line is url|description
You MUST place each image in the body using the same formatting style as the sample content.
If no image format is shown in the sample, use Markdown image syntax: ![alt](url)
{requiredMediaSection}";
        }

        if (contentType.Format == ContentFormat.MDX || contentType.Format == ContentFormat.MD)
        {
            if (componentAnalysis != null && componentAnalysis.Components.Any())
            {
                prompt += $@"

MDX COMPONENTS - STRICT ALLOWLIST:
You may ONLY use the following MDX components. DO NOT invent or use any components not listed here:
{string.Join("\n", componentAnalysis.Components.Select(c => FormatComponentInfo(c)))}

IMPORTANT MDX RULES:
- ONLY use components from the list above - do not invent new components
- Follow the exact prop structure shown in the examples
- If the sample content uses HTML elements, you may use the SAME HTML patterns - do not invent new HTML structures
- Match the exact indentation and formatting style from the sample body
- Component order is flexible: you may reorder, mix, and reuse allowed components when it improves the narrative
- Using a subset of allowed components is acceptable if the page remains complete and coherent
- If you include YAML frontmatter (between --- lines), it MUST be valid YAML
- Quote any single-line YAML value containing ':' with double quotes (example: SeoTitle: ""A: B"")";
            }
            else
            {
                prompt += $@"

MDX/MARKDOWN RULES:
- Use ONLY standard Markdown syntax and patterns present in the sample content
- DO NOT use custom MDX components unless they appear in the sample content
- If the sample uses any HTML, replicate only the EXACT same HTML patterns - do not invent new HTML structures
- The overall section order does not need to mirror the sample exactly; prioritize a logical flow and a meaningful story
- If you include YAML frontmatter (between --- lines), it MUST be valid YAML
- Quote any single-line YAML value containing ':' with double quotes (example: SeoTitle: ""A: B"")";
            }
        }
        else if (contentType.Format == ContentFormat.JSON)
        {
            prompt += $@"

JSON FORMAT RULES:
- The body MUST be valid JSON matching the EXACT structure shown in the sample body
- DO NOT add new properties that are not present in the sample
- DO NOT omit required properties that exist in the sample
- Preserve all property names exactly as shown
- Match data types (strings, numbers, arrays, objects) exactly as in the sample";
        }

        // Add slug pattern guidance if we have examples
        if (existingSlugPatterns.Any())
        {
            prompt += $@"

SLUG PATTERN EXAMPLES (follow this convention):
{string.Join("\n", existingSlugPatterns.Select(slug => $"- {slug}"))}";
        }

        prompt += $@"

CONTENT LENGTH REQUIREMENTS:
- Title: {minTitleLength}-{maxTitleLength} characters (SEO optimized)
- Description: {minDescriptionLength}-{maxDescriptionLength} characters (SEO optimized for meta descriptions)

OUTPUT FORMAT - Return ONLY valid JSON with this exact structure:
{{
  ""title"": ""Generated title"",
  ""description"": ""Generated description"",
  ""body"": ""Generated body content in {contentType.Format} format"",
  ""slug"": ""url-friendly-slug"",
  ""author"": ""Author name"",
  ""category"": ""Category name"",
  ""tags"": [""tag1"", ""tag2""],
  ""coverImageAlt"": ""Alt text for cover image""
}}

DISAMBIGUATION STRATEGY:
If the user's request is unclear or could be interpreted multiple ways:
1. Refer to the SITE PROFILE for context about the site's topic, audience, and voice
2. Use the SAMPLE CONTENT as a reference for appropriate style, tone, and structure
3. Make reasonable inferences based on existing content patterns
4. Prefer conservative choices that match existing content over creative inventions";

        return prompt;
    }

    private async Task<string> BuildRecentMediaSectionAsync(string? contentTypeUid = null)
    {
        const int maxPromptMediaItems = 200;
        const int prefetchMediaItems = 2000;

        var contentTypeTag = !string.IsNullOrWhiteSpace(contentTypeUid)
            ? contentTypeUid.Trim().ToLowerInvariant()
            : null;

        var mediaItems = await dbContext.Media!
            .AsNoTracking()
            .OrderByDescending(m => m.UsageCount)
            .ThenByDescending(m => m.UpdatedAt ?? m.CreatedAt)
            .Take(prefetchMediaItems)
            .Select(m => new
            {
                m.ScopeUid,
                m.Name,
                m.Description,
                m.Width,
                m.Height,
                m.Tags,
                m.UsageCount,
                m.UpdatedAt,
                m.CreatedAt,
            })
            .ToListAsync();

        if (mediaItems.Count == 0)
        {
            return string.Empty;
        }

        static bool HasTag(string[] tags, string? tag)
        {
            if (string.IsNullOrWhiteSpace(tag) || tags.Length == 0)
            {
                return false;
            }

            return Array.Exists(tags, t => string.Equals(t, tag, StringComparison.OrdinalIgnoreCase));
        }

        var orderedItems = mediaItems
            .OrderByDescending(m => HasTag(m.Tags, contentTypeTag) && m.UsageCount > 0)
            .ThenByDescending(m => HasTag(m.Tags, contentTypeTag))
            .ThenByDescending(m => m.UsageCount)
            .ThenByDescending(m => m.UpdatedAt ?? m.CreatedAt)
            .Take(maxPromptMediaItems)
            .ToList();

        var lines = orderedItems
            .Select(m =>
            {
                var description = string.IsNullOrWhiteSpace(m.Description)
                    ? m.Name
                    : m.Description.Trim();

                var dimensions = m.Width.HasValue && m.Height.HasValue
                    ? $"{m.Width}x{m.Height}"
                    : string.Empty;

                return string.IsNullOrEmpty(dimensions)
                    ? $"{m.ScopeUid}|{m.Name}|{description}"
                    : $"{m.ScopeUid}|{m.Name}|{description}|{dimensions}";
            })
            .ToList();

        return lines.Count == 0
            ? string.Empty
            : string.Join("\n", lines);
    }

    private async Task<(int minTitleLength, int maxTitleLength, int minDescriptionLength, int maxDescriptionLength)> GetContentLengthConstraintsAsync()
    {
        // Use the new convention-based method calls
        var minTitleLength = await settingService.GetIntSettingWithFallbackAsync(
            SettingKeys.MinTitleLength,
            SettingDefaults.MinTitleLength);

        var maxTitleLength = await settingService.GetIntSettingWithFallbackAsync(
            SettingKeys.MaxTitleLength,
            SettingDefaults.MaxTitleLength);

        var minDescriptionLength = await settingService.GetIntSettingWithFallbackAsync(
            SettingKeys.MinDescriptionLength,
            SettingDefaults.MinDescriptionLength);

        var maxDescriptionLength = await settingService.GetIntSettingWithFallbackAsync(
            SettingKeys.MaxDescriptionLength,
            SettingDefaults.MaxDescriptionLength);

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
                "Generated title length {TitleLength} is outside valid range {MinTitle}-{MaxTitle}",
                title.Length,
                minTitleLength,
                maxTitleLength);
        }

        if (!descriptionValid)
        {
            Log.Warning(
                "Generated description length {DescriptionLength} is outside valid range {MinDescription}-{MaxDescription}",
                description.Length,
                minDescriptionLength,
                maxDescriptionLength);
        }

        return titleValid && descriptionValid;
    }

    private string FormatComponentInfo(MdxComponentDto component)
    {
        var props = component.Properties.Any()
            ? string.Join(", ", component.Properties.Select(p => $"{p.Name}: {p.Type}"))
            : "no props";

        var example = component.Examples.FirstOrDefault() ?? $"<{component.Name} />";

        return $"- {component.Name} ({props}) - Example: {example}";
    }

    private string BuildUserPrompt(string userPrompt, string language, int? characterCount, int? wordCount, string requiredMediaSection)
    {
        var lengthConstraint = BuildLengthConstraintInstruction(characterCount, wordCount);

        var prompt = $@"Generate new content in {language} based on this request:

{userPrompt}
{lengthConstraint}
IMPORTANT REMINDERS:
- If any part of this request is unclear, use the site profile and sample content to make informed decisions
- Match the exact structure and format demonstrated in the sample content
- Do not invent new components, attributes, or patterns not shown in the sample
- Return only the JSON structure as specified in the system prompt";

        if (!string.IsNullOrEmpty(requiredMediaSection))
        {
            prompt += $@"

You MUST include all REQUIRED MEDIA in the body. Do not omit any required image URLs.";
        }

        return prompt;
    }

    private ContentCreateDto ParseGeneratedContent(string generatedJson)
    {
        try
        {
            using var document = JsonDocument.Parse(generatedJson);

            var root = document.RootElement;

            return new ContentCreateDto
            {
                Title = GetStringProperty(root, "title"),
                Description = GetStringProperty(root, "description"),
                Body = GetStringProperty(root, "body"),
                Slug = GetStringProperty(root, "slug"),
                Author = GetStringProperty(root, "author"),
                Category = GetStringProperty(root, "category"),
                Tags = GetStringArrayProperty(root, "tags"),
                CoverImageAlt = GetStringProperty(root, "coverImageAlt"),
            };
        }
        catch (JsonException ex)
        {
            Log.Error(ex, "Failed to parse generated content JSON: {Json}", generatedJson);
            throw new AIProviderException("ContentGeneration", $"AI generated invalid JSON content: {ex.Message}", ex);
        }
    }

    private string GetStringProperty(JsonElement element, string propertyName)
    {
        return element.TryGetProperty(propertyName, out var prop) && prop.ValueKind == JsonValueKind.String
            ? prop.GetString() ?? string.Empty
            : string.Empty;
    }

    private string[] GetStringArrayProperty(JsonElement element, string propertyName)
    {
        if (!element.TryGetProperty(propertyName, out var prop) || prop.ValueKind != JsonValueKind.Array)
        {
            return Array.Empty<string>();
        }

        var result = new List<string>();
        foreach (var item in prop.EnumerateArray())
        {
            if (item.ValueKind == JsonValueKind.String)
            {
                var value = item.GetString();
                if (!string.IsNullOrEmpty(value))
                {
                    result.Add(value);
                }
            }
        }

        return result.ToArray();
    }

    private void AddIfPresent(List<string> items, string label, List<Setting> settings, string key)
    {
        var setting = settings.FirstOrDefault(s => s.Key == key);
        if (setting != null && !string.IsNullOrWhiteSpace(setting.Value))
        {
            items.Add($"{label}: {setting.Value.Trim()}");
        }
    }

    private async Task<string> BuildSiteProfileSectionAsync()
    {
        var keys = new[]
        {
            AiSettingKeys.SiteTopic,
            AiSettingKeys.SiteAudience,
            AiSettingKeys.BrandVoice,
            AiSettingKeys.PreferredTerms,
            AiSettingKeys.AvoidTerms,
            AiSettingKeys.StyleExamples,
        };

        var settings = await settingService.FindSettingsByKeysAsync(keys);

        var items = new List<string>();
        AddIfPresent(items, "Topic", settings, AiSettingKeys.SiteTopic);
        AddIfPresent(items, "Audience", settings, AiSettingKeys.SiteAudience);
        AddIfPresent(items, "Voice", settings, AiSettingKeys.BrandVoice);
        AddIfPresent(items, "Preferred terms", settings, AiSettingKeys.PreferredTerms);
        AddIfPresent(items, "Avoid", settings, AiSettingKeys.AvoidTerms);
        AddIfPresent(items, "Style examples", settings, AiSettingKeys.StyleExamples);

        if (items.Count == 0)
        {
            return string.Empty;
        }

        return string.Join("\n", items.Select(i => $"- {i}"));
    }

    private async Task<string> BuildEditSystemPromptAsync(string requiredMediaSection, ContentFormat? contentFormat, MdxComponentAnalysisDto? componentAnalysis, string? contentTypeUid = null)
    {
        // Get content length constraints from settings/configuration
        var (minTitleLength, maxTitleLength, minDescriptionLength, maxDescriptionLength) = await GetContentLengthConstraintsAsync();

        var siteProfileSection = await BuildSiteProfileSectionAsync();
        var recentMediaSection = await BuildRecentMediaSectionAsync(contentTypeUid);

        var prompt = $@"You are a content editor assistant for an AI-powered CMS. Your task is to edit existing content based on user prompts while strictly preserving the original format and structure.

CRITICAL RULES - READ CAREFULLY:
1. PRESERVE FORMAT: Keep the exact same format (MDX, JSON, Markdown, HTML) as the original content
2. NO HALLUCINATION: Do not add new MDX components, HTML elements, or JSON attributes that are not present in the original content
3. MAINTAIN STRUCTURE: Keep the overall structure and organization of the original content
4. CONSERVATIVE EDITS: When the user's request is ambiguous, make the minimum changes necessary to fulfill the request
5. If the content uses MDX components, preserve them exactly - do not invent new components or props unless specifically asked to add a component that is in the allowlist.
6. If the content uses HTML, preserve only the exact same HTML patterns.

EDITING GUIDELINES:
- Apply the user's requested changes while preserving the core structure
- Maintain the tone and style consistent with the original content
- Ensure all content is factual and well-written
- If content contains MDX components or special markup, keep them intact unless specifically asked to modify";

        if (!string.IsNullOrEmpty(siteProfileSection))
        {
            prompt += $@"

SITE PROFILE (use this to understand context and resolve ambiguity):
{siteProfileSection}";
        }

        if (!string.IsNullOrEmpty(recentMediaSection))
        {
            var contentTypeLabel = !string.IsNullOrWhiteSpace(contentTypeUid) ? $" \u2014 prioritized for {contentTypeUid} content type" : string.Empty;
            prompt += $@"

AVAILABLE MEDIA (recent{contentTypeLabel}):
Each line is scopeUid|fileName|description (optionally |widthxheight)
If any item fits the edit request, reuse it in the body where it makes sense.
Build URLs as: /api/media/{{scopeUid}}/{{fileName}}
{recentMediaSection}";
        }

        if (!string.IsNullOrEmpty(requiredMediaSection))
        {
            prompt += $@"

REQUIRED MEDIA (must include ALL of these in the body):
Each line is url|description
You MUST place each image in the body using the same formatting style as the existing content.
If no image format is shown, use Markdown image syntax: ![alt](url)
{requiredMediaSection}";
        }

        if (contentFormat == ContentFormat.MDX || contentFormat == ContentFormat.MD)
        {
            if (componentAnalysis != null && componentAnalysis.Components.Any())
            {
                prompt += $@"

MDX COMPONENTS - STRICT ALLOWLIST:
You may ONLY use the following MDX components. DO NOT invent or use any components not listed here:
{string.Join("\n", componentAnalysis.Components.Select(c => FormatComponentInfo(c)))}

IMPORTANT MDX RULES:
- ONLY use components from the list above - do not invent new components
- Follow the exact prop structure shown in the examples (do not add, remove, or rename props)
- If the user asks to add or change a component, only use components and props from this allowlist
- If the content uses HTML elements, preserve only the exact same HTML patterns
- Match the exact indentation and formatting style from the original body
- If the body includes YAML frontmatter (between --- lines), keep it valid YAML
- Quote any single-line YAML value containing ':' with double quotes (example: SeoTitle: ""A: B"")";
            }
            else
            {
                prompt += $@"

MDX/MARKDOWN RULES:
- Use ONLY standard Markdown syntax and patterns present in the original content
- DO NOT use custom MDX components unless they appear in the original content
- If the content uses any HTML, preserve only the exact same HTML patterns
- If the body includes YAML frontmatter (between --- lines), keep it valid YAML
- Quote any single-line YAML value containing ':' with double quotes (example: SeoTitle: ""A: B"")";
            }
        }

        prompt += $@"

CONTENT LENGTH REQUIREMENTS:
- Title: {minTitleLength}-{maxTitleLength} characters (SEO optimized)
- Description: {minDescriptionLength}-{maxDescriptionLength} characters (SEO optimized for meta descriptions)

DISAMBIGUATION STRATEGY:
If the user's edit request is unclear:
1. Refer to the SITE PROFILE for context about the site's topic, audience, and voice
2. Preserve as much of the original content as possible
3. Make the minimal changes needed to address the request
4. Maintain consistency with the original content's style and format

OUTPUT FORMAT - Return ONLY valid JSON with this exact structure:
{{
  ""title"": ""Edited article title"",
  ""slug"": ""url-friendly-slug"",
  ""description"": ""Brief summary/meta description"",
  ""body"": ""Main content body (preserve original formatting)"",
  ""tags"": [""tag1"", ""tag2""],
  ""category"": ""category name""
}}";

        return prompt;
    }

    private string BuildEditUserPrompt(ContentEditRequest contentData, string userPrompt, int? characterCount, int? wordCount, string requiredMediaSection)
    {
        var lengthConstraint = BuildLengthConstraintInstruction(characterCount, wordCount);

        var prompt = $@"Edit the following content based on this request:

{userPrompt}
{lengthConstraint}
CURRENT CONTENT TO EDIT:
Title: {contentData.Title ?? "[No title]"}
Slug: {contentData.Slug ?? "[No slug]"}
Description: {contentData.Description ?? "[No description]"}
Tags: {string.Join(", ", contentData.Tags ?? Array.Empty<string>())}
Category: {contentData.Category ?? "[No category]"}

Body:
{contentData.Body ?? "[No content]"}

IMPORTANT REMINDERS:
- Preserve the exact format and structure of the original body content
- Do not add new components, elements, or attributes not present in the original
- If the request is unclear, use the site profile for context and make conservative changes
- Return only the JSON structure as specified";

        if (!string.IsNullOrEmpty(requiredMediaSection))
        {
            prompt += $@"

You MUST include all REQUIRED MEDIA in the body. Do not omit any required image URLs.";
        }

        return prompt;
    }

    private string NormalizeYamlFrontMatter(string body)
    {
        if (string.IsNullOrWhiteSpace(body))
        {
            return body;
        }

        var hasCrLf = body.Contains("\r\n", StringComparison.Ordinal);
        var normalized = body.Replace("\r\n", "\n", StringComparison.Ordinal);

        if (!normalized.StartsWith("---\n", StringComparison.Ordinal))
        {
            return body;
        }

        var closingIndex = normalized.IndexOf("\n---\n", 4, StringComparison.Ordinal);
        if (closingIndex < 0)
        {
            return body;
        }

        var frontMatter = normalized.Substring(4, closingIndex - 4);
        var fixedFrontMatter = FixFrontMatterInlineScalars(frontMatter);

        if (string.Equals(frontMatter, fixedFrontMatter, StringComparison.Ordinal))
        {
            return body;
        }

        var result = string.Concat("---\n", fixedFrontMatter, "\n---\n", normalized.AsSpan(closingIndex + 5));
        return hasCrLf ? result.Replace("\n", "\r\n", StringComparison.Ordinal) : result;
    }

    private string FixFrontMatterInlineScalars(string frontMatter)
    {
        var lines = frontMatter.Split('\n');
        var output = new StringBuilder(frontMatter.Length + 64);
        var inBlockScalar = false;

        foreach (var line in lines)
        {
            var trimmed = line.TrimStart();

            if (inBlockScalar)
            {
                if (trimmed.Length == 0 || (line.Length > 0 && char.IsWhiteSpace(line[0])))
                {
                    output.AppendLine(line);
                    continue;
                }

                inBlockScalar = false;
            }

            if (trimmed.Length == 0 || trimmed.StartsWith('#'))
            {
                output.AppendLine(line);
                continue;
            }

            var separatorIndex = trimmed.IndexOf(':');
            if (separatorIndex <= 0)
            {
                output.AppendLine(line);
                continue;
            }

            var key = trimmed.Substring(0, separatorIndex).Trim();
            var value = trimmed.Substring(separatorIndex + 1).TrimStart();

            if (value.StartsWith('|') || value.StartsWith('>'))
            {
                inBlockScalar = true;
                output.AppendLine(line);
                continue;
            }

            if (value.Length == 0 || value.StartsWith('"') || value.StartsWith('\''))
            {
                output.AppendLine(line);
                continue;
            }

            if (!value.Contains(':', StringComparison.Ordinal))
            {
                output.AppendLine(line);
                continue;
            }

            var indentationLength = line.Length - trimmed.Length;
            var indentation = indentationLength > 0 ? line.Substring(0, indentationLength) : string.Empty;
            var escaped = value
                .Replace("\\", "\\\\", StringComparison.Ordinal)
                .Replace("\"", "\\\"", StringComparison.Ordinal);

            output.Append(indentation)
                .Append(key)
                .Append(": \"")
                .Append(escaped)
                .AppendLine("\"");
        }

        return output.ToString().TrimEnd('\n');
    }

    private async Task<string> BuildRequiredMediaSectionAsync(List<string>? mediaPaths)
    {
        if (mediaPaths == null || mediaPaths.Count == 0)
        {
            return string.Empty;
        }

        var items = new List<string>();

        foreach (var path in mediaPaths.Where(p => !string.IsNullOrWhiteSpace(p)))
        {
            var (scopeUid, fileName, resolvedUrl) = ParseMediaUrl(path.Trim());

            if (string.IsNullOrWhiteSpace(scopeUid) || string.IsNullOrWhiteSpace(fileName))
            {
                items.Add($"{resolvedUrl}|(unresolved)");
                continue;
            }

            var media = await dbContext.Media!
                .FirstOrDefaultAsync(m => m.ScopeUid == scopeUid && m.Name == fileName);

            var description = media?.Description?.Trim();
            items.Add(string.IsNullOrWhiteSpace(description)
                ? $"{resolvedUrl}|"
                : $"{resolvedUrl}|{description}");
        }

        return items.Count == 0
            ? string.Empty
            : string.Join("\n", items);
    }

    private async Task<List<TextImageInput>?> BuildRequiredMediaInputsAsync(List<string>? mediaPaths)
    {
        if (mediaPaths == null || mediaPaths.Count == 0)
        {
            return null;
        }

        var inputs = new List<TextImageInput>();

        foreach (var path in mediaPaths.Where(p => !string.IsNullOrWhiteSpace(p)))
        {
            var (scopeUid, fileName, resolvedUrl) = ParseMediaUrl(path.Trim());

            if (string.IsNullOrWhiteSpace(scopeUid) || string.IsNullOrWhiteSpace(fileName))
            {
                throw new AIProviderException("ContentGeneration", $"Invalid required media path: '{resolvedUrl}'.");
            }

            var media = await dbContext.Media!
                .FirstOrDefaultAsync(m => m.ScopeUid == scopeUid && m.Name == fileName);

            if (media == null || media.Data == null || media.Data.Length == 0)
            {
                throw new AIProviderException("ContentGeneration", $"Required media not found or empty: '{resolvedUrl}'.");
            }

            inputs.Add(new TextImageInput
            {
                Data = media.Data,
                MimeType = media.MimeType,
                FileName = media.Name,
            });
        }

        return inputs;
    }
}
