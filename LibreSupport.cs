// Libre's Support Mod - BepInEx plugin for the PTCGO 2.95 client
// Copyright (C) 2026 LibreSoldier
//
// This program is free software: you can redistribute it and/or modify it under
// the terms of the GNU General Public License as published by the Free Software
// Foundation, either version 3 of the License, or (at your option) any later
// version. It is distributed WITHOUT ANY WARRANTY; see the LICENSE file or
// <https://www.gnu.org/licenses/> for details.
// SPDX-License-Identifier: GPL-3.0-or-later

using System;
using System.Collections;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using dwd.core.session;
using dwd.core.wargServer.messages;
using HarmonyLib;

namespace LibreTcgMod
{
    /// <summary>
    /// The message channel between server plugins and client mods (LibreModMessage).
    /// Other mods use it through a dependency on this mod:
    /// <code>
    /// [BepInDependency("libre.support", "1.1.0")]
    /// ...
    /// LibreSupport.On("my-plugin/show", data => { ... });
    /// LibreSupport.Send("my-plugin/choice", new Dictionary&lt;string, object&gt; { { "format", "expanded" } });
    /// </code>
    /// Channels are named "&lt;server plugin id&gt;/&lt;name&gt;". Handlers always run on
    /// Unity's main thread, so they can touch the game's UI.
    /// Session and game events, and the current server, are in LibreSupport.Session.cs.
    ///
    /// Everything that comes from a server is data: this class never runs code,
    /// writes files or opens links because a server asked.
    /// </summary>
    public static partial class LibreSupport
    {
        public const string MessageName = "LibreModMessage";

        /// <summary>Raised only on incompatible changes; new features keep the same number.</summary>
        public const int ApiVersion = 1;

        /// <summary>Channels starting with this belong to the server core and this mod.</summary>
        public const string ReservedPrefix = "libre/";

        private const int MaxPending = 1000;

        private static readonly Dictionary<string, Action<object>> handlers = new Dictionary<string, Action<object>>();
        private static readonly ConcurrentQueue<KeyValuePair<string, object>> pending = new ConcurrentQueue<KeyValuePair<string, object>>();
        private static readonly HashSet<string> warnedChannels = new HashSet<string>();

        /// <summary>
        /// Calls <paramref name="handler"/> with the data of every message the server sends on
        /// <paramref name="channel"/>. The data is what the game's JSON reader produces:
        /// Dictionary&lt;string, object&gt;, arrays, strings, numbers, booleans or null
        /// (LibreData reads it easily). One handler per channel; registering a channel twice
        /// is an error. Register in Awake: the channels registered at login are reported to
        /// the server, so its plugins know which messages this client understands.
        /// </summary>
        public static void On(string channel, Action<object> handler)
        {
            if (string.IsNullOrEmpty(channel))
                throw new ArgumentException("channel is empty", nameof(channel));
            if (channel.StartsWith(ReservedPrefix))
                throw new ArgumentException($"channels starting with '{ReservedPrefix}' are reserved", nameof(channel));
            if (handler == null)
                throw new ArgumentNullException(nameof(handler));
            if (handlers.ContainsKey(channel))
                throw new InvalidOperationException($"LibreSupport channel '{channel}' is already registered");
            handlers[channel] = handler;
        }

        /// <summary>Removes the handler of <paramref name="channel"/>, if any.</summary>
        public static void Off(string channel)
        {
            if (channel != null && !channel.StartsWith(ReservedPrefix))
                handlers.Remove(channel);
        }

        /// <summary>The channels that have a handler, in no particular order.</summary>
        public static IList<string> Channels => handlers.Keys.ToList();

        /// <summary>
        /// Sends <paramref name="data"/> to the server plugin that owns <paramref name="channel"/>.
        /// Use dictionaries, lists, strings, numbers and booleans. Call it from the main thread.
        /// Returns false (nothing sent) when the game isn't connected to a server.
        /// The server only accepts it after login (see LoggedIn).
        /// </summary>
        public static bool Send(string channel, object data)
        {
            if (string.IsNullOrEmpty(channel))
                throw new ArgumentException("channel is empty", nameof(channel));
            var session = CurrentSession();
            if (session == null)
                return false;
            session.Write(new Dictionary<string, object>
            {
                { "name", MessageName },
                { "value", new Dictionary<string, object> { { "channel", channel }, { "data", data } } },
            });
            DebugTrace.Add("->", channel, data);
            return true;
        }

        /// <summary>True while the game has an open connection to a server.</summary>
        public static bool Connected => CurrentSession() != null;

        private static SessionProvider CurrentSession()
        {
            try
            {
                var session = SessionProvider.Find();
                return session != null && session.get_Initialized() ? session : null;
            }
            catch (Exception)
            {
                return null;
            }
        }

        // Network thread: queue the message for the main thread.
        // Spirit wraps every JSON message as {"name": ..., "value": {...}}; the channel and the
        // data are inside "value". A flat {"messageName", "channel", "data"} is accepted too.
        internal static void Receive(IDictionary message)
        {
            if (message.Contains("value") && message["value"] is IDictionary body)
                message = body;
            if (!(message.Contains("channel") && message["channel"] is string channel) || channel.Length == 0)
            {
                Plugin.Log.LogWarning($"Ignored a {MessageName} without a channel");
                return;
            }
            if (pending.Count >= MaxPending)
            {
                Plugin.Log.LogWarning($"Too many {MessageName}s waiting; dropped one for '{channel}'");
                return;
            }
            pending.Enqueue(new KeyValuePair<string, object>(channel, message.Contains("data") ? message["data"] : null));
        }

        // Main thread (Plugin.Update): deliver queued messages to their handlers.
        internal static void Pump()
        {
            while (pending.TryDequeue(out var item))
            {
                DebugTrace.Add("<-", item.Key, item.Value);
                if (item.Key == HelloChannel)
                {
                    ReceiveHello(item.Value);
                    continue;
                }
                if (!handlers.TryGetValue(item.Key, out var handler))
                {
                    if (warnedChannels.Add(item.Key))
                        Plugin.Log.LogWarning($"No mod handles the channel '{item.Key}'; its messages are ignored");
                    continue;
                }
                try
                {
                    handler(item.Value);
                }
                catch (Exception e)
                {
                    Plugin.Log.LogError($"Error in the handler of channel '{item.Key}': {e}");
                }
            }
        }

        internal static bool IsLibreModMessage(IDictionary message)
        {
            foreach (var key in new[] { "messageName", "name" })
                if (message.Contains(key) && message[key] as string == MessageName)
                    return true;
            return false;
        }
    }

    // The game turns messages whose name it doesn't know into Unparseable and silently skips
    // them (WargSocket.Read, core.dll). LibreModMessage is one of those, so it is picked up
    // here, on the network thread, and handed to LibreSupport. The game still skips it.
    // The constructor only stores its argument, so patching it is safe.
    [HarmonyPatch(typeof(Unparseable), MethodType.Constructor, new[] { typeof(object) })]
    internal static class ReceiveModMessages
    {
        private static void Postfix(object message)
        {
            try
            {
                if (message is IDictionary dict && LibreSupport.IsLibreModMessage(dict))
                    LibreSupport.Receive(dict);
            }
            catch (Exception e)
            {
                Plugin.Log.LogError($"Could not read a {LibreSupport.MessageName}: {e.Message}");
            }
        }
    }
}
