"""Minimal, dependency-free ECMA-335 (.NET metadata) reader.

Just enough to compare the API surface a compiled mod uses with the assemblies
of an installed game: types, members, signatures, custom attributes, user
strings and IL call targets. It is read-only and never loads or runs any of
the assemblies it inspects."""
import struct
import sys

# ---- table schema -----------------------------------------------------------
CODED = {
    'TypeDefOrRef': (2, [2, 1, 27]),
    'HasConstant': (2, [4, 8, 23]),
    'HasCustomAttribute': (5, [6, 4, 1, 2, 8, 9, 10, 0, 14, 23, 20, 17, 26, 27, 32, 35, 38, 39, 40, 42, 44, 43]),
    'HasFieldMarshal': (1, [4, 8]),
    'HasDeclSecurity': (2, [2, 6, 32]),
    'MemberRefParent': (3, [2, 1, 26, 6, 27]),
    'HasSemantics': (1, [20, 23]),
    'MethodDefOrRef': (1, [6, 10]),
    'MemberForwarded': (1, [4, 6]),
    'Implementation': (2, [38, 35, 39]),
    'CustomAttributeType': (3, [None, None, 6, 10, None]),
    'ResolutionScope': (2, [0, 26, 35, 1]),
    'TypeOrMethodDef': (1, [2, 6]),
}

S, G, B = 'str', 'guid', 'blob'


def I(t):
    return ('idx', t)


def C(n):
    return ('coded', n)


SCHEMA = {
    0: [('Generation', 'u16'), ('Name', S), ('Mvid', G), ('EncId', G), ('EncBaseId', G)],
    1: [('Scope', C('ResolutionScope')), ('Name', S), ('Namespace', S)],
    2: [('Flags', 'u32'), ('Name', S), ('Namespace', S), ('Extends', C('TypeDefOrRef')), ('FieldList', I(4)), ('MethodList', I(6))],
    3: [('Field', I(4))],
    4: [('Flags', 'u16'), ('Name', S), ('Sig', B)],
    5: [('Method', I(6))],
    6: [('RVA', 'u32'), ('ImplFlags', 'u16'), ('Flags', 'u16'), ('Name', S), ('Sig', B), ('ParamList', I(8))],
    7: [('Param', I(8))],
    8: [('Flags', 'u16'), ('Sequence', 'u16'), ('Name', S)],
    9: [('Class', I(2)), ('Interface', C('TypeDefOrRef'))],
    10: [('Class', C('MemberRefParent')), ('Name', S), ('Sig', B)],
    11: [('Type', 'u16'), ('Parent', C('HasConstant')), ('Value', B)],
    12: [('Parent', C('HasCustomAttribute')), ('Type', C('CustomAttributeType')), ('Value', B)],
    13: [('Parent', C('HasFieldMarshal')), ('NativeType', B)],
    14: [('Action', 'u16'), ('Parent', C('HasDeclSecurity')), ('PermissionSet', B)],
    15: [('PackingSize', 'u16'), ('ClassSize', 'u32'), ('Parent', I(2))],
    16: [('Offset', 'u32'), ('Field', I(4))],
    17: [('Sig', B)],
    18: [('Parent', I(2)), ('EventList', I(20))],
    19: [('Event', I(20))],
    20: [('Flags', 'u16'), ('Name', S), ('EventType', C('TypeDefOrRef'))],
    21: [('Parent', I(2)), ('PropertyList', I(23))],
    22: [('Property', I(23))],
    23: [('Flags', 'u16'), ('Name', S), ('Type', B)],
    24: [('Semantics', 'u16'), ('Method', I(6)), ('Association', C('HasSemantics'))],
    25: [('Class', I(2)), ('Body', C('MethodDefOrRef')), ('Decl', C('MethodDefOrRef'))],
    26: [('Name', S)],
    27: [('Sig', B)],
    28: [('Flags', 'u16'), ('Member', C('MemberForwarded')), ('ImportName', S), ('ImportScope', I(26))],
    29: [('RVA', 'u32'), ('Field', I(4))],
    30: [('Token', 'u32'), ('Func', 'u32')],
    31: [('Token', 'u32')],
    32: [('HashAlg', 'u32'), ('Major', 'u16'), ('Minor', 'u16'), ('Build', 'u16'), ('Rev', 'u16'), ('Flags', 'u32'), ('PublicKey', B), ('Name', S), ('Culture', S)],
    33: [('Processor', 'u32')],
    34: [('OSPlatform', 'u32'), ('OSMajor', 'u32'), ('OSMinor', 'u32')],
    35: [('Major', 'u16'), ('Minor', 'u16'), ('Build', 'u16'), ('Rev', 'u16'), ('Flags', 'u32'), ('PublicKeyOrToken', B), ('Name', S), ('Culture', S), ('Hash', B)],
    36: [('Processor', 'u32'), ('AssemblyRef', I(35))],
    37: [('OSPlatform', 'u32'), ('OSMajor', 'u32'), ('OSMinor', 'u32'), ('AssemblyRef', I(35))],
    38: [('Flags', 'u32'), ('Name', S), ('Hash', B)],
    39: [('Flags', 'u32'), ('TypeDefId', 'u32'), ('Name', S), ('Namespace', S), ('Implementation', C('Implementation'))],
    40: [('Offset', 'u32'), ('Flags', 'u32'), ('Name', S), ('Implementation', C('Implementation'))],
    41: [('Nested', I(2)), ('Enclosing', I(2))],
    42: [('Number', 'u16'), ('Flags', 'u16'), ('Owner', C('TypeOrMethodDef')), ('Name', S)],
    43: [('Method', C('MethodDefOrRef')), ('Instantiation', B)],
    44: [('Owner', I(42)), ('Constraint', C('TypeDefOrRef'))],
}

ELEMENT = {0x01: 'void', 0x02: 'bool', 0x03: 'char', 0x04: 'sbyte', 0x05: 'byte', 0x06: 'short', 0x07: 'ushort',
           0x08: 'int', 0x09: 'uint', 0x0a: 'long', 0x0b: 'ulong', 0x0c: 'float', 0x0d: 'double', 0x0e: 'string',
           0x16: 'typedref', 0x18: 'nint', 0x19: 'nuint', 0x1c: 'object'}


def read_compressed(b, i):
    x = b[i]
    if x & 0x80 == 0:
        return x, i + 1
    if x & 0xC0 == 0x80:
        return ((x & 0x3F) << 8) | b[i + 1], i + 2
    return ((x & 0x1F) << 24) | (b[i + 1] << 16) | (b[i + 2] << 8) | b[i + 3], i + 4


class Asm:
    def __init__(self, path):
        self.path = path
        d = self.data = open(path, 'rb').read()
        lfa = struct.unpack_from('<I', d, 0x3C)[0]
        assert d[lfa:lfa + 4] == b'PE\0\0', 'not a PE file'
        coff = lfa + 4
        nsec, = struct.unpack_from('<H', d, coff + 2)
        optsize, = struct.unpack_from('<H', d, coff + 16)
        opt = coff + 20
        magic, = struct.unpack_from('<H', d, opt)
        dd = opt + (96 if magic == 0x10B else 112)
        cli_rva, _ = struct.unpack_from('<II', d, dd + 14 * 8)
        so = opt + optsize
        self.sections = []
        for k in range(nsec):
            _, vs, va, rs, rp = struct.unpack_from('<8sIIII', d, so + k * 40)
            self.sections.append((va, vs, rp, rs))
        cli = self.off(cli_rva)
        md_rva, md_size = struct.unpack_from('<II', d, cli + 8)
        md = self.off(md_rva)
        assert struct.unpack_from('<I', d, md)[0] == 0x424A5342
        vlen, = struct.unpack_from('<I', d, md + 12)
        p = md + 16 + vlen
        _, ns = struct.unpack_from('<HH', d, p)
        p += 4
        streams = {}
        for _ in range(ns):
            so_, sz = struct.unpack_from('<II', d, p)
            end = d.index(b'\0', p + 8)
            name = d[p + 8:end].decode()
            p += 8 + ((len(name) + 1 + 3) & ~3)
            streams[name] = (md + so_, sz)
        self.str_off = streams['#Strings'][0]
        self.blob_off = streams['#Blob'][0]
        self.us_off, self.us_size = streams.get('#US', (0, 0))
        t = streams.get('#~') or streams['#-']
        t = t[0]
        heap = d[t + 6]
        valid, = struct.unpack_from('<Q', d, t + 8)
        self.ss = 4 if heap & 1 else 2
        self.gs = 4 if heap & 2 else 2
        self.bs = 4 if heap & 4 else 2
        p = t + 24
        self.rows = {}
        for i in range(64):
            if valid >> i & 1:
                self.rows[i], = struct.unpack_from('<I', d, p)
                p += 4
        self.tab_off = {}
        self.row_size = {}
        self.layout = {}
        for tid in sorted(self.rows):
            lay = []
            size = 0
            for _, kind in SCHEMA[tid]:
                w, fmt = self._col(kind)
                lay.append((size, w, fmt))
                size += w
            self.layout[tid] = lay
            self.row_size[tid] = size
            self.tab_off[tid] = p
            p += size * self.rows[tid]
        self._tables = {}
        self._str = {}
        self._types = None

    def off(self, rva):
        for va, vs, rp, rs in self.sections:
            if va <= rva < va + max(vs, rs):
                return rp + rva - va
        raise ValueError('rva')

    def _col(self, kind):
        if kind == 'u16':
            return 2, 'H'
        if kind == 'u32':
            return 4, 'I'
        if kind == S:
            return self.ss, 'H' if self.ss == 2 else 'I'
        if kind == G:
            return self.gs, 'H' if self.gs == 2 else 'I'
        if kind == B:
            return self.bs, 'H' if self.bs == 2 else 'I'
        if kind[0] == 'idx':
            w = 2 if self.rows.get(kind[1], 0) < 65536 else 4
            return w, 'H' if w == 2 else 'I'
        bits, tabs = CODED[kind[1]]
        mx = max(self.rows.get(t, 0) for t in tabs if t is not None)
        w = 2 if mx < (1 << (16 - bits)) else 4
        return w, 'H' if w == 2 else 'I'

    def table(self, tid):
        """Rows as tuples; index 0 is a dummy so row numbers are 1-based."""
        if tid in self._tables:
            return self._tables[tid]
        out = [None]
        n = self.rows.get(tid, 0)
        if n:
            base = self.tab_off[tid]
            rs = self.row_size[tid]
            lay = self.layout[tid]
            d = self.data
            for r in range(n):
                o = base + r * rs
                row = []
                for off, w, fmt in lay:
                    row.append(struct.unpack_from('<' + fmt, d, o + off)[0])
                out.append(tuple(row))
        self._tables[tid] = out
        return out

    def s(self, off):
        v = self._str.get(off)
        if v is None:
            a = self.str_off + off
            v = self.data[a:self.data.index(b'\0', a)].decode('utf-8', 'replace')
            self._str[off] = v
        return v

    def blob(self, off):
        a = self.blob_off + off
        n, a2 = read_compressed(self.data, a)
        return self.data[a2:a2 + n]

    def user_strings(self):
        """yield (offset, text) for every #US heap entry"""
        i = 1
        while i < self.us_size:
            n, j = read_compressed(self.data, self.us_off + i)
            start = j
            total_hdr = j - (self.us_off + i)
            if n == 0:
                i += total_hdr
                continue
            raw = self.data[start:start + n - 1]
            yield i, raw.decode('utf-16-le', 'replace')
            i += total_hdr + n

    def il_body(self, mrow):
        md = self.table(6)[mrow]
        rva = md[0]
        if not rva:
            return b''
        o = self.off(rva)
        b0 = self.data[o]
        if b0 & 3 == 2:
            return self.data[o + 1:o + 1 + (b0 >> 2)]
        flags, = struct.unpack_from('<H', self.data, o)
        hdr = (flags >> 12) * 4
        code, = struct.unpack_from('<I', self.data, o + 4)
        return self.data[o + hdr:o + hdr + code]

    def decode_coded(self, kind, value):
        bits, tabs = CODED[kind]
        tag = value & ((1 << bits) - 1)
        return tabs[tag], value >> bits

    # ---- names ----------------------------------------------------------------
    def typedef_name(self, row):
        td = self.table(2)[row]
        nm, ns = self.s(td[1]), self.s(td[2])
        nested = self.nested_map().get(row)
        if nested:
            return self.typedef_name(nested) + '/' + nm
        return (ns + '.' if ns else '') + nm

    def typeref_name(self, row):
        tr = self.table(1)[row]
        nm, ns = self.s(tr[1]), self.s(tr[2])
        t, r = self.decode_coded('ResolutionScope', tr[0])
        if t == 1 and r:
            return self.typeref_name(r) + '/' + nm
        return (ns + '.' if ns else '') + nm

    def typeref_scope(self, row):
        tr = self.table(1)[row]
        t, r = self.decode_coded('ResolutionScope', tr[0])
        while t == 1 and r:
            tr = self.table(1)[r]
            t, r = self.decode_coded('ResolutionScope', tr[0])
        if t == 35:
            return self.s(self.table(35)[r][6])
        return '?'

    def nested_map(self):
        if not hasattr(self, '_nm'):
            self._nm = {n: e for _, n, e in ((0, r[0], r[1]) for r in self.table(41)[1:])}
        return self._nm

    def tdr_name(self, coded):
        t, r = self.decode_coded('TypeDefOrRef', coded)
        if t == 2:
            return self.typedef_name(r)
        if t == 1:
            return self.typeref_name(r)
        if t == 27:
            sig = self.blob(self.table(27)[r][0])
            return self.decode_type(sig, 0)[0]
        return '?'

    # ---- signatures -------------------------------------------------------------
    def decode_type(self, b, i):
        t = b[i]
        i += 1
        if t in ELEMENT:
            return ELEMENT[t], i
        if t == 0x0F:
            s, i = self.decode_type(b, i)
            return s + '*', i
        if t == 0x10:
            s, i = self.decode_type(b, i)
            return s + '&', i
        if t in (0x11, 0x12):
            tok, i = read_compressed(b, i)
            return self.tdr_name(tok), i
        if t == 0x13:
            n, i = read_compressed(b, i)
            return '!%d' % n, i
        if t == 0x1E:
            n, i = read_compressed(b, i)
            return '!!%d' % n, i
        if t == 0x1D:
            s, i = self.decode_type(b, i)
            return s + '[]', i
        if t == 0x14:
            s, i = self.decode_type(b, i)
            rank, i = read_compressed(b, i)
            ns_, i = read_compressed(b, i)
            for _ in range(ns_):
                _, i = read_compressed(b, i)
            nl, i = read_compressed(b, i)
            for _ in range(nl):
                _, i = read_compressed(b, i)
            return s + '[' + ',' * (rank - 1) + ']', i
        if t == 0x15:
            i += 1
            tok, i = read_compressed(b, i)
            name = self.tdr_name(tok)
            n, i = read_compressed(b, i)
            args = []
            for _ in range(n):
                a, i = self.decode_type(b, i)
                args.append(a)
            return name + '<' + ','.join(args) + '>', i
        if t in (0x1F, 0x20):
            _, i = read_compressed(b, i)
            return self.decode_type(b, i)
        if t in (0x45, 0x41):
            return self.decode_type(b, i)
        if t == 0x1B:
            return 'fnptr', len(b)
        return '?0x%02x' % t, i

    def decode_sig(self, blob):
        """Return dict(kind, static, ret, params) for a method/field signature."""
        i = 0
        cc = blob[i]
        i += 1
        kind = cc & 0x0F
        if kind == 6:
            ty, i = self.decode_type(blob, i)
            return {'kind': 'field', 'type': ty}
        if cc & 0x10:
            _, i = read_compressed(blob, i)
        n, i = read_compressed(blob, i)
        ret, i = self.decode_type(blob, i)
        params = []
        for _ in range(n):
            p, i = self.decode_type(blob, i)
            params.append(p)
        return {'kind': 'method', 'static': not (cc & 0x20), 'ret': ret, 'params': params,
                'generic': bool(cc & 0x10)}

    # ---- type / member queries --------------------------------------------------
    def types(self):
        """dict fullname -> typedef row"""
        if self._types is None:
            self._types = {}
            for r in range(1, self.rows.get(2, 0) + 1):
                self._types[self.typedef_name(r)] = r
        return self._types

    def members_of(self, row):
        tds = self.table(2)
        n = self.rows.get(2, 0)
        td = tds[row]
        m0 = td[5]
        m1 = tds[row + 1][5] if row < n else self.rows.get(6, 0) + 1
        f0 = td[4]
        f1 = tds[row + 1][4] if row < n else self.rows.get(4, 0) + 1
        methods = []
        mt = self.table(6)
        pt = self.table(8)
        np_ = self.rows.get(8, 0)
        for m in range(m0, m1):
            md = mt[m]
            p0 = md[5]
            p1 = mt[m + 1][5] if m < self.rows.get(6, 0) else np_ + 1
            pnames = [self.s(pt[p][2]) for p in range(p0, p1) if pt[p][1] > 0]
            methods.append({'row': m, 'name': self.s(md[3]), 'flags': md[2], 'rva': md[0],
                            'sig': self.decode_sig(self.blob(md[4])), 'pnames': pnames})
        fields = []
        ft = self.table(4)
        for f in range(f0, f1):
            fd = ft[f]
            fields.append({'row': f, 'name': self.s(fd[1]), 'flags': fd[0],
                           'sig': self.decode_sig(self.blob(fd[2]))})
        return methods, fields

    def base_name(self, row):
        td = self.table(2)[row]
        return self.tdr_name(td[3]) if td[3] else None

    def attrs(self):
        """(table, row) -> list of (attribute type name, blob, ctor parameter types)"""
        if hasattr(self, '_attrs'):
            return self._attrs
        out = {}
        mrs = self.table(10)
        mds = self.table(6)
        for ca in self.table(12)[1:]:
            ptab, prow = self.decode_coded('HasCustomAttribute', ca[0])
            ctab, crow = self.decode_coded('CustomAttributeType', ca[1])
            tname, params = '?', []
            if ctab == 10:
                kt, kr = self.decode_coded('MemberRefParent', mrs[crow][0])
                if kt == 1:
                    tname = self.typeref_name(kr)
                params = self.decode_sig(self.blob(mrs[crow][2]))['params']
            elif ctab == 6:
                tname = self._declaring_type_of_method(crow)
                params = self.decode_sig(self.blob(mds[crow][4]))['params']
            out.setdefault((ptab, prow), []).append((tname, self.blob(ca[2]), params))
        self._attrs = out
        return out

    def _declaring_type_of_method(self, mrow):
        tds = self.table(2)
        n = self.rows.get(2, 0)
        lo, hi = 1, n
        res = 1
        while lo <= hi:
            mid = (lo + hi) // 2
            if tds[mid][5] <= mrow:
                res = mid
                lo = mid + 1
            else:
                hi = mid - 1
        return self.typedef_name(res)


def sig_str(sig):
    if sig['kind'] == 'field':
        return sig['type']
    return '%s(%s)' % (sig['ret'], ', '.join(sig['params']))


if __name__ == '__main__':
    a = Asm(sys.argv[1])
    print(len(a.types()), 'types;', {k: v for k, v in a.rows.items() if k in (1, 2, 4, 6, 10, 35)})


# ---- IL walking ---------------------------------------------------------------
_OPERAND = {}
for _op in range(0x0E, 0x14):
    _OPERAND[_op] = 1
_OPERAND.update({0x1F: 1, 0x20: 4, 0x21: 8, 0x22: 4, 0x23: 8, 0xDE: 1})
for _op in (0x27, 0x28, 0x29, 0x6F, 0x70, 0x71, 0x72, 0x73, 0x74, 0x75, 0x79, 0x7B, 0x7C, 0x7D, 0x7E, 0x7F, 0x80,
            0x81, 0x8C, 0x8D, 0x8F, 0xA3, 0xA4, 0xA5, 0xC2, 0xC6, 0xD0, 0xDD):
    _OPERAND[_op] = 4
for _op in range(0x2B, 0x38):
    _OPERAND[_op] = 1
for _op in range(0x38, 0x45):
    _OPERAND[_op] = 4
_FE_OPERAND = {0x06: 4, 0x07: 4, 0x09: 2, 0x0A: 2, 0x0B: 2, 0x0C: 2, 0x0D: 2, 0x0E: 2, 0x12: 1, 0x15: 4, 0x16: 4,
               0x19: 1, 0x1C: 4}

OP_CALL, OP_CALLVIRT, OP_NEWOBJ, OP_LDSTR = 0x28, 0x6F, 0x73, 0x72


def iter_il(code):
    """Yield (offset, opcode, operand_bytes). Two-byte opcodes are 0xFE00 | second byte."""
    i = 0
    while i < len(code):
        start = i
        op = code[i]
        i += 1
        if op == 0xFE:
            op = 0xFE00 | code[i]
            i += 1
            size = _FE_OPERAND.get(op & 0xFF, 0)
        elif op == 0x45:
            n, = struct.unpack_from('<I', code, i)
            size = 4 + 4 * n
        else:
            size = _OPERAND.get(op, 0)
        yield start, op, code[i:i + size]
        i += size


def member_name(a, token):
    """Readable 'Type::Member' for a MethodDef/MemberRef/MethodSpec token (best effort)."""
    table, row = token >> 24, token & 0xFFFFFF
    if table == 0x06:
        md = a.table(6)[row]
        return '%s::%s' % (a._declaring_type_of_method(row), a.s(md[3]))
    if table == 0x0A:
        r = a.table(10)[row]
        kind, prow = a.decode_coded('MemberRefParent', r[0])
        if kind == 1:
            parent = a.typeref_name(prow)
        elif kind == 27:
            parent = a.decode_type(a.blob(a.table(27)[prow][0]), 0)[0]
        else:
            parent = '?'
        return '%s::%s' % (parent, a.s(r[1]))
    if table == 0x2B:
        kind, mrow = a.decode_coded('MethodDefOrRef', a.table(43)[row][0])
        return member_name(a, ({6: 0x06, 10: 0x0A}[kind] << 24) | mrow)
    return hex(token)


# ---- custom attribute arguments ---------------------------------------------------
def _ser_string(b, i):
    if b[i] == 0xFF:
        return None, i + 1
    n, i = read_compressed(b, i)
    return b[i:i + n].decode('utf-8', 'replace'), i + n


def attribute_args(sig_params, blob):
    """Decode the fixed arguments of a custom attribute whose ctor takes only
    string/System.Type/bool/int-like parameters. Returns a list, or None if the
    blob uses a shape this reader does not understand."""
    if len(blob) < 2 or blob[0] != 1 or blob[1] != 0:
        return None
    i = 2
    out = []
    for p in sig_params:
        if p in ('string', 'System.Type'):
            v, i = _ser_string(blob, i)
        elif p == 'bool':
            v, i = bool(blob[i]), i + 1
        elif p in ('int', 'uint'):
            v, i = struct.unpack_from('<i', blob, i)[0], i + 4
        else:
            return None
        out.append(v)
    return out
