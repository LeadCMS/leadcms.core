// <copyright file="SettingService.cs" company="WavePoint Co. Ltd.">
// Licensed under the MIT license. See LICENSE file in the samples root for full license information.
// </copyright>

using LeadCMS.Data;
using LeadCMS.Entities;
using LeadCMS.Helpers;
using LeadCMS.Interfaces;
using Microsoft.EntityFrameworkCore;

namespace LeadCMS.Services;

public class SettingService : ISettingService
{
    private readonly PgDbContext dbContext;
    private readonly IConfiguration configuration;
    private readonly IReadOnlyDictionary<string, SettingDefinition> settingDefinitionsByKey;

    public SettingService(PgDbContext dbContext, IConfiguration configuration, IEnumerable<ISettingsProvider> settingsProviders)
    {
        this.dbContext = dbContext;
        this.configuration = configuration;

        settingDefinitionsByKey = SettingDefinitionHelper.Merge(settingsProviders)
            .ToDictionary(d => d.Key, d => d, StringComparer.OrdinalIgnoreCase);
    }

    public async Task<string?> GetUserSettingAsync(string key, string userId, string? language = null)
    {
        // First try to get user-level setting
        var userSetting = await dbContext.Settings!
            .Where(s => s.Key == key && s.UserId == userId)
            .FirstOrDefaultAsync();

        if (userSetting != null)
        {
            return userSetting.Value;
        }

        // Fall back to system-level setting
        return await GetSystemSettingAsync(key, language);
    }

    public async Task<string?> GetSystemSettingAsync(string key, string? language = null)
    {
        var candidates = await dbContext.Settings!
            .Where(s => s.Key == key && s.UserId == null)
            .ToListAsync();

        var best = SettingListHelper.PickBestLanguageMatch(candidates, language);
        return best?.Value;
    }

    public async Task<Setting?> FindSystemSettingAsync(string key, string? language = null)
    {
        var candidates = await dbContext.Settings!
            .Where(s => s.Key == key && s.UserId == null)
            .ToListAsync();

        var best = SettingListHelper.PickBestLanguageMatch(candidates, language);

        if (best != null)
        {
            return best;
        }

        // Fallback to provider definition for settings not yet saved in DB
        if (settingDefinitionsByKey.TryGetValue(key, out var definition))
        {
            return new Setting
            {
                Key = definition.Key,
                Value = definition.DefaultValue,
                Required = definition.Required,
                Type = definition.Type,
                Description = definition.Description,
            };
        }

        return null;
    }

    public async Task<Setting?> FindEffectiveUserSettingAsync(string key, string userId, string? language = null)
    {
        var userCandidates = await dbContext.Settings!
            .Where(s => s.Key == key && s.UserId == userId)
            .ToListAsync();

        var userBest = SettingListHelper.PickBestLanguageMatch(userCandidates, language);
        if (userBest != null)
        {
            return userBest;
        }

        var systemCandidates = await dbContext.Settings!
            .Where(s => s.Key == key && s.UserId == null)
            .ToListAsync();

        return SettingListHelper.PickBestLanguageMatch(systemCandidates, language);
    }

    public async Task<List<Setting>> GetEffectiveUserSettingEntitiesAsync(string userId, string? language = null)
    {
        var systemSettings = await dbContext.Settings!
            .Where(s => s.UserId == null)
            .ToListAsync();

        var userSettings = await dbContext.Settings!
            .Where(s => s.UserId == userId)
            .ToListAsync();

        // Group system settings by key, pick best language match per key
        var result = new Dictionary<string, Setting>(StringComparer.OrdinalIgnoreCase);

        foreach (var group in systemSettings.GroupBy(s => s.Key))
        {
            var best = SettingListHelper.PickBestLanguageMatch(group.ToList(), language);
            if (best != null)
            {
                result[group.Key] = best;
            }
        }

        // Overlay user settings (also per-key with language matching)
        foreach (var group in userSettings.GroupBy(s => s.Key))
        {
            var best = SettingListHelper.PickBestLanguageMatch(group.ToList(), language);
            if (best != null)
            {
                result[group.Key] = best;
            }
        }

        return result.Values.ToList();
    }

    public async Task SetUserSettingAsync(string key, string? value, string userId)
    {
        // Reject masked placeholder to prevent accidentally overwriting the real secret
        if (value == SettingListHelper.SecretMask)
        {
            return;
        }

        // Skip saving if value is the same as the global (system-level) setting
        var globalSetting = await dbContext.Settings!
            .Where(s => s.Key == key && s.UserId == null && s.Language == null)
            .FirstOrDefaultAsync();

        if (globalSetting != null && globalSetting.Value == value)
        {
            // Remove any existing user override since it matches the global value
            var existingUserSetting = await dbContext.Settings!
                .Where(s => s.Key == key && s.UserId == userId)
                .FirstOrDefaultAsync();

            if (existingUserSetting != null)
            {
                dbContext.Settings!.Remove(existingUserSetting);
                await dbContext.SaveChangesAsync();
            }

            return;
        }

        var existingSetting = await dbContext.Settings!
            .Where(s => s.Key == key && s.UserId == userId)
            .FirstOrDefaultAsync();

        if (existingSetting != null)
        {
            existingSetting.Value = value;
            existingSetting.UpdatedAt = DateTime.UtcNow;
        }
        else
        {
            var newSetting = new Setting
            {
                Key = key,
                Value = value,
                UserId = userId,
            };

            EnrichWithPluginMetadata(newSetting);
            dbContext.Settings!.Add(newSetting);
        }

        await dbContext.SaveChangesAsync();
    }

    public async Task SetSystemSettingAsync(string key, string? value, string? language = null)
    {
        // Reject masked placeholder to prevent accidentally overwriting the real secret
        if (value == SettingListHelper.SecretMask)
        {
            return;
        }

        // For language-specific settings, skip saving if value matches the global setting
        if (!string.IsNullOrEmpty(language))
        {
            var globalSetting = await dbContext.Settings!
                .Where(s => s.Key == key && s.UserId == null && s.Language == null)
                .FirstOrDefaultAsync();

            if (globalSetting != null && globalSetting.Value == value)
            {
                // Remove any existing language override since it matches the global value
                var existingLangSetting = await dbContext.Settings!
                    .Where(s => s.Key == key && s.UserId == null && s.Language == language)
                    .FirstOrDefaultAsync();

                if (existingLangSetting != null)
                {
                    dbContext.Settings!.Remove(existingLangSetting);
                    await dbContext.SaveChangesAsync();
                }

                return;
            }
        }

        var existingSetting = await dbContext.Settings!
            .Where(s => s.Key == key && s.UserId == null && s.Language == language)
            .FirstOrDefaultAsync();

        if (existingSetting != null)
        {
            existingSetting.Value = value;
            existingSetting.UpdatedAt = DateTime.UtcNow;
        }
        else
        {
            var newSetting = new Setting
            {
                Key = key,
                Value = value,
                UserId = null,
                Language = language,
            };

            EnrichWithPluginMetadata(newSetting);
            dbContext.Settings!.Add(newSetting);
        }

        await dbContext.SaveChangesAsync();
    }

    public async Task DeleteUserSettingAsync(string key, string userId)
    {
        var setting = await dbContext.Settings!
            .Where(s => s.Key == key && s.UserId == userId)
            .FirstOrDefaultAsync();

        if (setting != null)
        {
            dbContext.Settings!.Remove(setting);
            await dbContext.SaveChangesAsync();
        }
    }

    public async Task DeleteSystemSettingAsync(string key, string? language = null)
    {
        var setting = await dbContext.Settings!
            .Where(s => s.Key == key && s.UserId == null && s.Language == language)
            .FirstOrDefaultAsync();

        if (setting != null)
        {
            dbContext.Settings!.Remove(setting);
            await dbContext.SaveChangesAsync();
        }
    }

    public async Task<List<Setting>> FindSettingsByKeysAsync(IEnumerable<string> keys, string? userId = null, string? language = null)
    {
        var keyList = keys.ToList();

        // Get system-level settings for the keys (all language variants)
        var systemSettings = await dbContext.Settings!
            .AsNoTracking()
            .Where(s => keyList.Contains(s.Key) && s.UserId == null)
            .ToListAsync();

        // Group by key and pick best language match per key
        var result = new Dictionary<string, Setting>(StringComparer.OrdinalIgnoreCase);

        foreach (var group in systemSettings.GroupBy(s => s.Key))
        {
            var best = SettingListHelper.PickBestLanguageMatch(group.ToList(), language);
            if (best != null)
            {
                result[group.Key] = best;
            }
        }

        // If userId is provided, override with user-level settings for the keys
        if (!string.IsNullOrEmpty(userId))
        {
            var userSettings = await dbContext.Settings!
                .AsNoTracking()
                .Where(s => keyList.Contains(s.Key) && s.UserId == userId)
                .ToListAsync();

            foreach (var group in userSettings.GroupBy(s => s.Key))
            {
                var best = SettingListHelper.PickBestLanguageMatch(group.ToList(), language);
                if (best != null)
                {
                    result[group.Key] = best;
                }
            }
        }

        return result.Values.ToList();
    }

    public async Task<string?> GetSettingWithFallbackAsync(string key, string configurationPath, string? userId = null, string? language = null)
    {
        // First try to get the setting from database (user-level first, then system-level)
        string? databaseValue = null;
        if (!string.IsNullOrEmpty(userId))
        {
            databaseValue = await GetUserSettingAsync(key, userId, language);
        }
        else
        {
            databaseValue = await GetSystemSettingAsync(key, language);
        }

        // If found in database, return it
        if (!string.IsNullOrEmpty(databaseValue))
        {
            return databaseValue;
        }

        // Fall back to configuration
        var configValue = configuration[configurationPath];
        return configValue;
    }

    public async Task<int> GetIntSettingWithFallbackAsync(string key, string configurationPath, int defaultValue = 0, string? userId = null, string? language = null)
    {
        var stringValue = await GetSettingWithFallbackAsync(key, configurationPath, userId, language);

        if (!string.IsNullOrEmpty(stringValue) && int.TryParse(stringValue, out var intValue) && intValue > 0)
        {
            return intValue;
        }

        return defaultValue;
    }

    public async Task<int> GetIntSettingWithFallbackAsync(string settingKey, int defaultValue = 0, string? userId = null, string? language = null)
    {
        var configurationPath = Constants.ConfigurationPaths.GetConfigurationPath(settingKey);
        return await GetIntSettingWithFallbackAsync(settingKey, configurationPath, defaultValue, userId, language);
    }

    public async Task<bool> GetBoolSettingWithFallbackAsync(string key, string configurationPath, bool defaultValue = false, string? userId = null, string? language = null)
    {
        var stringValue = await GetSettingWithFallbackAsync(key, configurationPath, userId, language);

        if (!string.IsNullOrEmpty(stringValue) && bool.TryParse(stringValue, out var boolValue))
        {
            return boolValue;
        }

        return defaultValue;
    }

    public async Task<bool> GetBoolSettingWithFallbackAsync(string settingKey, bool defaultValue = false, string? userId = null, string? language = null)
    {
        var configurationPath = Constants.ConfigurationPaths.GetConfigurationPath(settingKey);
        return await GetBoolSettingWithFallbackAsync(settingKey, configurationPath, defaultValue, userId, language);
    }

    /// <summary>
    /// Populates the Required, Type, and Description fields on a Setting entity
    /// from the matching provider setting definition, if one exists.
    /// </summary>
    /// <param name="setting">The setting entity to enrich.</param>
    private void EnrichWithPluginMetadata(Setting setting)
    {
        if (settingDefinitionsByKey.TryGetValue(setting.Key, out var definition))
        {
            setting.Required = definition.Required;
            setting.Type = string.IsNullOrWhiteSpace(definition.Type) ? "string" : definition.Type;
            setting.Description = definition.Description;
        }
    }
}
