# SPDX-License-Identifier: GPL-3.0-only
"""Builds the Crane release archive.

Split out of the shell packaging script, which embedded it in a heredoc. A
heredoc silently eats one level of escaping, and a build script that runs on
every release is a poor place for a latent quoting bug.

Usage: make_package.py <zip> <version> <asi> <manager-exe> <gpl-license>
                       <lua-license> <script> [sdk-dir]

The archive carries only files nothing ever writes to. DLTBRuntimeCrane.ini,
DLTBRuntimeCrane.manifest.json and scripts\\*.lua are created by CraneLoader on
first run rather than packaged, so a mod manager never owns a user's script list
or tuned parameters.

An sdk-dir is added under ph_ft/sdk/, so the release is one download for players
and script authors alike. Those files are inert: nothing loads them, and they sit
beside CraneLoader rather than in the game root.
"""

import pathlib
import sys
import zipfile

BASE = "ph_ft/work/bin/x64/"

# Everything the SDK folder holds except its own build outputs. A built archive
# inside an archive helps nobody, and the checker is wanted -- it is what
# validate.ps1 runs.
SDK_BASE = "ph_ft/sdk/"
SDK_SKIP_DIRS = {"package"}

# CraneLoader deploys to ph_ft, NOT beside the ASI.
#
# winmm.dll in ph_ft/work/bin/x64 is Ultimate ASI Loader, not Windows'. Windows
# resolves a DLL from the executable's own directory first, and WPF loads winmm
# for timing -- so a manager sitting in that folder loads the ASI loader, which
# injects every .asi into the manager's process. The Bridge then opens its
# console saying "waiting for the game to load", which looks exactly like the
# game starting. Reported as "launching CraneLoader also launches the game".
#
# ph_ft is where the Vortex extension already puts SuperModMerger and UTM, so
# this follows the convention rather than inventing one.
MANAGER_BASE = "ph_ft/"


def sdk_payload(sdk_dir):
    """Every SDK file, sorted, so two builds of one tree produce one archive."""
    root = pathlib.Path(sdk_dir)
    if not root.is_dir():
        print("sdk directory not found: %s" % sdk_dir, file=sys.stderr)
        sys.exit(2)
    out = []
    for path in sorted(root.rglob("*")):
        relative = path.relative_to(root)
        if not path.is_file() or SDK_SKIP_DIRS & set(relative.parts):
            continue
        out.append((str(path), SDK_BASE + str(relative).replace("\\", "/")))
    if not out:
        print("sdk directory is empty: %s" % sdk_dir, file=sys.stderr)
        sys.exit(2)
    return out


REPOSITORY = "https://github.com/GhoulMonkey/DLTBRuntimeCrane"

SOURCE_NOTICE = """DLTBRuntimeCrane %s -- where to get the source

DLTBRuntimeCrane.asi and CraneLoader.exe are licensed under the GNU General
Public License version 3.0 only. The full terms are in LICENSE-CRANE.txt,
beside this file.

The complete corresponding source for both binaries, together with the scripts
that build them, is published at:

    %s

at the commit tagged v%s. The repository is public; no charge or account is
required to download it.

Not everything is under the GPL. The script authoring kit, the Bridge client
headers it documents, and the example and bundled Lua scripts are MIT licensed,
and may be used in a script or client under any terms. Each source file names
its own terms on an SPDX-License-Identifier line.

Lua 5.4 is statically linked under its own MIT license; see LICENSE-Lua.txt.
"""


def source_notice(version):
    """The GPL section 6 directions, shipped alongside the binaries."""
    return SOURCE_NOTICE % (version, REPOSITORY, version)


def main(argv):
    if len(argv) not in (8, 9):
        print(__doc__.strip(), file=sys.stderr)
        return 2

    zip_path, version, asi, manager, gpl_license, lua_license, script = argv[1:8]
    payload = [
        (asi, BASE + "DLTBRuntimeCrane.asi"),
        (manager, MANAGER_BASE + "CraneLoader.exe"),
        # The GPL covers both binaries. Section 6 requires its terms and the
        # location of the corresponding source to accompany the object code, so
        # both ship inside the archive rather than on the mod page.
        (gpl_license, BASE + "LICENSE-CRANE.txt"),
        (lua_license, BASE + "LICENSE-Lua.txt"),
        # The one bundled script, into the folder CRANE already scans. It is the
        # only packaged file a user is expected to read, and the only one they
        # might reasonably want to edit; a redeploy restores this copy over their
        # changes, while their tuned VALUES survive in the generated manifest.
        (script, BASE + "scripts/quick_hands.lua"),
    ]
    if len(argv) == 9:
        payload += sdk_payload(argv[8])
    generated = [(source_notice(version), BASE + "SOURCE-CRANE.txt")]

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
        for text, arcname in generated:
            info = zipfile.ZipInfo(arcname, date_time=(1980, 1, 1, 0, 0, 0))
            info.compress_type = zipfile.ZIP_DEFLATED
            info.external_attr = 0o644 << 16
            archive.writestr(info, text.replace("\n", "\r\n").encode("utf-8"))

    print("archive written: %d files" % (len(payload) + len(generated)))
    return 0


if __name__ == "__main__":
    sys.exit(main(sys.argv))
