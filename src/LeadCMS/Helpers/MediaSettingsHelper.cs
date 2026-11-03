// <copyright file="MediaSettingsHelper.cs" company="WavePoint Co. Ltd.">
// Licensed under the MIT license. See LICENSE file in the samples root for full license information.
// </copyright>

using ImageMagick;
using LeadCMS.Constants;
using LeadCMS.Interfaces;

namespace LeadCMS.Helpers;

/// <summary>
/// Resolves effective media settings (database, then configuration, then <see cref="SettingDefaults"/>).
/// Shared by runtime consumers and by the settings API so both always report the same value.
/// </summary>
public static class MediaSettingsHelper
{
    private static readonly Lazy<HashSet<MagickFormat>> WritableFormatsLazy = new(() => MagickNET.SupportedFormats
        .Where(format => format.SupportsWriting)
        .Select(format => format.Format)
        .ToHashSet());

    /// <summary>
    /// Gets the image formats MagickNET can write.
    /// </summary>
    public static IReadOnlySet<MagickFormat> WritableFormats => WritableFormatsLazy.Value;

    public static Task<bool> IsCoverResizeEnabledAsync(this ISettingService settingService, string? userId = null)
    {
        return settingService.GetBoolSettingWithFallbackAsync(
            SettingKeys.MediaEnableCoverResize,
            SettingDefaults.MediaEnableCoverResize,
            userId);
    }

    /// <summary>
    /// Returns configured cover dimensions, or <see cref="SettingDefaults.MediaCoverDimensions"/> (empty: covers are not cropped).
    /// </summary>
    /// <param name="settingService">Setting service.</param>
    /// <param name="userId">Optional user ID for user-level settings.</param>
    /// <returns>Cover dimensions in "WxH" format, or an empty string.</returns>
    public static async Task<string> GetCoverDimensionsAsync(this ISettingService settingService, string? userId = null)
    {
        var coverDimensions = await settingService.GetSettingWithFallbackAsync(
            SettingKeys.MediaCoverDimensions,
            ConfigurationPaths.GetConfigurationPath(SettingKeys.MediaCoverDimensions),
            userId);

        return string.IsNullOrWhiteSpace(coverDimensions)
            ? SettingDefaults.MediaCoverDimensions
            : coverDimensions;
    }

    public static async Task<string> GetMaxDimensionsAsync(this ISettingService settingService, string? userId = null)
    {
        var maxDimensions = await settingService.GetSettingWithFallbackAsync(
            SettingKeys.MediaMaxDimensions,
            ConfigurationPaths.GetConfigurationPath(SettingKeys.MediaMaxDimensions),
            userId);

        return string.IsNullOrWhiteSpace(maxDimensions)
            ? SettingDefaults.MediaMaxDimensions
            : maxDimensions;
    }

    /// <summary>
    /// Returns the preferred output format, falling back to the default when the configured one cannot be written.
    /// </summary>
    /// <param name="settingService">Setting service.</param>
    /// <param name="userId">Optional user ID for user-level settings.</param>
    /// <returns>Lowercase format name without a leading dot.</returns>
    public static async Task<string> GetPreferredFormatAsync(this ISettingService settingService, string? userId = null)
    {
        var preferredFormat = await settingService.GetSettingWithFallbackAsync(
            SettingKeys.MediaPreferredFormat,
            ConfigurationPaths.GetConfigurationPath(SettingKeys.MediaPreferredFormat),
            userId);

        return ResolvePreferredFormat(preferredFormat);
    }

    public static async Task<int> GetMediaQualityAsync(this ISettingService settingService, string? userId = null)
    {
        var quality = await settingService.GetSettingWithFallbackAsync(
            SettingKeys.MediaQuality,
            ConfigurationPaths.GetConfigurationPath(SettingKeys.MediaQuality),
            userId);

        return !string.IsNullOrWhiteSpace(quality) && int.TryParse(quality, out var parsedQuality)
            ? Math.Clamp(parsedQuality, SettingDefaults.MediaMinQuality, SettingDefaults.MediaMaxQuality)
            : SettingDefaults.MediaQuality;
    }

    public static string ResolvePreferredFormat(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return SettingDefaults.MediaPreferredFormat;
        }

        var normalized = value.Trim();
        if (normalized.Length > 0 && normalized[0] == '.')
        {
            normalized = normalized.Substring(1);
        }

        if (!WritableFormats.Any(format => string.Equals(format.ToString(), normalized, StringComparison.OrdinalIgnoreCase)))
        {
            return SettingDefaults.MediaPreferredFormat;
        }

        return normalized.ToLowerInvariant();
    }
}
