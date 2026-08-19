"""Builds the Crane release archive.

Split out of the shell packaging script, which embedded it in a heredoc. A
heredoc silently eats one level of escaping, and a build script that runs on
every release is a poor place for a latent quoting bug.

Usage: make_package.py <zip> <asi> <manager-exe> <lua-license> <script>

The archive carries only files nothing ever writes to. DLTBRuntimeCrane.ini,
DLTBRuntimeCrane.manifest.json and scripts\\*.lua are created by CraneManager on
first run rather than packaged, so a mod manager never owns a user's script list
or tuned parameters.
"""

import sys
import zipfile

BASE = "ph_ft/work/bin/x64/"

# CraneManager deploys to ph_ft, NOT beside the ASI.
#
# winmm.dll in ph_ft/work/bin/x64 is Ultimate ASI Loader, not Windows'. Windows
# resolves a DLL from the executable's own directory first, and WPF loads winmm
# for timing -- so a manager sitting in that folder loads the ASI loader, which
# injects every .asi into the manager's process. The Bridge then opens its
# console saying "waiting for the game to load", which looks exactly like the
# game starting. Reported as "launching CraneManager also launches the game".
#
# ph_ft is where the Vortex extension already puts SuperModMerger and UTM, so
# this follows the convention rather than inventing one.
MANAGER_BASE = "ph_ft/"


def main(argv):
    if len(argv) != 6:
        print(__doc__.strip(), file=sys.stderr)
        return 2

    zip_path, asi, manager, license_path, script = argv[1:6]
    payload = [
        (asi, BASE + "DLTBRuntimeCrane.asi"),
        (manager, MANAGER_BASE + "CraneManager.exe"),
        (license_path, BASE + "LICENSE-Lua.txt"),
        # The one bundled script, into the folder CRANE already scans. It is the
        # only packaged file a user is expected to read, and the only one they
        # might reasonably want to edit; a redeploy restores this copy over their
        # changes, while their tuned VALUES survive in the generated manifest.
        (script, BASE + "scripts/quick_hands.lua"),
    ]

    with zipfile.ZipFile(zip_path, "w") as archive:
        for source, arcname in payload:
            # Fixed timestamps so the archive is reproducible, not just the
            # binaries inside it: two builds of identical sources should produce
            # identical zips, or the published hash means nothing.
            info = zipfile.ZipInfo(arcname, date_time=(1980, 1, 1, 0, 0, 0))
            info.compress_type = zipfile.ZIP_DEFLATED
            info.external_attr = 0o644 << 16
            with open(source, "rb") as handle:
                archive.writestr(info, handle.read())

    print("archive written: %d files" % len(payload))
    return 0


if __name__ == "__main__":
    sys.exit(main(sys.argv))
