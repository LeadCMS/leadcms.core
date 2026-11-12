// <copyright file="ContentDescriptionLengthAttribute.cs" company="WavePoint Co. Ltd.">
// Licensed under the MIT license. See LICENSE file in the samples root for full license information.
// </copyright>

using System.ComponentModel.DataAnnotations;
using LeadCMS.Constants;
using LeadCMS.Interfaces;

namespace LeadCMS.DataAnnotations;

/// <summary>
/// Validates the minimum and maximum length of content description based on configured limits.
/// Checks runtime settings first, then falls back to configuration defaults.
/// Uses SEO-optimized default of 155 characters for max and 1 for min if no configuration is available.
/// </summary>
public class ContentDescriptionLengthAttribute : ValidationAttribute
{
    /// <summary>
    /// Validates that the description does not exceed the configured maximum length and meets the minimum length.
    /// </summary>
    /// <param name="value">The value to validate.</param>
    /// <param name="validationContext">The validation context.</param>
    /// <returns>Validation result.</returns>
    protected override ValidationResult IsValid(object? value, ValidationContext validationContext)
    {
        if (value is not string description)
        {
            return ValidationResult.Success!;
        }

        var (minLength, maxLength) = GetMinMaxLength(validationContext);

        if (description.Length < minLength)
        {
            return new ValidationResult($"Description must be at least {minLength} characters.");
        }

        if (description.Length > maxLength)
        {
            return new ValidationResult($"Description cannot exceed {maxLength} characters for SEO optimization.");
        }

        return ValidationResult.Success!;
    }

    private (int minLength, int maxLength) GetMinMaxLength(ValidationContext validationContext)
    {
        int minLength = SettingDefaults.MinDescriptionLength;
        int maxLength = SettingDefaults.MaxDescriptionLength;

        var settingService = validationContext.GetService(typeof(ISettingService)) as ISettingService;
        if (settingService != null)
        {
            try
            {
                // Use the new convention-based method calls
                minLength = settingService.GetIntSettingWithFallbackAsync(
                    SettingKeys.MinDescriptionLength,
                    minLength).GetAwaiter().GetResult();

                maxLength = settingService.GetIntSettingWithFallbackAsync(
                    SettingKeys.MaxDescriptionLength,
                    maxLength).GetAwaiter().GetResult();
            }
            catch
            {
                // Fall back to defaults if anything fails
            }
        }

        return (minLength, maxLength);
    }
}