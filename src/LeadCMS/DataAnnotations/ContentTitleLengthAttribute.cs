// <copyright file="ContentTitleLengthAttribute.cs" company="WavePoint Co. Ltd.">
// Licensed under the MIT license. See LICENSE file in the samples root for full license information.
// </copyright>

using System.ComponentModel.DataAnnotations;
using LeadCMS.Constants;
using LeadCMS.Interfaces;

namespace LeadCMS.DataAnnotations;

public class ContentTitleLengthAttribute : ValidationAttribute
{
    protected override ValidationResult? IsValid(object? value, ValidationContext validationContext)
    {
        if (value is not string title)
        {
            return ValidationResult.Success;
        }

        var (minLength, maxLength) = GetMinMaxLength(validationContext);

        if (title.Length < minLength)
        {
            return new ValidationResult($"Title must be at least {minLength} characters. Current length: {title.Length}");
        }

        if (title.Length > maxLength)
        {
            return new ValidationResult($"Title must not exceed {maxLength} characters. Current length: {title.Length}");
        }

        return ValidationResult.Success;
    }

    private (int minLength, int maxLength) GetMinMaxLength(ValidationContext validationContext)
    {
        int minLength = SettingDefaults.MinTitleLength;
        int maxLength = SettingDefaults.MaxTitleLength;

        var settingService = validationContext.GetService(typeof(ISettingService)) as ISettingService;
        if (settingService != null)
        {
            try
            {
                // Use the new convention-based method calls
                minLength = settingService.GetIntSettingWithFallbackAsync(
                    SettingKeys.MinTitleLength,
                    minLength).GetAwaiter().GetResult();

                maxLength = settingService.GetIntSettingWithFallbackAsync(
                    SettingKeys.MaxTitleLength,
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