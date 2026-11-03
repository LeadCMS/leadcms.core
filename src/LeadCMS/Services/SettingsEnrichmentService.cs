// <copyright file="SettingsEnrichmentService.cs" company="WavePoint Co. Ltd.">
// Licensed under the MIT license. See LICENSE file in the samples root for full license information.
// </copyright>

using System.Text.Json;
using LeadCMS.Constants;
using LeadCMS.Entities;
using LeadCMS.Helpers;
using LeadCMS.Interfaces;

namespace LeadCMS.Services;

/// <summary>
/// Service for enriching settings lists with default values from configuration.
/// Handles null database values by falling back to configuration defaults.
/// </summary>
public class SettingsEnrichmentService : ISettingsEnrichmentService
{
    private readonly ISettingService settingService;
    private readonly IConfiguration configuration;
    private readonly IReadOnlyList<SettingDefinition> settingDefinitions;

    public SettingsEnrichmentService(
        ISettingService settingService,
        IConfiguration configuration,
        IEnumerable<ISettingsProvider> settingsProviders)
    {
        this.settingService = settingService;
        this.configuration = configuration;
        settingDefinitions = SettingDefinitionHelper.Merge(settingsProviders);
    }

    /// <summary>
    /// Enriches settings list with content validation defaults.
    /// Uses SettingService fallback methods to handle null database values.
    /// </summary>
    /// <param name="settings">List of settings to enrich.</param>
    /// <param name="userId">Optional user ID for user-level settings.</param>
    public async Task EnrichWithContentValidationSettingsAsync(List<Setting> settings, string? userId = null)
    {
        var minTitleLength = await settingService.GetIntSettingWithFallbackAsync(SettingKeys.MinTitleLength, SettingDefaults.MinTitleLength, userId);
        var maxTitleLength = await settingService.GetIntSettingWithFallbackAsync(SettingKeys.MaxTitleLength, SettingDefaults.MaxTitleLength, userId);
        var minDescriptionLength = await settingService.GetIntSettingWithFallbackAsync(SettingKeys.MinDescriptionLength, SettingDefaults.MinDescriptionLength, userId);
        var maxDescriptionLength = await settingService.GetIntSettingWithFallbackAsync(SettingKeys.MaxDescriptionLength, SettingDefaults.MaxDescriptionLength, userId);
        var enableRealtimeSyntaxValidation = await settingService.GetBoolSettingWithFallbackAsync(SettingKeys.EnableRealtimeSyntaxValidation, SettingDefaults.EnableRealtimeSyntaxValidation, userId);
        var enableCodeEditorLineNumbers = await settingService.GetBoolSettingWithFallbackAsync(SettingKeys.EnableCodeEditorLineNumbers, SettingDefaults.EnableCodeEditorLineNumbers, userId);

        SetSettingIfNullOrEmpty(settings, SettingKeys.MinTitleLength, minTitleLength.ToString());
        SetSettingIfNullOrEmpty(settings, SettingKeys.MaxTitleLength, maxTitleLength.ToString());
        SetSettingIfNullOrEmpty(settings, SettingKeys.MinDescriptionLength, minDescriptionLength.ToString());
        SetSettingIfNullOrEmpty(settings, SettingKeys.MaxDescriptionLength, maxDescriptionLength.ToString());
        SetSettingIfNullOrEmpty(settings, SettingKeys.EnableRealtimeSyntaxValidation, SettingDefaults.ToSettingValue(enableRealtimeSyntaxValidation));
        SetSettingIfNullOrEmpty(settings, SettingKeys.EnableCodeEditorLineNumbers, SettingDefaults.ToSettingValue(enableCodeEditorLineNumbers));
    }

    /// <summary>
    /// Enriches settings list with identity/password policy defaults.
    /// Uses SettingService fallback methods to handle null database values.
    /// </summary>
    /// <param name="settings">List of settings to enrich.</param>
    /// <param name="userId">Optional user ID for user-level settings.</param>
    public async Task EnrichWithIdentitySettingsAsync(List<Setting> settings, string? userId = null)
    {
        var requireDigit = await settingService.GetBoolSettingWithFallbackAsync(SettingKeys.RequireDigit, userId == null && SettingDefaults.RequireDigit, userId);
        var requireUppercase = await settingService.GetBoolSettingWithFallbackAsync(SettingKeys.RequireUppercase, userId == null && SettingDefaults.RequireUppercase, userId);
        var requireLowercase = await settingService.GetBoolSettingWithFallbackAsync(SettingKeys.RequireLowercase, SettingDefaults.RequireLowercase, userId);
        var requireNonAlphanumeric = await settingService.GetBoolSettingWithFallbackAsync(SettingKeys.RequireNonAlphanumeric, userId == null && SettingDefaults.RequireNonAlphanumeric, userId);
        var requiredLength = await settingService.GetIntSettingWithFallbackAsync(SettingKeys.RequiredLength, SettingDefaults.RequiredLength, userId);
        var requiredUniqueChars = await settingService.GetIntSettingWithFallbackAsync(SettingKeys.RequiredUniqueChars, SettingDefaults.RequiredUniqueChars, userId);

        SetSettingIfNullOrEmpty(settings, SettingKeys.RequireDigit, SettingDefaults.ToSettingValue(requireDigit));
        SetSettingIfNullOrEmpty(settings, SettingKeys.RequireUppercase, SettingDefaults.ToSettingValue(requireUppercase));
        SetSettingIfNullOrEmpty(settings, SettingKeys.RequireLowercase, SettingDefaults.ToSettingValue(requireLowercase));
        SetSettingIfNullOrEmpty(settings, SettingKeys.RequireNonAlphanumeric, SettingDefaults.ToSettingValue(requireNonAlphanumeric));
        SetSettingIfNullOrEmpty(settings, SettingKeys.RequiredLength, requiredLength.ToString());
        SetSettingIfNullOrEmpty(settings, SettingKeys.RequiredUniqueChars, requiredUniqueChars.ToString());
    }

    /// <summary>
    /// Enriches settings list with API configuration defaults.
    /// These settings are typically configuration-only and don't have database overrides.
    /// </summary>
    /// <param name="settings">List of settings to enrich.</param>
    public async Task EnrichWithApiSettingsAsync(List<Setting> settings)
    {
        var maxListSize = configuration["ApiSettings:MaxListSize"] ?? "100";
        var defaultFromEmail = configuration["ApiSettings:DefaultFromEmail"] ?? "no-reply@leadcms.ai";
        var defaultFromName = configuration["ApiSettings:DefaultFromName"] ?? "LeadCMS";

        SetSettingIfNullOrEmpty(settings, "ApiSettings.MaxListSize", maxListSize);
        SetSettingIfNullOrEmpty(settings, "ApiSettings.DefaultFromEmail", defaultFromEmail);
        SetSettingIfNullOrEmpty(settings, "ApiSettings.DefaultFromName", defaultFromName);

        await Task.CompletedTask;
    }

    /// <summary>
    /// Enriches settings list with general site link defaults.
    /// Values are sourced from appsettings and can be overridden via database settings.
    /// </summary>
    /// <param name="settings">List of settings to enrich.</param>
    public async Task EnrichWithGeneralSettingsAsync(List<Setting> settings)
    {
        var siteUrl = await settingService.GetSystemSettingAsync(SettingKeys.GeneralSiteUrl);
        if (string.IsNullOrWhiteSpace(siteUrl))
        {
            siteUrl = configuration["General:SiteUrl"];
        }

        if (string.IsNullOrWhiteSpace(siteUrl))
        {
            siteUrl = configuration["SiteUrl"];
        }

        var unsubscribeUrl = await settingService.GetSystemSettingAsync(SettingKeys.GeneralUnsubscribeUrl);
        if (string.IsNullOrWhiteSpace(unsubscribeUrl))
        {
            unsubscribeUrl = configuration["General:UnsubscribeUrl"];
        }

        var privacyUrl = await settingService.GetSystemSettingAsync(SettingKeys.GeneralPrivacyUrl);
        if (string.IsNullOrWhiteSpace(privacyUrl))
        {
            privacyUrl = configuration["General:PrivacyUrl"];
        }

        var normalizedSiteUrl = string.IsNullOrWhiteSpace(siteUrl)
            ? string.Empty
            : siteUrl.TrimEnd('/');

        if (string.IsNullOrWhiteSpace(unsubscribeUrl) && !string.IsNullOrWhiteSpace(normalizedSiteUrl))
        {
            unsubscribeUrl = $"{normalizedSiteUrl}/unsubscribe";
        }

        if (string.IsNullOrWhiteSpace(privacyUrl) && !string.IsNullOrWhiteSpace(normalizedSiteUrl))
        {
            privacyUrl = $"{normalizedSiteUrl}/privacy";
        }

        SetSettingIfNullOrEmpty(settings, SettingKeys.GeneralSiteUrl, normalizedSiteUrl);
        SetSettingIfNullOrEmpty(settings, SettingKeys.GeneralUnsubscribeUrl, string.IsNullOrWhiteSpace(unsubscribeUrl) ? string.Empty : unsubscribeUrl.TrimEnd('/'));
        SetSettingIfNullOrEmpty(settings, SettingKeys.GeneralPrivacyUrl, string.IsNullOrWhiteSpace(privacyUrl) ? string.Empty : privacyUrl.TrimEnd('/'));
    }

    /// <summary>
    /// Enriches settings list with media optimization defaults.
    /// </summary>
    /// <param name="settings">List of settings to enrich.</param>
    /// <param name="userId">Optional user ID for user-level settings.</param>
    public async Task EnrichWithMediaSettingsAsync(List<Setting> settings, string? userId = null)
    {
        var maxDimensions = await settingService.GetMaxDimensionsAsync(userId);
        var coverDimensions = await settingService.GetCoverDimensionsAsync(userId);
        var preferredFormat = await settingService.GetPreferredFormatAsync(userId);

        var maxFileSizeInKb = GetDefaultMediaMaxFileSize();

        var enableOptimisation = await settingService.GetBoolSettingWithFallbackAsync(
            SettingKeys.MediaEnableOptimisation,
            SettingDefaults.MediaEnableOptimisation,
            userId);
        var quality = await settingService.GetMediaQualityAsync(userId);
        var enableCoverResize = await settingService.IsCoverResizeEnabledAsync(userId);

        SetSettingIfNullOrEmpty(settings, SettingKeys.MediaMaxDimensions, maxDimensions);
        SetSettingIfNullOrEmpty(settings, SettingKeys.MediaCoverDimensions, coverDimensions);
        SetSettingIfNullOrEmpty(settings, SettingKeys.MediaPreferredFormat, preferredFormat);
        SetSettingIfNullOrEmpty(settings, SettingKeys.MediaMaxFileSize, maxFileSizeInKb.ToString());
        SetSettingIfNullOrEmpty(settings, SettingKeys.MediaEnableOptimisation, SettingDefaults.ToSettingValue(enableOptimisation));
        SetSettingIfNullOrEmpty(settings, SettingKeys.MediaQuality, quality.ToString());
        SetSettingIfNullOrEmpty(settings, SettingKeys.MediaEnableCoverResize, SettingDefaults.ToSettingValue(enableCoverResize));
    }

    /// <summary>
    /// Enriches settings list with lead capture defaults.
    /// Falls back to ContactUs.To configuration when LeadCapture.Email.Recipients is missing or empty.
    /// </summary>
    /// <param name="settings">List of settings to enrich.</param>
    public async Task EnrichWithLeadCaptureSettingsAsync(List<Setting> settings)
    {
        const string leadCaptureEmailRecipientsKey = "LeadCapture.Email.Recipients";

        var existing = settings.FirstOrDefault(s => s.Key == leadCaptureEmailRecipientsKey);
        var hasValue = existing != null
            && !string.IsNullOrWhiteSpace(existing.Value)
            && !string.Equals(existing.Value, "[]", StringComparison.Ordinal);

        if (!hasValue)
        {
            // Skip unresolved "$ENV" placeholders the same way LeadNotificationService does
            var contactUsTo = configuration.GetSection("ContactUs:To").Get<string[]>()?
                .Where(e => !string.IsNullOrEmpty(e) && !e.StartsWith('$'))
                .ToArray();
            if (contactUsTo != null && contactUsTo.Length > 0)
            {
                if (existing != null)
                {
                    existing.Value = JsonSerializer.Serialize(contactUsTo);
                }
                else
                {
                    settings.Add(new Setting { Key = leadCaptureEmailRecipientsKey, Value = JsonSerializer.Serialize(contactUsTo) });
                }
            }
        }

        await Task.CompletedTask;
    }

    /// <summary>
    /// Enriches settings list with all known settings categories.
    /// This is a convenience method that calls all specific enrichment methods.
    /// </summary>
    /// <param name="settings">List of settings to enrich.</param>
    /// <param name="userId">Optional user ID for user-level settings.</param>
    public async Task EnrichWithAllKnownSettingsAsync(List<Setting> settings, string? userId = null)
    {
        await EnrichWithContentValidationSettingsAsync(settings, userId);
        await EnrichWithIdentitySettingsAsync(settings, userId);
        await EnrichWithApiSettingsAsync(settings);
        await EnrichWithGeneralSettingsAsync(settings);
        await EnrichWithMediaSettingsAsync(settings, userId);
        await EnrichWithLeadCaptureSettingsAsync(settings);
        EnrichWithPluginSettings(settings);
    }

    /// <inheritdoc/>
    public IReadOnlyList<SettingDefinition> GetSettingDefinitions()
    {
        return settingDefinitions;
    }

    /// <summary>
    /// Sets a setting value in the list only if the key doesn't exist or the value is null/empty.
    /// If the setting doesn't exist, a new Setting is added to the list.
    /// </summary>
    /// <param name="settings">Settings list to update.</param>
    /// <param name="key">Setting key.</param>
    /// <param name="value">Value to set if key is missing or null/empty.</param>
    private static void SetSettingIfNullOrEmpty(List<Setting> settings, string key, string value)
    {
        var existing = settings.FirstOrDefault(s => s.Key == key);
        if (existing == null)
        {
            settings.Add(new Setting { Key = key, Value = value });
        }
        else if (string.IsNullOrEmpty(existing.Value))
        {
            existing.Value = value;
        }
    }

    /// <summary>
    /// Converts a file size string (e.g., "5MB", "1GB", "512KB") to kilobytes.
    /// If the value is already numeric, treats it as kilobytes.
    /// </summary>
    /// <param name="sizeString">Size string to convert (e.g., "5MB", "1024KB", "512").</param>
    /// <returns>Size in kilobytes as a long.</returns>
    private static long ConvertToKilobytes(string sizeString)
    {
        if (string.IsNullOrWhiteSpace(sizeString))
        {
            return SettingDefaults.MediaMaxFileSizeInKb;
        }

        sizeString = sizeString.Trim().ToUpperInvariant();

        // If it's just a number, assume it's already in kilobytes
        if (long.TryParse(sizeString, out var kb))
        {
            return kb;
        }

        // Parse size with unit suffix
        var numberPart = System.Text.RegularExpressions.Regex.Match(sizeString, @"[\d.]+").Value;
        if (!double.TryParse(numberPart, System.Globalization.CultureInfo.InvariantCulture, out var number))
        {
            return SettingDefaults.MediaMaxFileSizeInKb;
        }

        var unit = sizeString.Substring(numberPart.Length).Trim();

        return unit switch
        {
            "B" => (long)(number / 1024),
            "KB" => (long)number,
            "MB" => (long)(number * 1024),
            "GB" => (long)(number * 1024 * 1024),
            _ => (long)number, // Assume KB if unit is unknown
        };
    }

    /// <summary>
    /// Enriches settings list with plugin-registered setting defaults.
    /// Adds any plugin-declared keys that are missing or null in the list.
    /// </summary>
    /// <param name="settings">List of settings to enrich.</param>
    private void EnrichWithPluginSettings(List<Setting> settings)
    {
        foreach (var definition in settingDefinitions)
        {
            SetSettingIfNullOrEmpty(settings, definition.Key, definition.DefaultValue ?? string.Empty);
        }
    }

    /// <summary>
    /// Gets the default media max file size from configuration.
    /// Reads from Media:MaxSize configuration array, finds "default" extension entry, and converts to kilobytes.
    /// </summary>
    /// <returns>Max file size in kilobytes.</returns>
    private long GetDefaultMediaMaxFileSize()
    {
        var mediaSection = configuration.GetSection("Media");
        var maxSizeConfig = mediaSection.GetSection("MaxSize");

        // Try to find the default entry
        var defaultEntry = maxSizeConfig
            .GetChildren()
            .FirstOrDefault(item => item["Extension"] == "default");

        if (defaultEntry != null && !string.IsNullOrWhiteSpace(defaultEntry["MaxSize"]))
        {
            return ConvertToKilobytes(defaultEntry["MaxSize"]!);
        }

        // Fallback: built-in default
        return SettingDefaults.MediaMaxFileSizeInKb;
    }
}