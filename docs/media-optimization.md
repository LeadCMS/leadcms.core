# Media Optimization in LeadCMS

## Overview

LeadCMS provides a comprehensive media optimization system designed to automatically optimize image uploads, reduce file sizes, manage dimensions, and improve overall application performance. The system leverages **ImageMagick** (via Magick.NET) to process and optimize media files intelligently while maintaining quality and preserving transparency.

## Architecture

### Core Components

#### 1. **MediaOptimizationService**

The main service responsible for optimizing media files. Located at `src/LeadCMS/Services/MediaOptimizationService.cs`

Key features:

- Asynchronous media optimization pipeline
- Format conversion and quality management
- Dimension resizing based on configured limits
- Transparency preservation
- Error handling with fallback to original data

**Default Configuration:**

- **Max Width:** 2048px
- **Max Height:** 2048px
- **Preferred Format:** AVIF
- **Quality Level:** 75

#### 2. **MediaOptimizationHelper**

A utility helper class that determines whether specific image formats should be optimized.

**Non-Optimizable Formats:**

- `.ico` - Icon files
- `.gif` - Animated GIFs
- `.svg` / `.svgz` - Scalable Vector Graphics
- `.apng` - Animated PNG
- `.ani` - Animated cursor files

**Non-Optimizable MIME Types:**

- `image/x-icon`
- `image/vnd.microsoft.icon`
- `image/gif`
- `image/svg+xml`
- `image/apng`

#### 3. **Media Entity**

Represents stored media in the database with optimization metadata.

**Key Properties:**

```csharp
public string Name { get; set; }              // Optimized filename
public string? OriginalName { get; set; }    // Original filename
public long Size { get; set; }                // Optimized file size
public long? OriginalSize { get; set; }      // Original file size
public int? Width { get; set; }               // Optimized dimensions
public int? Height { get; set; }
public int? OriginalWidth { get; set; }      // Original dimensions
public int? OriginalHeight { get; set; }
public string Extension { get; set; }         // Optimized extension
public string? OriginalExtension { get; set; } // Original extension
public byte[] Data { get; set; }              // Optimized binary data
public byte[]? OriginalData { get; set; }    // Original binary data
```

## Configuration

### Settings Management

Media optimization is controlled through system settings accessed via the `ISettingService`. The configuration keys are:

| Setting Key               | Default Value | Description                                                                                                     |
| ------------------------- | ------------- | --------------------------------------------------------------------------------------------------------------- | --- |
| `MediaMaxDimensions`      | `2048x2048`   | Maximum image dimensions (format: `{width}x{height}`)                                                           |
| `MediaPreferredFormat`    | `avif`        | Target image format for optimization                                                                            |
| `MediaEnableOptimisation` | `false`       | Enable/disable automatic optimization on upload (manual optimization via `/optimize` endpoint always works)     |
| `MediaQuality`            | `75`          | Image quality level (1-100, higher = better quality but larger file size)                                       |
| `MediaCoverDimensions`    | _(empty)_     | Specific dimensions for cover images (format: `{width}x{height}`). Empty means cover images are not cropped     |
| `MediaEnableCoverResize`  | `true`        | Enable/disable automatic resize/crop for cover images. When disabled, cover images are treated as normal images |     |

### Supported Image Formats

The following formats are supported for optimization output:

- **JPEG/JPG** - Good for photographs with lossy compression
- **PNG** - Lossless format preserving quality and transparency
- **WebP** - Modern format with excellent compression
- **AVIF** - Latest format with superior compression ratios

## Optimization Pipeline

### Step 1: Input Validation

```
┌─────────────────────────────────────────────┐
│ 1. Check if optimization is enabled        │
│ 2. Validate file size (non-zero)           │
│ 3. Verify MIME type starts with "image/"   │
│ 4. Check if format should be skipped       │
└─────────────────────────────────────────────┘
```

### Step 2: Format Resolution

- If preferred format is not set, falls back to `AVIF`
- Validates format is supported by ImageMagick
- Falls back to original image format if validation fails

### Step 3: Image Processing

```csharp
using var image = new MagickImage(imageBytes);

// Apply resizing if dimensions exceed limits
ApplyResize(image, maxWidth, maxHeight);

// Preserve transparency if needed
EnsureTransparencyPreserved(image, targetFormat);

// Remove metadata to reduce file size
image.Strip();

// Set quality level
image.Quality = 75;

// Convert to target format
image.Format = targetFormat;

// Generate optimized bytes
var optimizedBytes = image.ToByteArray();
```

### Step 4: Metadata Extraction

- **Width & Height:** Extracted from original and optimized images
- **File Size:** Calculated from byte array length
- **MIME Type:** Resolved from file extension

### Step 5: Error Handling

If optimization fails (MagickException):

- Original image data is returned unchanged
- Warning is logged with image filename
- `WasOptimized` flag is set to `false`

## Upload & Update Workflows

### Media Upload Workflow (POST /api/media)

1. **File Reception**
   - Accept file from multipart form data
   - Extract original filename, extension, and MIME type
   - Read entire file into byte array

2. **Optimization**
   - Call `MediaOptimizationService.OptimizeAsync()`
   - Generate optimized filename with new extension

3. **Storage Decision**

   **If Optimization Enabled:**
   - Store **both** original and optimized versions
   - `OriginalName`, `OriginalData`, `OriginalSize`, `OriginalExtension`, `OriginalMimeType`
   - `Name`, `Data`, `Size`, `Extension`, `MimeType` (optimized)

   **If Optimization Disabled:**
   - Store only main version
   - Clear all `Original*` fields
   - Use original file directly as `Data`

4. **Cover Image Processing** (if "Cover" tag present)
   - Apply special cover dimensions if configured
   - Center-crop to exact dimensions
   - Maintain aspect ratio during scaling

5. **Dimension Extraction**
   - Extract dimensions from both original and optimized images
   - Populate `Width`, `Height`, `OriginalWidth`, `OriginalHeight`

### Media Update Workflow (PATCH /api/media)

Similar to upload workflow with optional file replacement:

- If file is provided: Re-optimize with new content
- If no file: Update metadata only (description, tags)

## Advanced Features

### Bulk Optimization

**Endpoint:** `POST /api/media/optimize-all` (Admin only)

Processes existing media images with current optimization settings. Supports optional folder filtering.

**Request Body:**

```json
{
  "folder": "optional/folder/path",
  "includeSubfolders": false
}
```

**Parameters:**

- `folder` (string, optional): Folder path to limit optimization scope (e.g., "folder1" or "folder1/subfolder"). When not set, all media files are processed.
- `includeSubfolders` (bool, optional): When true, includes files in subfolders of the specified folder. Only applicable when folder is set. Defaults to false.

**Response:**

```json
{
  "updated": 42,
  "message": null
}
```

### Single Image Optimization (Admin)

**Endpoint:** `POST /api/media/optimize`

Optimize a specific image:

- Applies current settings to selected image
- Updates all optimization metadata
- Returns updated media details

**Request Body:**

```json
{
  "scopeUid": "folder/path",
  "fileName": "image.avif"
}
```

### Reset to Original

#### Single Media Reset

**Endpoint:** `POST /api/media/reset` (Admin only)

Resets a single media file to its original state, removing optimization.

**Request Body:**

```json
{
  "scopeUid": "folder/path",
  "fileName": "image.avif"
}
```

Returns the updated media details with original content restored.

#### Bulk Reset

**Endpoint:** `POST /api/media/reset-all` (Admin only)

Resets all optimized media files to their original state. Supports optional folder filtering.

**Request Body:**

```json
{
  "folder": "optional/folder/path",
  "includeSubfolders": false
}
```

**Parameters:**

- `folder` (string, optional): Folder path to limit reset scope (e.g., "folder1" or "folder1/subfolder"). When not set, all media files are processed.
- `includeSubfolders` (bool, optional): When true, includes files in subfolders of the specified folder. Only applicable when folder is set. Defaults to false.

**Response:**

```json
{
  "updated": 42,
  "message": null
}
```

### Media Transformation APIs

#### Resize

**Endpoint:** `POST /api/media/resize`

Parameters:

- `Width` (int): Target width in pixels
- `Height` (int): Target height in pixels
- `MaintainAspectRatio` (bool): Preserve aspect ratio

```csharp
var geometry = new MagickGeometry(width, height)
{
    IgnoreAspectRatio = !request.MaintainAspectRatio,
};
image.Resize(geometry);
```

#### Crop

**Endpoint:** `POST /api/media/crop`

Parameters:

- `Width` (int): Crop width
- `Height` (int): Crop height
- `X` (int?): X offset (auto-centered if null)
- `Y` (int?): Y offset (auto-centered if null)

```csharp
var cropX = request.X ?? Math.Max(0, (imageWidth - request.Width) / 2);
var cropY = request.Y ?? Math.Max(0, (imageHeight - request.Height) / 2);
image.Crop(new MagickGeometry(cropX, cropY, width, height));
```

### Cover Image Resizing

**Purpose:** Special handling for cover/thumbnail images

**Features:**

- Applied when media is tagged with "Cover"
- Configurable dimensions via `MediaCoverDimensions`
- Always maintains aspect ratio
- Scales to fit then center-crops

**Algorithm:**

```csharp
private static byte[] ResizeToCover(byte[] data, int targetWidth, int targetHeight)
{
    using var image = new MagickImage(data);

    // Scale to cover the target dimensions
    var scale = Math.Max(
        (double)targetWidth / image.Width,
        (double)targetHeight / image.Height
    );

    var resizedWidth = (int)Math.Ceiling(image.Width * scale);
    var resizedHeight = (int)Math.Ceiling(image.Height * scale);

    image.Resize((uint)resizedWidth, (uint)resizedHeight);

    // Center-crop to exact dimensions
    var cropX = Math.Max(0, (resizedWidth - targetWidth) / 2);
    var cropY = Math.Max(0, (resizedHeight - targetHeight) / 2);

    image.Crop(new MagickGeometry(cropX, cropY, (uint)targetWidth, (uint)targetHeight));
    image.Page = new MagickGeometry(0, 0, 0, 0);

    return image.ToByteArray();
}
```

## Image Quality Management

### Quality Settings

- **Default Quality Level:** 75
- Balanced between file size and visual quality
- Applied to all optimized formats

### Transparency Preservation

**Supported Formats with Transparency:**

- PNG
- WebP
- AVIF
- TIFF

**Process:**

```csharp
private static void EnsureTransparencyPreserved(MagickImage image, MagickFormat targetFormat)
{
    if (!image.HasAlpha || !SupportsTransparency(targetFormat))
        return;

    image.Alpha(AlphaOption.Set);
    image.BackgroundColor = MagickColors.Transparent;
    image.ColorType = ColorType.TrueColorAlpha;
}
```

### Metadata Stripping

The `image.Strip()` method removes:

- EXIF data
- Color profiles
- Comments
- Other metadata

**Benefits:**

- Reduces file size by 5-15%
- Improves privacy (removes camera info)
- Standardizes output

## Dimension Management

### Default Resizing Logic

Images larger than configured maximum dimensions are automatically downscaled:

```csharp
var geometry = new MagickGeometry(maxWidth, maxHeight)
{
    IgnoreAspectRatio = false,  // Maintain aspect ratio
    Greater = true,              // Only scale down, not up
};
image.Resize(geometry);
```

**Key Points:**

- Aspect ratio is always maintained
- Images are never upscaled
- Only dimensions exceeding limits are processed

### Dimension Extraction

Both original and optimized images are analyzed:

```csharp
private static void TrySetImageDimensions(
    Media media,
    string originalMimeType,
    byte[] originalData,
    string optimizedMimeType,
    byte[] optimizedData)
{
    // Extract from original using MagickImage
    using var originalImage = new MagickImage(originalData);
    media.OriginalWidth = (int)originalImage.Width;
    media.OriginalHeight = (int)originalImage.Height;

    // Extract from optimized
    using var optimizedImage = new MagickImage(optimizedData);
    media.Width = (int)optimizedImage.Width;
    media.Height = (int)optimizedImage.Height;
}
```

## File Naming

### Optimized Filename Generation

When optimization is enabled, files are renamed to include their format:

```csharp
private static string GetOptimizedFileName(string originalName, string optimizedExtension)
{
    // Example: "photo.jpg" → "photo.avif"
    var nameWithoutExtension = Path.GetFileNameWithoutExtension(originalName);
    return $"{nameWithoutExtension}{optimizedExtension}";
}
```

### Name Sanitization

All filenames go through:

1. **Transliteration** - Convert special characters to ASCII
2. **Slugification** - Convert to URL-safe format

Example: "Фото с камеры.jpg" → "foto-s-kamery.jpg"

## API Endpoints

### Media Queries

| Method | Endpoint                   | Description           |
| ------ | -------------------------- | --------------------- |
| GET    | `/api/media/{*pathToFile}` | Retrieve media file   |
| GET    | `/api/media?scope=...`     | List media in scope   |
| GET    | `/api/media/formats`       | Get supported formats |

### Media Management

| Method | Endpoint                   | Description            | Requires      |
| ------ | -------------------------- | ---------------------- | ------------- |
| POST   | `/api/media`               | Upload new media       | Authenticated |
| PATCH  | `/api/media`               | Update media           | Authenticated |
| DELETE | `/api/media/{*pathToFile}` | Delete media           | Authenticated |
| POST   | `/api/media/optimize`      | Optimize single image  | Admin         |
| POST   | `/api/media/reoptimize`    | Re-optimize all images | Admin         |
| POST   | `/api/media/resize`        | Resize image           | Admin         |
| POST   | `/api/media/crop`          | Crop image             | Admin         |

### Query Parameters

**Media Resolution:**

```
GET /api/media/path/to/file?mediaResolution=absolute
Header: X-Media-Resolution: absolute
```

Options:

- `absolute` - Full URLs in response
- `relative` - Relative paths (default)

**Original File:**

```
GET /api/media/path/to/file?original=true
```

Returns original unoptimized version if available.

## Performance Considerations

### Storage Efficiency

**With Optimization Enabled:**

- Original file stored in `OriginalData`
- Optimized version in `Data`
- Typical size reduction: **30-60%**

**Example:**

- Original: 2.5 MB (JPEG)
- Optimized: 800 KB (AVIF)
- Savings: **68%**

### Processing Time

- ImageMagick processing: **100-500ms** per image (varies by size)
- Database operations: **10-50ms**
- For batch operations, processing is optimized with:
  - Pagination (1000 items per batch)
  - Selective re-optimization (only changed settings)

### Database Considerations

**Media Table Schema:**

```sql
CREATE TABLE media (
    id SERIAL PRIMARY KEY,
    scope_uid VARCHAR NOT NULL,
    name VARCHAR NOT NULL,
    original_name VARCHAR,
    size BIGINT,
    original_size BIGINT,
    width INT,
    height INT,
    original_width INT,
    original_height INT,
    extension VARCHAR,
    original_extension VARCHAR,
    mime_type VARCHAR,
    original_mime_type VARCHAR,
    tags TEXT[] DEFAULT '{}',
    data BYTEA,
    original_data BYTEA,
    description TEXT,
    created_at TIMESTAMP,
    updated_at TIMESTAMP
);
```

**Index Recommendations:**

- `(scope_uid, name)` - Primary lookup
- `(scope_uid, original_name)` - Original name search
- `(tags)` - GIN index for tag queries

## Configuration Examples

### Standard Setup (Recommended)

```json
{
  "MediaMaxDimensions": "2048x2048",
  "MediaPreferredFormat": "avif",
  "MediaEnableOptimisation": true,
  "MediaQuality": 75,
  "MediaCoverDimensions": "400x300"
}
```

### Web-Optimized Setup

```json
{
  "MediaMaxDimensions": "1920x1080",
  "MediaPreferredFormat": "webp",
  "MediaEnableOptimisation": true,
  "MediaQuality": 80,
  "MediaCoverDimensions": "300x200"
}
```

### High-Fidelity Setup

```json
{
  "MediaMaxDimensions": "4096x4096",
  "MediaPreferredFormat": "png",
  "MediaEnableOptimisation": true,
  "MediaQuality": 95,
  "MediaCoverDimensions": "600x400"
}
```

## Troubleshooting

### Common Issues

**Problem:** Images fail to optimize

- **Solution:** Check ImageMagick is installed and accessible
- Check for MagickException in logs
- Verify image file is valid

**Problem:** Original dimensions not extracted

- **Solution:** Ensure files have proper MIME types
- Check image files are not corrupted
- Verify width/height are extractable from format

**Problem:** Performance degradation

- **Solution:** Review batch sizes in re-optimization
- Check database indexes
- Monitor storage usage

### Debug Information

All optimization events are logged at appropriate levels:

- **Information:** Successful operations
- **Warning:** Optimization fallbacks, failed dimension extraction
- **Error:** Critical failures

Access logs: `ILogger<MediaOptimizationService>`

## Integration Points

### Content Type Association

Media optimization integrates with content type definitions:

```csharp
public class ContentType
{
    public bool SupportsCoverImage { get; set; } = false;
    // Other properties...
}
```

### Content Reference Updates

When media is renamed during optimization, all content references are automatically updated:

- Blog post media links
- Product images
- Gallery references
- Custom content fields

## Testing

### Test Coverage

Key test scenarios in `LeadCMS.Tests/MediaTests.cs`:

1. **Upload with optimization enabled/disabled**
2. **Format conversion verification**
3. **Dimension extraction**
4. **Cover image resizing**
5. **Re-optimization batch operations**
6. **Media transformation (resize, crop)**
7. **Original data preservation**
8. **Content reference updates**

### Running Tests

```bash
dotnet test tests/LeadCMS.Tests/LeadCMS.Tests.csproj --filter "MediaTests"
```

## Best Practices

### For Administrators

1. **Choose appropriate format:**
   - AVIF for modern browsers with highest compression
   - WebP for broader compatibility with good compression
   - PNG for images requiring transparency
   - JPEG for photographs when backward compatibility needed

2. **Set realistic dimensions:**
   - 2048x2048 for general web use
   - 1920x1080 for performance-critical sites
   - 4096x4096+ for print preparation

3. **Enable optimization for most sites:**
   - Significant storage savings (30-60%)
   - Faster downloads and page loads
   - Minimal quality loss at default settings

4. **Monitor storage:**
   - Track original vs. optimized sizes
   - Set appropriate cleanup policies
   - Archive old original data if needed

### For Developers

1. **Request original if needed:**
   - Use `?original=true` query parameter
   - Check `OriginalData` field exists before relying on it

2. **Handle optimization gracefully:**
   - Always check `WasOptimized` flag
   - Implement fallback logic if optimization fails
   - Don't assume format conversion succeeds

3. **Profile performance:**
   - Monitor optimization pipeline latency
   - Track batch operation progress
   - Watch ImageMagick resource usage

4. **Test with real images:**
   - Include various formats (JPEG, PNG, WebP, etc.)
   - Test different sizes (small, large, extreme)
   - Verify transparency preservation for PNGs
   - Validate cover image cropping

## Future Enhancements

Potential areas for expansion:

1. **Multiple format output** - Store multiple formats for different clients
2. **Progressive JPEG** - Generate progressive JPEGs for better perceived performance
3. **Adaptive sizing** - Generate multiple sizes for responsive design
4. **AVIF fallback** - Automatic JPEG fallback for older browsers
5. **CDN integration** - Direct optimization offload to CDN
6. **GPU acceleration** - CUDA/OpenCL support for batch operations

## Related Documentation

- [Media Management](./docs/)
- [Configuration Settings](./docs/)
- [API Reference](./docs/)
- [Database Schema](./docs/)

## Dependencies

- **Magick.NET-Q8-AnyCPU** v14.10.2 - Image processing library
- **ImageMagick** (native) - Required by Magick.NET
- **Microsoft.AspNetCore.StaticFiles** - Content type resolution
- **LeadCMS.Interfaces** - Service contracts

## License

This documentation is part of the LeadCMS project, licensed under the MIT License.

---
