// <copyright file="SettingDefaults.cs" company="WavePoint Co. Ltd.">
// Licensed under the MIT license. See LICENSE file in the samples root for full license information.
// </copyright>

namespace LeadCMS.Constants;

/// <summary>
/// Single source of truth for built-in setting defaults.
/// Used both when a setting is resolved at runtime and when its value is reported by the API,
/// so that saving a settings form without changes never alters the effective behavior.
/// </summary>
public static class SettingDefaults
{
    public const int MinTitleLength = 10;

    public const int MaxTitleLength = 60;

    public const int MinDescriptionLength = 20;

    public const int MaxDescriptionLength = 155;

    public const bool EnableRealtimeSyntaxValidation = true;

    public const bool EnableCodeEditorLineNumbers = true;

    public const bool RequireDigit = true;

    public const bool RequireUppercase = true;

    public const bool RequireLowercase = true;

    public const bool RequireNonAlphanumeric = true;

    public const int RequiredLength = 6;

    public const int RequiredUniqueChars = 1;

    public const int MediaMaxWidth = 2048;

    public const int MediaMaxHeight = 2048;

    /// <summary>
    /// Empty: cover images are not cropped until dimensions are configured explicitly.
    /// </summary>
    public const string MediaCoverDimensions = "";

    public const string MediaPreferredFormat = "avif";

    public const int MediaQuality = 75;

    public const int MediaMinQuality = 1;

    public const int MediaMaxQuality = 100;

    public const bool MediaEnableOptimisation = false;

    public const bool MediaEnableCoverResize = true;

    public const long MediaMaxFileSizeInKb = 500;

    public static readonly string MediaMaxDimensions = $"{MediaMaxWidth}x{MediaMaxHeight}";

    /// <summary>
    /// Formats a boolean default the way settings are stored ("true"/"false").
    /// </summary>
    /// <param name="value">Boolean value.</param>
    /// <returns>Lowercase string representation.</returns>
    public static string ToSettingValue(bool value) => value ? "true" : "false";
}
