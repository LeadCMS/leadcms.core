// <copyright file="LeadNotificationService.cs" company="WavePoint Co. Ltd.">
// Licensed under the MIT license. See LICENSE file in the samples root for full license information.
// </copyright>

using System.Net.Http.Json;
using LeadCMS.Entities;
using LeadCMS.Helpers;
using LeadCMS.Interfaces;
using LeadCMS.Models;
using LeadCMS.Plugin.Site.Configuration;
using LeadCMS.Plugin.Site.DTOs;
using LeadCMS.Plugin.Site.Exceptions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using MimeKit;

namespace LeadCMS.Plugin.Site.Services;

/// <summary>
/// Service for sending lead capture notifications to various channels.
/// </summary>
public class LeadNotificationService : ILeadNotificationService
{
    private const int TelegramMessageMaxLength = 4096;
    private const int SlackMessageMaxLength = 40000;
    private readonly IEmailFromTemplateService emailService;
    private readonly ISettingService settingService;
    private readonly PluginSettings pluginSettings;
    private readonly ILeadNotificationMessageBuilder leadNotificationMessageBuilder;
    private readonly ILiquidTemplateService liquidTemplateService;
    private readonly ILogger<LeadNotificationService> logger;

    public LeadNotificationService(
        IEmailFromTemplateService emailService,
        ISettingService settingService,
        IConfiguration configuration,
        ILeadNotificationMessageBuilder leadNotificationMessageBuilder,
        ILiquidTemplateService liquidTemplateService,
        ILogger<LeadNotificationService> logger)
    {
        this.emailService = emailService;
        this.settingService = settingService;
        this.leadNotificationMessageBuilder = leadNotificationMessageBuilder;
        this.liquidTemplateService = liquidTemplateService;
        this.logger = logger;

        var settings = configuration.Get<PluginSettings>();
        pluginSettings = settings ?? new PluginSettings();
    }

    /// <inheritdoc/>
    public async Task SendLeadNotificationsAsync(LeadNotificationInfo leadInfo)
    {
        // Load all lead capture settings from the database
        var settings = await settingService.FindSettingsByKeysAsync(LeadCaptureSettingKeys.All, language: leadInfo.Language);

        var tasks = new List<Task>();

        // Send email notification
        var emailEnabled = SettingListHelper.GetBool(settings, LeadCaptureSettingKeys.EmailEnabled, LeadCaptureSettingDefaults.EmailEnabled);
        if (emailEnabled)
        {
            tasks.Add(SendEmailNotificationAsync(leadInfo, settings));
        }

        // Send Telegram notification
        var telegramEnabled = SettingListHelper.GetBool(settings, LeadCaptureSettingKeys.TelegramEnabled, LeadCaptureSettingDefaults.TelegramEnabled);
        if (telegramEnabled)
        {
            tasks.Add(SendTelegramNotificationAsync(leadInfo, settings));
        }

        // Send Slack notification
        var slackEnabled = SettingListHelper.GetBool(settings, LeadCaptureSettingKeys.SlackEnabled, LeadCaptureSettingDefaults.SlackEnabled);
        if (slackEnabled)
        {
            tasks.Add(SendSlackNotificationAsync(leadInfo, settings));
        }

        // Wait for all notifications to complete
        await Task.WhenAll(tasks);
    }

    /// <inheritdoc/>
    public async Task SendEmailNotificationAsync(LeadNotificationInfo leadInfo, List<Setting> settings)
    {
        try
        {
            // Determine target emails: use LeadCapture.Email.Recipients if set, otherwise fall back to ContactUs.To from plugin settings
            var leadCaptureEmails = SettingListHelper.GetStringArray(settings, LeadCaptureSettingKeys.EmailRecipients);
            var contactUsEmails = pluginSettings.ContactUs.To
                .Where(e => !string.IsNullOrEmpty(e) && !e.StartsWith('$'))
                .ToArray();

            var targetEmails = leadCaptureEmails.Length > 0 ? leadCaptureEmails : contactUsEmails;
            targetEmails = targetEmails.Where(IsValidEmail).ToArray();

            if (targetEmails.Length == 0)
            {
                var error = new InvalidOperationException("No valid email addresses configured for lead capture notifications.");
                logger.LogError(error, "Failed to send lead notification email");
                throw error;
            }

            // Start from stored contact (includes Tags, UTMs, account info, etc.)
            // then overlay the actual submitted values so templates reflect what the user entered.
            var templateArgs = TemplateArgumentsBuilder.FromContact(leadInfo.Contact, includeNestedObjects: false);
            TemplateArgumentsBuilder.WithPendingUpdates(templateArgs, leadInfo.Contact);
            TemplateArgumentsBuilder.Merge(templateArgs, leadInfo.ToTemplateArguments());
            leadNotificationMessageBuilder.EnrichTemplateArguments(templateArgs, leadInfo);

            var templateName = string.IsNullOrWhiteSpace(leadInfo.NotificationType)
                ? "Contact_Us"
                : leadInfo.NotificationType;

            var utmParams = UtmsBuilder.Create()
                .WithDefaults()
                .WithContext(new Utms { Campaign = templateName.ToLowerInvariant(), Content = "notification" })
                .Build();

            TemplateArgumentsBuilder.WithUtmParameters(templateArgs, utmParams);

            await emailService.SendAsync(
                templateName,
                leadInfo.Language ?? "en",
                targetEmails,
                templateArgs,
                leadInfo.Attachments,
                leadInfo.ContactId ?? 0);

            logger.LogInformation("Lead notification email sent successfully to {Recipients}", string.Join(", ", targetEmails));
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to send lead notification email");
            throw;
        }
    }

    /// <inheritdoc/>
    public async Task SendTelegramNotificationAsync(LeadNotificationInfo leadInfo, List<Setting> settings)
    {
        var telegramEnabled = SettingListHelper.GetBool(settings, LeadCaptureSettingKeys.TelegramEnabled, LeadCaptureSettingDefaults.TelegramEnabled);
        if (!telegramEnabled)
        {
            logger.LogInformation("Telegram notifications are disabled. Lead notification will not be sent to Telegram.");
            return;
        }

        var botId = SettingListHelper.GetString(settings, LeadCaptureSettingKeys.TelegramBotId);
        var chatId = SettingListHelper.GetString(settings, LeadCaptureSettingKeys.TelegramChatId);

        if (string.IsNullOrEmpty(botId) || string.IsNullOrEmpty(chatId))
        {
            var error = new InvalidOperationException("Telegram bot ID or chat ID is not configured for lead capture notifications.");
            logger.LogError(error, "Failed to send lead notification to Telegram");
            throw error;
        }

        try
        {
            var message = await BuildTelegramMessageAsync(leadInfo, settings);
            message = TruncateMessage(message, TelegramMessageMaxLength);

            using var httpClient = new HttpClient();

            var sendMessageUrl = $"https://api.telegram.org/bot{botId}/sendMessage";
            using var messageContent = new FormUrlEncodedContent(
            [
                new KeyValuePair<string, string>("chat_id", chatId),
                new KeyValuePair<string, string>("text", message),
            ]);

            var response = await httpClient.PostAsync(sendMessageUrl, messageContent);

            if (!response.IsSuccessStatusCode)
            {
                var content = await response.Content.ReadAsStringAsync();
                throw new TelegramException($"Failed to send message to Telegram chat. Status Code: {response.StatusCode}. Response: {content}");
            }

            if (leadInfo.Attachments is { Count: > 0 })
            {
                var sendDocumentUrl = $"https://api.telegram.org/bot{botId}/sendDocument";

                foreach (var attachment in leadInfo.Attachments)
                {
                    if (attachment?.File == null || attachment.File.Length == 0)
                    {
                        continue;
                    }

                    using var multipart = new MultipartFormDataContent();
                    multipart.Add(new StringContent(chatId), "chat_id");

                    var fileContent = new ByteArrayContent(attachment.File);
                    fileContent.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("application/octet-stream");
                    multipart.Add(fileContent, "document", attachment.FileName);

                    var docResponse = await httpClient.PostAsync(sendDocumentUrl, multipart);
                    if (!docResponse.IsSuccessStatusCode)
                    {
                        var docContent = await docResponse.Content.ReadAsStringAsync();
                        throw new TelegramException($"Failed to send document to Telegram chat. Status Code: {docResponse.StatusCode}. Response: {docContent}");
                    }
                }
            }

            logger.LogInformation("Lead notification sent to Telegram successfully");
        }
        catch (TelegramException)
        {
            throw;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to send lead notification to Telegram");
            throw new TelegramException("Failed to send message to Telegram", ex);
        }
    }

    /// <inheritdoc/>
    public async Task SendSlackNotificationAsync(LeadNotificationInfo leadInfo, List<Setting> settings)
    {
        var webhookUrl = SettingListHelper.GetString(settings, LeadCaptureSettingKeys.SlackWebhookUrl);

        if (string.IsNullOrEmpty(webhookUrl))
        {
            var error = new InvalidOperationException("Slack webhook URL is not configured for lead capture notifications.");
            logger.LogError(error, "Failed to send lead notification to Slack");
            throw error;
        }

        try
        {
            var message = leadNotificationMessageBuilder.BuildTextMessage(leadInfo);
            message = TruncateMessage(message, SlackMessageMaxLength);

            var payload = new SlackMessagePayload
            {
                Text = message,
            };

            using var httpClient = new HttpClient();
            var response = await httpClient.PostAsJsonAsync(webhookUrl, payload);

            if (!response.IsSuccessStatusCode)
            {
                var content = await response.Content.ReadAsStringAsync();
                throw new SlackException($"Failed to send message to Slack. Status Code: {response.StatusCode}. Response: {content}");
            }

            logger.LogInformation("Lead notification sent to Slack successfully");
        }
        catch (SlackException)
        {
            throw;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to send lead notification to Slack");
            throw new SlackException("Failed to send message to Slack", ex);
        }
    }

    /// <summary>
    /// Builds the Telegram message from a Liquid template setting if configured,
    /// otherwise falls back to the hardcoded <see cref="ILeadNotificationMessageBuilder.BuildTextMessage"/> format.
    /// </summary>
    internal async Task<string> BuildTelegramMessageAsync(LeadNotificationInfo leadInfo, List<Setting> settings)
    {
        var templateText = SettingListHelper.GetString(settings, LeadCaptureSettingKeys.TelegramMessageTemplate);

        if (string.IsNullOrWhiteSpace(templateText))
        {
            return leadNotificationMessageBuilder.BuildTextMessage(leadInfo);
        }

        var templateArgs = TemplateArgumentsBuilder.FromContact(leadInfo.Contact, includeNestedObjects: false);
        TemplateArgumentsBuilder.WithPendingUpdates(templateArgs, leadInfo.Contact);
        TemplateArgumentsBuilder.Merge(templateArgs, leadInfo.ToTemplateArguments());
        leadNotificationMessageBuilder.EnrichTemplateArguments(templateArgs, leadInfo);

        var rendered = await liquidTemplateService.RenderAsync(templateText, templateArgs);

        // LiquidTemplateService converts \n to <br /> for HTML emails;
        // restore plain-text newlines for Telegram.
        return rendered.Replace("<br />", "\n");
    }

    private static bool IsValidEmail(string email)
    {
        if (string.IsNullOrWhiteSpace(email))
        {
            return false;
        }

        return MailboxAddress.TryParse(email, out _);
    }

    private static string TruncateMessage(string message, int maxLength)
    {
        if (string.IsNullOrEmpty(message) || maxLength <= 0)
        {
            return string.Empty;
        }

        if (message.Length <= maxLength)
        {
            return message;
        }

        if (maxLength == 1)
        {
            return "…";
        }

        return message.Substring(0, maxLength - 1) + "…";
    }

    private sealed class SlackMessagePayload
    {
        public string Text { get; set; } = string.Empty;
    }
}
