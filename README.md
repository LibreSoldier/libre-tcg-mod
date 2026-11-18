# Libre's Support Mod

<img src="logo.png" alt="Libre's Support Mod logo" width="128">

A [BepInEx 5](https://github.com/BepInEx/BepInEx) plugin for the Pokémon Trading Card Game Online **2.95.0.5815** PC client, made for private servers based on [Spirit](https://github.com/Bratah123/Spirit-PTCGO).

Only the mod is distributed. You need your own copy of the game; no game files are included or modified.

## What it does (v1.1.0)

Libre's Support Mod is the base other client mods build on. BepInEx still loads the mods; this one gives them a way to talk to the server.

- **Mod channel (`LibreModMessage`).** Server plugins and client mods exchange messages through it. Other mods depend on this mod and use the `LibreSupport` API (below) instead of patching the game's network code themselves.
- **Session and game events** for other mods: login, server hello, disconnect, screens, chat rooms, match start and end. Also the current server, its plugins, and a data folder per server.
- **Helpers** for reading message data (`LibreData`) and for drawing text in the game's font.
- **Debug panel** (F9, off by default).
- **Mod report at login.** Tells the server which mods are installed. Servers can then require mods and refuse logins without them. On servers that don't check, this extra field is ignored.
- **"(modded)" in the options menu.** The *Client Version* line gets " (modded)".
- **On-screen label.** While the options menu is open, shows `Libre's Support Mod <version>` in the top-left corner, so you can see the mod is active.

## Install

The easiest way is **Libre's TCG Online Launcher** (2.0 beta and later). When a server requires mods, it asks before installing BepInEx and the mod.

To install by hand:

1. Install **BepInEx 5.4.23.x, Windows x86**, in the game folder (the folder with `Pokemon Trading Card Game Online.exe`).
2. In `BepInEx/config/BepInEx.cfg`, set `HideManagerGameObject = true`. Start the game once if the file doesn't exist yet. Without this setting the game drops the plugin object and nothing is drawn.
3. Put `LibreTcgMod.dll` in `BepInEx/plugins/libre.support/`.
4. On Linux/macOS (Wine), start the game with `WINEDLLOVERRIDES="winhttp=n,b"`.

## Protocol: `libreMods`

Before the client sends its login message (`AuthenticateCASTicket`, and the other `Authenticate*` messages), the mod adds this field to the message body:

```json
"libreMods": {"libre.support": "1.1.0", "<other plugin guid>": "<version>"}
```

It lists every loaded BepInEx plugin. A server can compare it with its requirements and answer `AuthenticationFailed`, whose `reason` text the client shows in its login error dialog.

This is a dependency check, not anti-cheat: a modified client can report anything.

## For mod authors: the `LibreSupport` API

Reference `LibreTcgMod.dll` (with `Private=false`, so it isn't copied) and declare the dependency, so BepInEx loads this mod first and refuses to load yours without it:

```csharp
using LibreTcgMod;

[BepInPlugin("my.mod", "My Mod", "1.0.0")]
[BepInDependency("libre.support", "1.1.0")]
public class MyMod : BaseUnityPlugin
{
    void Awake()
    {
        // Register channels in Awake: they are reported to the server at login.
        LibreSupport.On("my-plugin/show", data =>
        {
            string title = LibreData.GetString(data, "title", "");
            // runs on Unity's main thread: safe to touch the UI
        });
        LibreSupport.ServerHello += server =>
        {
            if (server.HasPlugin("my-plugin", "1.0"))
                LibreSupport.Send("my-plugin/ready", null);
        };
    }

    void Choose(string format) =>
        LibreSupport.Send("my-plugin/choice", new Dictionary<string, object> { { "format", format } });
}
```

### Messages

| Member | What it does |
|---|---|
| `On(channel, handler)` | Calls `handler(data)` for every message the server sends on `channel`, on the main thread. One handler per channel. |
| `Off(channel)` | Removes the handler. |
| `Send(channel, data)` | Sends `data` (dictionaries, lists, strings, numbers, booleans) to the server. Call it from the main thread. Returns `false` when not connected. The server only accepts it after login. |
| `Channels` | The channels that have a handler. They are reported to the server at login. |

- Channels are named `<server plugin id>/<name>`: the server plugin `my-plugin` owns every `my-plugin/...` channel. Channels starting with `libre/` are reserved.
- An exception in a handler is logged and doesn't affect other mods. Messages for a channel no mod handles are logged once and dropped.
- Treat what a server sends as data, never as code. Don't run, write or open anything just because a server asked.

### Session and game events

All events run on Unity's main thread. An exception in one handler is logged and the others still run.

| Event | When |
|---|---|
| `ServerConnected(LibreServer)` | The game opened a connection (before login). |
| `LoggedIn` | The server accepted the login. From now on `Send` reaches the server's plugins. |
| `ServerHello(LibreServer)` | Right after login, on servers with the LibreTCG plugin system: name and plugins are known. |
| `Disconnected` | The connection closed. |
| `SceneChanged(string)` | One of the game's big screens finished loading. |
| `RoomJoined(string)` / `RoomLeft(string)` | The player joined or left a chat room, e.g. `Lobby_Spirit_1`. |
| `MatchStarted` / `MatchEnded` | A match was found / ended (win, loss, concede or disconnect: the game left the match screens). |
| `GameMessage(object)` | Every event the game's network session dispatches (advanced). |

State: `Connected`, `IsLoggedIn`, `Server`, `Rooms`, `InMatch`, `Scene`.

`LibreSupport.Server` (null when not connected):
- `Host`: the hostname from the game's `cake.cfg`.
- `HasPluginSystem`: true after `ServerHello`.
- `Name`, `ApiVersion`, `Plugins` (id → version), `HasPlugin(id, minVersion)`.

`LibreSupport.ServerDataDir("my.mod")` gives a folder for your mod's data about the current server: `BepInEx/data/<guid>/<host>/`.

### Reading data: `LibreData`

The game's JSON reader turns objects into `Dictionary<string, object>`, lists into arrays, and numbers into `Int32`, `Int64` or `Double` depending on the value. `LibreData` hides that and never throws on a missing key or wrong type; you get the fallback:

- `GetString`, `GetInt`, `GetLong`, `GetDouble`, `GetBool`, `Get<T>(data, key, fallback)`;
- `GetDict` (null if missing) and `GetList` (empty if missing);
- `As<T>(value)` for one value, for example an item of a list;
- `ToJson(value)` for logs, and `CompareVersions("1.10", "1.9")`.

### Text in IMGUI

Unity's built-in IMGUI font draws nothing in this game. Use `LibreSupport.GameFont`, or `LibreSupport.NewLabelStyle(14)` from inside `OnGUI`.

### Debug panel

In `BepInEx/config/libre.support.cfg`, set `Panel = true` under `[Debug]`. Then **F9** (`PanelKey`) shows:
- the server and its plugins;
- the rooms and the loaded mods;
- the registered channels;
- the recent LibreModMessages and events.

It can also list every game message. It's off by default, so players don't open it by accident.

### Wire format (for server developers)

In both directions: `{"name": "LibreModMessage", "value": {"channel": "...", "data": ...}}`. Spirit plugins don't build it by hand: `api.send_to_mod` does.

The login message also carries:
- `libreMods` ({guid: version});
- `libreApi` (1);
- `libreChannels` (since 1.1).

Servers with the plugin system answer right after login on the channel `libre/hello` with `{"api": 1, "server": {"name": "..."}, "plugins": {"id": "version"}}`. The vanilla game silently skips messages it doesn't know, but servers should still only send them to clients that reported `libre.support`.

## Build

Requirements: .NET SDK 8+, and a copy of the game with BepInEx installed. The project compiles against the game's and BepInEx's DLLs, but never copies or ships them.

```sh
dotnet build -c Release -p:GameDir="/path/to/PokemonTradingCardGameOnline"
# -> bin/Release/net471/LibreTcgMod.dll
```

## Notes for modders

- The game is Unity 2018.4 (Mono, 32-bit), so it needs BepInEx x86.
- The built-in IMGUI font doesn't render in this build. Use one of the game's fonts (`FunctionPro-Demi`, `-Bold`, `-Medium`).
- Every message the client sends goes through `dwd.core.session.SessionProvider.Write` (`core.dll`).
- Incoming JSON messages with an unknown `messageName` become `dwd.core.wargServer.messages.Unparseable` and are skipped by `WargSocket.Read`, on the network thread.
- Don't Harmony-patch methods of pie-src's obfuscated classes that read fields (for example `VersionLabel.Start`): their fields share names, and the patched method crashed the game.
- The game's TCP port, 39389, is hardcoded. `cake.cfg` only sets the hostname and HTTP URLs.

## License

GPL-3.0-or-later. See [LICENSE](LICENSE).
