// <copyright file="PasswordHelper.cs" company="WavePoint Co. Ltd.">
// Licensed under the MIT license. See LICENSE file in the samples root for full license information.
// </copyright>

using System.Security.Cryptography;
using LeadCMS.Configuration;
using LeadCMS.Constants;

namespace LeadCMS.Helpers
{
    public static class PasswordHelper
    {
        /// <summary>
        /// Generates a strong password based on the provided IdentityConfig requirements.
        /// If config is null, uses default ASP.NET Identity requirements.
        /// </summary>
        /// <param name="config">Identity configuration specifying password requirements.</param>
        /// <param name="length">Desired password length (minimum will be enforced from config).</param>
        /// <returns>A password that meets all specified requirements.</returns>
        public static string GenerateStrongPassword(IdentityConfig? config = null, int? length = null)
        {
            // Use provided config or fall back to defaults
            var requireDigit = config?.RequireDigit ?? SettingDefaults.RequireDigit;
            var requireUppercase = config?.RequireUppercase ?? SettingDefaults.RequireUppercase;
            var requireLowercase = config?.RequireLowercase ?? SettingDefaults.RequireLowercase;
            var requireNonAlphanumeric = config?.RequireNonAlphanumeric ?? SettingDefaults.RequireNonAlphanumeric;
            var requiredLength = Math.Max(length ?? 16, config?.RequiredLength ?? SettingDefaults.RequiredLength);

            // Calculate minimum required characters
            var requiredCharTypes = 0;
            if (requireDigit)
            {
                requiredCharTypes++;
            }

            if (requireUppercase)
            {
                requiredCharTypes++;
            }

            if (requireLowercase)
            {
                requiredCharTypes++;
            }

            if (requireNonAlphanumeric)
            {
                requiredCharTypes++;
            }

            if (requiredLength < requiredCharTypes)
            {
                throw new ArgumentException($"Password length ({requiredLength}) must be at least {requiredCharTypes} to include all required character types.", nameof(length));
            }

            // Character sets
            const string lowercase = "abcdefghijklmnopqrstuvwxyz";
            const string uppercase = "ABCDEFGHIJKLMNOPQRSTUVWXYZ";
            const string digits = "1234567890";
            const string special = "!@#$%^&*()-_=+[]{}|;:,.<>?";

            using var rng = RandomNumberGenerator.Create();
            var password = new List<char>();
            var allChars = string.Empty;

            // Add required character types
            if (requireLowercase)
            {
                password.Add(GetRandomCharacter(lowercase, rng));
                allChars += lowercase;
            }

            if (requireUppercase)
            {
                password.Add(GetRandomCharacter(uppercase, rng));
                allChars += uppercase;
            }

            if (requireDigit)
            {
                password.Add(GetRandomCharacter(digits, rng));
                allChars += digits;
            }

            if (requireNonAlphanumeric)
            {
                password.Add(GetRandomCharacter(special, rng));
                allChars += special;
            }

            // If no requirements are set, use all character types
            if (string.IsNullOrEmpty(allChars))
            {
                allChars = lowercase + uppercase + digits + special;
            }

            // Fill remaining length with random characters from all allowed categories
            for (int i = password.Count; i < requiredLength; i++)
            {
                password.Add(GetRandomCharacter(allChars, rng));
            }

            // Shuffle the password to avoid predictable patterns
            return new string(password.OrderBy(x => GetRandomNumber(rng)).ToArray());
        }

        private static char GetRandomCharacter(string chars, RandomNumberGenerator rng)
        {
            var bytes = new byte[4];
            rng.GetBytes(bytes);
            var randomIndex = BitConverter.ToUInt32(bytes, 0) % chars.Length;
            return chars[(int)randomIndex];
        }

        private static int GetRandomNumber(RandomNumberGenerator rng)
        {
            var bytes = new byte[4];
            rng.GetBytes(bytes);
            return BitConverter.ToInt32(bytes, 0);
        }
    }
}
