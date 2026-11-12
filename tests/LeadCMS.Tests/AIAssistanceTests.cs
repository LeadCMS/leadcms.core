// <copyright file="AIAssistanceTests.cs" company="WavePoint Co. Ltd.">
// Licensed under the MIT license. See LICENSE file in the samples root for full license information.
// </copyright>

using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Reflection;
using ImageMagick;
using LeadCMS.Constants;
using LeadCMS.Core.AIAssistance.Configuration;
using LeadCMS.Core.AIAssistance.DTOs;
using LeadCMS.Tests.TestServices;
using Microsoft.AspNetCore.StaticFiles;

namespace LeadCMS.Tests;

[Collection("AI Assistance Tests")]
public class AIAssistanceTests : BaseTestAutoLogin
{
    public AIAssistanceTests()
    {
        TestAIProviderService.Reset();
        TrackEntityType<Media>();
        TrackEntityType<Setting>();
    }

    [Fact]
    public async Task CoverImageGenerationEndpoint_ShouldGenerateCoverImage()
    {
        await SetSystemSettingAsync(SettingKeys.MediaEnableOptimisation, "true");
        await SetSystemSettingAsync(SettingKeys.MediaPreferredFormat, "avif");
        await SetSystemSettingAsync(SettingKeys.MediaMaxDimensions, "5000x5000");

        var request = new CoverImageGenerationRequest
        {
            ContentTitle = "Automated Testing in Practice",
            ContentDescription = "A practical guide to test automation strategies and tooling choices.",
            ContentSlug = "blog/automated-testing-in-practice",
            Prompt = "Generate a cover image with a laptop, code snippets, and testing icons",
        };

        var response = await PostTest<MediaDetailsDto>("/api/content/ai-cover", request, HttpStatusCode.OK);

        response.Should().NotBeNull();
        response!.ScopeUid.Should().Be(request.ContentSlug);
        response.Location.Should().Contain($"/api/media/{request.ContentSlug}/");
        response.OriginalName.Should().NotBeNullOrWhiteSpace();
        response.OriginalName.Should().Contain("cover");
        response.OriginalName.Should().EndWith(".png");

        var originalBytes = LoadEmbeddedResource("cover-sample.png");
        response.OriginalSize.Should().Be(originalBytes.LongLength);
        response.OriginalExtension.Should().Be(".png");
        response.OriginalMimeType.Should().Be("image/png");

        var lastRequest = TestAIProviderService.GetLastImageRequest();
        lastRequest.Should().NotBeNull();
        lastRequest!.Prompt.Should().Contain(request.ContentTitle);
        lastRequest.Prompt.Should().Contain(request.ContentDescription);
        lastRequest.Prompt.Should().Contain(request.Prompt!);
    }

    [Fact]
    public async Task CoverImageGenerationEndpoint_WithoutCoverDimensions_LetsProviderPickSizeAndDoesNotCrop()
    {
        await SetSystemSettingAsync(SettingKeys.MediaEnableOptimisation, "false");
        await SetSystemSettingAsync(SettingKeys.MediaMaxDimensions, "5000x5000");

        var request = new CoverImageGenerationRequest
        {
            ContentTitle = "Cover Without Dimensions",
            ContentDescription = "Cover generation when no cover dimensions are configured.",
            ContentSlug = "blog/cover-without-dimensions",
        };

        var response = await PostTest<MediaDetailsDto>("/api/content/ai-cover", request, HttpStatusCode.OK);

        var lastRequest = TestAIProviderService.GetLastImageRequest();
        lastRequest.Should().NotBeNull();
        lastRequest!.Width.Should().BeNull();
        lastRequest.Height.Should().BeNull();

        using var original = new MagickImage(LoadEmbeddedResource("cover-sample.png"));
        response!.Width.Should().Be((int)original.Width);
        response.Height.Should().Be((int)original.Height);
    }

    [Fact]
    public async Task CoverImageGenerationEndpoint_WithCoverDimensions_CropsEvenWhenUploadCoverResizeDisabled()
    {
        // Media.EnableCoverResize only applies to uploads; AI covers follow the configured dimensions.
        await SetSystemSettingAsync(SettingKeys.MediaEnableOptimisation, "false");
        await SetSystemSettingAsync(SettingKeys.MediaMaxDimensions, "5000x5000");
        await SetSystemSettingAsync(SettingKeys.MediaCoverDimensions, "300x150");
        await SetSystemSettingAsync(SettingKeys.MediaEnableCoverResize, "false");

        var request = new CoverImageGenerationRequest
        {
            ContentTitle = "Cover With Dimensions",
            ContentDescription = "Cover generation with configured cover dimensions.",
            ContentSlug = "blog/cover-with-dimensions",
        };

        var response = await PostTest<MediaDetailsDto>("/api/content/ai-cover", request, HttpStatusCode.OK);

        var lastRequest = TestAIProviderService.GetLastImageRequest();
        lastRequest!.Width.Should().Be(300);
        lastRequest.Height.Should().Be(150);
        response!.Width.Should().Be(300);
        response.Height.Should().Be(150);
    }

    [Fact]
    public async Task CoverImageGenerationEndpoint_ShouldCreateNewCoverVersion()
    {
        await SetSystemSettingAsync(SettingKeys.MediaEnableOptimisation, "false");

        var request = new CoverImageGenerationRequest
        {
            ContentTitle = "Duplicate Cover Test",
            ContentDescription = "Ensure repeated generation overwrites the existing cover.",
            ContentSlug = "ai-cover-duplicate-test",
            Prompt = "Initial cover",
        };

        var first = await PostTest<MediaDetailsDto>("/api/content/ai-cover", request, HttpStatusCode.OK);
        first.Should().NotBeNull();

        request.Prompt = "Updated cover";
        var second = await PostTest<MediaDetailsDto>("/api/content/ai-cover", request, HttpStatusCode.OK);

        second.Should().NotBeNull();
        second!.Id.Should().NotBe(first!.Id);

        var dbContext = App.GetDbContext();
        var coverCount = dbContext!.Media!
            .Where(m => m.ScopeUid == request.ContentSlug)
            .AsEnumerable()
            .Count(m => Array.Exists(m.Tags, tag => string.Equals(tag, "cover", StringComparison.OrdinalIgnoreCase)));
        coverCount.Should().Be(2);
    }

    [Fact]
    public async Task CoverImageEditEndpoint_ShouldEditCoverImage()
    {
        var createRequest = new CoverImageGenerationRequest
        {
            ContentTitle = "Scaling Test Pipelines",
            ContentDescription = "How to scale CI pipelines while keeping feedback fast and reliable.",
            ContentSlug = "ai-cover-edit-test",
        };

        var created = await PostTest<MediaDetailsDto>("/api/content/ai-cover", createRequest, HttpStatusCode.OK);
        created.Should().NotBeNull();

        var editRequest = new CoverImageEditRequest
        {
            CoverImageUrl = created!.Location,
            ContentTitle = "Scaling Test Pipelines (Updated)",
            ContentDescription = "Refined guidance on scaling CI pipelines with minimal friction.",
            Prompt = "Add a subtle gradient",
        };

        var response = await PostTest<MediaDetailsDto>("/api/content/ai-cover/edit", editRequest, HttpStatusCode.OK);

        response.Should().NotBeNull();
        response!.ScopeUid.Should().Be(createRequest.ContentSlug);

        var lastRequest = TestAIProviderService.GetLastImageRequest();
        lastRequest.Should().NotBeNull();
        lastRequest!.Prompt.Should().Be(editRequest.Prompt);
        lastRequest.EditImage.Should().NotBeNull();
        lastRequest.EditImage!.FileName.Should().EndWith(".png");
        lastRequest.EditImage.MimeType.Should().Be("image/png");
    }

    [Fact]
    public async Task CoverImageEdit_UsesOriginalImageWhenAvailable()
    {
        await SetSystemSettingAsync(SettingKeys.MediaEnableOptimisation, "true");
        await SetSystemSettingAsync(SettingKeys.MediaPreferredFormat, "avif");
        await SetSystemSettingAsync(SettingKeys.MediaMaxDimensions, "5000x5000");

        var originalBytes = LoadEmbeddedResource("cover-sample.png");
        var createdCover = await UploadMediaAsync(originalBytes, "original-cover.png", "blog/article/original-edit");

        createdCover.OriginalName.Should().NotBeNullOrWhiteSpace();
        createdCover.OriginalExtension.Should().NotBeNullOrWhiteSpace();
        createdCover.OriginalMimeType.Should().NotBeNullOrWhiteSpace();
        createdCover.OriginalSize.Should().NotBeNull();
        createdCover.OriginalWidth.Should().Be(736);
        createdCover.OriginalHeight.Should().Be(404);

        var editRequest = new CoverImageEditRequest
        {
            CoverImageUrl = createdCover.Location,
            ContentTitle = "Edit Using Original",
            ContentDescription = "Ensure edits send original image data.",
            Prompt = "Add a subtle highlight",
        };

        var response = await PostTest<MediaDetailsDto>("/api/content/ai-cover/edit", editRequest, HttpStatusCode.OK);
        response!.OriginalName.Should().NotBeNullOrWhiteSpace();
        response.OriginalExtension.Should().NotBeNullOrWhiteSpace();
        response.OriginalMimeType.Should().NotBeNullOrWhiteSpace();
        response.OriginalSize.Should().NotBeNull();
        response.Should().NotBeNull();
        response.Width.Should().NotBeNull();
        response.Height.Should().NotBeNull();
        response.OriginalWidth.Should().Be(736);
        response.OriginalHeight.Should().Be(404);

        var lastRequest = TestAIProviderService.GetLastImageRequest();
        lastRequest.Should().NotBeNull();
        lastRequest!.EditImage.Should().NotBeNull();
        lastRequest.EditImage!.FileName.Should().Be("original-cover.png");
        lastRequest.EditImage.MimeType.Should().Be("image/png");
        using var originalImage = new MagickImage(lastRequest.EditImage.Data);
        originalImage.Width.Should().Be(736);
        originalImage.Height.Should().Be(404);
    }

    [Fact]
    public async Task CoverImageEdit_ShouldNotAccumulateCoverSuffixes()
    {
        var createRequest = new CoverImageGenerationRequest
        {
            ContentTitle = "Cover Suffix Test",
            ContentDescription = "Ensure cover edits do not append repeated suffixes.",
            ContentSlug = "my-super-cool-article",
        };

        var created = await PostTest<MediaDetailsDto>("/api/content/ai-cover", createRequest, HttpStatusCode.OK);
        created.Should().NotBeNull();

        var editRequest = new CoverImageEditRequest
        {
            CoverImageUrl = created!.Location,
            ContentTitle = "Cover Suffix Test Updated",
            ContentDescription = "First edit",
            Prompt = "Add a subtle gradient",
        };

        var firstEdit = await PostTest<MediaDetailsDto>("/api/content/ai-cover/edit", editRequest, HttpStatusCode.OK);
        firstEdit.Should().NotBeNull();
        firstEdit!.Name.Should().Contain("-cover-");

        editRequest.ContentDescription = "Second edit";
        var secondEdit = await PostTest<MediaDetailsDto>("/api/content/ai-cover/edit", editRequest, HttpStatusCode.OK);
        secondEdit.Should().NotBeNull();

        CountOccurrences(secondEdit!.Name, "-cover-").Should().Be(1, "cover suffix should not be repeated across edits");
    }

    [Fact]
    public async Task CoverImageGeneration_WithOptimisationEnabled_UsesPreferredFormatAndCoverDimensions()
    {
        await SetSystemSettingAsync(SettingKeys.MediaCoverDimensions, "100x50");
        await SetSystemSettingAsync(SettingKeys.MediaMaxDimensions, "5000x5000");
        await SetSystemSettingAsync(SettingKeys.MediaPreferredFormat, "webp");
        await SetSystemSettingAsync(SettingKeys.MediaEnableOptimisation, "true");

        var request = new CoverImageGenerationRequest
        {
            ContentTitle = "Media Settings Optimized",
            ContentDescription = "Cover generation should use preferred format and cover dimensions.",
            ContentSlug = "ai-cover-settings-optimized",
        };

        var response = await PostTest<MediaDetailsDto>("/api/content/ai-cover", request, HttpStatusCode.OK);

        response.Should().NotBeNull();
        response!.Name.Should().EndWith(".webp");
        response.Extension.Should().Be(".webp");
        response.MimeType.Should().Be("image/webp");
        response.OriginalWidth.Should().Be(736);
        response.OriginalHeight.Should().Be(404);

        var lastRequest = TestAIProviderService.GetLastImageRequest();
        lastRequest.Should().NotBeNull();
        lastRequest!.Width.Should().Be(100);
        lastRequest.Height.Should().Be(50);

        var mediaBytes = await GetMediaBytesAsync(response.Location);
        using var image = new MagickImage(mediaBytes);
        image.Width.Should().Be(100);
        image.Height.Should().Be(50);
    }

    [Fact]
    public async Task CoverImageGeneration_WithOptimisationEnabled_SetsOriginalMetadata()
    {
        await SetSystemSettingAsync(SettingKeys.MediaCoverDimensions, "300x150");
        await SetSystemSettingAsync(SettingKeys.MediaMaxDimensions, "5000x5000");
        await SetSystemSettingAsync(SettingKeys.MediaPreferredFormat, "avif");
        await SetSystemSettingAsync(SettingKeys.MediaEnableOptimisation, "true");

        var request = new CoverImageGenerationRequest
        {
            ContentTitle = "Media Original Metadata",
            ContentDescription = "Cover generation should store original metadata.",
            ContentSlug = "ai-cover-original-metadata",
        };

        var response = await PostTest<MediaDetailsDto>("/api/content/ai-cover", request, HttpStatusCode.OK);

        response.Should().NotBeNull();
        response!.OriginalName.Should().NotBeNullOrWhiteSpace();
        response.OriginalName.Should().Contain("cover");
        response.OriginalName.Should().EndWith(".png");

        var originalBytes = LoadEmbeddedResource("cover-sample.png");
        response.OriginalSize.Should().Be(originalBytes.LongLength);
        response.OriginalExtension.Should().Be(".png");
        response.OriginalMimeType.Should().Be("image/png");
    }

    [Fact]
    public async Task CoverImageGeneration_WithOptimisationDisabled_UsesOriginalFormatAndCoverDimensions()
    {
        await SetSystemSettingAsync(SettingKeys.MediaCoverDimensions, "400x200");
        await SetSystemSettingAsync(SettingKeys.MediaMaxDimensions, "5000x5000");
        await SetSystemSettingAsync(SettingKeys.MediaPreferredFormat, "webp");
        await SetSystemSettingAsync(SettingKeys.MediaEnableOptimisation, "false");

        var request = new CoverImageGenerationRequest
        {
            ContentTitle = "Media Settings Original",
            ContentDescription = "Cover generation should keep original format when optimisation is disabled.",
            ContentSlug = "ai-cover-settings-original",
        };

        var response = await PostTest<MediaDetailsDto>("/api/content/ai-cover", request, HttpStatusCode.OK);

        response.Should().NotBeNull();
        response!.Name.Should().EndWith(".png");
        response.Extension.Should().Be(".png");
        response.MimeType.Should().Be("image/png");

        var lastRequest = TestAIProviderService.GetLastImageRequest();
        lastRequest.Should().NotBeNull();
        lastRequest!.Width.Should().Be(400);
        lastRequest.Height.Should().Be(200);

        var mediaBytes = await GetMediaBytesAsync(response.Location);
        using var image = new MagickImage(mediaBytes);
        image.Width.Should().Be(400);
        image.Height.Should().Be(200);
    }

    [Fact]
    public async Task CoverImageEdit_WithOptimisationDisabled_UsesOriginalFormatAndUpdatedCoverDimensions()
    {
        await SetSystemSettingAsync(SettingKeys.MediaCoverDimensions, "240x120");
        await SetSystemSettingAsync(SettingKeys.MediaMaxDimensions, "5000x5000");
        await SetSystemSettingAsync(SettingKeys.MediaPreferredFormat, "webp");
        await SetSystemSettingAsync(SettingKeys.MediaEnableOptimisation, "true");

        var createRequest = new CoverImageGenerationRequest
        {
            ContentTitle = "Media Edit Base",
            ContentDescription = "Initial cover image for edit tests.",
            ContentSlug = "ai-cover-edit-settings",
        };

        var created = await PostTest<MediaDetailsDto>("/api/content/ai-cover", createRequest, HttpStatusCode.OK);
        created.Should().NotBeNull();

        await SetSystemSettingAsync(SettingKeys.MediaCoverDimensions, "320x160");
        await SetSystemSettingAsync(SettingKeys.MediaPreferredFormat, "avif");
        await SetSystemSettingAsync(SettingKeys.MediaEnableOptimisation, "false");

        var editRequest = new CoverImageEditRequest
        {
            CoverImageUrl = created!.Location,
            ContentTitle = "Media Edit Updated",
            ContentDescription = "Updated cover image after toggling optimisation.",
            Prompt = "Add subtle highlights",
        };

        var response = await PostTest<MediaDetailsDto>("/api/content/ai-cover/edit", editRequest, HttpStatusCode.OK);

        response.Should().NotBeNull();
        response!.Name.Should().EndWith(".png");
        response.Extension.Should().Be(".png");
        response.MimeType.Should().Be("image/png");

        var lastRequest = TestAIProviderService.GetLastImageRequest();
        lastRequest.Should().NotBeNull();
        lastRequest!.Width.Should().Be(320);
        lastRequest.Height.Should().Be(160);
        lastRequest.EditImage.Should().NotBeNull();
        lastRequest.EditImage!.FileName.Should().EndWith(".png");
        lastRequest.EditImage.MimeType.Should().Be("image/png");

        var mediaBytes = await GetMediaBytesAsync(response.Location);
        using var image = new MagickImage(mediaBytes);
        image.Width.Should().Be(320);
        image.Height.Should().Be(160);
    }

    [Fact]
    public async Task CoverImageGeneration_WithSampleImagePaths_IncludesSamplesInProviderRequest()
    {
        await SetSystemSettingAsync(SettingKeys.MediaEnableOptimisation, "false");
        await SetSystemSettingAsync(SettingKeys.MediaMaxDimensions, "5000x5000");

        var sampleBytes = LoadEmbeddedResource("cover-sample.png");
        var sampleMedia = await UploadMediaAsync(sampleBytes, "sample-cover.png", "blog/article/some-name");
        var dbContext = App.GetDbContext();
        var sampleEntity = dbContext!.Media!
            .First(m => m.ScopeUid == sampleMedia.ScopeUid && m.Name == sampleMedia.Name);
        sampleEntity.OriginalData = sampleBytes;
        sampleEntity.OriginalExtension = ".png";
        sampleEntity.OriginalMimeType = "image/png";
        sampleEntity.OriginalName = "sample-cover-original.png";
        sampleEntity.Data = new byte[] { 1, 2, 3, 4 };
        sampleEntity.Extension = ".avif";
        sampleEntity.MimeType = "image/avif";
        await dbContext.SaveChangesAsync();

        var request = new CoverImageGenerationRequest
        {
            ContentTitle = "Sample Image Coverage",
            ContentDescription = "Ensure sample image paths are passed to the provider.",
            ContentSlug = "ai-cover-sample-images",
            SampleImagePaths = new List<string>
            {
                $"{sampleMedia.ScopeUid}/{sampleMedia.Name}",
            },
        };

        var response = await PostTest<MediaDetailsDto>("/api/content/ai-cover", request, HttpStatusCode.OK);

        response.Should().NotBeNull();

        var lastRequest = TestAIProviderService.GetLastImageRequest();
        lastRequest.Should().NotBeNull();
        lastRequest!.Prompt.Should().Contain(sampleEntity.OriginalName!);
        lastRequest!.SampleImages.Should().HaveCount(1);
        lastRequest.SampleImages[0].FileName.Should().Be(sampleEntity.OriginalName);
        lastRequest.SampleImages[0].MimeType.Should().Be(sampleEntity.OriginalMimeType);
        lastRequest.SampleImages[0].Data.Should().BeEquivalentTo(sampleBytes);
        using var sampleImage = new MagickImage(lastRequest.SampleImages[0].Data);
        sampleImage.Width.Should().Be(736);
        sampleImage.Height.Should().Be(404);
    }

    [Fact]
    public async Task CoverImageGeneration_WithoutSampleImagePaths_SendsNoSampleImages()
    {
        await SetSystemSettingAsync(SettingKeys.MediaEnableOptimisation, "false");
        await SetSystemSettingAsync(SettingKeys.MediaMaxDimensions, "5000x5000");

        var request = new CoverImageGenerationRequest
        {
            ContentTitle = "Auto Samples",
            ContentDescription = "No sample paths provided, no samples should be sent to the AI provider.",
            ContentSlug = "ai-cover-auto-samples",
        };

        var response = await PostTest<MediaDetailsDto>("/api/content/ai-cover", request, HttpStatusCode.OK);

        response.Should().NotBeNull();

        var lastRequest = TestAIProviderService.GetLastImageRequest();
        lastRequest.Should().NotBeNull();
        lastRequest!.SampleImages.Should().BeEmpty();
    }

    [Fact]
    public async Task CoverImageEdit_WithSampleImagePaths_IncludesSamplesInProviderRequest()
    {
        await SetSystemSettingAsync(SettingKeys.MediaEnableOptimisation, "false");
        await SetSystemSettingAsync(SettingKeys.MediaMaxDimensions, "5000x5000");

        var coverBytes = LoadEmbeddedResource("cover-sample.png");
        var sampleBytes = LoadEmbeddedResource("cover-sample.png");

        var createdCover = await UploadMediaAsync(coverBytes, "edit-cover.png", "blog/article/some-name");
        var sampleMedia = await UploadMediaAsync(sampleBytes, "sample-edit.png", "blog/article/some-name");

        var editRequest = new CoverImageEditRequest
        {
            CoverImageUrl = createdCover.Location,
            ContentTitle = "Edit With Samples",
            ContentDescription = "Editing cover image while providing samples.",
            Prompt = "Add soft lighting",
            SampleImagePaths = new List<string>
            {
                $"/api/media/{sampleMedia.ScopeUid}/{sampleMedia.Name}",
            },
        };

        var response = await PostTest<MediaDetailsDto>("/api/content/ai-cover/edit", editRequest, HttpStatusCode.OK);

        response.Should().NotBeNull();

        var lastRequest = TestAIProviderService.GetLastImageRequest();
        lastRequest.Should().NotBeNull();
        lastRequest!.EditImage.Should().NotBeNull();
        lastRequest.Prompt.Should().Contain(editRequest.Prompt);
        lastRequest.Prompt.Should().Contain(sampleMedia.Name);
        lastRequest.SampleImages.Should().HaveCount(1);
        lastRequest.SampleImages[0].FileName.Should().Be(sampleMedia.Name);
        lastRequest.SampleImages[0].MimeType.Should().Be(sampleMedia.MimeType);
        lastRequest.SampleImages[0].Data.Should().NotBeNullOrEmpty();
        using var editSampleImage = new MagickImage(lastRequest.SampleImages[0].Data);
        editSampleImage.Width.Should().Be(736);
        editSampleImage.Height.Should().Be(404);
    }

    [Fact]
    public async Task CoverImageEdit_WithoutSampleImagePaths_DoesNotUseRecentCoverImages()
    {
        await SetSystemSettingAsync(SettingKeys.MediaEnableOptimisation, "false");
        await SetSystemSettingAsync(SettingKeys.MediaMaxDimensions, "5000x5000");

        TrackEntityType<Content>();

        var sampleBytes = LoadEmbeddedResource("cover-sample.png");
        var recentMedia = await UploadMediaAsync(sampleBytes, "recent-cover.png", "recent-cover-scope");

        var contentCreate = new ContentCreateDto
        {
            Title = "Recent Cover Content",
            Description = "This content references a cover image for sampling.",
            Body = "Body for recent cover content.",
            Slug = "recent-cover-content-edit",
            Type = "blog-post",
            Author = "Tester",
            Language = "en",
            Category = "Product",
            Tags = new[] { "Tag1" },
            AllowComments = true,
            CoverImageUrl = $"/api/media/{recentMedia.ScopeUid}/{recentMedia.Name}",
        };

        await PostTest("/api/content", contentCreate, HttpStatusCode.Created);

        var coverBytes = LoadEmbeddedResource("cover-sample.png");
        var createdCover = await UploadMediaAsync(coverBytes, "edit-cover.png", "blog/article/edit-no-samples");

        var editRequest = new CoverImageEditRequest
        {
            CoverImageUrl = createdCover.Location,
            ContentTitle = "Edit Without Samples",
            ContentDescription = "Editing cover image without samples.",
            Prompt = "Add subtle highlights",
        };

        var response = await PostTest<MediaDetailsDto>("/api/content/ai-cover/edit", editRequest, HttpStatusCode.OK);

        response.Should().NotBeNull();

        var lastRequest = TestAIProviderService.GetLastImageRequest();
        lastRequest.Should().NotBeNull();
        lastRequest!.EditImage.Should().NotBeNull();
        lastRequest.SampleImages.Should().BeEmpty();
        lastRequest.Prompt.Should().NotContain(recentMedia.Name);
        lastRequest.Prompt.Should().Be(editRequest.Prompt);
    }

    [Fact]
    public async Task ContentDraftEndpoint_ShouldUseProviderAndReturnDraft()
    {
        TestAIProviderService.EnqueueTextResponse(@"{
  ""title"": ""AI Title"",
  ""description"": ""This is a long enough description for testing content generation."",
  ""body"": ""Generated body for tests."",
  ""slug"": ""ai-title"",
  ""author"": ""Test Author"",
  ""category"": ""Product"",
  ""tags"": [""Tag1""],
  ""coverImageAlt"": ""Cover alt""
}");

        TrackEntityType<Content>();
        await PostTest("/api/content", new TestContent("-ai"));

        var request = new ContentGenerationRequest
        {
            Language = "en",
            ContentType = "blog-post",
            Prompt = "Write about automated testing",
            WordCount = 50,
        };

        var response = await PostTest<ContentCreateDto>("/api/content/ai-draft", request, HttpStatusCode.OK);

        response.Should().NotBeNull();
        response!.Title.Should().Be("AI Title");
        response.Description.Should().Contain("long enough description");

        var lastRequest = TestAIProviderService.GetLastTextRequest();
        lastRequest.Should().NotBeNull();
        lastRequest!.UserPrompt.Should().Contain(request.Prompt);
        lastRequest.UserPrompt.Should().Contain("Generate new content in en");
    }

    [Fact]
    public async Task ContentDraftEndpoint_ShouldNormalizeYamlFrontMatterInlineValuesWithColon()
    {
        TestAIProviderService.EnqueueTextResponse(@"{
  ""title"": ""AI Title"",
  ""description"": ""This is a long enough description for testing content generation."",
  ""body"": ""---\nSeoTitle: A: B\nSeoDescription: >-\n  Sample description\n---\nBody"",
  ""slug"": ""ai-title"",
  ""author"": ""Test Author"",
  ""category"": ""Product"",
  ""tags"": [""Tag1""],
  ""coverImageAlt"": ""Cover alt""
}");

        TrackEntityType<Content>();
        await PostTest("/api/content", new TestContent("-ai-frontmatter"));

        var request = new ContentGenerationRequest
        {
            Language = "en",
            ContentType = "blog-post",
            Prompt = "Write about automated testing",
            WordCount = 50,
        };

        var response = await PostTest<ContentCreateDto>("/api/content/ai-draft", request, HttpStatusCode.OK);

        response.Should().NotBeNull();
        response!.Body.Should().Contain("SeoTitle: \"A: B\"");
    }

    [Fact]
    public async Task ContentDraftEndpoint_ShouldNotDoubleQuoteAlreadyQuotedYamlFrontMatterValue()
    {
        TestAIProviderService.EnqueueTextResponse(@"{
  ""title"": ""AI Title"",
  ""description"": ""This is a long enough description for testing content generation."",
  ""body"": ""---\nSeoTitle: \""A: B\""\nSeoDescription: >-\n  Sample description\n---\nBody"",
  ""slug"": ""ai-title"",
  ""author"": ""Test Author"",
  ""category"": ""Product"",
  ""tags"": [""Tag1""],
  ""coverImageAlt"": ""Cover alt""
}");

        TrackEntityType<Content>();
        await PostTest("/api/content", new TestContent("-ai-frontmatter-quoted"));

        var request = new ContentGenerationRequest
        {
            Language = "en",
            ContentType = "blog-post",
            Prompt = "Write about automated testing",
            WordCount = 50,
        };

        var response = await PostTest<ContentCreateDto>("/api/content/ai-draft", request, HttpStatusCode.OK);

        response.Should().NotBeNull();
        response!.Body.Should().Contain("SeoTitle: \"A: B\"");
        response.Body.Should().NotContain("SeoTitle: \"\"A: B\"\"");
    }

    [Fact]
    public async Task ContentEditEndpoint_ShouldUseProviderAndReturnEdits()
    {
        TestAIProviderService.EnqueueTextResponse(@"{
  ""title"": ""Edited Title"",
  ""description"": ""Edited description that is long enough for validation."",
  ""body"": ""Edited body"",
  ""slug"": ""edited-slug"",
  ""tags"": [""TagA""],
  ""category"": ""Edited""
}");

        var request = new ContentEditRequest
        {
            Prompt = "Shorten the content",
            Title = "Original Title",
            Description = "Original description that is long enough for validation.",
            Body = "Original body",
            Slug = "original-slug",
            Type = "blog-post",
            Author = "Tester",
            Language = "en",
            Category = "Original",
            Tags = new[] { "OriginalTag" },
            AllowComments = true,
        };

        var response = await PostTest<ContentCreateDto>("/api/content/ai-edit", request, HttpStatusCode.OK);

        response.Should().NotBeNull();
        response!.Title.Should().Be("Edited Title");
        response.Description.Should().Contain("Edited description");
        response.Slug.Should().Be("edited-slug");

        var lastRequest = TestAIProviderService.GetLastTextRequest();
        lastRequest.Should().NotBeNull();
        lastRequest!.UserPrompt.Should().Contain(request.Prompt);
    }

    [Fact]
    public async Task EmailTemplateGeneration_ShouldInjectSiteProfileIntoSystemPrompt()
    {
        TrackEntityType<EmailGroup>();

        await SetSystemSettingAsync(AiSettingKeys.SiteTopic, "Developer Tools");
        await SetSystemSettingAsync(AiSettingKeys.SiteAudience, "Software Engineers");
        await SetSystemSettingAsync(AiSettingKeys.BrandVoice, "Professional and technical");
        await SetSystemSettingAsync(AiSettingKeys.PreferredTerms, "deploy, pipeline, CI/CD");
        await SetSystemSettingAsync(AiSettingKeys.AvoidTerms, "simple, easy");
        await SetSystemSettingAsync(AiSettingKeys.StyleExamples, "Concise, data-driven");

        TestAIProviderService.EnqueueTextResponse(@"{
  ""name"": ""welcome-email"",
  ""subject"": ""Welcome to DevTools"",
  ""bodyTemplate"": ""<html><body><h1>Welcome</h1></body></html>"",
  ""fromName"": ""DevTools Team""
}");

        var groupUrl = await PostTest("/api/email-groups", new TestEmailGroup("-ai-gen"), HttpStatusCode.Created);
        var group = await GetTest<EmailGroup>(groupUrl);

        var request = new EmailTemplateGenerationRequest
        {
            Language = "en",
            EmailGroupId = group!.Id,
            Prompt = "Create a welcome email",
        };

        await PostTest<EmailTemplateDetailsDto>("/api/email-templates/ai-draft", request, HttpStatusCode.OK);

        var lastRequest = TestAIProviderService.GetLastTextRequest();
        lastRequest.Should().NotBeNull();
        lastRequest!.SystemPrompt.Should().Contain("SITE PROFILE");
        lastRequest.SystemPrompt.Should().Contain("Topic: Developer Tools");
        lastRequest.SystemPrompt.Should().Contain("Audience: Software Engineers");
        lastRequest.SystemPrompt.Should().Contain("Voice: Professional and technical");
        lastRequest.SystemPrompt.Should().Contain("Preferred terms: deploy, pipeline, CI/CD");
        lastRequest.SystemPrompt.Should().Contain("Avoid: simple, easy");
        lastRequest.SystemPrompt.Should().Contain("Style examples: Concise, data-driven");
    }

    [Fact]
    public async Task EmailTemplateEdit_ShouldInjectSiteProfileIntoSystemPrompt()
    {
        await SetSystemSettingAsync(AiSettingKeys.SiteTopic, "E-commerce Platform");
        await SetSystemSettingAsync(AiSettingKeys.BrandVoice, "Friendly and approachable");

        TestAIProviderService.EnqueueTextResponse(@"{
  ""name"": ""order-confirmation"",
  ""subject"": ""Order Confirmed"",
  ""bodyTemplate"": ""<html><body><h1>Thanks for your order</h1></body></html>"",
  ""fromName"": ""Shop Team""
}");

        var request = new EmailTemplateEditRequest
        {
            Prompt = "Make the tone more casual",
            Name = "order-confirmation",
            Subject = "Your Order",
            BodyTemplate = "<html><body><p>Order received.</p></body></html>",
            FromName = "Shop",
            FromEmail = "shop@test.com",
            Language = "en",
        };

        await PostTest<EmailTemplateDetailsDto>("/api/email-templates/ai-edit", request, HttpStatusCode.OK);

        var lastRequest = TestAIProviderService.GetLastTextRequest();
        lastRequest.Should().NotBeNull();
        lastRequest!.SystemPrompt.Should().Contain("SITE PROFILE");
        lastRequest.SystemPrompt.Should().Contain("Topic: E-commerce Platform");
        lastRequest.SystemPrompt.Should().Contain("Voice: Friendly and approachable");
    }

    [Fact]
    public async Task EmailTemplateGeneration_ShouldInjectEmailTemplateInstructionsIntoSystemPrompt()
    {
        TrackEntityType<EmailGroup>();

        await SetSystemSettingAsync(AiSettingKeys.EmailTemplateInstructions, "Always include an unsubscribe link. Use #FF5500 as the primary brand color.");

        TestAIProviderService.EnqueueTextResponse(@"{
  ""name"": ""newsletter"",
  ""subject"": ""Monthly Newsletter"",
  ""bodyTemplate"": ""<html><body><h1>Newsletter</h1></body></html>"",
  ""fromName"": ""Newsletter Team""
}");

        var groupUrl = await PostTest("/api/email-groups", new TestEmailGroup("-ai-instr"), HttpStatusCode.Created);
        var group = await GetTest<EmailGroup>(groupUrl);

        var request = new EmailTemplateGenerationRequest
        {
            Language = "en",
            EmailGroupId = group!.Id,
            Prompt = "Create a monthly newsletter template",
        };

        await PostTest<EmailTemplateDetailsDto>("/api/email-templates/ai-draft", request, HttpStatusCode.OK);

        var lastRequest = TestAIProviderService.GetLastTextRequest();
        lastRequest.Should().NotBeNull();
        lastRequest!.SystemPrompt.Should().Contain("EMAIL TEMPLATE INSTRUCTIONS");
        lastRequest.SystemPrompt.Should().Contain("Always include an unsubscribe link");
        lastRequest.SystemPrompt.Should().Contain("#FF5500");
    }

    [Fact]
    public async Task EmailTemplateEdit_ShouldInjectEmailTemplateInstructionsIntoSystemPrompt()
    {
        await SetSystemSettingAsync(AiSettingKeys.EmailTemplateInstructions, "Keep emails under 600px wide. Use table-based layouts only.");

        TestAIProviderService.EnqueueTextResponse(@"{
  ""name"": ""promo-edit"",
  ""subject"": ""Special Offer"",
  ""bodyTemplate"": ""<html><body><h1>Sale</h1></body></html>"",
  ""fromName"": ""Promo Team""
}");

        var request = new EmailTemplateEditRequest
        {
            Prompt = "Add a header image",
            Name = "promo-edit",
            Subject = "Special Offer",
            BodyTemplate = "<html><body><p>Check our deals.</p></body></html>",
            FromName = "Promo",
            FromEmail = "promo@test.com",
            Language = "en",
        };

        await PostTest<EmailTemplateDetailsDto>("/api/email-templates/ai-edit", request, HttpStatusCode.OK);

        var lastRequest = TestAIProviderService.GetLastTextRequest();
        lastRequest.Should().NotBeNull();
        lastRequest!.SystemPrompt.Should().Contain("EMAIL TEMPLATE INSTRUCTIONS");
        lastRequest.SystemPrompt.Should().Contain("Keep emails under 600px wide");
        lastRequest.SystemPrompt.Should().Contain("table-based layouts only");
    }

    [Fact]
    public async Task EmailTemplateEdit_ShouldInstructStructurePreservationInSystemPrompt()
    {
        TestAIProviderService.EnqueueTextResponse(@"{
  ""name"": ""preserve-structure-email"",
  ""subject"": ""Subject"",
  ""bodyTemplate"": ""<table><tr><td>Hello</td></tr></table>"",
  ""fromName"": ""Team""
}");

        var request = new EmailTemplateEditRequest
        {
            Prompt = "Update just one sentence in the second paragraph.",
            Name = "preserve-structure-email",
            Subject = "Original Subject",
            BodyTemplate = "<meta charset=\"UTF-8\"><table><tr><td><p>Paragraph 1</p><p>Paragraph 2</p></td></tr></table>",
            FromName = "Team",
            FromEmail = "team@test.com",
            Language = "en",
        };

        await PostTest<EmailTemplateDetailsDto>("/api/email-templates/ai-edit", request, HttpStatusCode.OK);

        var lastRequest = TestAIProviderService.GetLastTextRequest();
        lastRequest.Should().NotBeNull();
        lastRequest!.SystemPrompt.Should().Contain("STRUCTURAL LOCK");
        lastRequest.SystemPrompt.Should().Contain("MARKUP PARITY");
        lastRequest.SystemPrompt.Should().Contain("NO UNSOLICITED FOOTER CHANGES");
        lastRequest.SystemPrompt.Should().Contain("Do not normalize or rewrite untouched blocks");
        lastRequest.UserPrompt.Should().Contain("EDITING INTENT");
        lastRequest.UserPrompt.Should().Contain("Do not rewrite the entire template just to apply local text changes");
    }

    [Fact]
    public async Task EmailTemplateGeneration_SystemPrompt_ShouldIncludeTagsAndSocialMediaTemplateParameters()
    {
        TrackEntityType<EmailGroup>();

        TestAIProviderService.EnqueueTextResponse(@"{
  ""name"": ""params-check"",
  ""subject"": ""Subject"",
  ""bodyTemplate"": ""<html><body><p>Hello</p></body></html>"",
  ""fromName"": ""Team""
}");

        var groupUrl = await PostTest("/api/email-groups", new TestEmailGroup("-ai-params"), HttpStatusCode.Created);
        var group = await GetTest<EmailGroup>(groupUrl);

        var request = new EmailTemplateGenerationRequest
        {
            Language = "en",
            EmailGroupId = group!.Id,
            Prompt = "Create a lifecycle email",
        };

        await PostTest<EmailTemplateDetailsDto>("/api/email-templates/ai-draft", request, HttpStatusCode.OK);

        var lastRequest = TestAIProviderService.GetLastTextRequest();
        lastRequest.Should().NotBeNull();
        lastRequest!.SystemPrompt.Should().Contain("{{ Tags }}");
        lastRequest.SystemPrompt.Should().Contain("loop with {% for item in Tags %}...{% endfor %}");
        lastRequest.SystemPrompt.Should().Contain("{{ SocialMedia }}");
        lastRequest.SystemPrompt.Should().Contain("key-value map");
    }

    [Fact]
    public async Task EmailTemplateGeneration_WithNoSiteProfile_ShouldNotIncludeSiteProfileSection()
    {
        TrackEntityType<EmailGroup>();

        // Clear any previously set site profile settings
        await SetSystemSettingAsync(AiSettingKeys.SiteTopic, string.Empty);
        await SetSystemSettingAsync(AiSettingKeys.SiteAudience, string.Empty);
        await SetSystemSettingAsync(AiSettingKeys.BrandVoice, string.Empty);
        await SetSystemSettingAsync(AiSettingKeys.PreferredTerms, string.Empty);
        await SetSystemSettingAsync(AiSettingKeys.AvoidTerms, string.Empty);
        await SetSystemSettingAsync(AiSettingKeys.StyleExamples, string.Empty);
        await SetSystemSettingAsync(AiSettingKeys.EmailTemplateInstructions, string.Empty);

        TestAIProviderService.EnqueueTextResponse(@"{
  ""name"": ""bare-email"",
  ""subject"": ""Test"",
  ""bodyTemplate"": ""<html><body><p>Test</p></body></html>"",
  ""fromName"": ""Test""
}");

        var groupUrl = await PostTest("/api/email-groups", new TestEmailGroup("-ai-empty"), HttpStatusCode.Created);
        var group = await GetTest<EmailGroup>(groupUrl);

        var request = new EmailTemplateGenerationRequest
        {
            Language = "en",
            EmailGroupId = group!.Id,
            Prompt = "Create a basic email",
        };

        await PostTest<EmailTemplateDetailsDto>("/api/email-templates/ai-draft", request, HttpStatusCode.OK);

        var lastRequest = TestAIProviderService.GetLastTextRequest();
        lastRequest.Should().NotBeNull();
        lastRequest!.SystemPrompt.Should().NotContain("SITE PROFILE");
        lastRequest.SystemPrompt.Should().NotContain("EMAIL TEMPLATE INSTRUCTIONS");
    }

    private static int CountOccurrences(string value, string token)
    {
        if (string.IsNullOrEmpty(value) || string.IsNullOrEmpty(token))
        {
            return 0;
        }

        return value.Split(token).Length - 1;
    }

    private static byte[] LoadEmbeddedResource(string fileName)
    {
        var assembly = Assembly.GetExecutingAssembly();
        var resourcePath = assembly.GetManifestResourceNames().Single(name => name.EndsWith(fileName));
        using var stream = assembly.GetManifestResourceStream(resourcePath);
        if (stream == null)
        {
            throw new FileNotFoundException($"Embedded resource '{fileName}' not found.");
        }

        using var memory = new MemoryStream();
        stream.CopyTo(memory);
        return memory.ToArray();
    }

    private async Task SetSystemSettingAsync(string key, string value)
    {
        var url = $"/api/settings/system/{Uri.EscapeDataString(key)}?value={Uri.EscapeDataString(value)}";
        var response = await Request(HttpMethod.Put, url, null);
        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    private async Task<byte[]> GetMediaBytesAsync(string location)
    {
        var response = await GetTest(location, HttpStatusCode.OK);
        return await response.Content.ReadAsByteArrayAsync();
    }

    private async Task<MediaDetailsDto> UploadMediaAsync(byte[] bytes, string fileName, string scopeUid)
    {
        var contentTypeProvider = new FileExtensionContentTypeProvider();
        contentTypeProvider.TryGetContentType(fileName, out var contentType);

        var form = new MultipartFormDataContent();
        var fileContent = new StreamContent(new MemoryStream(bytes));
        fileContent.Headers.ContentType = new MediaTypeHeaderValue(contentType ?? "application/octet-stream");
        form.Add(fileContent, "File", fileName);
        form.Add(new StringContent(scopeUid), "ScopeUid");

        var request = new HttpRequestMessage(HttpMethod.Post, "/api/media")
        {
            Content = form,
        };
        request.Headers.Authorization = GetAuthenticationHeaderValue();

        var response = await client.SendAsync(request);
        response.StatusCode.Should().Be(HttpStatusCode.Created);

        var media = await response.Content.ReadFromJsonAsync<MediaDetailsDto>();
        media.Should().NotBeNull();
        media!.Location.Should().NotBeNullOrWhiteSpace();

        return media!;
    }
}
