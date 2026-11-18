// Libre's Support Mod - BepInEx plugin for the PTCGO 2.95 client
// Copyright (C) 2026 LibreSoldier
// SPDX-License-Identifier: GPL-3.0-or-later

using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using BepInEx;
using dwd.core;
using dwd.core.account.messages;
using dwd.core.matchmaking.messages.incoming;
using dwd.core.session;
using HarmonyLib;
using pie.socialv2.messages;
using UnityEngine.SceneManagement;

namespace LibreTcgMod
{
    /// <summary>The server the game is connected to.</summary>
    public sealed class LibreServer
    {
        internal LibreServer(string host)
        {
            Host = host ?? "";
        }

        /// <summary>Hostname from the game's configuration (cake.cfg), e.g. "127.0.0.1".</summary>
        public string Host { get; }

        /// <summary>
        /// True once the server greeted this mod after login (servers with the LibreTCG
        /// plugin system). Name, ApiVersion and Plugins are only filled after that.
        /// </summary>
        public bool HasPluginSystem { get; internal set; }

        /// <summary>The name the server admin set (may be empty).</summary>
        public string Name { get; internal set; } = "";

        /// <summary>Plugin API version of the server (spirit_api), 0 if unknown.</summary>
        public int ApiVersion { get; internal set; }

        /// <summary>Server plugins that are running: id -> version.</summary>
        public IReadOnlyDictionary<string, string> Plugins { get; internal set; } = new Dictionary<string, string>();

        /// <summary>Whether the server runs the plugin <paramref name="id"/> (at least <paramref name="minVersion"/>).</summary>
        public bool HasPlugin(string id, string minVersion = null)
        {
            if (id == null || !Plugins.TryGetValue(id, out var version))
                return false;
            return minVersion == null || LibreData.CompareVersions(version, minVersion) >= 0;
        }

        public override string ToString() => string.IsNullOrEmpty(Name) ? Host : $"{Name} ({Host})";
    }

    public static partial class LibreSupport
    {
        internal const string HelloChannel = "libre/hello";

        // -- events (always raised on Unity's main thread) ----------------- //

        /// <summary>The game opened a connection to a server (before login).</summary>
        public static event Action<LibreServer> ServerConnected;

        /// <summary>The server accepted the login. From now on Send reaches the server's plugins.</summary>
        public static event Action LoggedIn;

        /// <summary>
        /// The server greeted this mod right after login, with its name and plugins
        /// (only servers with the LibreTCG plugin system do).
        /// </summary>
        public static event Action<LibreServer> ServerHello;

        /// <summary>The connection was closed (logout, error, or the game quitting).</summary>
        public static event Action Disconnected;

        /// <summary>A Unity scene finished loading (the game's big screens: login, main menu, match...).</summary>
        public static event Action<string> SceneChanged;

        /// <summary>The player joined a chat room, e.g. "Lobby_Spirit_1" (the Spirit Lobby).</summary>
        public static event Action<string> RoomJoined;

        /// <summary>The player left a chat room.</summary>
        public static event Action<string> RoomLeft;

        /// <summary>A match was found and is starting.</summary>
        public static event Action MatchStarted;

        /// <summary>The match ended (win, loss or concede).</summary>
        public static event Action MatchEnded;

        /// <summary>
        /// Every event the game's network session dispatches (server messages and connection
        /// events), as the game's own objects. Use the message's type name to pick the ones
        /// you need. Advanced: prefer the specific events above when one fits.
        /// </summary>
        public static event Action<object> GameMessage;

        // -- state ------------------------------------------------------------- //

        /// <summary>The server the game is connected to, or null.</summary>
        public static LibreServer Server { get; private set; }

        /// <summary>True between LoggedIn and Disconnected.</summary>
        public static bool IsLoggedIn { get; private set; }

        /// <summary>Chat rooms the player is in.</summary>
        public static IList<string> Rooms => rooms.ToList();

        /// <summary>True between MatchStarted and MatchEnded.</summary>
        public static bool InMatch { get; private set; }

        /// <summary>Name of the current Unity scene.</summary>
        public static string Scene { get; private set; } = "";

        private static readonly HashSet<string> rooms = new HashSet<string>();

        /// <summary>
        /// A folder for <paramref name="modGuid"/>'s data about the current server
        /// (BepInEx/data/&lt;guid&gt;/&lt;host&gt;/), created if needed. Null when not connected.
        /// </summary>
        public static string ServerDataDir(string modGuid)
        {
            if (Server == null)
                return null;
            if (string.IsNullOrEmpty(modGuid) || modGuid.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0 || modGuid.Contains(".."))
                throw new ArgumentException("not a valid mod GUID", nameof(modGuid));
            string host = new string(Server.Host.Select(c => char.IsLetterOrDigit(c) || c == '.' || c == '-' ? c : '_').ToArray());
            if (host.Length == 0 || host.Trim('.').Length == 0)
                host = "_unknown";
            string dir = Path.Combine(Path.Combine(Path.Combine(BepInEx.Paths.BepInExRootPath, "data"), modGuid), host);
            Directory.CreateDirectory(dir);
            return dir;
        }

        // -- wiring ------------------------------------------------------------ //

        internal static void Initialize()
        {
            SceneManager.sceneLoaded += (scene, mode) =>
            {
                if (mode != LoadSceneMode.Single)
                    return;
                Scene = scene.name;
                DebugTrace.Add("event", "SceneChanged " + scene.name);
                Raise(SceneChanged, scene.name);
            };
        }

        // Main thread: SessionProvider.dispatch runs in its Update.
        internal static void OnGameEvent(DWDEvent message)
        {
            if (message == null)
                return;
            DebugTrace.AddGameMessage(message);
            switch (message)
            {
                case SessionProvider.Connected _:
                    Server = new LibreServer(CurrentHost());
                    DebugTrace.Add("event", $"ServerConnected {Server.Host}");
                    Raise(ServerConnected, Server);
                    break;
                case SessionProvider.Disconnected _:
                    if (Server == null && !IsLoggedIn)
                        break;
                    ResetSession();
                    DebugTrace.Add("event", "Disconnected");
                    Raise(Disconnected);
                    break;
                case AuthenticationSuccessful _:
                    IsLoggedIn = true;
                    DebugTrace.Add("event", "LoggedIn");
                    Raise(LoggedIn);
                    break;
                case ChatConnected connected:
                    string joined = RoomId(connected.room);
                    if (joined != null && rooms.Add(joined))
                    {
                        DebugTrace.Add("event", "RoomJoined " + joined);
                        Raise(RoomJoined, joined);
                    }
                    break;
                case ChatDisconnected disconnected:
                    if (disconnected.roomID != null && rooms.Remove(disconnected.roomID))
                    {
                        DebugTrace.Add("event", "RoomLeft " + disconnected.roomID);
                        Raise(RoomLeft, disconnected.roomID);
                    }
                    break;
                case MatchFound _:
                    if (!InMatch)
                    {
                        InMatch = true;
                        DebugTrace.Add("event", "MatchStarted");
                        Raise(MatchStarted);
                    }
                    break;
                case GameCompletedMessage _:
                    if (InMatch)
                    {
                        InMatch = false;
                        DebugTrace.Add("event", "MatchEnded");
                        Raise(MatchEnded);
                    }
                    break;
            }
            Raise(GameMessage, (object)message);
        }

        internal static void ReceiveHello(object data)
        {
            if (Server == null)
                Server = new LibreServer(CurrentHost());
            Server.HasPluginSystem = true;
            Server.Name = LibreData.GetString(LibreData.GetDict(data, "server"), "name", "");
            Server.ApiVersion = LibreData.GetInt(data, "api");
            var plugins = new Dictionary<string, string>();
            if (LibreData.GetDict(data, "plugins") is IDictionary list)
                foreach (DictionaryEntry entry in list)
                    if (entry.Key is string id && entry.Value != null)
                        plugins[id] = entry.Value.ToString();
            Server.Plugins = plugins;
            Plugin.Log.LogInfo($"Server hello: {Server}, plugin API {Server.ApiVersion}, plugins: " +
                               (plugins.Count == 0 ? "none" : string.Join(", ", plugins.Select(p => $"{p.Key} {p.Value}"))));
            Raise(ServerHello, Server);
        }

        private static void ResetSession()
        {
            Server = null;
            IsLoggedIn = false;
            InMatch = false;
            rooms.Clear();
        }

        // MessageCenterBehaviour (pie-src) is the game's SessionProvider; "host" comes from cake.cfg.
        private static string CurrentHost()
        {
            try
            {
                return Finder.TryFind<MessageCenterBehaviour>()?.host ?? "";
            }
            catch (Exception)
            {
                return "";
            }
        }

        // The room object's class is obfuscated (its fields share names), so the room ID is
        // found through the field's [JsonName("roomID")] attribute, never by field name.
        private static readonly Dictionary<Type, FieldInfo> roomIdFields = new Dictionary<Type, FieldInfo>();

        private static string RoomId(object room)
        {
            if (room == null)
                return null;
            var type = room.GetType();
            if (!roomIdFields.TryGetValue(type, out var field))
            {
                field = type.GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
                    .FirstOrDefault(f => f.FieldType == typeof(string) && f.GetCustomAttributesData().Any(a =>
                        a.AttributeType.Name == "JsonNameAttribute" && a.ConstructorArguments.Count == 1 &&
                        a.ConstructorArguments[0].Value as string == "roomID"));
                roomIdFields[type] = field;
            }
            return field?.GetValue(room) as string;
        }

        // One mod's exception must not stop the others' handlers.
        private static void Raise(Delegate handlers, params object[] args)
        {
            if (handlers == null)
                return;
            foreach (var handler in handlers.GetInvocationList())
            {
                try
                {
                    handler.DynamicInvoke(args);
                }
                catch (TargetInvocationException e)
                {
                    Plugin.Log.LogError($"Error in a LibreSupport event handler ({handler.Method.DeclaringType?.FullName}.{handler.Method.Name}): {e.InnerException}");
                }
            }
        }
    }

    // Every event the game's network session delivers goes through SessionProvider.dispatch
    // (core.dll), on the main thread: connection events, login, chat, matchmaking, game end.
    // Only observed (prefix), never changed. SessionProvider.Write is patched the same way.
    [HarmonyPatch(typeof(SessionProvider), "dispatch", new[] { typeof(DWDEvent), typeof(bool) })]
    internal static class ObserveGameEvents
    {
        private static void Prefix(DWDEvent message)
        {
            try
            {
                LibreSupport.OnGameEvent(message);
            }
            catch (Exception e)
            {
                Plugin.Log.LogError($"Could not process a game event: {e}");
            }
        }
    }
}
