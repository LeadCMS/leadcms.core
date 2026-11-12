// <copyright file="LeadCaptureSettingDefinitions.cs" company="WavePoint Co. Ltd.">
// Licensed under the MIT license. See LICENSE file in the samples root for full license information.
// </copyright>

using LeadCMS.Constants;
using LeadCMS.Interfaces;

namespace LeadCMS.Plugin.Site.Configuration;

/// <summary>
/// Reusable lead capture setting definitions for Site-based plugins.
/// </summary>
public static class LeadCaptureSettingDefinitions
{
    private static readonly IReadOnlyDictionary<string, SettingDefinition> DefinitionsByKey =
        new Dictionary<string, SettingDefinition>
        {
            [LeadCaptureSettingKeys.EmailEnabled] = CreateDefinition(
                LeadCaptureSettingKeys.EmailEnabled,
                SettingDefaults.ToSettingValue(LeadCaptureSettingDefaults.EmailEnabled),
                SettingValueTypes.Bool,
                false,
                "Whether email notifications are enabled for lead capture."),
            [LeadCaptureSettingKeys.EmailRecipients] = CreateDefinition(
                LeadCaptureSettingKeys.EmailRecipients,
                "[]",
                SettingValueTypes.EmailArray,
                false,
                "Array of email addresses to send lead notifications to."),
            [LeadCaptureSettingKeys.TelegramEnabled] = CreateDefinition(
                LeadCaptureSettingKeys.TelegramEnabled,
                SettingDefaults.ToSettingValue(LeadCaptureSettingDefaults.TelegramEnabled),
                SettingValueTypes.Bool,
                false,
                "Whether Telegram notifications are enabled for lead capture."),
            [LeadCaptureSettingKeys.TelegramBotId] = CreateDefinition(
                LeadCaptureSettingKeys.TelegramBotId,
                string.Empty,
                SettingValueTypes.Text,
                true,
                "The Telegram bot ID for sending lead notifications."),
            [LeadCaptureSettingKeys.TelegramChatId] = CreateDefinition(
                LeadCaptureSettingKeys.TelegramChatId,
                string.Empty,
                SettingValueTypes.Text,
                true,
                "The Telegram chat ID to send lead notifications to."),
            [LeadCaptureSettingKeys.TelegramMessageTemplate] = CreateDefinition(
                LeadCaptureSettingKeys.TelegramMessageTemplate,
                string.Empty,
                SettingValueTypes.TextArea,
                false,
                "Optional Liquid template for Telegram lead notification messages. When set, overrides the default message format. Supports all lead and contact template variables."),
            [LeadCaptureSettingKeys.SlackEnabled] = CreateDefinition(
                LeadCaptureSettingKeys.SlackEnabled,
                SettingDefaults.ToSettingValue(LeadCaptureSettingDefaults.SlackEnabled),
                SettingValueTypes.Bool,
                false,
                "Whether Slack notifications are enabled for lead capture."),
            [LeadCaptureSettingKeys.SlackWebhookUrl] = CreateDefinition(
                LeadCaptureSettingKeys.SlackWebhookUrl,
                string.Empty,
                SettingValueTypes.Text,
                true,
                "The Slack incoming webhook URL for sending lead notifications."),
        };

    /// <summary>
    /// Gets all Site lead capture setting definitions.
    /// </summary>
    public static IEnumerable<SettingDefinition> All => DefinitionsByKey.Values.Select(Clone);

    /// <summary>
    /// Returns only requested lead capture definitions.
    /// </summary>
    /// <param name="keys">Requested setting keys.</param>
    /// <returns>Matching definitions in requested order.</returns>
    public static IEnumerable<SettingDefinition> ForKeys(IEnumerable<string> keys)
    {
        foreach (var key in keys)
        {
            if (DefinitionsByKey.TryGetValue(key, out var definition))
            {
                yield return Clone(definition);
            }
        }
    }

    /// <summary>
    /// Tries to get a lead capture definition by key.
    /// </summary>
    /// <param name="key">Setting key.</param>
    /// <param name="definition">Definition if found.</param>
    /// <returns>True when a definition exists for the key.</returns>
    public static bool TryGet(string key, out SettingDefinition? definition)
    {
        if (DefinitionsByKey.TryGetValue(key, out var found))
        {
            definition = Clone(found);
            return true;
        }

        definition = null;
        return false;
    }

    private static SettingDefinition CreateDefinition(string key, string? defaultValue, string type, bool required, string description)
    {
        return new SettingDefinition
        {
            Key = key,
            DefaultValue = defaultValue,
            Type = type,
            Required = required,
            Description = description,
        };
    }

    private static SettingDefinition Clone(SettingDefinition source)
    {
        return new SettingDefinition
        {
            Key = source.Key,
            DefaultValue = source.DefaultValue,
            Required = source.Required,
            Type = source.Type,
            Description = source.Description,
        };
    }
}
