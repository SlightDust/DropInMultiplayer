using RoR2;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using UnityEngine;

namespace DropInMultiplayer.Helpers
{
    /// <summary>
    /// Resolves survivor names typed by players.
    ///
    /// Vanilla RoR2 only exposes the internal body name (e.g. "Engi" for Engineer,
    /// "Enforcer2" for Enforcer) plus the display name in the client's current language,
    /// so typing an English display name in a non-English game (or the display name of a
    /// DLC survivor whose body name differs) used to fail. This resolver accepts all of:
    /// internal name, body prefab name, localized display names of every installed
    /// language (English included), and a small alias table for legacy names.
    /// </summary>
    internal static class SurvivorNameResolver
    {
        private static readonly Regex RichTextTagRegex = new Regex("<[^>]+>", RegexOptions.Compiled);
        private static readonly Dictionary<SurvivorDef, string[]> NameCache = new Dictionary<SurvivorDef, string[]>();

        internal static bool PreloadComplete { get; private set; }

        // Keyed by normalized internal name. Only needed for names that are not present in
        // any of the game's language files or that players commonly spell differently.
        private static readonly Dictionary<string, string[]> ExtraAliases = new Dictionary<string, string[]>
        {
            { "engi", new[] { "engineer" } },
            { "toolbot", new[] { "mult" } },
            { "treebot", new[] { "rex" } },
            { "voidsurvivor", new[] { "voidfiend" } },
            { "dronetech", new[] { "operator" } },
            { "enforcer2", new[] { "enforcer" } },
        };

        internal static string Normalize(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return string.Empty;
            }

            // Display names may contain rich text tags such as <style=cShrine>...</style>.
            string stripped = RichTextTagRegex.Replace(value, string.Empty);

            // Convert full-width characters to their half-width equivalents.
            string normalized;
            try
            {
                normalized = stripped.Normalize(NormalizationForm.FormKC);
            }
            catch (ArgumentException)
            {
                // Malformed UTF-16 input, fall back to the unnormalized string.
                normalized = stripped;
            }

            StringBuilder builder = new StringBuilder(normalized.Length);
            foreach (char character in normalized)
            {
                if (char.IsLetterOrDigit(character))
                {
                    builder.Append(char.ToLowerInvariant(character));
                }
            }

            return builder.ToString();
        }

        internal static bool Matches(SurvivorDef survivorDef, string normalizedQuery)
        {
            if (survivorDef == null || string.IsNullOrEmpty(normalizedQuery))
            {
                return false;
            }

            return GetNames(survivorDef).Contains(normalizedQuery);
        }

        /// <summary>
        /// Loads the strings of every installed language so survivor names can be matched in
        /// any language, no matter which language the host plays in. The game itself only
        /// keeps English plus the current language loaded; other languages would otherwise
        /// fall back to English. One language is loaded per frame to avoid a long hitch.
        /// </summary>
        internal static IEnumerator PreloadLanguageStrings()
        {
            // Language.Init runs after plugin Awake, wait until the language list is built.
            while (!Language.GetAllLanguages().Any())
            {
                yield return null;
            }

            foreach (Language language in Language.GetAllLanguages().ToArray())
            {
                if (language == null || language.stringsLoaded)
                {
                    continue;
                }

                try
                {
                    language.LoadStrings();
                }
                catch (Exception exception)
                {
                    Debug.LogWarning($"DropInMultiplayer: failed to preload language {language.name}: {exception}");
                }

                yield return null;
            }

            PreloadComplete = true;
        }

        private static string[] GetNames(SurvivorDef survivorDef)
        {
            if (NameCache.TryGetValue(survivorDef, out string[] cachedNames))
            {
                return cachedNames;
            }

            HashSet<string> names = new HashSet<string>(StringComparer.Ordinal);
            AddName(names, survivorDef.cachedName);

            GameObject bodyPrefab = survivorDef.bodyPrefab;
            if (bodyPrefab)
            {
                AddName(names, bodyPrefab.name);
                if (bodyPrefab.name.EndsWith("Body", StringComparison.OrdinalIgnoreCase))
                {
                    AddName(names, bodyPrefab.name.Substring(0, bodyPrefab.name.Length - "Body".Length));
                }
            }

            string displayNameToken = survivorDef.displayNameToken;
            if (!string.IsNullOrEmpty(displayNameToken))
            {
                AddName(names, Language.GetString(displayNameToken));
                AddName(names, Language.GetString(displayNameToken, "en"));

                foreach (Language language in Language.GetAllLanguages())
                {
                    if (language != null && !string.IsNullOrEmpty(language.name))
                    {
                        AddName(names, Language.GetString(displayNameToken, language.name));
                    }
                }
            }

            if (ExtraAliases.TryGetValue(Normalize(survivorDef.cachedName), out string[] aliases))
            {
                foreach (string alias in aliases)
                {
                    AddName(names, alias);
                }
            }

            string[] result = names.ToArray();

            // Only cache once every language has been loaded, otherwise localized names
            // would be missing for the rest of the session.
            if (PreloadComplete)
            {
                NameCache[survivorDef] = result;
            }

            return result;
        }

        private static void AddName(HashSet<string> names, string value)
        {
            string normalized = Normalize(value);
            if (normalized.Length > 0)
            {
                names.Add(normalized);
            }
        }
    }
}
