# LibreTCG Mod

A [BepInEx 5](https://github.com/BepInEx/BepInEx) plugin for the Pokémon Trading Card Game Online **2.95.0.5815** PC client, made for private servers based on [Spirit](https://github.com/Bratah123/Spirit-PTCGO).

Only the mod is distributed. You need your own copy of the game; no game files are included or modified.

## What it does (v0.2.0)

- Shows a small `LibreTCG mod <version>` label in the top-left corner, so you can see the mod is active.
- Tells the server which mods are installed when you log in. Servers can then require the mod and refuse logins without it. On servers that don't check, this extra field is ignored.

Next: client-side windows driven by server plugins, such as a format selector.

## Install

The easiest way is **Libre's TCG Online Launcher** (2.0 beta and later). When a server requires mods, it asks before installing BepInEx and the mod.

To install by hand:

1. Install **BepInEx 5.4.23.x, Windows x86**, in the game folder (the folder with `Pokemon Trading Card Game Online.exe`).
2. In `BepInEx/config/BepInEx.cfg`, set `HideManagerGameObject = true`. Start the game once if the file doesn't exist yet. Without this setting the game drops the plugin object and nothing is drawn.
3. Put `LibreTcgMod.dll` in `BepInEx/plugins/libretcg.mod/`.
4. On Linux/macOS (Wine), start the game with `WINEDLLOVERRIDES="winhttp=n,b"`.

## Protocol: `libreMods`

Before the client sends its login message (`AuthenticateCASTicket`, and the other `Authenticate*` messages), the mod adds this field to the message body:

```json
"libreMods": {"libretcg.mod": "0.2.0", "<other plugin guid>": "<version>"}
```

It lists every loaded BepInEx plugin. A server can compare it with its requirements and answer `AuthenticationFailed`, whose `reason` text the client shows in its login error dialog.

This is a dependency check, not anti-cheat: a modified client can report anything.

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
- The game's TCP port, 39389, is hardcoded. `cake.cfg` only sets the hostname and HTTP URLs.

## License

GPL-3.0-or-later. See [LICENSE](LICENSE).
