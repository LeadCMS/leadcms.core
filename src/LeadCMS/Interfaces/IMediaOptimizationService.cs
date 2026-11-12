// <copyright file="IMediaOptimizationService.cs" company="WavePoint Co. Ltd.">
// Licensed under the MIT license. See LICENSE file in the samples root for full license information.
// </copyright>

using LeadCMS.Constants;

namespace LeadCMS.Interfaces;

public sealed class MediaOptimizationRequest
{
    public byte[] Data { get; set; } = Array.Empty<byte>();

    public string FileName { get; set; } = string.Empty;

    public string Extension { get; set; } = string.Empty;

    public string MimeType { get; set; } = string.Empty;
}

public sealed class MediaOptimizationResult
{
    public byte[] Data { get; set; } = Array.Empty<byte>();

    public long Size { get; set; }

    public string Extension { get; set; } = string.Empty;

    public string MimeType { get; set; } = string.Empty;

    public bool WasOptimized { get; set; }
}

public sealed class MediaOptimizationSettings
{
    public string? MaxDimensions { get; set; }

    public string PreferredFormat { get; set; } = string.Empty;

    public bool EnableOptimisation { get; set; } = SettingDefaults.MediaEnableOptimisation;

    public int Quality { get; set; } = SettingDefaults.MediaQuality;
}

public interface IMediaOptimizationService
{
    Task<MediaOptimizationResult> OptimizeAsync(MediaOptimizationRequest request, bool force = false, CancellationToken cancellationToken = default);

    Task<MediaOptimizationSettings> GetSettingsAsync(CancellationToken cancellationToken = default);
}