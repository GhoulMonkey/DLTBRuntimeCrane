# Changelog

## Unreleased

## 2.3.0

- Sorts the script list alphabetically, so a script is where its name says it
  is.
- Warns when two enabled scripts want the same setting, before the game is
  started. A lease is exclusive, so the script CRANE loads first keeps the
  setting and the other is refused, which otherwise produces a script that is
  ticked, loads without error, and does nothing. The refused row is marked
  amber and names the script that beat it; selecting it explains the clash and
  offers Run this one instead, which moves it ahead in the load order.
- Adds the `@claims` script header tag, with `when=`, `off=`, `fallback=` and
  `needs=`. It declares which settings a script leases, so the warning above
  can be worked out without running the script. CRANE itself does not read it,
  and a script must still handle a refused claim at runtime.
- Renames Move up and Move down to Load earlier and Load later. The list is
  alphabetical now and no longer shows load order, so the detail pane states
  where the selected script sits in it.
- Fixes the connection readout, which changed to GAME STARTING roughly six
  minutes into every session. It judged CRANE's status report by age, and
  CRANE writes that report when it loads scripts and not again, so a healthy
  session's report aged out of the window while the game was still running.
  The report is now compared against the game process's start time.
- Adds a tooltip to the connection indicator that explains the verdict: which
  run of the game is up, when CRANE last reported, and how the two compare.
- Fixes the two ends of the status bar disagreeing about how many scripts are
  installed. Both now count the rows shown.
- Documents `@claims` in the SDK. `validate.ps1` checks its options, rejects a
  `when=` naming an undeclared parameter, and warns when a script leases
  without declaring. Script metadata is read from the first 120 lines rather
  than 60.

## 2.2.0

- Relicenses the scripting host and the manager under the GNU General Public
  License version 3.0 only. A distributed fork carries its source under the
  same terms.
- Keeps the script authoring kit, the Bridge client headers, and the example
  and bundled Lua scripts under the MIT license. Every source file names its
  own terms on an `SPDX-License-Identifier` line.
- Releases up to and including 2.1.1 remain MIT licensed for anyone who
  received them.
- Adds `LICENSE-CRANE.txt` and `SOURCE-CRANE.txt` to the release archive, so
  the terms and the location of the source ship with the binaries.
- Renames `CraneManager.exe` to `CraneLoader.exe`. A previously installed
  `CraneManager.exe` is left behind by the update and can be deleted.
- Adds a theme selector: Light, Paper, Graphite, Slate and Beast. Beast is
  matched to the game's own menus. Changing it repaints immediately and the
  choice is remembered.
- Replaces the stock Windows control chrome with buttons, checkboxes,
  dropdowns, text fields, list rows and scrollbars drawn in the chosen theme,
  and drops the system's blue selection and focus rectangles.
- The title bar now follows the theme, still drawn by Windows.
- Settings are indented under their section heading, with a rule beneath each
  heading.
- Fixes the Settings window cutting off its fields at UI scales above 100%.
  Present in 2.1.1 and every earlier release.
- Text now scales cleanly above 100% instead of being snapped to the pixel
  grid.
- Icons come from Windows' own icon font, so they stay sharp at every scale.
- Removes the application name and icon from inside the window, where they
  duplicated the title bar.

## 2.1.1

- Ships the SDK inside the release archive, at `ph_ft/sdk/`, so one download
  serves both players and script authors.

## 2.1.0

- Adds `sdk/`, the script authoring kit: the Lua API reference, two templates,
  four worked examples, language-server annotations, the Bridge path and event
  catalog, and the validation and packaging tools.

## 2.0.0

First release under the DLTBRuntimeCrane name, continuing LuaHost 1.0.x.

## 1.0.2

- Adds `bridge.list(prefix)`, returning matching Bridge state paths and the
  total match count.
- Makes the reflected families enumerable from Lua, including the 3111 named
  PlayerVariables as `var.<Name>`.
- Requires the `operation:state.enumerate` Bridge feature.
- Listings past the 4096-record ceiling return what was retrieved, report the
  real total, and log the shortfall.

## 1.0.1

No changelog recorded.

## 1.0.0

First release, as the LuaHost optional file on the DLTBRuntimeBridge page.

- Read-only Lua 5.4 client: state queries, path descriptions, scoped event
  observation, merged logging, hot-reloaded scripts, and a named-pipe console.
- Caps scripts at 100,000 instructions per call and 2 MiB of interpreter memory.
- Includes the Lua 5.4 MIT license required by the statically linked
  interpreter.
