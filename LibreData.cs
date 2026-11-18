// Libre's Support Mod - BepInEx plugin for the PTCGO 2.95 client
// Copyright (C) 2026 LibreSoldier
// SPDX-License-Identifier: GPL-3.0-or-later

using System;
using System.Collections;
using System.Globalization;

namespace LibreTcgMod
{
    /// <summary>
    /// Reads the data that arrives on LibreSupport channels. The game's JSON reader turns
    /// objects into Dictionary&lt;string, object&gt;, lists into arrays, and numbers into
    /// Int32, Int64 or Double depending on the value; these helpers hide that.
    /// Nothing here throws on missing keys or wrong types: you get the fallback.
    /// <code>
    /// LibreSupport.On("my-plugin/show", data =>
    /// {
    ///     string title = LibreData.GetString(data, "title", "");
    ///     int seconds = LibreData.GetInt(data, "seconds", 5);
    ///     foreach (var option in LibreData.GetList(data, "options"))
    ///         Add(LibreData.As&lt;string&gt;(option));
    /// });
    /// </code>
    /// </summary>
    public static class LibreData
    {
        /// <summary>data[key] converted to T, or <paramref name="fallback"/>.</summary>
        public static T Get<T>(object data, string key, T fallback = default)
        {
            return TryGetValue(data, key, out var value) ? As(value, fallback) : fallback;
        }

        public static string GetString(object data, string key, string fallback = null) => Get(data, key, fallback);

        public static int GetInt(object data, string key, int fallback = 0) => Get(data, key, fallback);

        public static long GetLong(object data, string key, long fallback = 0) => Get(data, key, fallback);

        public static double GetDouble(object data, string key, double fallback = 0) => Get(data, key, fallback);

        public static bool GetBool(object data, string key, bool fallback = false) => Get(data, key, fallback);

        /// <summary>data[key] as a dictionary (a JSON object), or null.</summary>
        public static IDictionary GetDict(object data, string key) =>
            TryGetValue(data, key, out var value) ? value as IDictionary : null;

        /// <summary>data[key] as a list (a JSON array); an empty list when missing.</summary>
        public static IList GetList(object data, string key) =>
            TryGetValue(data, key, out var value) && value is IList list ? list : new object[0];

        /// <summary>Whether data is a dictionary that has <paramref name="key"/>.</summary>
        public static bool Has(object data, string key) => TryGetValue(data, key, out _);

        /// <summary>A single value converted to T (numbers between number types, anything to string).</summary>
        public static T As<T>(object value, T fallback = default)
        {
            if (value is T same)
                return same;
            if (value == null)
                return fallback;
            var target = Nullable.GetUnderlyingType(typeof(T)) ?? typeof(T);
            try
            {
                if (target == typeof(string))
                    return value is IConvertible ? (T)(object)Convert.ToString(value, CultureInfo.InvariantCulture) : fallback;
                if (target == typeof(bool) && value is string text)
                    return bool.TryParse(text, out var flag) ? (T)(object)flag : fallback;
                if (target.IsEnum)
                    return value is string name ? (T)Enum.Parse(target, name, true) : (T)Enum.ToObject(target, value);
                if (value is IConvertible && typeof(IConvertible).IsAssignableFrom(target))
                    return (T)Convert.ChangeType(value, target, CultureInfo.InvariantCulture);
            }
            catch (Exception)
            {
                // Out of range, not a number, unknown enum name...
            }
            return fallback;
        }

        /// <summary>The value as JSON text, written by the game's own JSON writer (for logs and debugging).</summary>
        public static string ToJson(object value)
        {
            try
            {
                return JSON.ToJSON(value, prettyPrint: false);
            }
            catch (Exception)
            {
                return value?.ToString() ?? "null";
            }
        }

        /// <summary>Compares versions like "1.10.2" and "1.9" by number (missing parts count as 0).</summary>
        public static int CompareVersions(string a, string b)
        {
            var left = (a ?? "").Split('.');
            var right = (b ?? "").Split('.');
            for (int i = 0; i < Math.Max(left.Length, right.Length); i++)
            {
                int x = i < left.Length ? LeadingNumber(left[i]) : 0;
                int y = i < right.Length ? LeadingNumber(right[i]) : 0;
                if (x != y)
                    return x.CompareTo(y);
            }
            return 0;
        }

        private static int LeadingNumber(string part)
        {
            int n = 0, i = 0;
            while (i < part.Length && char.IsDigit(part[i]) && n < 100000000)
                n = n * 10 + (part[i++] - '0');
            return n;
        }

        private static bool TryGetValue(object data, string key, out object value)
        {
            value = null;
            if (!(data is IDictionary dict) || key == null || !dict.Contains(key))
                return false;
            value = dict[key];
            return true;
        }
    }
}
