// <copyright file="MediaOptimizationService.cs" company="WavePoint Co. Ltd.">
// Licensed under the MIT license. See LICENSE file in the samples root for full license information.
// </copyright>

using ImageMagick;
using LeadCMS.Constants;
using LeadCMS.Helpers;
using LeadCMS.Interfaces;
using Microsoft.AspNetCore.StaticFiles;

namespace LeadCMS.Services;

public class MediaOptimizationService : IMediaOptimizationService
{
    private readonly ISettingService settingService;
    private readonly ILogger<MediaOptimizationService> logger;

    public MediaOptimizationService(ISettingService settingService, ILogger<MediaOptimizationService> logger)
    {
        this.settingService = settingService;
        this.logger = logger;
    }

    public async Task<MediaOptimizationSettings> GetSettingsAsync(CancellationToken cancellationToken = default)
    {
        var maxDimensions = await settingService.GetMaxDimensionsAsync();
        var preferredFormat = await settingService.GetPreferredFormatAsync();
        var enableOptimisation = await settingService.GetBoolSettingWithFallbackAsync(
            SettingKeys.MediaEnableOptimisation,
            SettingDefaults.MediaEnableOptimisation);
        var quality = await settingService.GetMediaQualityAsync();

        return new MediaOptimizationSettings
        {
            MaxDimensions = maxDimensions,
            PreferredFormat = preferredFormat,
            EnableOptimisation = enableOptimisation,
            Quality = quality,
        };
    }

    public async Task<MediaOptimizationResult> OptimizeAsync(MediaOptimizationRequest request, bool force = false, CancellationToken cancellationToken = default)
    {
        var settings = await GetSettingsAsync(cancellationToken);

        // If optimization is disabled and not forced, return original data as-is
        // When force=true (manual optimization), bypass the EnableOptimisation check
        if (!settings.EnableOptimisation && !force)
        {
            return new MediaOptimizationResult
            {
                Data = request.Data,
                Size = request.Data.Length,
                Extension = request.Extension,
                MimeType = request.MimeType,
                WasOptimized = false,
            };
        }

        if (request.Data.Length == 0)
        {
            return new MediaOptimizationResult
            {
                Data = request.Data,
                Size = request.Data.Length,
                Extension = request.Extension,
                MimeType = request.MimeType,
                WasOptimized = false,
            };
        }

        if (!request.MimeType.StartsWith("image/", StringComparison.OrdinalIgnoreCase))
        {
            return new MediaOptimizationResult
            {
                Data = request.Data,
                Size = request.Data.Length,
                Extension = request.Extension,
                MimeType = request.MimeType,
                WasOptimized = false,
            };
        }

        if (MediaOptimizationHelper.ShouldSkipOptimization(request.Extension, request.MimeType))
        {
            return new MediaOptimizationResult
            {
                Data = request.Data,
                Size = request.Data.Length,
                Extension = request.Extension,
                MimeType = request.MimeType,
                WasOptimized = false,
            };
        }

        var targetFormat = ResolveMagickFormat(settings.PreferredFormat);

        try
        {
            using var image = new MagickImage(request.Data);

            if (targetFormat == MagickFormat.Unknown)
            {
                targetFormat = image.Format;
            }

            var (maxWidth, maxHeight) = MediaSizeHelper.ParseSize(settings.MaxDimensions, SettingDefaults.MediaMaxWidth, SettingDefaults.MediaMaxHeight);
            ApplyResize(image, maxWidth, maxHeight);
            EnsureTransparencyPreserved(image, targetFormat);
            image.Strip();
            image.Quality = (uint)settings.Quality;
            image.Format = targetFormat;

            var optimizedBytes = image.ToByteArray();
            var optimizedExtension = $".{targetFormat.ToString().ToLowerInvariant()}";
            var optimizedMimeType = ResolveMimeType(optimizedExtension) ?? request.MimeType;

            return new MediaOptimizationResult
            {
                Data = optimizedBytes,
                Size = optimizedBytes.Length,
                Extension = optimizedExtension,
                MimeType = optimizedMimeType,
                WasOptimized = true,
            };
        }
        catch (MagickException ex)
        {
            logger.LogWarning(ex, "Failed to optimize image {FileName}. Returning original data.", request.FileName);
            return new MediaOptimizationResult
            {
                Data = request.Data,
                Size = request.Data.Length,
                Extension = request.Extension,
                MimeType = request.MimeType,
                WasOptimized = false,
            };
        }
    }

    private static void ApplyResize(MagickImage image, int? maxWidth, int? maxHeight)
    {
        if ((maxWidth ?? 0) <= 0 && (maxHeight ?? 0) <= 0)
        {
            return;
        }

        var targetWidth = maxWidth.HasValue && maxWidth.Value > 0
            ? (uint)maxWidth.Value
            : image.Width;
        var targetHeight = maxHeight.HasValue && maxHeight.Value > 0
            ? (uint)maxHeight.Value
            : image.Height;

        var geometry = new MagickGeometry(targetWidth, targetHeight)
        {
            IgnoreAspectRatio = false,
            Greater = true,
        };

        image.Resize(geometry);
    }

    private static void EnsureTransparencyPreserved(MagickImage image, MagickFormat targetFormat)
    {
        if (!image.HasAlpha || !SupportsTransparency(targetFormat))
        {
            return;
        }

        image.Alpha(AlphaOption.Set);
        image.BackgroundColor = MagickColors.Transparent;
        image.ColorType = ColorType.TrueColorAlpha;
    }

    private static bool SupportsTransparency(MagickFormat format)
    {
        return format == MagickFormat.Avif
            || format == MagickFormat.WebP
            || format == MagickFormat.Png
            || format == MagickFormat.Tiff;
    }

    private static MagickFormat ResolveMagickFormat(string format)
    {
        var supported = MagickNET.SupportedFormats
            .Where(item => item.SupportsWriting)
            .Select(item => item.Format)
            .ToHashSet();

        if (Enum.TryParse<MagickFormat>(format, true, out var magickFormat) && supported.Contains(magickFormat))
        {
            return magickFormat;
        }

        return MagickFormat.Unknown;
    }

    private static string? ResolveMimeType(string extension)
    {
        var provider = ContentTypeHelper.CreateCustomizedProvider();
        if (provider.TryGetContentType($"file{extension}", out var mimeType))
        {
            return mimeType;
        }

        var fallbackProvider = new FileExtensionContentTypeProvider();
        return fallbackProvider.TryGetContentType($"file{extension}", out var fallbackMime)
            ? fallbackMime
            : null;
    }
}