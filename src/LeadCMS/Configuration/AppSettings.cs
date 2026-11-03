// <copyright file="AppSettings.cs" company="WavePoint Co. Ltd.">
// Licensed under the MIT license. See LICENSE file in the samples root for full license information.
// </copyright>

using LeadCMS.Constants;

namespace LeadCMS.Configuration;

public class EntitiesConfig
{
    public string[] Include { get; set; } = Array.Empty<string>();

    public string[] Exclude { get; set; } = Array.Empty<string>();
}

public class BaseServiceConfig
{
    public string Server { get; set; } = string.Empty;

    public int Port { get; set; } = 0;

    public string UserName { get; set; } = string.Empty;

    public string Password { get; set; } = string.Empty;
}

public class PostgresConfig : BaseServiceConfig
{
    public string Database { get; set; } = string.Empty;

    public string ConnectionString => $"User ID={UserName};Password={Password};Server={Server};Port={Port};Database={Database};Pooling=true;";
}

public class ElasticConfig : BaseServiceConfig
{
    public bool Enable { get; set; } = true;

    public bool UseHttps { get; set; } = false;

    public string IndexPrefix { get; set; } = string.Empty;

    public string Url => $"http{(UseHttps ? "s" : string.Empty)}://{Server}:{Port}";
}

public class ExtensionConfig
{
    public string Extension { get; set; } = string.Empty;

    public string MaxSize { get; set; } = string.Empty;
}

public class MediaConfig
{
    public string[] Extensions { get; set; } = Array.Empty<string>();

    public ExtensionConfig[] MaxSize { get; set; } = Array.Empty<ExtensionConfig>();

    public string? CacheTime { get; set; }
}

public class FileConfig
{
    public string[] Extensions { get; set; } = Array.Empty<string>();

    public ExtensionConfig[] MaxSize { get; set; } = Array.Empty<ExtensionConfig>();
}

public class EmailVerificationApiConfig
{
    public string Url { get; set; } = string.Empty;

    public string ApiKey { get; set; } = string.Empty;
}

public class AccountDetailsApiConfig
{
    public string Url { get; set; } = string.Empty;

    public string ApiKey { get; set; } = string.Empty;
}

public class ApiSettingsConfig
{
    public int MaxListSize { get; set; }

    public string MaxRequestBodySize { get; set; } = string.Empty;

    public string DefaultFromEmail { get; set; } = "no-reply@leadcms.ai";

    public string DefaultFromName { get; set; } = "LeadCMS";
}

public class GeolocationApiConfig
{
    public string Url { get; set; } = string.Empty;

    public string AuthKey { get; set; } = string.Empty;
}

public class TaskConfig
{
    public bool Enable { get; set; }

    public string CronSchedule { get; set; } = string.Empty;

    public int RetryCount { get; set; }

    public int RetryInterval { get; set; }

    /// <summary>
    /// Gets or sets the maximum number of execution log records to retain per task.
    /// Records beyond this limit are automatically deleted, oldest first. Defaults to 1000.
    /// </summary>
    public int MaxLogRecords { get; set; } = 1000;
}

public class TaskWithBatchConfig : TaskConfig
{
    public int BatchSize { get; set; }
}

public class DomainVerificationTaskConfig : TaskWithBatchConfig
{
    public int BatchInterval { get; set; }
}

public class CacheProfileSettings
{
    public string Type { get; set; } = string.Empty;

    public string VaryByHeader { get; set; } = string.Empty;

    public int? Duration { get; set; }
}

public class AppSettings
{
    public PostgresConfig Postgres { get; set; } = new PostgresConfig();

    public ElasticConfig Elastic { get; set; } = new ElasticConfig();
}

public class CorsConfig
{
    public string[] AllowedOrigins { get; set; } = Array.Empty<string>();
}

public class EmailConfig : BaseServiceConfig
{
    public bool UseSsl { get; set; }

    public bool RequireAuthentication => !string.IsNullOrEmpty(UserName) && !string.IsNullOrEmpty(Password) &&
                                         UserName != "$EMAIL__USERNAME" && Password != "$EMAIL__PASSWORD";
}

public class JwtConfig
{
    public string Issuer { get; set; } = string.Empty;

    public string Audience { get; set; } = string.Empty;

    public string Secret { get; set; } = string.Empty;

    public bool IsInitialized()
    {
        return !string.IsNullOrEmpty(Secret) && Secret != "$JWT__SECRET" &&
               !string.IsNullOrEmpty(Issuer) && Issuer != "$JWT__ISSUER" &&
               !string.IsNullOrEmpty(Audience) && Audience != "$JWT__AUDIENCE";
    }
}

public class AzureADConfig
{
    public string Instance { get; set; } = string.Empty;

    public string TenantId { get; set; } = string.Empty;

    public string Domain { get; set; } = string.Empty;

    public string ClientId { get; set; } = string.Empty;

    public string ClientSecret { get; set; } = string.Empty;

    public string Authority { get; set; } = string.Empty;

    public bool IsInitialized()
    {
        return !string.IsNullOrEmpty(ClientId) && ClientId != "$AZUREAD__CLIENTID" &&
               !string.IsNullOrEmpty(TenantId) && TenantId != "$AZUREAD__TENANTID";
    }
}

public class DefaultRolesConfig : List<string>
{
}

public class DefaultUserConfig
{
    public string UserName { get; set; } = string.Empty;

    public string Password { get; set; } = string.Empty;

    public string Email { get; set; } = string.Empty;

    public DefaultRolesConfig Roles { get; set; } = new DefaultRolesConfig();
}

public class DefaultUsersConfig : List<DefaultUserConfig>
{
}

public class SupportedLanguagesConfig : List<string>
{
}

public class CookiesConfig
{
    public bool Enable { get; set; }

    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets expiration time in hours.
    /// </summary>
    public int ExpireTime { get; set; } = 12;
}

public class IdentityConfig
{
    public double LockoutTime { get; set; } = 5;

    public int MaxFailedAccessAttempts { get; set; } = 10;

    // Password policy configuration
    public bool RequireDigit { get; set; } = SettingDefaults.RequireDigit;

    public bool RequireUppercase { get; set; } = SettingDefaults.RequireUppercase;

    public bool RequireLowercase { get; set; } = SettingDefaults.RequireLowercase;

    public bool RequireNonAlphanumeric { get; set; } = SettingDefaults.RequireNonAlphanumeric;

    public int RequiredLength { get; set; } = SettingDefaults.RequiredLength;

    public int RequiredUniqueChars { get; set; } = SettingDefaults.RequiredUniqueChars;
}

public class ContentConfig
{
    /// <summary>
    /// Gets or sets the minimum length for Content Title field. Default value of 10 characters.
    /// </summary>
    public int MinTitleLength { get; set; } = SettingDefaults.MinTitleLength;

    /// <summary>
    /// Gets or sets the maximum length for Content Title field. Default value of 60 characters is SEO-optimized for page titles.
    /// </summary>
    public int MaxTitleLength { get; set; } = SettingDefaults.MaxTitleLength;

    /// <summary>
    /// Gets or sets the minimum length for Content Description field. Default value of 20 characters.
    /// </summary>
    public int MinDescriptionLength { get; set; } = SettingDefaults.MinDescriptionLength;

    /// <summary>
    /// Gets or sets the maximum length for Content Description field. Default value of 155 characters is SEO-optimized for meta descriptions.
    /// </summary>
    public int MaxDescriptionLength { get; set; } = SettingDefaults.MaxDescriptionLength;

    /// <summary>
    /// Gets or sets a value indicating whether real-time MDX/JSON format validation is enabled for content records in the admin UI. Default value of true enables validation.
    /// </summary>
    public bool EnableRealtimeSyntaxValidation { get; set; } = SettingDefaults.EnableRealtimeSyntaxValidation;

    /// <summary>
    /// Gets or sets a value indicating whether line numbers are enabled in the code editor for content records in the admin UI. Default value of true enables line numbers.
    /// </summary>
    public bool EnableCodeEditorLineNumbers { get; set; } = SettingDefaults.EnableCodeEditorLineNumbers;
}

public class SiteLinksConfig
{
    /// <summary>
    /// Gets or sets the main site URL (e.g. "https://example.com"). Available in email templates as {{ site_url }}.
    /// </summary>
    public string SiteUrl { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the base unsubscribe URL (e.g. "https://example.com/unsubscribe"). Available in email templates as {{ unsubscribe_url }}.
    /// </summary>
    public string UnsubscribeUrl { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the privacy policy URL (e.g. "https://example.com/privacy"). Available in email templates as {{ privacy_url }}.
    /// </summary>
    public string PrivacyUrl { get; set; } = string.Empty;
}