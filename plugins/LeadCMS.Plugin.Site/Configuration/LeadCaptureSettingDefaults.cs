// <copyright file="LeadCaptureSettingDefaults.cs" company="WavePoint Co. Ltd.">
// Licensed under the MIT license. See LICENSE file in the samples root for full license information.
// </copyright>

namespace LeadCMS.Plugin.Site.Configuration;

/// <summary>
/// Single source of truth for lead capture setting defaults.
/// Used both by <see cref="LeadCaptureSettingDefinitions"/> (what the API reports)
/// and by the notification service (what is actually applied).
/// </summary>
public static class LeadCaptureSettingDefaults
{
    public const bool EmailEnabled = true;

    public const bool TelegramEnabled = false;

    public const bool SlackEnabled = false;
}
