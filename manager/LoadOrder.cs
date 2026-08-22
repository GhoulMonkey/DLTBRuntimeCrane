// SPDX-License-Identifier: GPL-3.0-only
// The load-order move, separated from the gesture that asks for it.
//
// The drag and the Load earlier / Load later buttons end in the same operation
// on the same list. That operation is index arithmetic with an off-by-one in
// it: a row dropped below its own position is one place too far once the row
// has been taken out. A test cannot drive a drag, so the arithmetic lives here
// as a pure function over the manifest list, where TestManifest can reach it.

using System.Collections.Generic;

namespace CraneLoader
{
    public static class LoadOrder
    {
        /*
         * Moves `moved` so it lands at `target`, an index into `entries` as it
         * stands before the move, which is the list the caller has on screen.
         *
         * Returns false and changes nothing when the move is a no-op. Dropping a
         * row on its own top edge and on the top edge of the row beneath it are
         * different targets that both mean "leave it where it is", and both have
         * to be refused: a caller that reports "moved" and rewrites the manifest
         * for either one is teaching the user its own readout is unreliable.
         */
        public static bool Move(List<ScriptEntry> entries, ScriptEntry moved, int target)
        {
            if (entries == null || moved == null) return false;
            int from = entries.IndexOf(moved);
            if (from < 0) return false;
            if (target < 0 || target > entries.Count) return false;

            // Taking the entry out shifts everything after it down one place, so
            // a target past the entry's own position is one too many by the time
            // the insert happens.
            int to = target > from ? target - 1 : target;
            if (to == from) return false;

            entries.RemoveAt(from);
            entries.Insert(to, moved);
            return true;
        }

        /*
         * Where an entry ends up, for a caller that needs to say so afterwards.
         * Separate from Move because Move's own return value answers a different
         * question and overloading it with a position would lose "nothing
         * happened".
         */
        public static int PositionOf(List<ScriptEntry> entries, ScriptEntry moved)
        {
            return entries == null || moved == null ? -1 : entries.IndexOf(moved);
        }

        /*
         * The boundary: the index one past the last enabled entry, ignoring
         * `except`.
         *
         * Scanned rather than counted. Counting gives the same answer only while
         * the enabled entries are a prefix, and the manifest is a text file a
         * user can edit, so the two disagree precisely on an interleaved list.
         */
        public static int Boundary(List<ScriptEntry> entries, ScriptEntry except)
        {
            if (entries == null) return 0;
            int boundary = 0;
            for (int i = 0; i < entries.Count; i++)
                if (!ReferenceEquals(entries[i], except) && entries[i].Enabled)
                    boundary = i + 1;
            return boundary;
        }

        /*
         * Puts an entry on the correct side of that boundary after its tick has
         * changed: last among the enabled, or first among the disabled.
         *
         * Both directions are the same insertion point, so this is one function.
         * Enabling a script puts it after the last enabled one, where it claims
         * after everything already running; that is the safe default for a
         * script the user has not ordered yet. Disabling puts it at the head of
         * the disabled block.
         *
         * Doing only the first half breaks the arrangement: enable three,
         * disable the middle one, and a disabled script sits inside the enabled
         * run. The tick moves the row either way, so "enabled above disabled"
         * holds after every action and not only after the ones that add.
         */
        public static bool Regroup(List<ScriptEntry> entries, ScriptEntry moved)
        {
            if (entries == null || moved == null) return false;
            int from = entries.IndexOf(moved);
            if (from < 0) return false;
            // Already on the right side: leave it where it is. An enabled script
            // third in a run of five is correctly placed, and moving it to the
            // back unconditionally would reorder scripts the user arranged, as a
            // side effect of ticking something else.
            if (Placed(entries, from)) return false;
            return Move(entries, moved, Boundary(entries, moved));
        }

        /*
         * Whether one entry is on the correct side of the boundary.
         *
         * Asked about the single entry whose tick changed, not about the list.
         * Regroup does not sort. A manifest can be hand-edited into an
         * interleaved order, and repairing all of it because the user ticked one
         * script would move rows they did not touch. One tick, one move.
         */
        private static bool Placed(List<ScriptEntry> entries, int at)
        {
            if (entries[at].Enabled)
            {
                for (int i = 0; i < at; i++)
                    if (!entries[i].Enabled) return false;
                return true;
            }
            for (int i = at + 1; i < entries.Count; i++)
                if (entries[i].Enabled) return false;
            return true;
        }

        /*
         * How many entries are enabled.
         *
         * A count, and not interchangeable with `Boundary`, which is an index.
         * They agree only while the enabled entries start at the top, and a
         * manifest written by an earlier version, or edited by hand, can have
         * disabled entries above the run. Using the count where an index
         * belonged printed "Loads 8 of 6" on a manifest with two disabled
         * scripts above six enabled ones.
         *
         * Anything asking where the enabled run ends wants Boundary.
         */
        public static int EnabledCount(List<ScriptEntry> entries)
        {
            int count = 0;
            if (entries != null)
                foreach (ScriptEntry entry in entries) if (entry.Enabled) count++;
            return count;
        }
    }
}
