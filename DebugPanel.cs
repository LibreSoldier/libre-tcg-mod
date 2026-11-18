// Libre's Support Mod - BepInEx plugin for the PTCGO 2.95 client
// Copyright (C) 2026 LibreSoldier
// SPDX-License-Identifier: GPL-3.0-or-later

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using BepInEx.Bootstrap;
using UnityEngine;

namespace LibreTcgMod
{
    // Recent LibreModMessages and events, for the debug panel. Main thread only.
    internal static class DebugTrace
    {
        private const int Max = 200;
        private const int MaxText = 160;
        private static readonly LinkedList<string> lines = new LinkedList<string>();

        internal static bool GameMessages;   // also record every game message type (noisy)

        internal static IEnumerable<string> Lines => lines;

        internal static void Add(string direction, string channel, object data)
        {
            // The game's JSON writer escapes every non-ASCII character (\u00E1); show them as text.
            string json = Regex.Replace(LibreData.ToJson(data), @"\\u([0-9A-Fa-f]{4})",
                m => ((char)Convert.ToInt32(m.Groups[1].Value, 16)).ToString());
            if (json.Length > MaxText)
                json = json.Substring(0, MaxText) + "…";
            Add(direction, $"{channel} {json}");
        }

        internal static void Add(string kind, string text)
        {
            lines.AddFirst($"{DateTime.Now:HH:mm:ss} {kind,-5} {text}");
            if (lines.Count > Max)
                lines.RemoveLast();
        }

        internal static void AddGameMessage(object message)
        {
            if (GameMessages)
                Add("game", message.GetType().Name);
        }
    }

    // F9 (configurable) shows the connection, the mods, the channels and the recent
    // traffic. Off unless enabled in BepInEx/config/libre.support.cfg, so players don't
    // open it by accident.
    internal class DebugPanel
    {
        private bool visible;
        private Rect window = new Rect(40, 40, 620, 520);
        private Vector2 scroll;
        private GUIStyle windowStyle, labelStyle, buttonStyle, toggleStyle;

        internal void Toggle() => visible = !visible;

        internal void OnGUI()
        {
            if (!visible)
                return;
            // The game's font only draws with an explicit size, so every style is a copy
            // with font and size set (GUI.skin.font alone leaves the text invisible).
            if (labelStyle == null || labelStyle.font == null)
            {
                windowStyle = LibreSupport.NewStyle(GUI.skin.window, 14);
                labelStyle = LibreSupport.NewStyle(GUI.skin.label, 13);
                labelStyle.wordWrap = true;
                buttonStyle = LibreSupport.NewStyle(GUI.skin.button, 13);
                toggleStyle = LibreSupport.NewStyle(GUI.skin.toggle, 13);
            }
            window = GUI.Window(0x11B7E, window, Draw, $"{Plugin.Name} {Plugin.Version} - debug (F9)", windowStyle);
        }

        private void Draw(int id)
        {
            var server = LibreSupport.Server;
            Label("Server: " + (server == null ? "not connected" :
                $"{server}  ·  logged in: {(LibreSupport.IsLoggedIn ? "yes" : "no")}  ·  plugin system: {(server.HasPluginSystem ? $"yes (API {server.ApiVersion})" : "no")}"));
            if (server != null && server.Plugins.Count > 0)
                Label("Server plugins: " + string.Join(", ", server.Plugins.Select(p => $"{p.Key} {p.Value}")));
            Label($"Scene: {LibreSupport.Scene}  ·  rooms: {Join(LibreSupport.Rooms)}  ·  in match: {(LibreSupport.InMatch ? "yes" : "no")}");
            Label("Mods: " + string.Join(", ", Chainloader.PluginInfos.Values.Select(p => $"{p.Metadata.Name} {p.Metadata.Version}")));
            Label("Channels: " + Join(LibreSupport.Channels));
            GUILayout.BeginHorizontal();
            DebugTrace.GameMessages = GUILayout.Toggle(DebugTrace.GameMessages, " Also list game messages", toggleStyle);
            GUILayout.FlexibleSpace();
            if (GUILayout.Button("Close", buttonStyle, GUILayout.Width(80)))
                visible = false;
            GUILayout.EndHorizontal();
            scroll = GUILayout.BeginScrollView(scroll);
            foreach (var line in DebugTrace.Lines)
                Label(line);
            GUILayout.EndScrollView();
            GUI.DragWindow(new Rect(0, 0, 10000, 24));
        }

        private void Label(string text) => GUILayout.Label(text, labelStyle);

        private static string Join(IEnumerable<string> items)
        {
            var list = items.OrderBy(s => s).ToList();
            return list.Count == 0 ? "none" : string.Join(", ", list);
        }
    }
}
