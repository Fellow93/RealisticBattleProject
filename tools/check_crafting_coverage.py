"""Find smithing piece combinations that no weapon description covers.

Crafting.GenerateCraftedItem gives a crafted item one weapon per WeaponDescription that lists ALL of its used
pieces in AvailablePieces. A combination no description covers yields an item with zero weapons: the smithy's
RefreshStats throws on Weapons.ElementAt(0) (a hard crash as soon as the piece is picked) and XML crafted items
built from it can't attack or block.

RBM's weapon descriptions are appended (XmlLoadingPatches.MergeTwoXmlsPatch), so RBM's copy of each description
deserializes last and REPLACES vanilla's AvailablePieces, while CraftingTemplate pieces only accumulate. Any
piece a game patch or DLC adds after RBM's list was written is selectable but uncovered. This script rebuilds
the merged XML the way MBObjectManager.CreateMergedXmlFile does (per module: apply its XSLT, then merge its XML),
for each supported module setup, and reports the pieces that end up in an unbuildable combination.

Run it after every game patch / DLC update and after editing RBMCombat_weapon_descriptions.xml,
RBMCombat_no_bastard_axes.xml or the RBM_WS_XML crafting XSLTs:

    python tools/check_crafting_coverage.py
    python tools/check_crafting_coverage.py --modules "D:/.../Modules"

Exit code 1 if any RBM setup has an unbuildable combination; 0 otherwise. Requires lxml (pip install lxml).
"""
import argparse
import copy
import glob
import itertools
import os
import sys

try:
    from lxml import etree
except ImportError:
    sys.exit("lxml is required: pip install lxml")

REPO = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))


class Paths:
    def __init__(self, modules, repo):
        self.native = os.path.join(modules, "Native", "ModuleData")
        self.naval = os.path.join(modules, "NavalDLC", "ModuleData")
        self.rbm_xml = os.path.join(repo, "RBMXML")
        self.rbm_ws_xml = os.path.join(repo, "RBM_WS_XML")


def apply_xslt(path, doc):
    return etree.XSLT(etree.parse(path))(doc)


def append_xml(doc, path):
    # RBM's MergeTwoXmlsPatch appends every top-level node of an RBM-tagged file instead of merging by id.
    doc = copy.deepcopy(doc)
    for element in etree.parse(path).getroot():
        if isinstance(element.tag, str):
            doc.getroot().append(copy.deepcopy(element))
    return doc


def build(paths, rbm, naval, rbm_ws):
    wd = etree.parse(os.path.join(paths.native, "weapon_descriptions.xml"))
    ct = etree.parse(os.path.join(paths.native, "crafting_templates.xml"))
    # Module order: Native -> NavalDLC -> RBM -> RBM_WS.
    if naval:
        wd = apply_xslt(os.path.join(paths.naval, "XSLT", "NavalDLC_Native_WeaponDescriptions.xslt"), wd)
        ct = apply_xslt(os.path.join(paths.naval, "XSLT", "NavalDLC_Native_CraftingTemplates.xslt"), ct)
    if rbm:
        wd = append_xml(wd, os.path.join(paths.rbm_xml, "RBMCombat_weapon_descriptions.xml"))
        ct = append_xml(ct, os.path.join(paths.rbm_xml, "RBMCombat_no_bastard_axes.xml"))
    if rbm_ws:
        wd = apply_xslt(os.path.join(paths.rbm_ws_xml, "RBMCombat_WS_WeaponDescriptions.xslt"), wd)
        ct = apply_xslt(os.path.join(paths.rbm_ws_xml, "RBMCombat_WS_CraftingTemplates.xslt"), ct)

    # Duplicate WeaponDescription ids: the last one deserialized replaces AvailablePieces.
    descriptions = {}
    for node in wd.getroot().iter("WeaponDescription"):
        available = node.find("AvailablePieces")
        if available is not None:
            descriptions[node.get("id")] = {p.get("id") for p in available.iter("AvailablePiece")}

    # Duplicate CraftingTemplate ids: pieces accumulate, the last WeaponDescriptions list wins.
    templates = {}
    for node in ct.getroot().iter("CraftingTemplate"):
        template = templates.setdefault(node.get("id"), {"descriptions": [], "pieces": set(), "types": set()})
        ids = [w.get("id") for w in node.iter("WeaponDescription")]
        if ids:
            template["descriptions"] = ids
        template["pieces"] |= {u.get("piece_id") for u in node.iter("UsablePiece")}
        # <PieceDatas><PieceData piece_type=.../> is what CraftingTemplate.BuildOrders deserializes from.
        template["types"] |= {b.get("piece_type") for b in node.iter("PieceData") if b.get("piece_type")}
    return descriptions, templates


def piece_types(paths, rbm, naval, rbm_ws):
    files = [os.path.join(paths.native, "crafting_pieces.xml")]
    if naval:
        files.append(os.path.join(paths.naval, "naval_crafting_pieces.xml"))
    if rbm:
        files += glob.glob(os.path.join(paths.rbm_xml, "*.xml"))
    if rbm_ws:
        files.append(os.path.join(paths.rbm_ws_xml, "RBMCombat_WS_crafting_pieces.xml"))
    types = {}
    for path in files:
        for node in etree.parse(path).getroot().iter("CraftingPiece"):
            types[node.get("id")] = node.get("piece_type")
    return types


def heal(descriptions, template, types):
    # Mirrors RBM/CraftingCoveragePatches.HealUncoveredPieces: a piece in no description of its template is added
    # to the description covering most of the template's pieces.
    ids = template["descriptions"]
    primary = max(ids, key=lambda d: len(template["pieces"] & descriptions.get(d, set())))
    for piece in template["pieces"]:
        if types.get(piece) not in template["types"]:
            continue
        if not any(piece in descriptions.get(d, set()) for d in ids):
            descriptions.setdefault(primary, set()).add(piece)


def count_combos(ids, groups):
    """Enumerates piece groups; returns per piece (broken_combos, total_combos, example_partner)."""
    broken, total, partner = {}, {}, {}
    slot_types = sorted(groups)
    for combo in itertools.product(*[list(groups[t].items()) for t in slot_types]):
        common = frozenset(ids)
        for covering, _ in combo:
            common &= covering
        sizes = [len(pieces) for _, pieces in combo]
        for index, (_, pieces) in enumerate(combo):
            others = 1
            for other_index, size in enumerate(sizes):
                if other_index != index:
                    others *= size
            for piece in pieces:
                total[piece] = total.get(piece, 0) + others
                if not common:
                    broken[piece] = broken.get(piece, 0) + others
                    if piece not in partner:
                        partner[piece] = next(p for i, (_, ps) in enumerate(combo) if i != index for p in ps)
    return {p: (broken[p], total[p], partner[p]) for p in broken}


def find_broken(paths, rbm, naval, rbm_ws, healed):
    """Returns {template: (always, partial)}: pieces that crash with any other parts, as (piece, type), and
    pieces that crash only with some other parts, as (piece, type, broken_combos, total_combos, example_partner)."""
    descriptions, templates = build(paths, rbm, naval, rbm_ws)
    types = piece_types(paths, rbm, naval, rbm_ws)
    result = {}
    for template_id, template in templates.items():
        if healed:
            heal(descriptions, template, types)
        ids = template["descriptions"]
        # Pieces with the same type and the same covering descriptions behave identically, so enumerate groups.
        groups = {}
        for piece in template["pieces"]:
            piece_type = types.get(piece)
            if piece_type is None or piece_type not in template["types"]:
                continue  # removed from the game, or a type this template doesn't build
            covering = frozenset(d for d in ids if piece in descriptions.get(d, set()))
            groups.setdefault(piece_type, {}).setdefault(covering, []).append(piece)
        if not groups:
            # Nothing to enumerate means the parse went wrong, not that the template is clean.
            raise RuntimeError(f"template {template_id}: no usable pieces found, check the XML parsing")

        # A piece that crashes with everything makes every piece it can pair with look partly broken. Peel those
        # off first, then re-enumerate so the partial list only holds genuine pairwise conflicts.
        always = []
        while True:
            counts = count_combos(ids, groups)
            peeled = [p for p, (bad, total, _) in counts.items() if bad == total]
            if not peeled:
                break
            always += [(p, types[p]) for p in peeled]
            for piece_type in list(groups):
                for covering in list(groups[piece_type]):
                    groups[piece_type][covering] = [p for p in groups[piece_type][covering] if p not in peeled]
                    if not groups[piece_type][covering]:
                        del groups[piece_type][covering]
            if any(not by_covering for by_covering in groups.values()):
                counts = {}  # a whole slot is unbuildable; the always list already says so
                break
        partial = sorted(((p, types[p]) + counts[p] for p in counts), key=lambda r: (-r[2] / r[3], r[0]))
        if always or partial:
            result[template_id] = (sorted(always), partial)
    return result


def report(label, broken):
    if not broken:
        print(f"  {label}: OK")
        return
    print(f"  {label}: BROKEN")
    for template_id, (always, partial) in broken.items():
        print(f"    {template_id}:")
        if always:
            print("      crash with ANY other parts: " + ", ".join(f"{p} ({t})" for p, t in always))
        for piece, piece_type, bad, total, partner in partial:
            print(f"      {piece} ({piece_type}): {bad}/{total} combos, e.g. with {partner}")


def main():
    parser = argparse.ArgumentParser(description=__doc__.split("\n\n")[0])
    parser.add_argument("--modules", default=os.path.dirname(REPO),
                        help="Bannerlord Modules folder (default: this repo's parent)")
    parser.add_argument("--repo", default=REPO,
                        help="RBM checkout whose RBMXML/ and RBM_WS_XML/ are checked (default: this repo), "
                             "e.g. a git worktree of an older commit")
    args = parser.parse_args()
    paths = Paths(args.modules, args.repo)
    if not os.path.isdir(paths.native):
        sys.exit(f"Native module not found under {args.modules}; pass --modules")
    has_naval = os.path.isdir(paths.naval)
    if not has_naval:
        print("NavalDLC not installed: skipping War Sails setups")

    # (label, rbm, naval, rbm_ws)
    setups = [("Vanilla", False, False, False), ("RBM", True, False, False)]
    if has_naval:
        setups += [("Vanilla + War Sails", False, True, False),
                   ("RBM + War Sails + RBM_WS", True, True, True),
                   ("RBM + War Sails, no RBM_WS", True, True, False)]

    failed = False
    for healed in (False, True):
        print("With CraftingCoveragePatches healing:" if healed else "XML only (no runtime healing):")
        for label, rbm, naval, rbm_ws in setups:
            # RBM's XML never covers War Sails pieces on its own (that is RBM_WS's job), so that setup relies on
            # the runtime heal and only fails the check once healed. Every other RBM setup must be clean in XML.
            expected = not healed and naval and not rbm_ws
            broken = find_broken(paths, rbm, naval, rbm_ws, healed)
            report(label + (" (expected without healing)" if expected and broken else ""), broken)
            if broken and rbm and not expected:
                failed = True
        print()
    if failed:
        print("FAIL: add the listed pieces to the right <WeaponDescription> in RBMXML/RBMCombat_weapon_descriptions.xml "
              "(or to RBM_WS_XML/RBMCombat_WS_WeaponDescriptions.xslt for War Sails pieces).")
    return 1 if failed else 0


if __name__ == "__main__":
    sys.exit(main())
