// <copyright file="KnownSettingMetadata.cs" company="WavePoint Co. Ltd.">
// Licensed under the MIT license. See LICENSE file in the samples root for full license information.
// </copyright>

using LeadCMS.Core.AIAssistance.Configuration;

namespace LeadCMS.Constants;

public sealed class SettingMetadataDefinition
{
    public SettingMetadataDefinition(string key, bool required, string type, string description, string? defaultValue = null)
    {
        Key = key;
        Required = required;
        Type = type;
        Description = description;
        DefaultValue = defaultValue;
    }

    public string Key { get; }

    public bool Required { get; }

    public string Type { get; }

    public string Description { get; }

    public string? DefaultValue { get; }
}

public static class KnownSettingMetadata
{
    public static IReadOnlyList<SettingMetadataDefinition> All { get; } = new List<SettingMetadataDefinition>
    {
        new(SettingKeys.PreviewUrlTemplate, false, SettingValueTypes.Text, "Preview URL template used for content preview links."),
        new(SettingKeys.LivePreviewUrlTemplate, false, SettingValueTypes.Text, "Live preview URL template used for published content links."),
        new(SettingKeys.MinTitleLength, false, SettingValueTypes.Int, "Minimum allowed title length for content."),
        new(SettingKeys.MaxTitleLength, false, SettingValueTypes.Int, "Maximum allowed title length for content."),
        new(SettingKeys.MinDescriptionLength, false, SettingValueTypes.Int, "Minimum allowed description length for content."),
        new(SettingKeys.MaxDescriptionLength, false, SettingValueTypes.Int, "Maximum allowed description length for content."),
        new(SettingKeys.EnableRealtimeSyntaxValidation, false, SettingValueTypes.Bool, "Enables real-time content syntax validation in the editor."),
        new(SettingKeys.EnableCodeEditorLineNumbers, false, SettingValueTypes.Bool, "Shows line numbers in the content code editor."),

        new(SettingKeys.RequireDigit, false, SettingValueTypes.Bool, "Require at least one digit in user passwords."),
        new(SettingKeys.RequireUppercase, false, SettingValueTypes.Bool, "Require at least one uppercase character in user passwords."),
        new(SettingKeys.RequireLowercase, false, SettingValueTypes.Bool, "Require at least one lowercase character in user passwords."),
        new(SettingKeys.RequireNonAlphanumeric, false, SettingValueTypes.Bool, "Require at least one non-alphanumeric character in user passwords."),
        new(SettingKeys.RequiredLength, false, SettingValueTypes.Int, "Minimum password length."),
        new(SettingKeys.RequiredUniqueChars, false, SettingValueTypes.Int, "Minimum number of unique characters in a password."),

        new(SettingKeys.MediaCoverDimensions, false, SettingValueTypes.Text, "Target dimensions for generated cover images (e.g. 512x256)."),
        new(SettingKeys.MediaMaxDimensions, false, SettingValueTypes.Text, "Maximum media dimensions allowed for optimization."),
        new(SettingKeys.MediaPreferredFormat, false, SettingValueTypes.Text, "Preferred image output format for optimized media."),
        new(SettingKeys.MediaMaxFileSize, false, SettingValueTypes.Int, "Maximum media file size in KB."),
        new(SettingKeys.MediaEnableOptimisation, false, SettingValueTypes.Bool, "Enable media optimization pipeline."),
        new(SettingKeys.MediaQuality, false, SettingValueTypes.Int, "Default output quality for optimized media."),
        new(SettingKeys.MediaEnableCoverResize, false, SettingValueTypes.Bool, "Resize uploaded cover images to the configured cover dimensions. AI-generated covers always follow the configured dimensions."),

        new("ApiSettings.MaxListSize", false, SettingValueTypes.Int, "Maximum number of records returned by list endpoints."),
        new("ApiSettings.DefaultFromEmail", false, SettingValueTypes.Text, "Default sender email address used for system-generated emails."),
        new("ApiSettings.DefaultFromName", false, SettingValueTypes.Text, "Default sender display name used for system-generated emails."),
        new(SettingKeys.GeneralSiteUrl, false, SettingValueTypes.Text, "Main site URL used in email templates as {{ site_url }}."),
        new(SettingKeys.GeneralUnsubscribeUrl, false, SettingValueTypes.Text, "Unsubscribe URL used in email templates as {{ unsubscribe_url }}."),
        new(SettingKeys.GeneralPrivacyUrl, false, SettingValueTypes.Text, "Privacy policy URL used in email templates as {{ privacy_url }}."),
        new(SettingKeys.DeploymentLastSuccessDate, false, SettingValueTypes.DateTime, "Date and time of the last successful deployment."),
        new(SettingKeys.DeploymentWebhooksApiKey, false, SettingValueTypes.Secret, "API key for authenticating external deployment notifications."),

        new(AiSettingKeys.SiteTopic, false, SettingValueTypes.TextArea, "Main site topic used to guide AI-generated content and templates."),
        new(AiSettingKeys.SiteAudience, false, SettingValueTypes.TextArea, "Target audience profile used for AI-generated content and templates."),
        new(AiSettingKeys.BrandVoice, false, SettingValueTypes.TextArea, "Brand voice and tone guidance for AI-generated outputs."),
        new(AiSettingKeys.PreferredTerms, false, SettingValueTypes.TextArea, "Preferred terminology that AI should favor."),
        new(AiSettingKeys.AvoidTerms, false, SettingValueTypes.TextArea, "Terminology that AI should avoid."),
        new(AiSettingKeys.StyleExamples, false, SettingValueTypes.TextArea, "Examples of desired writing style for AI outputs."),
        new(AiSettingKeys.BlogCoverInstructions, false, SettingValueTypes.TextArea, "Additional instructions for AI-generated blog cover images."),
        new(AiSettingKeys.EmailTemplateInstructions, false, SettingValueTypes.TextArea, "Additional instructions for AI-generated email templates."),

        // Owned by the Site plugin: metadata only, defaults come from the plugin definitions which take precedence.
        new("LeadCapture.Email.Enabled", false, SettingValueTypes.Bool, "Whether email notifications are enabled for lead capture."),
        new("LeadCapture.Email.Recipients", false, SettingValueTypes.EmailArray, "Array of email addresses to send lead notifications to."),
        new("LeadCapture.Telegram.Enabled", false, SettingValueTypes.Bool, "Whether Telegram notifications are enabled for lead capture."),
        new("LeadCapture.Telegram.BotId", true, SettingValueTypes.Text, "The Telegram bot ID for sending lead notifications."),
        new("LeadCapture.Telegram.ChatId", true, SettingValueTypes.Text, "The Telegram chat ID to send lead notifications to."),
        new("LeadCapture.Slack.Enabled", false, SettingValueTypes.Bool, "Whether Slack notifications are enabled for lead capture."),
        new("LeadCapture.Slack.WebhookUrl", true, SettingValueTypes.Text, "The Slack incoming webhook URL for sending lead notifications."),
    }.AsReadOnly();

    public static bool TryGet(string key, out SettingMetadataDefinition definition)
    {
        definition = All.FirstOrDefault(d => string.Equals(d.Key, key, StringComparison.OrdinalIgnoreCase))
            ?? new SettingMetadataDefinition(string.Empty, false, string.Empty, string.Empty);
        return !string.IsNullOrEmpty(definition.Key);
    }
}
