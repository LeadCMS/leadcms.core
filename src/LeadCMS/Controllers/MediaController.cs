// <copyright file="MediaController.cs" company="WavePoint Co. Ltd.">
// Licensed under the MIT license. See LICENSE file in the samples root for full license information.
// </copyright>

using System.ComponentModel.DataAnnotations;
using System.Globalization;
using System.Text.RegularExpressions;
using AutoMapper;
using LeadCMS.Constants;
using LeadCMS.Data;
using LeadCMS.DTOs;
using LeadCMS.Entities;
using LeadCMS.Helpers;
using LeadCMS.Infrastructure;
using LeadCMS.Interfaces;
using LeadCMS.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace LeadCMS.Controllers;

[Authorize]
[Route("api/[controller]")]
public class MediaController : ControllerBase
{
    private readonly PgDbContext pgDbContext;
    private readonly QueryProviderFactory<Media> queryProviderFactory;
    private readonly ISyncService syncService;
    private readonly IMapper mapper;
    private readonly IMediaResolver mediaResolver;
    private readonly IMediaOptimizationService mediaOptimizationService;
    private readonly ISettingService settingService;
    private readonly IMediaChangeLogService mediaChangeLogService;
    private readonly IMediaUsageService mediaUsageService;

    public MediaController(
        PgDbContext pgDbContext,
        QueryProviderFactory<Media> queryProviderFactory,
        ISyncService syncService,
        IMapper mapper,
        IMediaResolver mediaResolver,
        IMediaOptimizationService mediaOptimizationService,
        ISettingService settingService,
        IMediaChangeLogService mediaChangeLogService,
        IMediaUsageService mediaUsageService)
    {
        this.pgDbContext = pgDbContext;
        this.queryProviderFactory = queryProviderFactory;
        this.syncService = syncService;
        this.mapper = mapper;
        this.mediaResolver = mediaResolver;
        this.mediaOptimizationService = mediaOptimizationService;
        this.settingService = settingService;
        this.mediaChangeLogService = mediaChangeLogService;
        this.mediaUsageService = mediaUsageService;
    }

    /// <summary>
    /// Uploads a new media file or updates an existing one.
    /// Supports X-Media-Resolution header or mediaResolution query parameter: "absolute" for full URLs, otherwise returns relative paths.
    /// </summary>
    [HttpPost]
    [ProducesResponseType(typeof(MediaDetailsDto), StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status422UnprocessableEntity)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status500InternalServerError)]
    public async Task<ActionResult> Post([FromForm] MediaCreateDto imageCreateDto)
    {
        var rawFileName = imageCreateDto.File!.FileName;
        var incomingFileName = rawFileName.ToTranslit().Slugify();
        var incomingFileExtension = Path.GetExtension(imageCreateDto.File!.FileName);
        var incomingFileSize = imageCreateDto.File!.Length; // bytes
        var incomingFileMimeType = ContentTypeHelper.GetMimeTypeOrThrow(incomingFileName, ModelState);

        using var fileStream = imageCreateDto.File.OpenReadStream();
        var imageInBytes = new byte[incomingFileSize];
        await fileStream.ReadAsync(imageInBytes, 0, (int)imageCreateDto.File.Length);

        var optimizationResult = await mediaOptimizationService.OptimizeAsync(new MediaOptimizationRequest
        {
            Data = imageInBytes,
            FileName = incomingFileName,
            Extension = incomingFileExtension,
            MimeType = incomingFileMimeType,
        });

        var settings = await mediaOptimizationService.GetSettingsAsync();
        var normalizedTags = NormalizeTags(imageCreateDto.Tags);
        var hasCoverTag = HasCoverTag(normalizedTags);

        var processedResult = await ApplyCoverDimensionsIfNeeded(
            optimizationResult.Data,
            optimizationResult.MimeType,
            hasCoverTag);

        var scopeAndFileExists = from i in pgDbContext!.Media!
                                 where i.ScopeUid == imageCreateDto.ScopeUid.Trim() &&
                                     (i.Name == incomingFileName || i.OriginalName == incomingFileName)
                                 select i;

        Media uploadedMedia;

        if (scopeAndFileExists.Count() > 0)
        {
            uploadedMedia = scopeAndFileExists!.FirstOrDefault()!;
            var oldScope = uploadedMedia.ScopeUid;
            var oldName = uploadedMedia.Name;
            var oldOriginalName = uploadedMedia.OriginalName;
            var newScope = imageCreateDto.ScopeUid.Trim();

            var newName = ApplyMediaBinaryData(
                uploadedMedia,
                settings.EnableOptimisation,
                imageInBytes,
                incomingFileSize,
                incomingFileExtension,
                incomingFileMimeType,
                incomingFileName,
                processedResult.Data,
                processedResult.Size,
                optimizationResult.Extension,
                optimizationResult.MimeType);

            TrySetImageDimensions(
                uploadedMedia,
                incomingFileMimeType,
                imageInBytes,
                optimizationResult.MimeType,
                processedResult.Data);

            // Update optional description if provided
            if (!string.IsNullOrWhiteSpace(imageCreateDto.Description))
            {
                uploadedMedia.Description = imageCreateDto.Description!.Trim();
            }

            if (imageCreateDto.Tags != null)
            {
                uploadedMedia.Tags = normalizedTags;
            }

            if (!string.Equals(oldScope, newScope, StringComparison.OrdinalIgnoreCase) ||
                !string.Equals(oldName, newName, StringComparison.OrdinalIgnoreCase))
            {
                await UpdateContentReferencesAsync(
                    oldScope,
                    oldName,
                    oldOriginalName,
                    newScope,
                    newName);
            }

            // Also update references from the raw upload file name if it differs
            // (e.g., content was uploaded via SDK with "UPPERCASE.png" but stored as "uppercase.png")
            // Use case-sensitive comparison because slugification lowercases the name.
            if (!string.Equals(rawFileName, oldName, StringComparison.Ordinal) &&
                !string.Equals(rawFileName, oldOriginalName, StringComparison.Ordinal))
            {
                await UpdateContentReferencesAsync(
                    newScope,
                    rawFileName,
                    null,
                    newScope,
                    newName);
            }

            pgDbContext.Media!.Update(uploadedMedia);
        }
        else
        {
            uploadedMedia = new Media()
            {
                ScopeUid = imageCreateDto.ScopeUid.Trim(),
                Description = string.IsNullOrWhiteSpace(imageCreateDto.Description) ? null : imageCreateDto.Description!.Trim(),
                Tags = normalizedTags,
            };

            ApplyMediaBinaryData(
                uploadedMedia,
                settings.EnableOptimisation,
                imageInBytes,
                incomingFileSize,
                incomingFileExtension,
                incomingFileMimeType,
                incomingFileName,
                processedResult.Data,
                processedResult.Size,
                optimizationResult.Extension,
                optimizationResult.MimeType);

            TrySetImageDimensions(
                uploadedMedia,
                incomingFileMimeType,
                imageInBytes,
                optimizationResult.MimeType,
                processedResult.Data);
            await pgDbContext.Media!.AddAsync(uploadedMedia);

            // Update content references from the raw upload file name to the final stored name
            // Use case-sensitive comparison because slugification lowercases the name.
            var newScope = imageCreateDto.ScopeUid.Trim();
            if (!string.Equals(rawFileName, uploadedMedia.Name, StringComparison.Ordinal))
            {
                await UpdateContentReferencesAsync(
                    newScope,
                    rawFileName,
                    null,
                    newScope,
                    uploadedMedia.Name);
            }
        }

        await pgDbContext.SaveChangesAsync();

        Log.Information("Request scheme {0}", HttpContext.Request.Scheme);
        Log.Information("Request host {0}", HttpContext.Request.Host.Value);

        var fileData = mapper.Map<MediaDetailsDto>(uploadedMedia);
        fileData.Location = CalculateMediaLocation(uploadedMedia.ScopeUid, uploadedMedia.Name);

        return CreatedAtAction(nameof(Get), new { scopeUid = uploadedMedia.ScopeUid, fileName = uploadedMedia.Name }, fileData);
    }

    [HttpGet]
    [AllowAnonymous]
    [Route("{*pathToFile}")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status422UnprocessableEntity)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status500InternalServerError)]
    public async Task<ActionResult> Get([Required] string pathToFile, [FromQuery] bool original = false)
    {
        pathToFile = Uri.UnescapeDataString(pathToFile);

        var scope = Path.GetDirectoryName(pathToFile)!.Replace(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        var fname = Path.GetFileName(pathToFile);

        var uploadedImageData = await pgDbContext!.Media!
            .FirstOrDefaultAsync(e => e.ScopeUid == scope && e.Name == fname);

        var servedData = uploadedImageData?.Data;
        var servedMimeType = uploadedImageData?.MimeType;
        var servedSize = uploadedImageData?.Size ?? 0;

        if (uploadedImageData != null)
        {
            if (original && uploadedImageData.OriginalData != null && uploadedImageData.OriginalData.Length > 0)
            {
                servedData = uploadedImageData.OriginalData;
                servedMimeType = uploadedImageData.OriginalMimeType ?? uploadedImageData.MimeType;
                servedSize = uploadedImageData.OriginalSize ?? servedData.LongLength;
            }
            else
            {
                servedData = uploadedImageData.Data;
                servedMimeType = uploadedImageData.MimeType;
                servedSize = uploadedImageData.Size;
            }
        }
        else
        {
            uploadedImageData = await pgDbContext!.Media!
                .FirstOrDefaultAsync(e => e.ScopeUid == scope && e.OriginalName == fname);

            if (uploadedImageData == null)
            {
                throw new EntityNotFoundException(nameof(Media), pathToFile);
            }

            // Matched by OriginalName - always return original data (legacy behavior)
            servedData = uploadedImageData.OriginalData ?? uploadedImageData.Data;
            servedMimeType = uploadedImageData.OriginalMimeType ?? uploadedImageData.MimeType;
            servedSize = uploadedImageData.OriginalSize ?? servedData.LongLength;
        }

        // Compute ETag (using file size and updatedAt/createdAt)
        DateTime lastModified = uploadedImageData.UpdatedAt ?? uploadedImageData.CreatedAt;
        string etag = $"\"{servedSize}-{lastModified.ToUniversalTime().Ticks}\"";
        string lastModifiedString = lastModified.ToUniversalTime().ToString("R"); // RFC1123

        // Set ETag and Last-Modified headers
        Response.Headers["Cache-Control"] = CacheHeaderPolicies.GetMediaCacheControl(Request.Query["v"].ToString());
        Response.Headers["Vary"] = "User-Agent";
        Response.Headers["ETag"] = etag;
        Response.Headers["Last-Modified"] = lastModifiedString;

        // Check If-None-Match and If-Modified-Since
        var ifNoneMatch = Request.Headers["If-None-Match"].FirstOrDefault();
        var ifModifiedSince = Request.Headers["If-Modified-Since"].FirstOrDefault();

        if ((!string.IsNullOrEmpty(ifNoneMatch) && ifNoneMatch == etag) ||
            (!string.IsNullOrEmpty(ifModifiedSince) &&
                DateTime.TryParse(ifModifiedSince, out var since) &&
                lastModified.ToUniversalTime() <= since.ToUniversalTime().AddSeconds(1)))
        {
            return StatusCode(StatusCodes.Status304NotModified);
        }

        // For images, return FileStreamResult with no filename so Content-Disposition is not set
        if (servedMimeType.StartsWith("image/", StringComparison.OrdinalIgnoreCase))
        {
            return new FileContentResult(servedData, servedMimeType);
        }

        // For other types, keep default (attachment)
        return File(servedData, servedMimeType, fname);
    }

    [HttpDelete]
    [Route("{*pathToFile}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status500InternalServerError)]
    public async Task<ActionResult> Delete([Required] string pathToFile)
    {
        pathToFile = Uri.UnescapeDataString(pathToFile);

        var scope = Path.GetDirectoryName(pathToFile)!.Replace(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        var fname = Path.GetFileName(pathToFile);

        var mediaToDelete = await pgDbContext!.Media!.FirstOrDefaultAsync(e => e.ScopeUid == scope && e.Name == fname);

        if (mediaToDelete == null)
        {
            throw new EntityNotFoundException(nameof(Media), pathToFile);
        }

        await mediaChangeLogService.LogMediaDeletedAsync(mediaToDelete);
        pgDbContext.Media!.Remove(mediaToDelete);
        await pgDbContext.SaveChangesAsync();

        return NoContent();
    }

    [HttpDelete("bulk")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status422UnprocessableEntity)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status500InternalServerError)]
    public async Task<ActionResult> DeleteMany([FromBody] List<int> ids)
    {
        return await BulkDeleteHelper.ExecuteAsync(
            pgDbContext,
            pgDbContext.Media!,
            ids,
            onAfterDelete: async (deletedEntities) =>
            {
                await mediaChangeLogService.LogMediaDeletedBatchAsync(deletedEntities);
            });
    }

    [HttpGet("tags")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status500InternalServerError)]
    public async Task<ActionResult<string[]>> GetTags()
    {
        var tags = TagsHelper.ToDistinctTags(await pgDbContext.Media!.Select(media => media.Tags).ToArrayAsync());
        return Ok(tags);
    }

    /// <summary>
    /// Retrieves a list of media files, optionally including folder structure.
    /// Supports X-Media-Resolution header or mediaResolution query parameter: "absolute" for full URLs, otherwise returns relative paths.
    /// </summary>
    [HttpGet]
    [ProducesResponseType(typeof(List<MediaDetailsDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status500InternalServerError)]
    public async Task<ActionResult<List<MediaDetailsDto>>> GetList(
        [FromQuery] string? query = null,
        [FromQuery] string? scopeUid = null,
        [FromQuery] bool includeFolders = false,
        [FromQuery] string? order = null)
    {
        if (!string.IsNullOrWhiteSpace(query))
        {
            includeFolders = false;
        }

        if (!includeFolders)
        {
            var qp = queryProviderFactory.BuildQueryProvider();
            var result = await qp.GetResult();

            Response.Headers.Append(ResponseHeaderNames.TotalCount, result.TotalCount.ToString());
            Response.Headers.Append(ResponseHeaderNames.AccessControlExposeHeader, ResponseHeaderNames.TotalCount);

            var mediaList = result.Records ?? new List<Media>();

            var mapped = mediaList.Select(MapToMediaDto).ToList();

            return Ok(mapped);
        }
        else
        {
            var scopePrefix = string.IsNullOrEmpty(scopeUid) ? string.Empty : scopeUid.TrimEnd('/');

            // Parse order parameter (default: Name ASC)
            var (orderProperty, orderAscending) = ParseOrderParameter(order);

            // Query media without binary Data fields for efficiency
            var mediaQuery = pgDbContext.Media!
                .Select(m => new Media
                {
                    Id = m.Id,
                    ScopeUid = m.ScopeUid,
                    Name = m.Name,
                    Description = m.Description,
                    Size = m.Size,
                    MimeType = m.MimeType,
                    Extension = m.Extension,
                    Tags = m.Tags,
                    UsageCount = m.UsageCount,
                    CreatedAt = m.CreatedAt,
                    UpdatedAt = m.UpdatedAt,
                    Width = m.Width,
                    Height = m.Height,
                    OriginalWidth = m.OriginalWidth,
                    OriginalHeight = m.OriginalHeight,
                    OriginalName = m.OriginalName,
                    OriginalSize = m.OriginalSize,
                    OriginalExtension = m.OriginalExtension,
                    OriginalMimeType = m.OriginalMimeType,
                });

            List<string> folderScopeUids;
            List<MediaDetailsDto> fileDtos;

            if (string.IsNullOrEmpty(scopePrefix))
            {
                // Root level: need all media to compute folder stats
                var allMedia = await mediaQuery.ToListAsync();

                // Root: get all distinct first-level ScopeUid parts
                folderScopeUids = allMedia
                    .Where(m => !string.IsNullOrEmpty(m.ScopeUid))
                    .Select(m => m.ScopeUid.TrimStart('/'))
                    .Where(s => !string.IsNullOrEmpty(s))
                    .Select(s => s.Split('/')[0])
                    .Where(name => !string.IsNullOrEmpty(name))
                    .Distinct()
                    .ToList();

                // Files in root (ScopeUid is empty)
                fileDtos = allMedia
                    .Where(m => string.IsNullOrEmpty(m.ScopeUid))
                    .Select(MapToMediaDto)
                    .ToList();

                // Build folder DTOs from in-memory data
                var folderDtos = BuildFolderDtos(folderScopeUids, allMedia);
                var resultList = folderDtos.Concat(fileDtos).ToList();

                // Apply sorting
                resultList = ApplyMediaSorting(resultList, orderProperty, orderAscending);

                return Ok(resultList);
            }
            else
            {
                // Subfolder: only query media in this folder and below
                var prefix = scopePrefix + "/";

                var relevantMedia = await mediaQuery
                    .Where(m => m.ScopeUid == scopePrefix || m.ScopeUid.StartsWith(prefix))
                    .ToListAsync();

                // Get distinct next-level folder names
                folderScopeUids = relevantMedia
                    .Where(m => m.ScopeUid.StartsWith(prefix))
                    .Select(m => m.ScopeUid.Substring(prefix.Length).TrimStart('/'))
                    .Where(s => !string.IsNullOrEmpty(s))
                    .Select(s => s.Split('/')[0])
                    .Where(name => !string.IsNullOrEmpty(name))
                    .Distinct()
                    .Select(s => scopePrefix + "/" + s)
                    .ToList();

                // Files in this folder
                fileDtos = relevantMedia
                    .Where(m => m.ScopeUid == scopePrefix)
                    .Select(MapToMediaDto)
                    .ToList();

                // Build folder DTOs from in-memory data
                var folderDtos = BuildFolderDtos(folderScopeUids, relevantMedia);
                var resultList = folderDtos.Concat(fileDtos).ToList();

                // Apply sorting
                resultList = ApplyMediaSorting(resultList, orderProperty, orderAscending);

                return Ok(resultList);
            }
        }
    }

    /// <summary>
    /// Updates an existing media file's content or metadata.
    /// Supports X-Media-Resolution header or mediaResolution query parameter: "absolute" for full URLs, otherwise returns relative paths.
    /// </summary>
    [HttpPatch]
    [ProducesResponseType(typeof(MediaDetailsDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status422UnprocessableEntity)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status500InternalServerError)]
    public async Task<ActionResult<MediaDetailsDto>> Patch([FromForm] MediaUpdateDto mediaUpdateDto)
    {
        // Find existing media record by scope UID and file name
        var existingMedia = await pgDbContext.Media!.FirstOrDefaultAsync(m =>
            m.ScopeUid == mediaUpdateDto.ScopeUid.Trim() && m.Name == mediaUpdateDto.FileName.Trim());

        if (existingMedia == null)
        {
            throw new EntityNotFoundException(nameof(Media), $"{mediaUpdateDto.ScopeUid}/{mediaUpdateDto.FileName}");
        }

        // If a file is provided, update the binary keeping name/extension; otherwise only update metadata
        if (mediaUpdateDto.File != null)
        {
            var oldScope = existingMedia.ScopeUid;
            var oldName = existingMedia.Name;
            var oldOriginalName = existingMedia.OriginalName;

            // Process the new file content
            var incomingFileExtension = Path.GetExtension(mediaUpdateDto.File.FileName);
            var incomingFileSize = mediaUpdateDto.File.Length;
            var incomingFileMimeType = ContentTypeHelper.GetMimeTypeOrThrow(mediaUpdateDto.File.FileName, ModelState);

            using var fileStream = mediaUpdateDto.File.OpenReadStream();
            var imageInBytes = new byte[incomingFileSize];
            await fileStream.ReadAsync(imageInBytes, 0, (int)mediaUpdateDto.File.Length);

            var optimizationResult = await mediaOptimizationService.OptimizeAsync(new MediaOptimizationRequest
            {
                Data = imageInBytes,
                FileName = existingMedia.Name,
                Extension = incomingFileExtension,
                MimeType = incomingFileMimeType,
            });

            var settings = await mediaOptimizationService.GetSettingsAsync();
            var normalizedTags = mediaUpdateDto.Tags != null ? NormalizeTags(mediaUpdateDto.Tags) : existingMedia.Tags;
            var hasCoverTag = HasCoverTag(normalizedTags);
            var processedResult = await ApplyCoverDimensionsIfNeeded(
                optimizationResult.Data,
                optimizationResult.MimeType,
                hasCoverTag);

            // Update only the binary content and size, preserve other properties (ScopeUid, Name, Extension)
            var baseFileName = mediaUpdateDto.File.FileName.ToTranslit().Slugify();

            var newName = ApplyMediaBinaryData(
                existingMedia,
                settings.EnableOptimisation,
                imageInBytes,
                incomingFileSize,
                incomingFileExtension,
                incomingFileMimeType,
                baseFileName,
                processedResult.Data,
                processedResult.Size,
                optimizationResult.Extension,
                optimizationResult.MimeType);

            TrySetImageDimensions(
                existingMedia,
                incomingFileMimeType,
                imageInBytes,
                optimizationResult.MimeType,
                processedResult.Data);

            if (!string.Equals(oldName, newName, StringComparison.OrdinalIgnoreCase) ||
                !string.Equals(oldScope, existingMedia.ScopeUid, StringComparison.OrdinalIgnoreCase))
            {
                await UpdateContentReferencesAsync(
                    oldScope,
                    oldName,
                    oldOriginalName,
                    existingMedia.ScopeUid,
                    newName);
            }
        }

        // Update description if provided (can be set to empty to clear)
        if (mediaUpdateDto.Description != null)
        {
            var trimmed = mediaUpdateDto.Description.Trim();
            existingMedia.Description = string.IsNullOrEmpty(trimmed) ? null : trimmed;
        }

        if (mediaUpdateDto.Tags != null)
        {
            existingMedia.Tags = NormalizeTags(mediaUpdateDto.Tags);
        }

        pgDbContext.Media!.Update(existingMedia);
        await pgDbContext.SaveChangesAsync();

        Log.Information(
            "Updated media '{ScopeUid}/{FileName}' (ID: {Id}), preserving ScopeUid, Name, and Extension",
            existingMedia.ScopeUid,
            existingMedia.Name,
            existingMedia.Id);

        // Return the updated media details
        var updatedMediaDto = MapToMediaDto(existingMedia);

        return Ok(updatedMediaDto);
    }

    /// <summary>
    /// Optimizes all existing image media using current media optimization settings.
    /// Optionally filters by folder path.
    /// </summary>
    [HttpPost("optimize-all")]
    [Authorize(Roles = "Admin")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status500InternalServerError)]
    public async Task<ActionResult<MediaOptimizeResponseDto>> OptimizeAllImages([FromBody] MediaBulkOptimizeRequestDto? request = null)
    {
        const int batchSize = 10;
        var updatedCount = 0;
        var updatedMediaIds = new HashSet<int>();
        var offset = 0;
        var settings = await mediaOptimizationService.GetSettingsAsync();

        var preferredFormat = settings.PreferredFormat.Trim().TrimStart('.');
        var (maxWidth, maxHeight) = MediaSizeHelper.ParseSize(settings.MaxDimensions);

        var folder = request?.Folder?.Trim().Trim('/');
        var includeSubfolders = request?.IncludeSubfolders ?? false;

        while (true)
        {
            var query = pgDbContext.Media!.OrderBy(m => m.Id);
            IQueryable<Media> filteredQuery = query;

            if (!string.IsNullOrWhiteSpace(folder))
            {
                if (includeSubfolders)
                {
                    var prefix = folder + "/";
                    filteredQuery = query.Where(m => m.ScopeUid == folder || m.ScopeUid.StartsWith(prefix));
                }
                else
                {
                    filteredQuery = query.Where(m => m.ScopeUid == folder);
                }
            }

            var mediaItems = await filteredQuery
                .Skip(offset)
                .Take(batchSize)
                .ToListAsync();

            if (mediaItems.Count == 0)
            {
                break;
            }

            // Collect all rename operations for batch content update
            var deferredUpdates = new List<ContentReferenceUpdate>();

            foreach (var media in mediaItems)
            {
                if (string.IsNullOrWhiteSpace(media.MimeType) || !media.MimeType.StartsWith("image/", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                if (media.Data == null || media.Data.Length == 0)
                {
                    continue;
                }

                var skipOptimization = MediaOptimizationHelper.ShouldSkipOptimization(
                    media.OriginalExtension ?? media.Extension ?? string.Empty,
                    media.OriginalMimeType ?? media.MimeType ?? string.Empty);

                if (skipOptimization)
                {
                    if (media.OriginalData != null && media.OriginalData.Length > 0)
                    {
                        var originalName = RevertToOriginalData(media);

                        if (!string.IsNullOrWhiteSpace(originalName) &&
                            !string.Equals(media.Name, originalName, StringComparison.OrdinalIgnoreCase))
                        {
                            await RenameMediaAsync(media, media.ScopeUid, originalName, deferredUpdates);
                        }

                        if (updatedMediaIds.Add(media.Id))
                        {
                            updatedCount++;
                        }
                    }

                    continue;
                }

                EnsureOriginalDataPreserved(media);

                var shouldReoptimize = false;

                if (!string.IsNullOrWhiteSpace(preferredFormat))
                {
                    var currentFormat = (media.Extension ?? string.Empty).Trim().TrimStart('.');
                    if (string.IsNullOrWhiteSpace(currentFormat) ||
                        !string.Equals(currentFormat, preferredFormat, StringComparison.OrdinalIgnoreCase))
                    {
                        shouldReoptimize = true;
                    }
                }

                if (!shouldReoptimize && (maxWidth.HasValue || maxHeight.HasValue))
                {
                    if (!media.Width.HasValue || !media.Height.HasValue)
                    {
                        shouldReoptimize = true;
                    }
                    else
                    {
                        if (maxWidth.HasValue && media.Width > maxWidth)
                        {
                            shouldReoptimize = true;
                        }

                        if (maxHeight.HasValue && media.Height > maxHeight)
                        {
                            shouldReoptimize = true;
                        }
                    }
                }

                if (!shouldReoptimize)
                {
                    continue;
                }

                var sourceData = media.OriginalData ?? media.Data;
                var sourceExtension = media.OriginalExtension ?? media.Extension ?? string.Empty;
                var sourceMimeType = media.OriginalMimeType ?? media.MimeType ?? string.Empty;

                var optimizationResult = await mediaOptimizationService.OptimizeAsync(
                    new MediaOptimizationRequest
                    {
                        Data = sourceData,
                        FileName = media.Name ?? string.Empty,
                        Extension = sourceExtension,
                        MimeType = sourceMimeType,
                    },
                    force: true);

                var hasCoverTag = HasCoverTag(media.Tags);
                var processedResult = await ApplyCoverDimensionsIfNeeded(
                    optimizationResult.Data,
                    optimizationResult.MimeType,
                    hasCoverTag);

                TrySetImageDimensions(media, sourceMimeType, sourceData, optimizationResult.MimeType, processedResult.Data);

                var baseName = media.OriginalName ?? media.Name ?? string.Empty;
                var newName = !string.IsNullOrWhiteSpace(baseName)
                    ? GetOptimizedFileName(baseName, optimizationResult.Extension)
                    : media.Name;

                media.Data = processedResult.Data;
                media.Size = processedResult.Size;
                media.Extension = optimizationResult.Extension;
                media.MimeType = optimizationResult.MimeType;
                media.UpdatedAt = DateTime.UtcNow;

                if (!string.IsNullOrWhiteSpace(newName) &&
                    !string.Equals(media.Name, newName, StringComparison.OrdinalIgnoreCase))
                {
                    await RenameMediaAsync(media, media.ScopeUid, newName, deferredUpdates);
                }

                if (updatedMediaIds.Add(media.Id))
                {
                    updatedCount++;
                }
            }

            // Apply all deferred content reference updates in a single batch
            await ApplyDeferredContentUpdatesAsync(deferredUpdates);

            await pgDbContext.SaveChangesAsync();
            offset += mediaItems.Count;
        }

        return Ok(new MediaOptimizeResponseDto
        {
            Updated = updatedCount,
        });
    }

    /// <summary>
    /// Resets a single media file to its original state, removing optimization.
    /// </summary>
    [HttpPost("reset")]
    [Authorize(Roles = "Admin")]
    [ProducesResponseType(typeof(MediaDetailsDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status422UnprocessableEntity)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status500InternalServerError)]
    public async Task<ActionResult<MediaDetailsDto>> ResetMedia([FromBody] MediaTransformRequestDto request)
    {
        var media = await ResolveMediaAsync(request.ScopeUid, request.FileName);
        if (media == null)
        {
            return NotFound(new ProblemDetails
            {
                Title = "Media not found",
                Detail = $"Media '{request.ScopeUid}/{request.FileName}' was not found.",
                Status = StatusCodes.Status404NotFound,
            });
        }

        if (media.OriginalData == null || media.OriginalData.Length == 0)
        {
            return UnprocessableEntity(new ProblemDetails
            {
                Title = "No original data",
                Detail = "This media file does not have original data to reset to.",
                Status = StatusCodes.Status422UnprocessableEntity,
            });
        }

        var originalName = RevertToOriginalData(media);

        // Rename if needed
        if (!string.IsNullOrWhiteSpace(originalName) &&
            !string.Equals(media.Name, originalName, StringComparison.OrdinalIgnoreCase))
        {
            await RenameMediaAsync(media, media.ScopeUid, originalName);
        }

        await pgDbContext.SaveChangesAsync();

        return Ok(MapToMediaDto(media));
    }

    /// <summary>
    /// Resets all media files to their original state, removing optimization.
    /// Optionally filters by folder path.
    /// </summary>
    [HttpPost("reset-all")]
    [Authorize(Roles = "Admin")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status500InternalServerError)]
    public async Task<ActionResult<MediaOptimizeResponseDto>> ResetAllMedia([FromBody] MediaBulkResetRequestDto? request = null)
    {
        const int batchSize = 10;
        var updatedCount = 0;
        var updatedMediaIds = new HashSet<int>();

        var folder = request?.Folder?.Trim().Trim('/');
        var includeSubfolders = request?.IncludeSubfolders ?? false;

        while (true)
        {
            var query = pgDbContext.Media!.OrderBy(m => m.Id);
            IQueryable<Media> filteredQuery = query;

            if (!string.IsNullOrWhiteSpace(folder))
            {
                if (includeSubfolders)
                {
                    var prefix = folder + "/";
                    filteredQuery = query.Where(m => m.ScopeUid == folder || m.ScopeUid.StartsWith(prefix));
                }
                else
                {
                    filteredQuery = query.Where(m => m.ScopeUid == folder);
                }
            }

            var mediaItems = await filteredQuery
                .Where(m => m.OriginalData != null)
                .Take(batchSize)
                .ToListAsync();

            if (mediaItems.Count == 0)
            {
                break;
            }

            // Collect all rename operations for batch content update
            var deferredUpdates = new List<ContentReferenceUpdate>();

            foreach (var media in mediaItems)
            {
                if (media.OriginalData == null || media.OriginalData.Length == 0)
                {
                    continue;
                }

                var originalName = RevertToOriginalData(media);

                // Rename if needed
                if (!string.IsNullOrWhiteSpace(originalName) &&
                    !string.Equals(media.Name, originalName, StringComparison.OrdinalIgnoreCase))
                {
                    await RenameMediaAsync(media, media.ScopeUid, originalName, deferredUpdates);
                }

                if (updatedMediaIds.Add(media.Id))
                {
                    updatedCount++;
                }
            }

            // Apply all deferred content reference updates in a single batch
            await ApplyDeferredContentUpdatesAsync(deferredUpdates);

            await pgDbContext.SaveChangesAsync();
        }

        return Ok(new MediaOptimizeResponseDto
        {
            Updated = updatedCount,
        });
    }

    /// <summary>
    /// Renames (moves) all media files within a folder, optionally including subfolders.
    /// Updates content references in a single batch to avoid multiple change log records.
    /// </summary>
    [HttpPost("rename-folder")]
    [Authorize(Roles = "Admin")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status422UnprocessableEntity)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status500InternalServerError)]
    public async Task<ActionResult<MediaOptimizeResponseDto>> RenameFolder([FromBody] MediaBulkRenameRequestDto request)
    {
        if (string.IsNullOrWhiteSpace(request.Folder) || string.IsNullOrWhiteSpace(request.NewFolder))
        {
            return UnprocessableEntity(new ProblemDetails
            {
                Title = "Invalid rename folder request",
                Detail = "Folder and NewFolder are required.",
                Status = StatusCodes.Status422UnprocessableEntity,
            });
        }

        var folder = request.Folder.Trim().Trim('/');
        var newFolder = request.NewFolder.Trim().Trim('/');

        if (string.IsNullOrWhiteSpace(folder) || string.IsNullOrWhiteSpace(newFolder))
        {
            return UnprocessableEntity(new ProblemDetails
            {
                Title = "Invalid rename folder request",
                Detail = "Folder and NewFolder must be non-empty after trimming.",
                Status = StatusCodes.Status422UnprocessableEntity,
            });
        }

        if (string.Equals(folder, newFolder, StringComparison.OrdinalIgnoreCase))
        {
            return UnprocessableEntity(new ProblemDetails
            {
                Title = "Invalid rename folder request",
                Detail = "Folder and NewFolder must be different.",
                Status = StatusCodes.Status422UnprocessableEntity,
            });
        }

        var disallowedPrefix = folder + "/";
        if (newFolder.StartsWith(disallowedPrefix, StringComparison.OrdinalIgnoreCase))
        {
            return UnprocessableEntity(new ProblemDetails
            {
                Title = "Invalid rename folder request",
                Detail = "NewFolder cannot be a subfolder of Folder.",
                Status = StatusCodes.Status422UnprocessableEntity,
            });
        }

        var prefix = folder + "/";
        IQueryable<Media> query = pgDbContext.Media!.OrderBy(m => m.Id)
            .Where(m => m.ScopeUid == folder || m.ScopeUid.StartsWith(prefix));

        var mediaItems = await query.ToListAsync();
        if (mediaItems.Count == 0)
        {
            return Ok(new MediaOptimizeResponseDto
            {
                Updated = 0,
            });
        }

        var deferredUpdates = new List<ContentReferenceUpdate>();
        var updatedCount = 0;

        foreach (var media in mediaItems)
        {
            var newScopeUid = ResolveNewScopeUid(media.ScopeUid, folder, newFolder);
            if (string.Equals(media.ScopeUid, newScopeUid, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            await RenameMediaAsync(media, newScopeUid, media.Name, deferredUpdates);
            updatedCount++;
        }

        await ApplyDeferredContentUpdatesAsync(deferredUpdates);
        await pgDbContext.SaveChangesAsync();

        return Ok(new MediaOptimizeResponseDto
        {
            Updated = updatedCount,
        });
    }

    /// <summary>
    /// Deletes all media files within a folder, including all subfolders.
    /// </summary>
    [HttpPost("delete-folder")]
    [Authorize(Roles = "Admin")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status422UnprocessableEntity)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status500InternalServerError)]
    public async Task<ActionResult<MediaOptimizeResponseDto>> DeleteFolder([FromBody] MediaBulkDeleteRequestDto request)
    {
        if (string.IsNullOrWhiteSpace(request.Folder))
        {
            return UnprocessableEntity(new ProblemDetails
            {
                Title = "Invalid delete folder request",
                Detail = "Folder is required.",
                Status = StatusCodes.Status422UnprocessableEntity,
            });
        }

        var folder = request.Folder.Trim().Trim('/');
        if (string.IsNullOrWhiteSpace(folder))
        {
            return UnprocessableEntity(new ProblemDetails
            {
                Title = "Invalid delete folder request",
                Detail = "Folder must be non-empty after trimming.",
                Status = StatusCodes.Status422UnprocessableEntity,
            });
        }

        const int batchSize = 50;
        var deletedCount = 0;
        var prefix = folder + "/";

        while (true)
        {
            var batch = await pgDbContext.Media!
                .Where(m => m.ScopeUid == folder || m.ScopeUid.StartsWith(prefix))
                .OrderBy(m => m.Id)
                .Take(batchSize)
                .ToListAsync();

            if (batch.Count == 0)
            {
                break;
            }

            await mediaChangeLogService.LogMediaDeletedBatchAsync(batch);
            pgDbContext.Media!.RemoveRange(batch);
            await pgDbContext.SaveChangesAsync();
            deletedCount += batch.Count;
        }

        return Ok(new MediaOptimizeResponseDto
        {
            Updated = deletedCount,
        });
    }

    [HttpPost("rename")]
    [Authorize(Roles = "Admin")]
    [ProducesResponseType(typeof(MediaDetailsDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status422UnprocessableEntity)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status500InternalServerError)]
    public async Task<ActionResult<MediaDetailsDto>> RenameMedia([FromBody] MediaRenameRequestDto request)
    {
        if (string.IsNullOrWhiteSpace(request.ScopeUid) || string.IsNullOrWhiteSpace(request.FileName) ||
            string.IsNullOrWhiteSpace(request.NewScopeUid) || string.IsNullOrWhiteSpace(request.NewFileName))
        {
            return UnprocessableEntity(new ProblemDetails
            {
                Title = "Invalid rename request",
                Detail = "ScopeUid, FileName, NewScopeUid, and NewFileName are required.",
                Status = StatusCodes.Status422UnprocessableEntity,
            });
        }

        var media = await pgDbContext.Media!
            .FirstOrDefaultAsync(m => m.ScopeUid == request.ScopeUid &&
                                      (m.Name == request.FileName || m.OriginalName == request.FileName));

        if (media == null)
        {
            return NotFound(new ProblemDetails
            {
                Title = "Media not found",
                Detail = $"Media '{request.ScopeUid}/{request.FileName}' was not found.",
                Status = StatusCodes.Status404NotFound,
            });
        }

        var linksUpdated = await RenameMediaAsync(media, request.NewScopeUid, request.NewFileName);
        var dto = MapToMediaDto(media);
        dto.UsageCount = linksUpdated;

        return Ok(dto);
    }

    [HttpPost("optimize")]
    [Authorize(Roles = "Admin")]
    [ProducesResponseType(typeof(MediaDetailsDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status422UnprocessableEntity)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status500InternalServerError)]
    public async Task<ActionResult<MediaDetailsDto>> OptimizeMedia([FromBody] MediaTransformRequestDto request)
    {
        var media = await ResolveMediaAsync(request.ScopeUid, request.FileName);
        if (media == null)
        {
            return NotFound(new ProblemDetails
            {
                Title = "Media not found",
                Detail = $"Media '{request.ScopeUid}/{request.FileName}' was not found.",
                Status = StatusCodes.Status404NotFound,
            });
        }

        var sourceData = media.OriginalData ?? media.Data;
        var sourceExtension = media.OriginalExtension ?? media.Extension ?? string.Empty;
        var sourceMimeType = media.OriginalMimeType ?? media.MimeType ?? string.Empty;
        var baseName = !string.IsNullOrWhiteSpace(media.OriginalName)
            ? media.OriginalName
            : media.Name;

        if (sourceData == null || sourceData.Length == 0 ||
            string.IsNullOrWhiteSpace(sourceMimeType) ||
            !sourceMimeType.StartsWith("image/", StringComparison.OrdinalIgnoreCase))
        {
            return UnprocessableEntity(new ProblemDetails
            {
                Title = "Unsupported media",
                Detail = "Only image media can be optimized.",
                Status = StatusCodes.Status422UnprocessableEntity,
            });
        }

        var response = await ApplyMediaTransformAsync(
            media,
            sourceData,
            sourceData,
            sourceExtension,
            sourceMimeType,
            baseName,
            forceOptimize: true);

        return Ok(response);
    }

    [HttpPost("resize")]
    [Authorize(Roles = "Admin")]
    [ProducesResponseType(typeof(MediaDetailsDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status422UnprocessableEntity)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status500InternalServerError)]
    public async Task<ActionResult<MediaDetailsDto>> ResizeMedia([FromBody] MediaResizeRequestDto request)
    {
        var media = await ResolveMediaAsync(request.ScopeUid, request.FileName);
        if (media == null)
        {
            return NotFound(new ProblemDetails
            {
                Title = "Media not found",
                Detail = $"Media '{request.ScopeUid}/{request.FileName}' was not found.",
                Status = StatusCodes.Status404NotFound,
            });
        }

        var sourceData = media.OriginalData ?? media.Data;
        var sourceExtension = media.OriginalExtension ?? media.Extension ?? string.Empty;
        var sourceMimeType = media.OriginalMimeType ?? media.MimeType ?? string.Empty;
        var baseName = !string.IsNullOrWhiteSpace(media.OriginalName)
            ? media.OriginalName
            : media.Name;

        if (sourceData == null || sourceData.Length == 0 ||
            string.IsNullOrWhiteSpace(sourceMimeType) ||
            !sourceMimeType.StartsWith("image/", StringComparison.OrdinalIgnoreCase))
        {
            return UnprocessableEntity(new ProblemDetails
            {
                Title = "Unsupported media",
                Detail = "Only image media can be resized.",
                Status = StatusCodes.Status422UnprocessableEntity,
            });
        }

        byte[] resizedData;
        try
        {
            using var image = new ImageMagick.MagickImage(sourceData);
            var geometry = new ImageMagick.MagickGeometry((uint)request.Width, (uint)request.Height)
            {
                IgnoreAspectRatio = !request.MaintainAspectRatio,
            };
            image.Resize(geometry);
            resizedData = image.ToByteArray();
        }
        catch
        {
            return UnprocessableEntity(new ProblemDetails
            {
                Title = "Resize failed",
                Detail = "Unable to resize the provided image.",
                Status = StatusCodes.Status422UnprocessableEntity,
            });
        }

        var response = await ApplyMediaTransformAsync(
            media,
            sourceData,
            resizedData,
            sourceExtension,
            sourceMimeType,
            baseName);

        return Ok(response);
    }

    [HttpPost("crop")]
    [Authorize(Roles = "Admin")]
    [ProducesResponseType(typeof(MediaDetailsDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status422UnprocessableEntity)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status500InternalServerError)]
    public async Task<ActionResult<MediaDetailsDto>> CropMedia([FromBody] MediaCropRequestDto request)
    {
        var media = await ResolveMediaAsync(request.ScopeUid, request.FileName);
        if (media == null)
        {
            return NotFound(new ProblemDetails
            {
                Title = "Media not found",
                Detail = $"Media '{request.ScopeUid}/{request.FileName}' was not found.",
                Status = StatusCodes.Status404NotFound,
            });
        }

        var sourceData = media.OriginalData ?? media.Data;
        var sourceExtension = media.OriginalExtension ?? media.Extension ?? string.Empty;
        var sourceMimeType = media.OriginalMimeType ?? media.MimeType ?? string.Empty;
        var baseName = !string.IsNullOrWhiteSpace(media.OriginalName)
            ? media.OriginalName
            : media.Name;

        if (sourceData == null || sourceData.Length == 0 ||
            string.IsNullOrWhiteSpace(sourceMimeType) ||
            !sourceMimeType.StartsWith("image/", StringComparison.OrdinalIgnoreCase))
        {
            return UnprocessableEntity(new ProblemDetails
            {
                Title = "Unsupported media",
                Detail = "Only image media can be cropped.",
                Status = StatusCodes.Status422UnprocessableEntity,
            });
        }

        byte[] croppedData;
        try
        {
            using var image = new ImageMagick.MagickImage(sourceData);
            var imageWidth = (int)image.Width;
            var imageHeight = (int)image.Height;
            var cropX = request.X ?? Math.Max(0, (imageWidth - request.Width) / 2);
            var cropY = request.Y ?? Math.Max(0, (imageHeight - request.Height) / 2);
            cropX = Math.Max(0, Math.Min(cropX, imageWidth - request.Width));
            cropY = Math.Max(0, Math.Min(cropY, imageHeight - request.Height));
            image.Crop(new ImageMagick.MagickGeometry(cropX, cropY, (uint)request.Width, (uint)request.Height));
            image.Page = new ImageMagick.MagickGeometry(0, 0, 0, 0);
            croppedData = image.ToByteArray();
        }
        catch
        {
            return UnprocessableEntity(new ProblemDetails
            {
                Title = "Crop failed",
                Detail = "Unable to crop the provided image.",
                Status = StatusCodes.Status422UnprocessableEntity,
            });
        }

        var response = await ApplyMediaTransformAsync(
            media,
            sourceData,
            croppedData,
            sourceExtension,
            sourceMimeType,
            baseName);

        return Ok(response);
    }

    /// <summary>
    /// Synchronizes media data based on the sync token for incremental updates.
    /// Supports X-Media-Resolution header or mediaResolution query parameter: "absolute" for full URLs, otherwise returns relative paths.
    /// </summary>
    [HttpGet("sync")]
    [AllowAnonymous]
    [ProducesResponseType(typeof(SyncResponseDto<MediaDetailsDto, MediaDeletedDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status500InternalServerError)]
    public async Task<IActionResult> Sync([FromQuery] string? syncToken = null, [FromQuery] string? query = null)
    {
        var result = await syncService.SyncMediaAsync(queryProviderFactory, mapper, syncToken, query);

        // Calculate Location for each MediaDetailsDto if we have items in the result
        if (result is OkObjectResult okResult && okResult.Value is SyncResponseDto<MediaDetailsDto, MediaDeletedDto> syncResponse)
        {
            foreach (var item in syncResponse.Items)
            {
                item.Location = CalculateMediaLocation(item.ScopeUid, item.Name);
            }
        }

        return result;
    }

    /// <summary>
    /// Returns the supported image formats that can be used as preferred media format.
    /// </summary>
    [HttpGet("formats")]
    [AllowAnonymous]
    [ProducesResponseType(typeof(string[]), StatusCodes.Status200OK)]
    public ActionResult<string[]> GetSupportedImageFormats()
    {
        var allowedFormats = new[] { "jpeg", "jpg", "png", "webp", "avif" };
        var supportedFormats = ImageMagick.MagickNET.SupportedFormats
            .Where(format => format.SupportsWriting)
            .Select(format => format.Format.ToString().ToLowerInvariant())
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var result = allowedFormats
            .Where(format => supportedFormats.Contains(format))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        return Ok(result);
    }

    /// <summary>
    /// Triggers a full re-index of media usage counts, descriptions and content-type tags
    /// by scanning all content items.
    /// </summary>
    [HttpPost("reindex-usage")]
    [Authorize(Roles = "Admin")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<ActionResult> ReindexUsage()
    {
        var result = await mediaUsageService.UpdateMediaUsageFromAllContentAsync();

        return Ok(new
        {
            result.ContentsProcessed,
            result.MediaUpdated,
        });
    }

    private static byte[] ResizeToCover(byte[] data, int targetWidth, int targetHeight)
    {
        using var image = new ImageMagick.MagickImage(data);

        var scale = Math.Max((double)targetWidth / image.Width, (double)targetHeight / image.Height);
        var resizedWidth = (int)Math.Ceiling(image.Width * scale);
        var resizedHeight = (int)Math.Ceiling(image.Height * scale);

        image.Resize((uint)resizedWidth, (uint)resizedHeight);

        var cropX = Math.Max(0, (resizedWidth - targetWidth) / 2);
        var cropY = Math.Max(0, (resizedHeight - targetHeight) / 2);

        image.Crop(new ImageMagick.MagickGeometry(cropX, cropY, (uint)targetWidth, (uint)targetHeight));
        image.Page = new ImageMagick.MagickGeometry(0, 0, 0, 0);

        return image.ToByteArray();
    }

    private static bool HasCoverTag(string[]? tags)
    {
        return tags != null && Array.Exists(tags, tag => string.Equals(tag, "cover", StringComparison.OrdinalIgnoreCase));
    }

    private static string[] NormalizeTags(string[]? tags)
    {
        if (tags == null)
        {
            return Array.Empty<string>();
        }

        return tags
            .Where(tag => !string.IsNullOrWhiteSpace(tag))
            .Select(tag => tag.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    /// <summary>
    /// Applies media binary data to an entity, handling both optimized and non-optimized scenarios.
    /// When optimization is enabled, stores the incoming data as Original* fields and the processed data as main fields.
    /// When disabled, stores the incoming data as main fields and clears Original* fields.
    /// Returns the resulting file name.
    /// </summary>
    private static string ApplyMediaBinaryData(
        Media media,
        bool enableOptimisation,
        byte[] incomingData,
        long incomingSize,
        string incomingExtension,
        string incomingMimeType,
        string incomingName,
        byte[] processedData,
        long processedSize,
        string optimizedExtension,
        string optimizedMimeType)
    {
        media.Data = processedData;
        media.Size = processedSize;

        if (enableOptimisation)
        {
            media.OriginalData = incomingData;
            media.OriginalSize = incomingSize;
            media.OriginalExtension = incomingExtension;
            media.OriginalMimeType = incomingMimeType;
            media.OriginalName = incomingName;
            media.Extension = optimizedExtension;
            media.MimeType = optimizedMimeType;
            var optimizedName = GetOptimizedFileName(incomingName, optimizedExtension);
            media.Name = optimizedName;
            return optimizedName;
        }
        else
        {
            media.Extension = incomingExtension;
            media.MimeType = incomingMimeType;
            media.Name = incomingName;
            ClearOriginalFields(media);
            return incomingName;
        }
    }

    /// <summary>
    /// Reverts a media entity to its original (pre-optimization) state.
    /// Copies Original* fields into the main fields, clears all Original* fields, and returns the original name.
    /// Returns null if no original data is available.
    /// </summary>
    private static string? RevertToOriginalData(Media media)
    {
        if (media.OriginalData == null || media.OriginalData.Length == 0)
        {
            return null;
        }

        var originalExtension = media.OriginalExtension ?? media.Extension ?? string.Empty;
        var originalMimeType = media.OriginalMimeType ?? media.MimeType ?? string.Empty;
        var originalName = media.OriginalName ?? media.Name ?? string.Empty;

        media.Data = media.OriginalData;
        media.Size = media.OriginalSize ?? media.OriginalData.Length;
        media.Extension = originalExtension;
        media.MimeType = originalMimeType;
        media.UpdatedAt = DateTime.UtcNow;

        TrySetImageDimensions(media, originalMimeType, media.OriginalData, originalMimeType, media.OriginalData);
        ClearOriginalFields(media, includeDimensions: true);

        return originalName;
    }

    /// <summary>
    /// Clears all Original* fields on a media entity.
    /// When <paramref name="includeDimensions"/> is true, also clears OriginalWidth and OriginalHeight.
    /// </summary>
    private static void ClearOriginalFields(Media media, bool includeDimensions = false)
    {
        media.OriginalData = null;
        media.OriginalSize = null;
        media.OriginalExtension = null;
        media.OriginalMimeType = null;
        media.OriginalName = null;

        if (includeDimensions)
        {
            media.OriginalWidth = null;
            media.OriginalHeight = null;
        }
    }

    /// <summary>
    /// Ensures original data fields are populated before re-optimization.
    /// Copies current main fields to Original* if OriginalData is empty,
    /// and ensures OriginalName is set.
    /// </summary>
    private static void EnsureOriginalDataPreserved(Media media)
    {
        if (media.OriginalData == null || media.OriginalData.Length == 0)
        {
            media.OriginalData = media.Data;
            media.OriginalSize = media.Size;
            media.OriginalExtension = media.Extension;
            media.OriginalMimeType = media.MimeType;
        }

        if (string.IsNullOrWhiteSpace(media.OriginalName))
        {
            media.OriginalName = media.Name;
        }
    }

    private static void TrySetImageDimensions(
        Media media,
        string originalMimeType,
        byte[] originalData,
        string optimizedMimeType,
        byte[] optimizedData)
    {
        if (!originalMimeType.StartsWith("image/", StringComparison.OrdinalIgnoreCase) &&
            !optimizedMimeType.StartsWith("image/", StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        if (originalMimeType.StartsWith("image/", StringComparison.OrdinalIgnoreCase))
        {
            try
            {
                using var originalImage = new ImageMagick.MagickImage(originalData);
                media.OriginalWidth = (int)originalImage.Width;
                media.OriginalHeight = (int)originalImage.Height;
            }
            catch
            {
                // Ignore dimension extraction failures
            }
        }

        if (optimizedMimeType.StartsWith("image/", StringComparison.OrdinalIgnoreCase))
        {
            try
            {
                using var optimizedImage = new ImageMagick.MagickImage(optimizedData);
                media.Width = (int)optimizedImage.Width;
                media.Height = (int)optimizedImage.Height;
            }
            catch
            {
                // Ignore dimension extraction failures
            }
        }
    }

    private static string GetOptimizedFileName(string originalName, string optimizedExtension)
    {
        var extension = optimizedExtension;
        if (string.IsNullOrWhiteSpace(extension))
        {
            return originalName;
        }

        if (!extension.StartsWith('.'))
        {
            extension = "." + extension;
        }

        return Path.ChangeExtension(originalName, extension);
    }

    private static string EnsureFileNameExtension(string fileName, string extension)
    {
        if (string.IsNullOrWhiteSpace(fileName))
        {
            return fileName;
        }

        if (Path.HasExtension(fileName))
        {
            return fileName;
        }

        if (string.IsNullOrWhiteSpace(extension))
        {
            return fileName;
        }

        var normalized = extension.StartsWith('.') ? extension : "." + extension;
        return Path.ChangeExtension(fileName, normalized);
    }

    private static (string Property, bool Ascending) ParseOrderParameter(string? order)
    {
        // Default: Name ASC
        if (string.IsNullOrWhiteSpace(order))
        {
            return ("Name", true);
        }

        var parts = order.Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries);
        var property = parts.Length > 0 ? parts[0] : "Name";
        var ascending = parts.Length < 2 || !parts[1].Equals("DESC", StringComparison.OrdinalIgnoreCase);

        return (property, ascending);
    }

    private static List<MediaDetailsDto> ApplyMediaSorting(List<MediaDetailsDto> list, string property, bool ascending)
    {
        // Get property info for dynamic sorting
        var propInfo = typeof(MediaDetailsDto).GetProperty(property, System.Reflection.BindingFlags.IgnoreCase | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance);

        if (propInfo == null)
        {
            // Fallback to Name if property not found
            propInfo = typeof(MediaDetailsDto).GetProperty("Name");
        }

        if (ascending)
        {
            return list.OrderBy(m => propInfo!.GetValue(m)).ToList();
        }
        else
        {
            return list.OrderByDescending(m => propInfo!.GetValue(m)).ToList();
        }
    }

    private static List<MediaDetailsDto> BuildFolderDtos(List<string> folderScopeUids, List<Media> mediaList)
    {
        var folderDtos = new List<MediaDetailsDto>();

        foreach (var folder in folderScopeUids.Distinct())
        {
            var folderPrefix = folder + "/";

            // Filter for files in this folder and subfolders
            var folderFiles = mediaList
                .Where(m => m.ScopeUid == folder || m.ScopeUid.StartsWith(folderPrefix))
                .ToList();

            // Total count is all files in this folder and subfolders (not counting subfolders themselves)
            var totalCount = folderFiles.Count;

            var createdAt = folderFiles
                .Select(f => f.CreatedAt)
                .OrderBy(d => d)
                .FirstOrDefault();
            if (createdAt == default)
            {
                createdAt = DateTime.UtcNow;
            }

            var updatedAt = folderFiles
                .Select(f => f.UpdatedAt)
                .Where(d => d.HasValue)
                .OrderByDescending(d => d)
                .FirstOrDefault();

            var size = folderFiles.Sum(f => f.Size);
            var usageCount = folderFiles.Sum(f => f.UsageCount);
            var namePart = folder.Split('/').Last();

            var humanName = Regex.Replace(namePart, "([a-z])([A-Z])", "$1 $2").Replace("-", " ").Replace("_", " ");
            humanName = CultureInfo.CurrentCulture.TextInfo.ToTitleCase(humanName);

            folderDtos.Add(new MediaDetailsDto
            {
                Id = totalCount,
                ScopeUid = folder,
                Location = folder,
                Name = humanName,
                Description = null,
                Size = size,
                MimeType = "inode/directory",
                Tags = Array.Empty<string>(),
                UsageCount = usageCount,
                CreatedAt = createdAt,
                UpdatedAt = updatedAt,
            });
        }

        return folderDtos;
    }

    private async Task<Media?> ResolveMediaAsync(string scopeUid, string fileName)
    {
        var normalizedScope = scopeUid.Trim();
        var normalizedFile = fileName.Trim();
        return await pgDbContext.Media!
            .FirstOrDefaultAsync(m => m.ScopeUid == normalizedScope &&
                                      (m.Name == normalizedFile || m.OriginalName == normalizedFile));
    }

    private async Task<MediaDetailsDto> ApplyMediaTransformAsync(
        Media media,
        byte[] sourceData,
        byte[] transformedData,
        string sourceExtension,
        string sourceMimeType,
        string? baseName,
        bool forceOptimize = false)
    {
        if (sourceData == null || sourceData.Length == 0)
        {
            throw new InvalidOperationException("Source media data is empty.");
        }

        var safeBaseName = string.IsNullOrWhiteSpace(baseName)
            ? (media.OriginalName ?? media.Name ?? string.Empty)
            : baseName;
        safeBaseName = EnsureFileNameExtension(safeBaseName, sourceExtension);

        var settings = await mediaOptimizationService.GetSettingsAsync();
        var oldName = media.Name;

        if (media.OriginalData == null || media.OriginalData.Length == 0)
        {
            media.OriginalData = media.Data;
            media.OriginalSize = media.Size;
            media.OriginalExtension = media.Extension;
            media.OriginalMimeType = media.MimeType;
            media.OriginalName = EnsureFileNameExtension(media.OriginalName ?? media.Name ?? safeBaseName, media.Extension ?? string.Empty);
        }

        var finalData = transformedData;
        var finalExtension = sourceExtension;
        var finalMimeType = sourceMimeType;
        var newName = safeBaseName;

        // Optimize if enabled OR if explicitly forced (manual optimization)
        if (settings.EnableOptimisation || forceOptimize)
        {
            var optimizationResult = await mediaOptimizationService.OptimizeAsync(
                new MediaOptimizationRequest
                {
                    Data = transformedData,
                    FileName = safeBaseName,
                    Extension = sourceExtension,
                    MimeType = sourceMimeType,
                },
                force: forceOptimize);

            var hasCoverTag = HasCoverTag(media.Tags);
            var processedResult = await ApplyCoverDimensionsIfNeeded(
                optimizationResult.Data,
                optimizationResult.MimeType,
                hasCoverTag);

            finalData = processedResult.Data;
            finalExtension = optimizationResult.Extension;
            finalMimeType = optimizationResult.MimeType;
            newName = GetOptimizedFileName(safeBaseName, optimizationResult.Extension);
        }
        else
        {
            media.OriginalData = media.OriginalData ?? sourceData;
            media.OriginalSize = media.OriginalSize ?? sourceData.LongLength;
            media.OriginalExtension = media.OriginalExtension ?? sourceExtension;
            media.OriginalMimeType = media.OriginalMimeType ?? sourceMimeType;
            media.OriginalName = media.OriginalName ?? EnsureFileNameExtension(safeBaseName, sourceExtension);
        }

        media.Data = finalData;
        media.Size = finalData.LongLength;
        media.Extension = finalExtension;
        media.MimeType = finalMimeType;
        media.UpdatedAt = DateTime.UtcNow;

        TrySetImageDimensions(media, sourceMimeType, sourceData, finalMimeType, finalData);

        if (!string.IsNullOrWhiteSpace(finalMimeType) &&
            finalMimeType.StartsWith("image/", StringComparison.OrdinalIgnoreCase))
        {
            try
            {
                using var updatedImage = new ImageMagick.MagickImage(finalData);
                media.Width = (int)updatedImage.Width;
                media.Height = (int)updatedImage.Height;
            }
            catch
            {
                // Ignore dimension extraction failures
            }
        }

        var linksUpdated = 0;
        if (!string.IsNullOrWhiteSpace(newName) &&
            !string.Equals(oldName, newName, StringComparison.OrdinalIgnoreCase))
        {
            linksUpdated = await RenameMediaAsync(media, media.ScopeUid, newName);
        }
        else
        {
            media.UsageCount = linksUpdated;
            pgDbContext.Media!.Update(media);
            await pgDbContext.SaveChangesAsync();
        }

        var dto = MapToMediaDto(media);

        dto.UsageCount = linksUpdated;
        return dto;
    }

    private async Task<int> RenameMediaAsync(
        Media media,
        string newScopeUid,
        string newFileName,
        List<ContentReferenceUpdate>? deferredUpdates = null)
    {
        var currentScope = media.ScopeUid;
        var currentName = media.Name;
        var currentOriginalName = media.OriginalName;

        var scopeChanged = !string.Equals(currentScope, newScopeUid, StringComparison.OrdinalIgnoreCase);
        var nameChanged = !string.Equals(currentName, newFileName, StringComparison.OrdinalIgnoreCase);

        if (!scopeChanged && !nameChanged)
        {
            return 0;
        }

        // Log the old file path as a Modified ChangeLog entry so sync clients can detect the rename
        await mediaChangeLogService.LogMediaRenamedAsync(media.Id, currentScope, currentName);

        if (nameChanged && !string.IsNullOrWhiteSpace(media.OriginalName))
        {
            var originalExtension = Path.GetExtension(media.OriginalName);
            if (string.IsNullOrWhiteSpace(originalExtension))
            {
                originalExtension = media.OriginalExtension ?? string.Empty;
            }

            media.OriginalName = string.IsNullOrWhiteSpace(originalExtension)
                ? newFileName
                : Path.ChangeExtension(newFileName, originalExtension);
        }

        media.ScopeUid = newScopeUid;
        media.Name = newFileName;

        // If deferring updates, add to collection and skip immediate processing
        if (deferredUpdates != null)
        {
            deferredUpdates.Add(new ContentReferenceUpdate(
                currentScope,
                currentName,
                currentOriginalName,
                newScopeUid,
                newFileName));
            return 0;
        }

        var linksUpdated = await UpdateContentReferencesAsync(
            currentScope,
            currentName,
            currentOriginalName,
            newScopeUid,
            newFileName);

        media.UsageCount = linksUpdated;
        pgDbContext.Media!.Update(media);
        await pgDbContext.SaveChangesAsync();

        return linksUpdated;
    }

    private async Task<int> UpdateContentReferencesAsync(
        string oldScopeUid,
        string oldFileName,
        string? oldOriginalName,
        string newScopeUid,
        string newFileName)
    {
        var oldRelativePath = BuildMediaPath(oldScopeUid, oldFileName);
        var newRelativePath = BuildMediaPath(newScopeUid, newFileName);

        var oldRelativeOriginal = string.IsNullOrWhiteSpace(oldOriginalName)
            ? null
            : BuildMediaPath(oldScopeUid, oldOriginalName);

        var contents = await pgDbContext.Content!
            .Where(c =>
                     (c.CoverImageUrl != null &&
                      (c.CoverImageUrl.Contains(oldRelativePath) ||
                        (oldRelativeOriginal != null && c.CoverImageUrl.Contains(oldRelativeOriginal)))) ||
                     (c.Seo != null &&
                      c.Seo.OpenGraphImageUrl != null &&
                      (c.Seo.OpenGraphImageUrl.Contains(oldRelativePath) ||
                        (oldRelativeOriginal != null && c.Seo.OpenGraphImageUrl.Contains(oldRelativeOriginal)))) ||
                     (c.Body != null &&
                      (c.Body.Contains(oldRelativePath) ||
                        (oldRelativeOriginal != null && c.Body.Contains(oldRelativeOriginal)))))
            .ToListAsync();

        var linksUpdated = 0;

        foreach (var content in contents)
        {
            var updated = false;
            var coverImageUrl = content.CoverImageUrl;
            var openGraphImageUrl = content.Seo?.OpenGraphImageUrl;
            var body = content.Body;

            linksUpdated += ReplaceOccurrences(coverImageUrl, oldRelativePath, newRelativePath, ref coverImageUrl, ref updated);
            linksUpdated += ReplaceOccurrences(openGraphImageUrl, oldRelativePath, newRelativePath, ref openGraphImageUrl, ref updated);

            // Replace old original name links with new current name (not new original)
            // Both old paths should point to the new current (optimized) file
            if (oldRelativeOriginal != null)
            {
                linksUpdated += ReplaceOccurrences(coverImageUrl, oldRelativeOriginal, newRelativePath, ref coverImageUrl, ref updated);
                linksUpdated += ReplaceOccurrences(openGraphImageUrl, oldRelativeOriginal, newRelativePath, ref openGraphImageUrl, ref updated);
            }

            linksUpdated += ReplaceOccurrences(body, oldRelativePath, newRelativePath, ref body, ref updated);

            if (oldRelativeOriginal != null)
            {
                linksUpdated += ReplaceOccurrences(body, oldRelativeOriginal, newRelativePath, ref body, ref updated);
            }

            if (updated)
            {
                content.CoverImageUrl = coverImageUrl;
                if (content.Seo != null)
                {
                    content.Seo.OpenGraphImageUrl = openGraphImageUrl;
                }

                content.Body = body ?? content.Body;
                pgDbContext.Content!.Update(content);
            }
        }

        if (contents.Count > 0)
        {
            await pgDbContext.SaveChangesAsync();
        }

        return linksUpdated;
    }

    private int ReplaceOccurrences(
        string? value,
        string oldValue,
        string newValue,
        ref string? result,
        ref bool updated)
    {
        if (string.IsNullOrWhiteSpace(value) ||
            string.IsNullOrWhiteSpace(oldValue) ||
            string.IsNullOrWhiteSpace(newValue))
        {
            return 0;
        }

        var count = 0;
        var index = 0;
        while (true)
        {
            index = value.IndexOf(oldValue, index, StringComparison.OrdinalIgnoreCase);
            if (index < 0)
            {
                break;
            }

            count++;
            index += oldValue.Length;
        }

        if (count == 0)
        {
            return 0;
        }

        result = value.Replace(oldValue, newValue, StringComparison.OrdinalIgnoreCase);
        updated = true;
        return count;
    }

    private string BuildMediaPath(string scopeUid, string fileName)
    {
        return $"/media/{scopeUid}/{fileName}";
    }

    private async Task<CoverResizeResult> ApplyCoverDimensionsIfNeeded(byte[] data, string mimeType, bool hasCoverTag)
    {
        if (!hasCoverTag || string.IsNullOrWhiteSpace(mimeType) ||
            !mimeType.StartsWith("image/", StringComparison.OrdinalIgnoreCase))
        {
            return new CoverResizeResult(data);
        }

        // Check if cover resize is enabled
        var enableCoverResize = await settingService.IsCoverResizeEnabledAsync();

        if (!enableCoverResize)
        {
            return new CoverResizeResult(data);
        }

        var coverDimensions = await settingService.GetCoverDimensionsAsync();
        var (targetWidth, targetHeight) = MediaSizeHelper.ParseSize(coverDimensions);

        if (!targetWidth.HasValue || !targetHeight.HasValue || targetWidth <= 0 || targetHeight <= 0)
        {
            return new CoverResizeResult(data);
        }

        try
        {
            return new CoverResizeResult(ResizeToCover(data, targetWidth.Value, targetHeight.Value));
        }
        catch
        {
            return new CoverResizeResult(data);
        }
    }

    /// <summary>
    /// Calculates the media location URL using the media resolver with resolution mode from headers/query parameters.
    /// </summary>
    /// <param name="scopeUid">The scope UID for the media file.</param>
    /// <param name="fileName">The name of the media file.</param>
    /// <returns>The resolved media location URL.</returns>
    private string CalculateMediaLocation(string scopeUid, string fileName)
    {
        var relativePath = Path.Combine("/api/media", scopeUid ?? string.Empty, fileName ?? string.Empty).Replace("\\", "/");
        return mediaResolver.Resolve(relativePath, HttpContext, MediaResolutionHelper.GetResolutionMode(HttpContext));
    }

    private MediaDetailsDto MapToMediaDto(Media media)
    {
        var dto = mapper.Map<MediaDetailsDto>(media);
        dto.Location = CalculateMediaLocation(media.ScopeUid, media.Name);
        return dto;
    }

    private async Task<int> ApplyDeferredContentUpdatesAsync(List<ContentReferenceUpdate> updates)
    {
        if (updates.Count == 0)
        {
            return 0;
        }

        // Build all path pairs for searching
        var pathMappings = new List<(string OldPath, string NewPath)>();
        foreach (var update in updates)
        {
            var oldPath = BuildMediaPath(update.OldScopeUid, update.OldName);
            var newPath = BuildMediaPath(update.NewScopeUid, update.NewName);
            pathMappings.Add((oldPath, newPath));

            if (!string.IsNullOrWhiteSpace(update.OldOriginalName))
            {
                var oldOriginalPath = BuildMediaPath(update.OldScopeUid, update.OldOriginalName);
                pathMappings.Add((oldOriginalPath, newPath));
            }
        }

        // Build a single query to find all affected content
        var allOldPaths = pathMappings.Select(p => p.OldPath).Distinct().ToList();
        var contents = await pgDbContext.Content!
            .Where(c =>
                (c.CoverImageUrl != null && allOldPaths.Any(p => c.CoverImageUrl.Contains(p))) ||
                (c.Seo != null &&
                                 (c.Seo.OpenGraphImageUrl != null && allOldPaths.Any(p => c.Seo.OpenGraphImageUrl.Contains(p)))) ||
                (c.Body != null && allOldPaths.Any(p => c.Body.Contains(p))))
            .ToListAsync();

        var totalLinksUpdated = 0;

        foreach (var content in contents)
        {
            var updated = false;
            var coverImageUrl = content.CoverImageUrl;
            var openGraphImageUrl = content.Seo?.OpenGraphImageUrl;
            var body = content.Body;

            foreach (var (oldPath, newPath) in pathMappings)
            {
                totalLinksUpdated += ReplaceOccurrences(coverImageUrl, oldPath, newPath, ref coverImageUrl, ref updated);
                totalLinksUpdated += ReplaceOccurrences(openGraphImageUrl, oldPath, newPath, ref openGraphImageUrl, ref updated);
                totalLinksUpdated += ReplaceOccurrences(body, oldPath, newPath, ref body, ref updated);
            }

            if (updated)
            {
                content.CoverImageUrl = coverImageUrl;
                if (content.Seo != null)
                {
                    content.Seo.OpenGraphImageUrl = openGraphImageUrl;
                }

                content.Body = body ?? content.Body;
                pgDbContext.Content!.Update(content);
            }
        }

        return totalLinksUpdated;
    }

    private string ResolveNewScopeUid(string currentScopeUid, string folder, string newFolder)
    {
        if (string.Equals(currentScopeUid, folder, StringComparison.OrdinalIgnoreCase))
        {
            return newFolder;
        }

        var prefix = folder + "/";
        if (currentScopeUid.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
        {
            var suffix = currentScopeUid.Substring(prefix.Length);
            return string.IsNullOrWhiteSpace(suffix)
                ? newFolder
                : newFolder + "/" + suffix;
        }

        return currentScopeUid;
    }

    private readonly struct CoverResizeResult
    {
        public CoverResizeResult(byte[] data)
        {
            Data = data;
            Size = data.LongLength;
        }

        public byte[] Data { get; }

        public long Size { get; }
    }

    private sealed class ContentReferenceUpdate
    {
        public ContentReferenceUpdate(
            string oldScopeUid,
            string oldName,
            string? oldOriginalName,
            string newScopeUid,
            string newName)
        {
            OldScopeUid = oldScopeUid;
            OldName = oldName;
            OldOriginalName = oldOriginalName;
            NewScopeUid = newScopeUid;
            NewName = newName;
        }

        public string OldScopeUid { get; }

        public string OldName { get; }

        public string? OldOriginalName { get; }

        public string NewScopeUid { get; }

        public string NewName { get; }
    }
}