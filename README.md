# Projector Dashboard

A bedside control panel for an ASUS Transformer Book T100TAF (2 GB RAM, Atom Z3735F) permanently wired to a projector. The tablet screen becomes the remote control; the projector stays clean. It is a native WPF application compiled with the C# compiler that is already part of Windows 8.1, so there is nothing to install — no Electron, no browser runtime, no frameworks, no internet needed to build.

Typical footprint: roughly 30–45 MB of RAM, near-zero CPU when idle (one clock tick per second), and it starts in about two seconds on the Atom.

## Why this stack

WPF on the built-in .NET Framework 4.5.1 was chosen deliberately for this hardware. It is already installed on every Windows 8.1 machine, it renders through DirectX rather than a browser engine, it has first-class touch support, and Windows even ships the compiler (`csc.exe`), so the tablet builds its own executable — always as a **32-bit (x86)** binary matching the 32-bit OS and Atom Z3735F. A browser kiosk or Electron app would cost 10–20× the memory before showing a single button.

## Setup (on the tablet)

1. Open the [latest release](https://github.com/M-Tameem/projectordashboard/releases/latest) on the tablet and download `ProjectorDashboard-vX.Y.Z-win-x86.zip`.
2. Extract the ZIP to a permanent writable folder such as `C:\Dashboard`. Do not run it from inside the ZIP.
3. Double-tap **Dashboard.exe**. It is already compiled as a **32-bit (x86)** Windows 8.1 application; no developer tools or build step are required.
4. On first launch, a number flashes on every screen. Tap **Tablet** next to the screen you are touching and **Projector** next to the other one, then **Save and start**. This choice is saved permanently.
5. Double-tap **install-startup.bat** if you want the dashboard to launch at every boot.

`build.bat` is only needed when compiling a source checkout yourself.

Recommended Windows tweaks for the full "appliance" feel:

- Install **Supermium** for website tiles. The dashboard auto-detects its usual install locations; if needed, choose `chrome.exe` or `supermium.exe` under Settings. Website tiles intentionally do not fall back to another browser.
- Enable automatic sign-in (`netplwiz`) so a reboot lands straight in the dashboard.
- The projector does not need to be the Windows main display. The dashboard sends both websites and program windows to the assigned projector itself.

## Using it

- **Tiles** launch websites or programs on the active target. In normal mode that is the assigned projector; in **Tablet only** mode it is the tablet itself. Website tiles always use **Supermium** and default to borderless fullscreen; Settings > Projector can switch launches to normal framed maximized windows. Twitch, Caedrel, and Miruro are included automatically, and ordinary web tiles cache their site icon locally after the first successful download.
- **Weather + Sky now** use one manually saved location rather than Windows Location Services, which is unreliable on the target Windows 8.1 tablet. Open **Settings > Ambient** once, search a city/town/postal code, optionally refine the exact latitude/longitude, and set the true compass heading toward the physical top edge of the projected image. If that edge is above your head, use the direction your head points; if it is above your feet, use the direction your feet point. Weather and next sunrise come from Open-Meteo; aircraft use adsb.lol with airplanes.live as an automatic fallback; the ISS position comes from Where the ISS at?; planet and bright-star bearings are calculated locally. None requires an API key, account, subscription, or paid service. The **Live data** control on the main Sky Now strip (also available under Settings > Ambient) immediately cancels aircraft/ISS requests and prevents further Sky Now polling until it is turned back on; local planets and stars remain available, and the separate weather schedule is unchanged. Bearings include both true compass direction and a view-relative cue such as `NW · left`.
- **Overhead** is a first-class action on the main screen: **Project sky** sends it straight to the projector by default, while systems without an available projector open the tablet view. It is a live ceiling view seen from below, so east/west are intentionally mirrored from a ground map and the complete 360° compass wraps around the center according to the saved display heading. Celestial objects remain a literal full-sky hemisphere: center is zenith (90°) and all four outer edges are horizon (0°). Aircraft use the separate selected-distance ruler: center is 0 km and the marked inner limit is the chosen search radius, so an aircraft 60 km away in a 120 km view renders halfway to that limit instead of being crushed against the horizon. Aircraft silhouettes show callsign, type, registration, course, speed, altitude, distance, and viewing elevation. Dashed edge-to-edge lines show reported course; brighter solid segments retain observed movement. Markers glide between network samples using reported ADS-B track and groundspeed, and brief feed omissions do not make an aircraft vanish immediately. The **Range** control selects 20–460 km in 20 km steps; `−` and `+` only change a pending value, and **Done** saves it and makes one refresh. The normal 40 km view keeps the 5° clutter cutoff; expanded views admit visible aircraft down to 0.5° above the curved-earth horizon. The ISS has a compact azimuth/elevation/range label and observed pass trail; major stars and planets continue moving from local clock-based calculations.
- **Color** in the main toolbar cycles Cyan, Amber, Violet, and Emerald palettes. The saved palette applies immediately to the entire tablet UI, settings, overlays, projector idle screen, and live sky map.
- **Tablet only** under Settings > Projector restarts into a complete one-screen workflow without deleting the saved projector assignment. Fullscreen apps open on the tablet and a compact always-on-top **Dashboard / OFF** overlay lets you return without a keyboard or mouse. Alarm, scheduled lock, app sleep/wake, shortcuts, brightness, and audio controls continue to work normally.
- **Preview** opens a live view of the current projector app on the dashboard while leaving volume, brightness, and the TouchMousePointer area available. **Fullscreen on tablet** expands that same live surface across the tablet, so the tablet and projector show one shared Supermium window—not two tabs, decoders, sessions, or audio streams. The small Dashboard / OFF overlay remains available in fullscreen preview. Windows creates this as [a dynamic DWM source-to-destination relationship](https://learn.microsoft.com/en-us/windows/win32/dwm/thumbnail-ovw) rather than a screenshot loop. Protected/DRM video may deliberately render black in the preview even while ordinary pages and video continue working on the projector.
- **Settings** is split into five large touch-friendly tabs: Shortcuts, Projector, Audio + browser, Alarm, and Ambient. The `+` tile opens the shortcut editor directly.
- **Output volume** controls the endpoint named beside it. Open **Audio + browser** to select a Windows audio device. Each endpoint retains its own volume and mute state, so switching between tablet Speakers, projector HDMI, and Bluetooth does not copy one endpoint's settings onto another. Supermium is routed through the selected Windows console/media output; the dashboard deliberately never changes Windows' separate Communications device, preventing Bluetooth headphones from being pushed into hands-free call mode.
- **Tablet brightness** controls only the built-in tablet backlight and hides if Windows does not expose a backlight interface. **Projector brightness** is a separate saved bar that places a black, click-through dimmer above every projected window, including Supermium and ordinary apps, without blocking their input. It lowers visible image brightness reliably over HDMI but does not reduce the projector lamp/LED power; use the projector's own eco mode when power or heat reduction is the goal.
- **Idle** hides the open projector apps without closing their windows or browser tabs and shows the ambient clock, weather, a live next-sunrise countdown, ISS/planet status, and nearby-aircraft count. **Wake** restores those same windows.
- **Reset now** under Settings > Audio + browser force-closes every running Supermium process and opens a clean projector window. It is the fix-it control for a stuck browser, not the normal website launcher.
- **Tools > Keyboard** opens the movable Windows on-screen keyboard and places it to the left of the TouchMousePointer corner. If OSK cannot start, the touch keyboard is used as a fallback. **Tools > Windows desktop** minimizes the dashboard when you genuinely need Windows; tap it in the taskbar to come back.
- **Tools > Daily tablet auto-lock** opens a compact daily schedule. At the enabled time it locks the tablet exactly like Win+L. Windows still requires the normal sign-in to unlock; the dashboard does not store or bypass a password.
- **Power** keeps the destructive actions out of the main toolbar. It can close the current projected app, turn off Windows display signals, lock plus emergency-off, exit the dashboard, or send physical projector standby through HDMI-CEC when compatible hardware is detected.
- Normal PC HDMI output can blank the signal but usually cannot originate HDMI-CEC commands. Physical projector standby requires both a CEC-capable projector and a CEC-capable source; on this tablet that will commonly mean a USB-CEC adapter. If the free `cec-client.exe` from libCEC is installed beside `Dashboard.exe` or in its standard Pulse-Eight folder, the dashboard detects it automatically and enables the physical standby path. No CEC adapter is required for the normal display-signal-off action.
- **Check for update** under Settings > Audio + browser installs stable releases from `M-Tameem/projectordashboard`. It downloads `Dashboard.exe` and its SHA-256 checksum, verifies both the checksum and embedded version, closes the dashboard, replaces the executable through a temporary helper, and relaunches it. If the new build cannot start, the helper restores the previous executable. Until the repository has its first release, the button simply reports that no release has been published.
- The daily **Alarm** is configured in the Alarm tab. A safe built-in Speakers endpoint must be selected before it can be enabled. While ringing, the dashboard switches to that tablet endpoint, forces it to 100% and unmuted, mutes other application/browser sessions, then restores the prior default output and every saved volume/mute state on Dismiss or Snooze. Bluetooth, USB, HDMI, headphones, and projector endpoints are rejected for alarm use. The alarm runs only while the dashboard and tablet are awake.

### Shortcut examples

Each shortcut also has independent one-tap **website launch options** in the editor. They all start off; enable only the workaround(s) that site needs:

- **Direct comp** — adds `--disable-direct-composition`.
- **D3D9** — adds `--use-angle=d3d9`.
- **VSync** — adds `--disable-gpu-vsync`.
- **Incognito** — opens the site in a private window (`--incognito`).
- **Hide address** — turns the tile into a discreet alias: its URL and site icon are hidden on the dashboard and in the shortcut list, while the real target remains editable after selecting it.

The three GPU switches can be used separately or in any combination. A shortcut saved by an older build with the single **GPU compat** switch on is migrated once with all three enabled so its existing behavior does not silently change.

These options combine freely with the Arguments column, which customizes how Supermium
opens the site. Leave Arguments empty for the selected global launch mode, or use the
placeholder `{url}` where the address should be substituted.

| Name | Target | Arguments | Result |
|---|---|---|---|
| Crunchyroll | `https://www.crunchyroll.com` | | fullscreen Supermium kiosk window |
| YouTube (app) | `https://www.youtube.com` | `--app={url}` | chromeless app window — cleanest on a projector |
| Netflix kiosk | `https://www.netflix.com` | `--kiosk {url}` | true fullscreen, exit with Alt+F4 |
| Jellyfin | `http://192.168.1.20:8096` | `--app={url}` | |
| VLC | `C:\Program Files\VideoLAN\VLC\vlc.exe` | `--fullscreen` | (non-web: args passed straight through) |
| Any bookmark file | `C:\Users\you\Desktop\show.url` | | opened by Windows |

## The TouchMousePointer corner

The bottom-right rectangle of the tablet screen (dashed outline, labeled *touch pad*) is intentionally empty. Nothing interactive is ever placed there, so TouchMousePointer's pad always has clear ground beneath it. Its new default is a taller, narrower 420 × 340 px area. If your pad is a different size, change **Settings > Projector > Touch pad area** and the whole layout reflows around it — the settings panel itself also stays out of that strip.

The **Keyboard** button uses the classic movable OSK so it can be resized into the lower-left safe area instead of docking across TouchMousePointer. Windows may still use its own placement if OSK is running at a different privilege level; the dashboard's reserved corner remains the fallback protection.

## Display handling details

- Screens are remembered by their Windows device name (`\\.\DISPLAY1`, …). If a remembered screen is missing at startup (projector unplugged and only then), the picker reappears; otherwise nothing is asked twice.
- With only one screen attached, enable **Tablet only** to launch and control fullscreen apps on that screen with the floating return overlay.
- Window placement uses raw device pixels (`SetWindowPos`), so mixed-DPI setups (tablet at 100 %, projector at 100 %/125 %) position correctly.
- Launched app windows are detected after startup (including Chromium's hand-off to an existing process), placed on the active projector/tablet target, and made borderless fullscreen or maximized according to Settings.
- Preview uses Windows' live DWM thumbnail API and follows whichever ordinary app window is currently topmost on the projector. It is view-only; TouchMousePointer remains the input path to the original projector window.
- **Settings → Reassign displays** wipes the saved mapping and restarts the app into the picker.

## Files

```
src\Program.cs            entry point, single-instance guard, restart logic
src\Config.cs             XML config model, saved to %APPDATA%\ProjectorDashboard\config.xml
src\ScreenUtil.cs         screen enumeration + pixel-exact fullscreen placement
src\AudioEndpoint.cs      Core Audio devices, routing, volume/mute, alarm isolation
src\BrightnessControl.cs  WMI backlight brightness
src\AmbientData.cs        free weather/aircraft/ISS clients + local planet/star positions
src\OverheadMap.cs        animated full-hemisphere aircraft/ISS/celestial ceiling view
src\CecControl.cs          optional HDMI-CEC physical projector standby through libCEC
src\SiteIconCache.cs      asynchronous per-site favicon cache
src\DwmWindowMirror.cs    one-source live DWM thumbnail relationship
src\MirrorWindow.cs       full-tablet projector mirror surface
src\SelfUpdater.cs        verified GitHub download + safe replacement helper
src\VersionInfo.cs        app version stamped from v1.2.3 release tags
src\Ui.cs                 theme, flat button factory, finger-sized TouchSlider
src\DisplayPickerWindow.cs first-run screen assignment with identify flashes
src\ControllerWindow.cs   the tablet UI + settings overlay
src\ProjectorWindow.cs    ambient projector display (clock or blank)
src\ProjectorDimmerWindow.cs saved click-through projector image dimmer
src\ReturnOverlayWindow.cs tablet-only return / emergency-off touch overlay
build.bat                 compiles with Windows' own csc.exe
install-startup.bat       auto-start at boot (per-user Startup folder)
uninstall-startup.bat     removes auto-start
.github\workflows\release.yml  tag-triggered build and release publisher
```

The config file is plain XML — you can also edit it directly (with the dashboard closed) if that's ever faster than the settings page.

## Publishing updates

The public update channel is:

`https://github.com/M-Tameem/projectordashboard/releases/latest`

The included GitHub Actions workflow builds and publishes releases automatically. For a normal update:

1. Commit and push the finished source to the repository's default branch.
2. Create and push a strictly numeric tag such as `v1.0.1`.
3. GitHub Actions stamps that version into the executable, builds the same x86 WPF app as `build.bat`, creates `Dashboard.exe.sha256` and a portable ZIP, then publishes all three assets to the GitHub release.

Example commands:

```powershell
git tag v1.0.1
git push origin v1.0.1
```

Do not manually rename a different program to `Dashboard.exe`: the updater requires the release tag, embedded assembly version, filename, and checksum to agree. Drafts and prereleases are not offered by the one-tap updater. The repository must remain public unless authentication is deliberately added to the app later.

## Performance notes

- The ordinary bedside and idle surfaces remain nearly static. Only a visible Overhead view repaints at 8 frames per second, which is enough for smooth projected motion without burdening the Atom CPU/GPU.
- Weather is requested every 15 minutes. Aircraft/ISS data syncs every 15 seconds while Overhead is visible and every 45 seconds otherwise. Turning **Live data** off cancels an in-flight Sky Now request, prevents future aircraft/ISS polling, clears remote tracks, and retains local planet/star calculations; weather remains separately enabled. If the primary aircraft feed fails, the primary is still retried every 45 seconds but airplanes.live is fetched at most once every three minutes (480 requests/day maximum) and its intermediate samples are extrapolated locally. Motion between samples makes no extra requests. With no saved location, no ambient network requests run. Planet and bright-star positions are tiny on-device calculations refreshed from the current clock.
- One shared button template, frozen brushes, and a static projector background keep GPU/CPU use negligible.
- The projector window is pure ambient rendering — it never animates, so the Atom idles while you're not watching anything.
- Website icons are fetched only when missing, validated as images, capped at 1 MB, and then loaded from the local cache.
- Projector preview is composed by DWM from the existing window; the dashboard does not poll screenshots or start a second browser/video decoder.
- Ambient data uses the last successful in-memory result when a public feed is temporarily unavailable, so a network hiccup does not clear the bedside display.
