// <copyright file="PluginSettingsRegistrationTests.cs" company="WavePoint Co. Ltd.">
// Licensed under the MIT license. See LICENSE file in the samples root for full license information.
// </copyright>

using LeadCMS.Constants;
using LeadCMS.Entities;
using LeadCMS.Plugin.Site.Configuration;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace LeadCMS.Tests;

public class PluginSettingsRegistrationTests : BaseTestAutoLogin
{
    public PluginSettingsRegistrationTests()
        : base()
    {
        TrackEntityType<Setting>();
    }

    [Fact]
    public async Task GetSystemSettings_ReturnsPluginRegisteredSettings()
    {
        // Act - request system settings
        var settings = await GetTest<List<SettingDetailsDto>>("/api/settings/system", HttpStatusCode.OK);

        // Assert - plugin-registered lead capture settings should be present
        Assert.NotNull(settings);
        var settingDict = settings.ToDictionary(s => s.Key, s => s.Value);

        // The Site plugin registers LeadCapture.* settings via ISettingsProvider
        Assert.True(settingDict.ContainsKey("LeadCapture.Email.Enabled"));
        Assert.True(settingDict.ContainsKey("LeadCapture.Email.Recipients"));
        Assert.True(settingDict.ContainsKey("LeadCapture.Telegram.Enabled"));
        Assert.True(settingDict.ContainsKey("LeadCapture.Telegram.BotId"));
        Assert.True(settingDict.ContainsKey("LeadCapture.Telegram.ChatId"));
        Assert.True(settingDict.ContainsKey("LeadCapture.Slack.Enabled"));
        Assert.True(settingDict.ContainsKey("LeadCapture.Slack.WebhookUrl"));
    }

    [Fact]
    public async Task GetSystemSettings_PluginSettingsHaveDefaultValues()
    {
        // Act
        var settings = await GetTest<List<SettingDetailsDto>>("/api/settings/system", HttpStatusCode.OK);

        // Assert - check default values from plugin registration
        Assert.NotNull(settings);
        var settingDict = settings.ToDictionary(s => s.Key, s => s.Value);

        // Reported defaults must match what LeadNotificationService applies when the setting is absent
        Assert.Equal(SettingDefaults.ToSettingValue(LeadCaptureSettingDefaults.EmailEnabled), settingDict[LeadCaptureSettingKeys.EmailEnabled]);
        Assert.Equal(SettingDefaults.ToSettingValue(LeadCaptureSettingDefaults.TelegramEnabled), settingDict[LeadCaptureSettingKeys.TelegramEnabled]);
        Assert.Equal(SettingDefaults.ToSettingValue(LeadCaptureSettingDefaults.SlackEnabled), settingDict[LeadCaptureSettingKeys.SlackEnabled]);
    }

    [Fact]
    public async Task GetSystemSettings_PluginSettingsHaveMetadata()
    {
        // Act
        var settings = await GetTest<List<SettingDetailsDto>>("/api/settings/system", HttpStatusCode.OK);

        // Assert - check metadata is populated from plugin definitions
        Assert.NotNull(settings);

        var telegramBotId = settings.FirstOrDefault(s => s.Key == "LeadCapture.Telegram.BotId");
        Assert.NotNull(telegramBotId);
        Assert.Equal("text", telegramBotId.Type);
        Assert.True(telegramBotId.Required);
        Assert.False(string.IsNullOrEmpty(telegramBotId.Description));

        var emailEnabled = settings.FirstOrDefault(s => s.Key == "LeadCapture.Email.Enabled");
        Assert.NotNull(emailEnabled);
        Assert.Equal("bool", emailEnabled.Type);
    }

    [Fact]
    public async Task GetSystemSettings_DatabaseOverridesPluginDefault()
    {
        // Arrange - set a plugin-registered setting in the database
        var testSetting = new SettingCreateDto
        {
            Key = "LeadCapture.Telegram.Enabled",
            Value = "true",
            UserId = null,
        };

        await PostTest("/api/settings", testSetting);

        // Act
        var settings = await GetTest<List<SettingDetailsDto>>("/api/settings/system", HttpStatusCode.OK);

        // Assert - database value should override plugin default
        Assert.NotNull(settings);
        var telegramEnabled = settings.FirstOrDefault(s => s.Key == "LeadCapture.Telegram.Enabled");
        Assert.NotNull(telegramEnabled);
        Assert.Equal("true", telegramEnabled.Value);
    }

    [Fact]
    public void GetSettingDefinitions_ReturnsRegisteredDefinitions()
    {
        // Arrange
        using var scope = App.Services.CreateScope();
        var enrichmentService = scope.ServiceProvider.GetRequiredService<ISettingsEnrichmentService>();

        // Act
        var definitions = enrichmentService.GetSettingDefinitions();

        // Assert
        Assert.NotEmpty(definitions);
        Assert.Contains(definitions, d => d.Key == "LeadCapture.Email.Enabled");
        Assert.Contains(definitions, d => d.Key == "LeadCapture.Telegram.BotId" && d.Required);
        Assert.Contains(definitions, d => d.Key == "LeadCapture.Slack.WebhookUrl" && d.Type == "text");
    }

    [Fact]
    public async Task EnrichWithAllKnownSettings_IncludesPluginSettings()
    {
        // Arrange
        using var scope = App.Services.CreateScope();
        var enrichmentService = scope.ServiceProvider.GetRequiredService<ISettingsEnrichmentService>();

        var settings = new List<Setting>();

        // Act
        await enrichmentService.EnrichWithAllKnownSettingsAsync(settings);

        // Assert - plugin settings should be present alongside core settings
        Assert.Contains(settings, s => s.Key == "LeadCapture.Email.Enabled");
        Assert.Contains(settings, s => s.Key == "LeadCapture.Telegram.BotId");

        // Core settings should also still be present
        Assert.Contains(settings, s => s.Key == SettingKeys.MinTitleLength);
    }

    [Fact]
    public async Task EnrichWithAllKnownSettings_DoesNotOverrideExistingValues()
    {
        // Arrange
        using var scope = App.Services.CreateScope();
        var enrichmentService = scope.ServiceProvider.GetRequiredService<ISettingsEnrichmentService>();

        var settings = new List<Setting>
        {
            new Setting { Key = LeadCaptureSettingKeys.EmailEnabled, Value = "false" },
        };

        // Act
        await enrichmentService.EnrichWithAllKnownSettingsAsync(settings);

        // Assert - existing value should not be overwritten by the "true" default
        Assert.Equal("false", settings.First(s => s.Key == LeadCaptureSettingKeys.EmailEnabled).Value);
    }
}
