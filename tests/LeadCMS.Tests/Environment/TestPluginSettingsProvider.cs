// <copyright file="TestPluginSettingsProvider.cs" company="WavePoint Co. Ltd.">
// Licensed under the MIT license. See LICENSE file in the samples root for full license information.
// </copyright>

using LeadCMS.Interfaces;
using LeadCMS.Plugin.Site.Configuration;

namespace LeadCMS.Tests.Environment;

/// <summary>
/// Test plugin settings provider that registers the same definitions as the real Site plugin.
/// Used to validate the ISettingsProvider pipeline in integration tests.
/// </summary>
public class TestPluginSettingsProvider : ISettingsProvider
{
    /// <inheritdoc/>
    public IEnumerable<SettingDefinition> GetSettingDefinitions() => LeadCaptureSettingDefinitions.All;
}
