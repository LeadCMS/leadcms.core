// <copyright file="MediaExtensionAttribute.cs" company="WavePoint Co. Ltd.">
// Licensed under the MIT license. See LICENSE file in the samples root for full license information.
// </copyright>

using System.ComponentModel.DataAnnotations;
using LeadCMS.Configuration;
using LeadCMS.Constants;
using LeadCMS.Helpers;
using LeadCMS.Interfaces;
using Microsoft.Extensions.Options;

namespace LeadCMS.DataAnnotations
{
    public class MediaExtensionAttribute : ValidationAttribute
    {
        private static int builtInLimitWarningLogged;

        protected override ValidationResult IsValid(object? value, ValidationContext validationContext)
        {
            var configuration = (IOptions<MediaConfig>?)validationContext!.GetService(typeof(IOptions<MediaConfig>));
            if (configuration == null)
            {
                throw new MissingConfigurationException("Failed to resolve IOptions<ImagesConfig> object.");
            }

            if (value is not IFormFile file)
            {
                return ValidationResult.Success!;
            }

            var fileExtension = Path.GetExtension(file.FileName);
            if (!configuration.Value.Extensions.Contains(fileExtension))
            {
                return new ValidationResult("Invalid file extension.");
            }

            var fileLength = file.Length;

            var settingService = (ISettingService?)validationContext.GetService(typeof(ISettingService));
            if (settingService == null)
            {
                throw new MissingConfigurationException("Failed to resolve ISettingService object.");
            }

            // Resolution order: extension-specific limit, Media.Max.FileSize setting, "default" limit, built-in default.
            // An entry with an empty MaxSize is treated as missing.
            var maxFileSizeSetting = configuration.Value.MaxSize
                .FirstOrDefault(info =>
                    !string.Equals(info.Extension, "default", StringComparison.OrdinalIgnoreCase)
                    && string.Equals(info.Extension, fileExtension, StringComparison.OrdinalIgnoreCase))?.MaxSize;

            if (string.IsNullOrWhiteSpace(maxFileSizeSetting))
            {
                maxFileSizeSetting = settingService
                    .GetSystemSettingAsync(SettingKeys.MediaMaxFileSize)
                    .GetAwaiter()
                    .GetResult();
            }

            if (string.IsNullOrWhiteSpace(maxFileSizeSetting))
            {
                maxFileSizeSetting = configuration.Value.MaxSize
                    .FirstOrDefault(info => string.Equals(info.Extension, "default", StringComparison.OrdinalIgnoreCase))?.MaxSize;
            }

            if (string.IsNullOrWhiteSpace(maxFileSizeSetting))
            {
                if (Interlocked.Exchange(ref builtInLimitWarningLogged, 1) == 0)
                {
                    Log.Warning(
                        "No media size limit configured for '{Extension}' (Media:MaxSize, Media.Max.FileSize); using built-in limit of {MaxFileSizeInKb} KB.",
                        fileExtension,
                        SettingDefaults.MediaMaxFileSizeInKb);
                }

                maxFileSizeSetting = SettingDefaults.MediaMaxFileSizeInKb.ToString();
            }

            long fileLengthAllowedSize;
            if (long.TryParse(maxFileSizeSetting, out var maxSizeInKb))
            {
                fileLengthAllowedSize = maxSizeInKb * 1024L;
            }
            else
            {
                var parsedSize = StringHelper.GetSizeInBytesFromString(maxFileSizeSetting);
                if (!parsedSize.HasValue)
                {
                    throw new MissingConfigurationException("Invalid Media.Max.FileSize format.");
                }

                fileLengthAllowedSize = parsedSize.Value;
            }

            if (fileLength > fileLengthAllowedSize)
            {
                return new ValidationResult($"Invalid file length. Expected {fileLengthAllowedSize} for '{fileExtension}'. Got {fileLength}.");
            }

            return ValidationResult.Success!;
        }
    }
}