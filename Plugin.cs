// LibreTCG Mod - BepInEx plugin for the PTCGO 2.95 client
// Copyright (C) 2026 LibreSoldier
//
// This program is free software: you can redistribute it and/or modify it under
// the terms of the GNU General Public License as published by the Free Software
// Foundation, either version 3 of the License, or (at your option) any later
// version. It is distributed WITHOUT ANY WARRANTY; see the LICENSE file or
// <https://www.gnu.org/licenses/> for details.
// SPDX-License-Identifier: GPL-3.0-or-later

using System.Collections;
using System.Collections.Generic;
using BepInEx;
using BepInEx.Bootstrap;
using BepInEx.Logging;
using dwd.core.session;
using HarmonyLib;
using UnityEngine;

namespace LibreTcgMod
{
    [BepInPlugin(Guid, Name, Version)]
    public class Plugin : BaseUnityPlugin
    {
        public const string Guid = "libretcg.mod";
        public const string Name = "LibreTCG Mod";
        public const string Version = "0.2.0";

        internal static ManualLogSource Log;

        private GUIStyle labelStyle;

        private void Awake()
        {
            Log = Logger;
            Log.LogInfo($"LibreTCG mod loaded (v{Version})");
            new Harmony(Guid).PatchAll(typeof(Plugin).Assembly);
        }

        // The built-in IMGUI Arial does not render in this build, so use a font
        // the game already loaded (FunctionPro), or fall back to an OS font.
        private static Font PickFont()
        {
            Font fallback = null;
            foreach (var f in Resources.FindObjectsOfTypeAll<Font>())
            {
                if (f.name == "FunctionPro-Demi")
                    return f;
                if (fallback == null && f.name != "Arial")
                    fallback = f;
            }
            return fallback ?? Font.CreateDynamicFontFromOSFont(new[] { "Arial", "Tahoma", "Liberation Sans", "DejaVu Sans" }, 14);
        }

        // Small marker so the player can see the mod is active.
        private void OnGUI()
        {
            if (labelStyle == null)
            {
                labelStyle = new GUIStyle(GUI.skin.label) { fontSize = 14, font = PickFont() };
                labelStyle.normal.textColor = new Color(1f, 1f, 1f, 0.7f);
            }
            GUI.Label(new Rect(6, 2, 300, 20), $"LibreTCG mod {Version}", labelStyle);
        }

        // GUID -> version of every loaded BepInEx plugin, reported to the server at login.
        internal static Dictionary<string, object> LoadedMods()
        {
            var mods = new Dictionary<string, object>();
            foreach (var info in Chainloader.PluginInfos.Values)
                mods[info.Metadata.GUID] = info.Metadata.Version.ToString();
            return mods;
        }
    }

    // Every message the client sends to the server goes through SessionProvider.Write (core.dll).
    // Login messages are plain dictionaries {name, value:{...}}; the mod adds "libreMods" to the
    // value so the server can check which mods the client has. Servers that don't know the key ignore it.
    [HarmonyPatch(typeof(SessionProvider), nameof(SessionProvider.Write))]
    internal static class AnnounceModsAtLogin
    {
        private static void Prefix(object message)
        {
            string name = MessageName(message);
            Plugin.Log.LogDebug($"-> {name}");

            if (!(message is IDictionary dict) || !IsLoginMessage(name))
                return;
            IDictionary body = null;
            foreach (DictionaryEntry entry in dict)
                if (entry.Value is IDictionary inner)
                {
                    body = inner;
                    break;
                }
            if (body == null)
                return;

            var mods = Plugin.LoadedMods();
            body["libreMods"] = mods;
            Plugin.Log.LogInfo($"Attached libreMods to {name}: {string.Join(", ", Describe(mods))}");
        }

        private static bool IsLoginMessage(string name)
        {
            return name != null && (name.StartsWith("Authenticate") || name == "StartAuthentication" || name == "UpgradeDeviceAccount");
        }

        private static IEnumerable<string> Describe(Dictionary<string, object> mods)
        {
            foreach (var kv in mods)
                yield return $"{kv.Key} {kv.Value}";
        }

        // Dictionary messages have the message name as their first entry.
        private static string MessageName(object message)
        {
            if (message == null)
                return "(null)";
            if (message is IDictionary dict)
                foreach (DictionaryEntry entry in dict)
                    return entry.Value as string ?? "(dictionary)";
            return message.GetType().Name;
        }
    }
}
