// Libre's Support Mod - BepInEx plugin for the PTCGO 2.95 client
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
using System.Linq;
using BepInEx;
using BepInEx.Bootstrap;
using BepInEx.Configuration;
using BepInEx.Logging;
using dwd.core.session;
using HarmonyLib;
using UnityEngine;

namespace LibreTcgMod
{
    [BepInPlugin(Guid, Name, Version)]
    public class Plugin : BaseUnityPlugin
    {
        public const string Guid = "libre.support";
        public const string Name = "Libre's Support Mod";
        public const string Version = "1.1.0";

        internal static ManualLogSource Log;

        private GUIStyle labelStyle;
        private ConfigEntry<bool> debugPanelEnabled;
        private ConfigEntry<KeyboardShortcut> debugPanelKey;
        private readonly DebugPanel debugPanel = new DebugPanel();

        private void Awake()
        {
            Log = Logger;
            Log.LogInfo($"{Name} loaded (v{Version}, API {LibreSupport.ApiVersion})");
            debugPanelEnabled = Config.Bind("Debug", "Panel", false,
                "Lets PanelKey open the debug panel (connection, mods, channels, recent LibreModMessages and events).");
            debugPanelKey = Config.Bind("Debug", "PanelKey", new KeyboardShortcut(KeyCode.F9),
                "Opens and closes the debug panel when Panel is true.");
            new Harmony(Guid).PatchAll(typeof(Plugin).Assembly);
            LibreSupport.Initialize();
            StartCoroutine(WatchOptionsMenu());
        }

        // Messages from the server arrive on the network thread; handlers run here.
        private void Update()
        {
            LibreSupport.Pump();
            if (debugPanelEnabled.Value && debugPanelKey.Value.IsDown())
                debugPanel.Toggle();
        }

        // The options menu shows "Client Version: ..." (VersionLabel, pie-src). With the mod
        // loaded the client is modified, so say so there; the server marks its own line.
        // VersionLabel.Start must NOT be Harmony-patched: its obfuscated fields share names
        // and the rewritten method read the wrong field and crashed the game. So the text
        // is changed from outside, after the game wrote it.
        private const string ModdedSuffix = " (modded)";

        private static readonly AccessTools.FieldRef<VersionLabel, PieFTPTextArea> VersionText =
            AccessTools.FieldRefAccess<VersionLabel, PieFTPTextArea>("versionLabel");

        // VersionLabel only exists while the options menu is open, so it also tells
        // when to show the on-screen label.
        private static bool optionsOpen;

        private IEnumerator WatchOptionsMenu()
        {
            var wait = new WaitForSecondsRealtime(0.25f);
            bool canMark = true;
            while (true)
            {
                yield return wait;
                VersionLabel[] labels;
                try
                {
                    labels = FindObjectsOfType<VersionLabel>();
                }
                catch (System.Exception e)
                {
                    Log.LogWarning($"Could not look for the options menu: {e.Message}");
                    optionsOpen = false;
                    yield break;
                }
                optionsOpen = labels.Length > 0;
                if (!canMark)
                    continue;
                try
                {
                    foreach (var label in labels)
                    {
                        if (label.type != VersionLabel.Type.Client)
                            continue;
                        var area = VersionText(label);
                        string text = area?.get_text();
                        if (!string.IsNullOrEmpty(text) && !text.EndsWith(ModdedSuffix))
                            area.set_text(text + ModdedSuffix);
                    }
                }
                catch (System.Exception e)
                {
                    Log.LogWarning($"Could not mark the client version as modded: {e.Message}");
                    canMark = false;
                }
            }
        }

        private void OnGUI()
        {
            // Small marker in the options menu, so the player can see the mod is active.
            if (optionsOpen)
            {
                if (labelStyle == null)
                {
                    labelStyle = LibreSupport.NewLabelStyle(14);
                    labelStyle.normal.textColor = new Color(1f, 1f, 1f, 0.7f);
                }
                GUI.Label(new Rect(6, 2, 300, 20), $"{Name} {Version}", labelStyle);
            }
            debugPanel.OnGUI();
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
            // Since 1.1: the API version and the channels the mods listen on, so server
            // plugins know which messages this client understands.
            body["libreApi"] = LibreSupport.ApiVersion;
            body["libreChannels"] = LibreSupport.Channels.ToArray();
            Plugin.Log.LogInfo($"Attached libreMods to {name}: {string.Join(", ", Describe(mods))}; " +
                               $"{LibreSupport.Channels.Count} mod channel(s)");
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
