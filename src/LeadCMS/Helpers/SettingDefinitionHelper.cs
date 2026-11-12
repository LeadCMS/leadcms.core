// <copyright file="SettingDefinitionHelper.cs" company="WavePoint Co. Ltd.">
// Licensed under the MIT license. See LICENSE file in the samples root for full license information.
// </copyright>

using LeadCMS.Configuration;
using LeadCMS.Interfaces;

namespace LeadCMS.Helpers;

public static class SettingDefinitionHelper
{
    /// <summary>
    /// Merges definitions from all providers into one definition per key.
    /// Plugin providers take precedence over <see cref="CoreSettingsProvider"/>: core only
    /// carries fallback metadata for plugin-owned keys, while the plugin owns their defaults.
    /// </summary>
    /// <param name="settingsProviders">Registered setting providers.</param>
    /// <returns>One definition per key.</returns>
    public static IReadOnlyList<SettingDefinition> Merge(IEnumerable<ISettingsProvider> settingsProviders)
    {
        return settingsProviders
            .OrderBy(p => p is CoreSettingsProvider ? 1 : 0)
            .SelectMany(p => p.GetSettingDefinitions())
            .GroupBy(d => d.Key, StringComparer.OrdinalIgnoreCase)
            .Select(g => g.First())
            .ToList()
            .AsReadOnly();
    }
}
