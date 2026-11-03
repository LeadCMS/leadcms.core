// <copyright file="SettingDefaultsConsistencyTests.cs" company="WavePoint Co. Ltd.">
// Licensed under the MIT license. See LICENSE file in the samples root for full license information.
// </copyright>

using System.Net.Http.Headers;
using LeadCMS.Configuration;
using LeadCMS.Constants;
using LeadCMS.Entities;
using LeadCMS.Helpers;
using LeadCMS.Interfaces;
using LeadCMS.Plugin.Site.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace LeadCMS.Tests;

/// <summary>
/// Guards against the API reporting a default that differs from the one applied at runtime.
/// Otherwise saving the settings form unchanged silently alters the effective behavior.
/// </summary>
public class SettingDefaultsConsistencyTests : BaseTestAutoLogin
{
    public SettingDefaultsConsistencyTests()
        : base()
    {
        TrackEntityType<Setting>();
    }

    [Fact]
    public async Task GetSystemSettings_ReportsBuiltInDefaults_WhenNothingIsSaved()
    {
        var settings = await GetSystemSettingsAsync();

        settings[LeadCaptureSettingKeys.EmailEnabled].Should().Be(SettingDefaults.ToSettingValue(LeadCaptureSettingDefaults.EmailEnabled));
        settings[LeadCaptureSettingKeys.TelegramEnabled].Should().Be(SettingDefaults.ToSettingValue(LeadCaptureSettingDefaults.TelegramEnabled));
        settings[LeadCaptureSettingKeys.SlackEnabled].Should().Be(SettingDefaults.ToSettingValue(LeadCaptureSettingDefaults.SlackEnabled));

        settings[SettingKeys.MediaMaxDimensions].Should().Be(SettingDefaults.MediaMaxDimensions);
        settings[SettingKeys.MediaCoverDimensions].Should().Be(SettingDefaults.MediaCoverDimensions);
        settings[SettingKeys.MediaPreferredFormat].Should().Be(SettingDefaults.MediaPreferredFormat);
        settings[SettingKeys.MediaQuality].Should().Be(SettingDefaults.MediaQuality.ToString());
        settings[SettingKeys.MediaEnableCoverResize].Should().Be(SettingDefaults.ToSettingValue(SettingDefaults.MediaEnableCoverResize));

        settings[SettingKeys.EnableRealtimeSyntaxValidation].Should().Be(SettingDefaults.ToSettingValue(SettingDefaults.EnableRealtimeSyntaxValidation));
        settings[SettingKeys.EnableCodeEditorLineNumbers].Should().Be(SettingDefaults.ToSettingValue(SettingDefaults.EnableCodeEditorLineNumbers));
    }

    [Fact]
    public async Task GetSystemSettings_MediaDefaults_MatchRuntimeMediaOptimizationSettings()
    {
        var settings = await GetSystemSettingsAsync();
        var runtime = await GetRuntimeMediaSettingsAsync();

        settings[SettingKeys.MediaMaxDimensions].Should().Be(runtime.MaxDimensions);
        settings[SettingKeys.MediaPreferredFormat].Should().Be(runtime.PreferredFormat);
        settings[SettingKeys.MediaQuality].Should().Be(runtime.Quality.ToString());
        settings[SettingKeys.MediaEnableOptimisation].Should().Be(SettingDefaults.ToSettingValue(runtime.EnableOptimisation));
    }

    [Fact]
    public async Task SavingReportedDefaults_DoesNotChangeRuntimeBehavior()
    {
        var before = await GetRuntimeMediaSettingsAsync();
        var reported = await GetSystemSettingsAsync();

        // Simulate the admin saving the settings form without touching anything
        foreach (var key in new[]
        {
            LeadCaptureSettingKeys.EmailEnabled,
            SettingKeys.MediaMaxDimensions,
            SettingKeys.MediaPreferredFormat,
            SettingKeys.MediaQuality,
            SettingKeys.MediaEnableOptimisation,
            SettingKeys.MediaEnableCoverResize,
        })
        {
            await SetSystemSettingAsync(key, reported[key]!);
        }

        var after = await GetRuntimeMediaSettingsAsync();
        after.Should().BeEquivalentTo(before);

        using var scope = App.Services.CreateScope();
        var settingService = scope.ServiceProvider.GetRequiredService<ISettingService>();
        var leadCaptureSettings = await settingService.FindSettingsByKeysAsync(LeadCaptureSettingKeys.All);
        SettingListHelper.GetBool(leadCaptureSettings, LeadCaptureSettingKeys.EmailEnabled, LeadCaptureSettingDefaults.EmailEnabled)
            .Should().Be(LeadCaptureSettingDefaults.EmailEnabled);

        var enableCoverResize = await settingService.GetBoolSettingWithFallbackAsync(
            SettingKeys.MediaEnableCoverResize,
            SettingDefaults.MediaEnableCoverResize);
        enableCoverResize.Should().Be(SettingDefaults.MediaEnableCoverResize);
    }

    [Fact]
    public void MergedDefinitions_PreferPluginDefaultsOverCoreMetadata()
    {
        using var scope = App.Services.CreateScope();
        var providers = scope.ServiceProvider.GetServices<ISettingsProvider>();

        var definition = SettingDefinitionHelper.Merge(providers)
            .Single(d => d.Key == LeadCaptureSettingKeys.EmailEnabled);

        definition.DefaultValue.Should().Be(SettingDefaults.ToSettingValue(LeadCaptureSettingDefaults.EmailEnabled));
        definition.Type.Should().Be(SettingValueTypes.Bool);
    }

    [Fact]
    public void CoreMetadata_KeepsLeadCaptureMetadata_WithoutDefaults()
    {
        // Plugins can be disabled ("Plugins": []), so core still describes the keys,
        // but must not carry a default that could shadow the plugin one.
        var definitions = new CoreSettingsProvider().GetSettingDefinitions()
            .Where(d => d.Key.StartsWith("LeadCapture.", StringComparison.Ordinal))
            .ToList();

        definitions.Should().NotBeEmpty();
        definitions.Should().OnlyContain(d => d.DefaultValue == null && !string.IsNullOrEmpty(d.Type));
    }

    [Fact]
    public void CoreLeadCaptureMetadata_MatchesPluginDefinitions()
    {
        // Core duplicates the plugin metadata so the keys stay described when the plugin is disabled.
        // Both copies must stay identical, otherwise the UI text changes depending on whether the plugin is loaded.
        var coreDefinitions = new CoreSettingsProvider().GetSettingDefinitions()
            .Where(d => d.Key.StartsWith("LeadCapture.", StringComparison.Ordinal))
            .ToList();

        coreDefinitions.Should().NotBeEmpty();

        foreach (var core in coreDefinitions)
        {
            LeadCaptureSettingDefinitions.TryGet(core.Key, out var plugin)
                .Should().BeTrue($"core describes '{core.Key}', so the Site plugin must define it too");

            plugin!.Type.Should().Be(core.Type, $"type of '{core.Key}'");
            plugin.Required.Should().Be(core.Required, $"required flag of '{core.Key}'");
            plugin.Description.Should().Be(core.Description, $"description of '{core.Key}'");
        }
    }

    [Fact]
    public async Task MediaMaxFileSize_ReportedValue_IsTheLimitAppliedOnUpload()
    {
        var settings = await GetSystemSettingsAsync();
        var limitInBytes = long.Parse(settings[SettingKeys.MediaMaxFileSize]!) * 1024L;

        // .png has no extension-specific limit, so the reported default limit must be the one enforced
        var form = new MultipartFormDataContent();
        var fileContent = new ByteArrayContent(new byte[limitInBytes + 1]);
        fileContent.Headers.ContentType = new MediaTypeHeaderValue("image/png");
        form.Add(fileContent, "File", "too-large.png");
        form.Add(new StringContent("setting-defaults-tests"), "ScopeUid");

        var request = new HttpRequestMessage(HttpMethod.Post, "/api/media") { Content = form };
        request.Headers.Authorization = GetAuthenticationHeaderValue();
        var response = await client.SendAsync(request);

        response.IsSuccessStatusCode.Should().BeFalse();
        var body = await response.Content.ReadAsStringAsync();
        body.Should().Contain($"Expected {limitInBytes} for '.png'");
    }

    private async Task<Dictionary<string, string?>> GetSystemSettingsAsync()
    {
        var settings = await GetTest<List<SettingDetailsDto>>("/api/settings/system", HttpStatusCode.OK);
        settings.Should().NotBeNull();
        return settings!.ToDictionary(s => s.Key, s => s.Value);
    }

    private async Task<MediaOptimizationSettings> GetRuntimeMediaSettingsAsync()
    {
        using var scope = App.Services.CreateScope();
        var mediaOptimizationService = scope.ServiceProvider.GetRequiredService<IMediaOptimizationService>();
        return await mediaOptimizationService.GetSettingsAsync();
    }

    private async Task SetSystemSettingAsync(string key, string value)
    {
        var url = $"/api/settings/system/{Uri.EscapeDataString(key)}?value={Uri.EscapeDataString(value)}";
        var response = await Request(HttpMethod.Put, url, null);
        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }
}
