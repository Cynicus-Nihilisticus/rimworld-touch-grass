#!/usr/bin/env python3
"""Does this compiled mod still fit the installed RimWorld?

Reads (never loads or runs) the mod DLL and the game's assemblies and checks:

  1. every type and member the mod references from the game, Unity and Harmony
     exists with an identical signature;
  2. every override still matches a virtual member of its base class and no new
     abstract member is left unimplemented;
  3. every [HarmonyPatch] target method exists and each patch parameter that
     Harmony injects by name (leftX, curBaseY, ...) still exists on it;
  4. members the mod reaches by reflection (REFLECTION_TARGETS) still exist;
  5. (warning only) Unity API the mod uses that the game's Unity compatibility
     layer implements as a logging stub or by throwing.

Exit status 0 = nothing to fix, 1 = at least one hard failure.

    python -I apicheck.py --dll Build\\Release\\TouchGrass.dll ^
        --game "C:\\...\\RimWorld" --harmony "C:\\...\\1.7\\Assemblies\\0Harmony.dll"
"""
import argparse
import glob
import os
import struct
import sys
from collections import Counter

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
from dnmeta import Asm, OP_CALL, OP_LDSTR, OP_NEWOBJ, attribute_args, iter_il, member_name, sig_str  # noqa: E402

# Private members the mod reads through reflection: (declaring type, member name).
REFLECTION_TARGETS = [
    ('RimWorld.Autosaver', 'ticksSinceSave'),
]

BCL_SCOPES = ('mscorlib', 'System', 'netstandard', 'Microsoft.')
HARMONY_ATTRS = {
    'HarmonyLib.HarmonyPostfix', 'HarmonyLib.HarmonyPrefix',
    'HarmonyLib.HarmonyTranspiler', 'HarmonyLib.HarmonyFinalizer',
}
HARMONY_NAMES = {'Postfix', 'Prefix', 'Transpiler', 'Finalizer'}
ABSTRACT, VIRTUAL, NEWSLOT = 0x400, 0x40, 0x100


def load_targets(game, harmony):
    """label -> Asm for every assembly the mod may legitimately reference."""
    targets = {}
    root = os.path.join(game, 'Assembly-CSharp.dll')
    if os.path.isfile(root):  # 1.7 layout: .NET 10 + UnityStubs compatibility layer
        targets['Assembly-CSharp'] = Asm(root)
        stubs = os.path.join(game, 'UnityStubs.dll')
        if os.path.isfile(stubs):
            targets['UnityStubs'] = Asm(stubs)
    else:  # 1.6 layout: Unity managed folder
        managed = os.path.join(game, 'RimWorldWin64_Data', 'Managed')
        for path in sorted(glob.glob(os.path.join(managed, 'Assembly-CSharp.dll')) +
                           glob.glob(os.path.join(managed, 'UnityEngine*.dll'))):
            targets[os.path.splitext(os.path.basename(path))[0]] = Asm(path)
    if harmony:
        targets['0Harmony'] = Asm(harmony)
    if 'Assembly-CSharp' not in targets:
        raise SystemExit('Assembly-CSharp.dll not found under %s' % game)
    return targets


class Index:
    def __init__(self, targets):
        self.targets = targets
        self.by_name = {}
        for label, asm in targets.items():
            for full, row in asm.types().items():
                self.by_name.setdefault(full, []).append((label, asm, row))

    def find_type(self, full, prefer=None):
        found = self.by_name.get(full)
        if not found:
            return None
        for item in found:
            if item[0] == prefer:
                return item
        return found[0]

    def chain(self, full, prefer=None):
        out = []
        cur = self.find_type(full, prefer)
        while cur:
            out.append(cur)
            base = cur[1].base_name(cur[2])
            cur = self.find_type(base) if base else None
        return out

    def members(self, full, name, prefer=None):
        found = []
        for label, asm, row in self.chain(full, prefer):
            methods, fields = asm.members_of(row)
            found += [(label, asm.typedef_name(row), 'method', m) for m in methods if m['name'] == name]
            found += [(label, asm.typedef_name(row), 'field', f) for f in fields if f['name'] == name]
        return found


def check_references(mod, index, report):
    ok = 0
    sys_types = Counter()
    for r in mod.table(10)[1:]:
        kind, row = mod.decode_coded('MemberRefParent', r[0])
        if kind != 1:  # generic instantiations, module refs: not game API
            continue
        scope, full, name = mod.typeref_scope(row), mod.typeref_name(row), mod.s(r[1])
        if scope.startswith(BCL_SCOPES):
            sys_types[full] += 1
            continue
        want = sig_str(mod.decode_sig(mod.blob(r[2])))
        prefer = scope if scope in index.targets else None
        if not index.find_type(full, prefer):
            report.fail('type %s (from %s) no longer exists' % (full, scope))
            continue
        candidates = index.members(full, name, prefer)
        if not candidates:
            report.fail('%s::%s no longer exists' % (full, name))
        elif any(sig_str(c[3]['sig']) == want for c in candidates):
            ok += 1
        else:
            have = '; '.join(sig_str(c[3]['sig']) for c in candidates)
            report.fail('%s::%s changed. mod calls %s, game has %s' % (full, name, want, have))
    report.info('%d member references match (%d BCL types left to the runtime)' % (ok, len(sys_types)))

    missing = 0
    for row in range(1, mod.rows[1] + 1):
        scope = mod.typeref_scope(row)
        full = mod.typeref_name(row)
        if scope.startswith(BCL_SCOPES) or index.find_type(full):
            continue
        missing += 1
        report.fail('type %s (from %s) no longer exists' % (full, scope))
    if not missing:
        report.info('every referenced game/Unity/Harmony type exists')


def check_overrides(mod, index, report):
    checked = 0
    for trow in range(1, mod.rows[2] + 1):
        base = mod.base_name(trow)
        if not base or base in ('System.Object', 'System.Enum', 'System.ValueType', 'System.Attribute'):
            continue
        full = mod.typedef_name(trow)
        chain = index.chain(base)
        if not chain:
            if not base.startswith(BCL_SCOPES) and not base.startswith('System.'):
                report.fail('%s: base type %s no longer exists' % (full, base))
            continue
        methods, _ = mod.members_of(trow)
        mine = {(m['name'], len(m['sig']['params'])) for m in methods}
        for m in methods:
            if m['flags'] & VIRTUAL and not m['flags'] & NEWSLOT:
                checked += 1
                if not any(bm['name'] == m['name'] and bm['flags'] & VIRTUAL and sig_str(bm['sig']) == sig_str(m['sig'])
                           for _, a, r in chain for bm in a.members_of(r)[0]):
                    report.fail('%s.%s %s overrides nothing in %s' % (full, m['name'], sig_str(m['sig']), base))
        for _, a, r in chain:
            for bm in a.members_of(r)[0]:
                if bm['flags'] & ABSTRACT and (bm['name'], len(bm['sig']['params'])) not in mine:
                    implemented = any(
                        o['name'] == bm['name'] and not o['flags'] & ABSTRACT and
                        len(o['sig']['params']) == len(bm['sig']['params'])
                        for _, a2, r2 in chain for o in a2.members_of(r2)[0])
                    if not implemented:
                        report.fail('%s does not implement abstract %s.%s' % (full, a.typedef_name(r), bm['name']))
    report.info('%d overrides match their base members' % checked)


def parse_type_arg(value):
    return value.split(',')[0].strip() if value else None


def check_harmony(mod, index, report):
    attrs = mod.attrs()
    patches = 0
    for trow in range(1, mod.rows[2] + 1):
        target = None
        for tname, blob, params in attrs.get((2, trow), []):
            if tname == 'HarmonyLib.HarmonyPatch':
                args = attribute_args(params, blob)
                if args is None:
                    report.warn('%s: [HarmonyPatch] shape not understood; target not verified' % mod.typedef_name(trow))
                    continue
                type_name = next((parse_type_arg(a) for p, a in zip(params, args) if p == 'System.Type'), None)
                strings = [a for p, a in zip(params, args) if p == 'string']
                if type_name and strings:
                    target = (type_name, strings[0])
        if not target:
            continue
        patches += 1
        type_name, method_name = target
        candidates = index.members(type_name, method_name)
        candidates = [c for c in candidates if c[2] == 'method']
        if not candidates:
            report.fail('Harmony target %s::%s no longer exists' % target)
            continue
        methods, _ = mod.members_of(trow)
        for m in methods:
            is_patch = m['name'] in HARMONY_NAMES or any(
                t in HARMONY_ATTRS for t, _b, _p in attrs.get((6, m['row']), []))
            if not is_patch:
                continue
            wanted = [n for n in m['pnames'] if not n.startswith('__')]
            if not any(all(n in c[3]['pnames'] for n in wanted) for c in candidates):
                report.fail('Harmony patch %s.%s injects %s but %s::%s has parameters %s' % (
                    mod.typedef_name(trow), m['name'], wanted, type_name, method_name,
                    [c[3]['pnames'] for c in candidates]))
    report.info('%d Harmony patch target(s) found with matching parameter names' % patches)


def check_reflection(index, report):
    for type_name, member in REFLECTION_TARGETS:
        if index.members(type_name, member):
            report.info('reflection target %s.%s exists' % (type_name, member))
        else:
            report.fail('reflection target %s.%s no longer exists' % (type_name, member))


def check_unity_stubs(mod, targets, report):
    stubs = targets.get('UnityStubs')
    if not stubs:
        return
    tokens = {0x70000000 | off for off, text in stubs.user_strings() if 'Stub!' in text}
    bodies = {}
    loggers = set()
    for m in range(1, stubs.rows[6] + 1):
        code = stubs.il_body(m)
        bodies[m] = code
        for _pos, op, operand in iter_il(code):
            if op == OP_LDSTR and struct.unpack('<I', operand)[0] in tokens:
                loggers.add(m)
    logger_tokens = {0x06000000 | m for m in loggers}
    flagged = {}
    for m, code in bodies.items():
        for _pos, op, operand in iter_il(code):
            if op == OP_CALL and struct.unpack('<I', operand)[0] in logger_tokens:
                flagged[m] = 'is a logging stub'
            elif op == OP_NEWOBJ:
                name = member_name(stubs, struct.unpack('<I', operand)[0])
                if name.startswith(('System.NotImplementedException', 'System.NotSupportedException')):
                    flagged[m] = 'throws ' + name.split('::')[0].split('.')[-1]
    declaring = {m: stubs._declaring_type_of_method(m) for m in flagged}
    names = {}
    for m, why in flagged.items():
        names.setdefault((declaring[m], stubs.s(stubs.table(6)[m][3])), why)
    seen = set()
    for r in mod.table(10)[1:]:
        kind, row = mod.decode_coded('MemberRefParent', r[0])
        if kind == 1 and mod.typeref_name(row).startswith(('UnityEngine.', 'Unity.')):
            key = (mod.typeref_name(row), mod.s(r[1]))
            if key in names and key not in seen:
                seen.add(key)
                report.warn('Unity compatibility layer: %s.%s %s' % (key[0], key[1], names[key]))
    if not seen:
        report.info('no Unity API used by the mod is a stub or throws NotImplemented')


class Report:
    def __init__(self):
        self.failures, self.warnings = [], []

    def info(self, text):
        print('  ok   ' + text)

    def warn(self, text):
        self.warnings.append(text)
        print('  warn ' + text)

    def fail(self, text):
        self.failures.append(text)
        print('  FAIL ' + text)


def main():
    ap = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    ap.add_argument('--dll', required=True, help='compiled mod assembly')
    ap.add_argument('--game', required=True, help='RimWorld install folder')
    ap.add_argument('--harmony', help='0Harmony.dll the mod will run against')
    args = ap.parse_args()

    mod = Asm(args.dll)
    targets = load_targets(args.game, args.harmony)
    index = Index(targets)
    ident = []
    for label, asm in targets.items():
        r = asm.table(32)[1]
        ident.append('%s %d.%d.%d.%d' % (label, r[1], r[2], r[3], r[4]))
    print('API check: %s' % os.path.basename(args.dll))
    print('  against: ' + ', '.join(ident))
    refs = [mod.s(r[6]) for r in mod.table(35)[1:]]
    print('  mod binds to: ' + ', '.join(refs))

    report = Report()
    check_references(mod, index, report)
    check_overrides(mod, index, report)
    check_harmony(mod, index, report)
    check_reflection(index, report)
    check_unity_stubs(mod, targets, report)

    if report.failures:
        print('RESULT: %d problem(s) must be fixed before this build can work.' % len(report.failures))
        return 1
    print('RESULT: OK%s' % (' (%d warning(s))' % len(report.warnings) if report.warnings else ''))
    return 0


if __name__ == '__main__':
    sys.stdout.reconfigure(encoding='utf-8', errors='replace')
    sys.exit(main())
