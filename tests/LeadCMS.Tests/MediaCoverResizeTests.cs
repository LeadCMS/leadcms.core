// <copyright file="MediaCoverResizeTests.cs" company="WavePoint Co. Ltd.">
// Licensed under the MIT license. See LICENSE file in the samples root for full license information.
// </copyright>

using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Reflection;
using ImageMagick;
using LeadCMS.Constants;
using LeadCMS.DTOs;
using LeadCMS.Entities;
using Microsoft.AspNetCore.StaticFiles;

namespace LeadCMS.Tests;

public class MediaCoverResizeTests : BaseTestAutoLogin
{
    private const string TestScope = "cover-resize-tests";
    private const string CoverSampleFileName = "cover-sample.png";

    public MediaCoverResizeTests()
    {
        TrackEntityType<Media>();
        TrackEntityType<Setting>();
    }

    [Fact]
    public async Task UploadCover_WithOptimisationEnabled_ResizesToCoverDimensions()
    {
        await SetSystemSettingAsync(SettingKeys.MediaCoverDimensions, "200x100");
        await SetSystemSettingAsync(SettingKeys.MediaEnableOptimisation, "true");
        await SetSystemSettingAsync(SettingKeys.MediaMaxDimensions, "5000x5000");

        var imageBytes = LoadEmbeddedResource(CoverSampleFileName);
        var media = await UploadMediaAsync(imageBytes, CoverSampleFileName, new[] { "Cover" });

        var downloadedBytes = await GetMediaBytesAsync(media.Location);
        using var image = new MagickImage(downloadedBytes);

        image.Width.Should().Be(200);
        image.Height.Should().Be(100);
        media.Size.Should().Be(downloadedBytes.LongLength);
        media.Width.Should().Be(200);
        media.Height.Should().Be(100);
        media.OriginalName.Should().NotBeNull();
        media.OriginalSize.Should().NotBeNull();
    }

    [Fact]
    public async Task UploadCover_WithOptimisationDisabled_ResizesToCoverDimensions_AndKeepsOriginalEmpty()
    {
        await SetSystemSettingAsync(SettingKeys.MediaCoverDimensions, "200x100");
        await SetSystemSettingAsync(SettingKeys.MediaEnableOptimisation, "false");
        await SetSystemSettingAsync(SettingKeys.MediaMaxDimensions, "5000x5000");

        var imageBytes = LoadEmbeddedResource(CoverSampleFileName);
        var media = await UploadMediaAsync(imageBytes, CoverSampleFileName, new[] { "cover" });

        var downloadedBytes = await GetMediaBytesAsync(media.Location);
        using var image = new MagickImage(downloadedBytes);

        image.Width.Should().Be(200);
        image.Height.Should().Be(100);
        media.Size.Should().Be(downloadedBytes.LongLength);
        media.Width.Should().Be(200);
        media.Height.Should().Be(100);
        media.OriginalName.Should().BeNull();
        media.OriginalSize.Should().BeNull();
    }

    [Fact]
    public async Task UploadCover_WithoutCoverSettings_IsNotCropped()
    {
        // Nothing saved for Media.Cover.Dimensions / Media.EnableCoverResize: built-in defaults apply.
        await SetSystemSettingAsync(SettingKeys.MediaEnableOptimisation, "false");
        await SetSystemSettingAsync(SettingKeys.MediaMaxDimensions, "5000x5000");

        var imageBytes = LoadEmbeddedResource(CoverSampleFileName);
        using var originalImage = new MagickImage(imageBytes);

        var media = await UploadMediaAsync(imageBytes, CoverSampleFileName, new[] { "cover" });

        media.Width.Should().Be((int)originalImage.Width);
        media.Height.Should().Be((int)originalImage.Height);
    }

    [Fact]
    public async Task UploadWithoutCoverTag_DoesNotForceCoverDimensions()
    {
        await SetSystemSettingAsync(SettingKeys.MediaCoverDimensions, "200x100");
        await SetSystemSettingAsync(SettingKeys.MediaEnableOptimisation, "true");
        await SetSystemSettingAsync(SettingKeys.MediaMaxDimensions, "5000x5000");

        var imageBytes = LoadEmbeddedResource(CoverSampleFileName);
        using var originalImage = new MagickImage(imageBytes);

        var media = await UploadMediaAsync(imageBytes, CoverSampleFileName, new[] { "photo" });
        var downloadedBytes = await GetMediaBytesAsync(media.Location);
        using var storedImage = new MagickImage(downloadedBytes);

        storedImage.Width.Should().Be(originalImage.Width);
        storedImage.Height.Should().Be(originalImage.Height);
        media.Size.Should().Be(downloadedBytes.LongLength);
        media.Width.Should().Be((int)storedImage.Width);
        media.Height.Should().Be((int)storedImage.Height);
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

    private async Task<MediaDetailsDto> UploadMediaAsync(byte[] bytes, string fileName, string[] tags)
    {
        var contentTypeProvider = new FileExtensionContentTypeProvider();
        contentTypeProvider.TryGetContentType(fileName, out var contentType);

        var form = new MultipartFormDataContent();
        var fileContent = new StreamContent(new MemoryStream(bytes));
        fileContent.Headers.ContentType = new MediaTypeHeaderValue(contentType ?? "application/octet-stream");
        form.Add(fileContent, "File", fileName);
        form.Add(new StringContent(TestScope), "ScopeUid");

        foreach (var tag in tags)
        {
            form.Add(new StringContent(tag), "Tags");
        }

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

        return media;
    }

    private async Task<byte[]> GetMediaBytesAsync(string location)
    {
        var response = await GetTest(location, HttpStatusCode.OK);
        return await response.Content.ReadAsByteArrayAsync();
    }
}
