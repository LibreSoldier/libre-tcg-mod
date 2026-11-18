// Libre's Support Mod - BepInEx plugin for the PTCGO 2.95 client
// Copyright (C) 2026 LibreSoldier
// SPDX-License-Identifier: GPL-3.0-or-later

using UnityEngine;

namespace LibreTcgMod
{
    public static partial class LibreSupport
    {
        private static Font gameFont;

        /// <summary>
        /// A font that renders in this game. Unity's built-in IMGUI font (Arial) draws
        /// nothing in this build, so IMGUI text needs one of the game's own fonts
        /// (FunctionPro-Demi). Null only before the game loaded any font.
        /// </summary>
        public static Font GameFont
        {
            get
            {
                if (gameFont != null)
                    return gameFont;
                Font fallback = null;
                foreach (var font in Resources.FindObjectsOfTypeAll<Font>())
                {
                    if (font.name == "FunctionPro-Demi")
                        return gameFont = font;
                    if (fallback == null && font.name != "Arial")
                        fallback = font;
                }
                return fallback;   // not cached: FunctionPro may load later
            }
        }

        /// <summary>
        /// A GUIStyle for IMGUI labels in the game's font. Call it from OnGUI (it copies
        /// GUI.skin.label, which only exists there) and keep the result.
        /// </summary>
        public static GUIStyle NewLabelStyle(int fontSize = 14) => NewStyle(GUI.skin.label, fontSize);

        /// <summary>
        /// A copy of any IMGUI style (GUI.skin.button, .window, .toggle...) in the game's
        /// font. Always give a size: with the default size (0) the game's font draws nothing.
        /// Call it from OnGUI and keep the result.
        /// </summary>
        public static GUIStyle NewStyle(GUIStyle baseStyle, int fontSize = 14)
        {
            return new GUIStyle(baseStyle) { fontSize = fontSize, font = GameFont };
        }
    }
}
