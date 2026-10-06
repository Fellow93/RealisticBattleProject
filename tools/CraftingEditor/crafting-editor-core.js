// Crafting editor: the piece/template model, problem checks, the weapon-stat calculator (the game's crafting math with
// RBM's patches) and the text-splicing export. No DOM; index.html uses it, and it runs under node (module.exports) to
// test an export or the calculator. The XML text helpers (tokenizer, getAttr/setAttr/removeAttr, reindent, ...) come
// from ../TroopLoadoutEditor/troop-loadout-core.js, which a page must load first. See README.md.
(function (root) {
  'use strict';
  var T = (typeof module !== 'undefined' && module.exports) ? require('../TroopLoadoutEditor/troop-loadout-core.js') : root.TroopLoadoutCore;
  var F = Math.fround;

  // ---------------------------------------------------------------- XML text helpers
  // (decode/attrsOf/childInfo/makeTag/insertChild/... are copied from ../ItemEditor/item-editor-core.js)

  var ATTR_SRC = '(\\s+)([A-Za-z_][\\w.:-]*)(\\s*=\\s*)("([^"]*)"|\'([^\']*)\')';
  function decode(s) {
    return s.replace(/&(#x[0-9a-fA-F]+|#\d+|amp|lt|gt|quot|apos);/g, function (m, e) {
      if (e === 'amp') return '&'; if (e === 'lt') return '<'; if (e === 'gt') return '>';
      if (e === 'quot') return '"'; if (e === 'apos') return '\'';
      return String.fromCharCode(e.charAt(1) === 'x' ? parseInt(e.slice(2), 16) : parseInt(e.slice(1), 10));
    });
  }
  // A start tag's attributes, decoded, in document order (a repeated name keeps its last value).
  function attrsOf(tag) {
    var re = new RegExp(ATTR_SRC, 'g'), out = {}, m;
    while ((m = re.exec(tag))) out[m[2]] = decode(m[5] != null ? m[5] : m[6]);
    return out;
  }
  function childInfo(text) {
    var st = T.startTagOf(text);
    if (st.self) return { st: st, kids: [], close: text.length };
    var close = text.lastIndexOf('</');
    return { st: st, kids: T.tokens(text, st.end, close), close: close };
  }
  function elKids(kids, name) { return kids.filter(function (k) { return k.kind === 'el' && (!name || k.name === name); }); }
  function setOrRemove(tag, n, v) { return v == null ? T.removeAttr(tag, n) : T.setAttr(tag, n, v); }
  function patchStartTag(text, ops) {
    if (!ops.length) return text;
    var st = T.startTagOf(text), tag = st.text;
    ops.forEach(function (o) { tag = setOrRemove(tag, o.n, o.v); });
    return tag + text.slice(st.end);
  }
  // The indentation unit of an element's text: a tab if any deeper line starts with one, else the smallest run of
  // spaces deeper lines add to baseIndent.
  function detectUnit(text, baseIndent) {
    var lines = text.split('\n'), min = 0;
    for (var i = 1; i < lines.length; i++) {
      var ws = /^[ \t]*/.exec(lines[i])[0];
      if (ws.length <= baseIndent.length || ws.indexOf(baseIndent) !== 0) continue;
      var rest = ws.slice(baseIndent.length);
      if (rest.charAt(0) === '\t') return '\t';
      var sp = /^ */.exec(rest)[0].length;
      if (sp && (!min || sp < min)) min = sp;
    }
    if (min) return new Array(Math.min(min, 8) + 1).join(' ');
    return T.unitFor(baseIndent);
  }
  // A start tag's element sample from a text: {tag, indent} of the first <name ...> found at the start of a line.
  function sampleIn(text, name) {
    var m = new RegExp('^([ \\t]*)<' + name + '\\b', 'm').exec(text);
    if (!m) return null;
    var st = T.startTagOf(text.slice(m.index + m[1].length));
    return { tag: st.text, indent: m[1], self: st.self };
  }
  // A new element <name a="v" .../> shaped like the sample (separators, quote placement, "/>" vs " />"), at indent.
  // open: an open tag (no "/>"), for a container whose children follow.
  function makeTag(name, list, sample, indent, dflt, open) {
    var sep1 = dflt.sep1, sepN = dflt.sepN, eq = '=', tail = dflt.tail, sIndent = indent;
    if (sample) {
      var re = new RegExp(ATTR_SRC, 'g'), ms = [], m;
      while ((m = re.exec(sample.tag))) ms.push(m);
      if (ms.length) {
        sep1 = ms[0][1]; sepN = ms.length > 1 ? ms[1][1] : sep1; eq = ms[0][3];
        var last = ms[ms.length - 1];
        tail = sample.tag.slice(last.index + last[0].length);
      }
      sIndent = sample.indent;
    }
    if (open) tail = tail.replace(/\s*\/?>$/, '>');
    else if (!/\/>$/.test(tail)) tail = tail.replace(/\s*>$/, dflt.tail || ' />');
    var t = '<' + name + list.map(function (a, i) { return (i ? sepN : sep1) + a[0] + eq + '"' + T.escAttr(a[1]) + '"'; }).join('') + (list.length ? tail : tail.replace(/^\s+/, open ? '' : ' '));
    return sample ? T.reindent(t, sIndent, indent) : t;
  }
  function childIndentOf(text, kids, elIndent, ctx) {
    var first = kids.filter(function (k) { return k.kind !== 'text'; })[0];
    if (first) { var i = T.indentAt(text, first.start); if (i) return i; }
    return elIndent + ctx.unit;
  }
  function defaultShape(elStartTag, childIndent, ctx) {
    var multi = /\n/.test(elStartTag.replace(/\s*\/?>$/, ''));
    var sep = multi ? ctx.eol + childIndent + ctx.unit : ' ';
    return { sep1: sep, sepN: sep, tail: ctx.selfClose };
  }
  // Inserts newText as a child of the element elText (indent elIndent): after the token `after` (a kid of elText),
  // or first (`first` true), or at the end; a self-closing element becomes an open one.
  function insertChild(elText, newText, ctx, elIndent, after, first) {
    var ci = childInfo(elText), childIndent = childIndentOf(elText, ci.kids, elIndent, ctx);
    if (ci.st.self) {
      return ci.st.text.replace(/\s*\/>$/, '>') + ctx.eol + childIndent + newText + ctx.eol + elIndent + '</' + ci.st.name + '>' + elText.slice(ci.st.end);
    }
    if (after) return elText.slice(0, after.end) + ctx.eol + childIndent + newText + elText.slice(after.end);
    var nonText = ci.kids.filter(function (k) { return k.kind !== 'text'; });
    if (first && nonText.length) {
      // Before the first child: on its own line, the child moved down with the same indentation.
      var f = nonText[0], fi = T.indentAt(elText, f.start);
      if (fi || elText.charAt(f.start - 1) === '\n') return elText.slice(0, f.start) + newText + ctx.eol + fi + elText.slice(f.start);
      return ci.st.text + ctx.eol + childIndent + newText + elText.slice(ci.st.end);
    }
    var cs = ci.close, pre = elText.slice(0, cs).replace(/\s*$/, '');
    return pre + ctx.eol + childIndent + newText + ctx.eol + elIndent + elText.slice(cs);
  }
  function removeChildTok(text, tok) {
    var before = text.slice(0, tok.start), ws = /\s*$/.exec(before)[0];
    return before.slice(0, before.length - ws.length) + text.slice(tok.end);
  }
  // Re-indents an element taken from another file: lines starting with oldIndent get newIndent, and leading
  // whole units of the old file's indentation become the new file's unit.
  function restyle(snippet, oldIndent, newIndent, oldUnit, newUnit) {
    var lines = snippet.split('\n');
    for (var i = 1; i < lines.length; i++) {
      var ws = /^[ \t]*/.exec(lines[i])[0];
      if (ws.indexOf(oldIndent) !== 0) continue;
      var rest = ws.slice(oldIndent.length), k = 0;
      if (oldUnit !== newUnit && oldUnit) { while (rest.indexOf(oldUnit) === 0) { rest = rest.slice(oldUnit.length); k++; } }
      lines[i] = newIndent + new Array(k + 1).join(newUnit) + rest + lines[i].slice(ws.length);
    }
    return lines.join('\n');
  }
  function copyMap(o) { var c = {}; Object.keys(o || {}).forEach(function (k) { c[k] = o[k]; }); return c; }
  function pairs(o) { return Object.keys(o || {}).map(function (n) { return [n, o[n]]; }); }
  function fromPairs(p) { var o = {}; (p || []).forEach(function (a) { o[a[0]] = a[1]; }); return o; }

  // ---------------------------------------------------------------- the model
  // What CraftingPiece.Deserialize / CraftingTemplate.Deserialize read (decompiled TaleWorlds.Core):
  //   singles: child elements read by name for their attributes (nested: their own single children);
  //   lists:   a container of items, keyed by an attribute (a repeated key gets "#2", "#3", ...);
  //   multi:   containers that may repeat, told apart by an attribute (StatsData weapon_description).
  var SCHEMA = {
    CraftingPiece: {
      singles: ['BladeData', 'BuildData', 'StatContributions'], nested: { BladeData: ['Thrust', 'Swing'] },
      lists: { Materials: { item: 'Material', key: 'id' }, Flags: { item: 'Flag', key: 'name' }, CraftingTemplates: { item: 'CraftingTemplate', key: 'id' } },
      multi: {}
    },
    CraftingTemplate: {
      singles: [], nested: {},
      lists: { PieceDatas: { item: 'PieceData', key: 'piece_type' }, WeaponDescriptions: { item: 'WeaponDescription', key: 'id' }, UsablePieces: { item: 'UsablePiece', key: 'piece_id' } },
      multi: { StatsData: { keyAttr: 'weapon_description', item: 'StatData', key: 'stat_type' } }
    }
  };
  var EMPTY_SCHEMA = { singles: [], nested: {}, lists: {}, multi: {} };
  function schemaOf(kind) { return SCHEMA[kind] || EMPTY_SCHEMA; }
  function listSpec(kind, path) {
    var sc = schemaOf(kind), name = path.split('|')[0];
    if (sc.lists[name]) return { name: name, item: sc.lists[name].item, key: sc.lists[name].key };
    if (sc.multi[name]) return { name: name, item: sc.multi[name].item, key: sc.multi[name].key, keyAttr: sc.multi[name].keyAttr, kv: path.slice(name.length + 1) };
    return null;
  }
  // An element's text as the editor edits it:
  //   { kind, attrs, els: { 'BladeData': {..}, 'BladeData/Thrust': {..}, ... }, lists: { 'Materials': { attrs, items: [{ k, attrs }] },
  //     'StatsData|<weapon_description>': ... }, other: [names of other children] }. All values are strings as written.
  // A single element written twice keeps the last one (the game reads both in order, the last wins).
  function parseModel(text) {
    var ci = childInfo(text), kind = ci.st.name, sc = schemaOf(kind);
    var m = { kind: kind, attrs: attrsOf(ci.st.text), els: {}, lists: {}, other: [] };
    elKids(ci.kids).forEach(function (k) {
      var kt = text.slice(k.start, k.end);
      if (sc.singles.indexOf(k.name) >= 0) {
        m.els[k.name] = attrsOf(T.startTagOf(kt).text);
        (sc.nested[k.name] || []).forEach(function (sub) {
          var kk = elKids(childInfo(kt).kids, sub);
          if (kk.length) { var last = kk[kk.length - 1]; m.els[k.name + '/' + sub] = attrsOf(T.startTagOf(kt.slice(last.start, last.end)).text); }
        });
        return;
      }
      var ls = sc.lists[k.name], mu = sc.multi[k.name];
      if (ls || mu) {
        var spec = ls || mu, ca = attrsOf(T.startTagOf(kt).text);
        var path = mu ? k.name + '|' + (ca[mu.keyAttr] == null ? '' : ca[mu.keyAttr]) : k.name;
        var items = [], cnt = {};
        elKids(childInfo(kt).kids, spec.item).forEach(function (it) {
          var a = attrsOf(T.startTagOf(kt.slice(it.start, it.end)).text), kv = a[spec.key] == null ? '' : a[spec.key];
          cnt[kv] = (cnt[kv] || 0) + 1;
          items.push({ k: kv + (cnt[kv] > 1 ? '#' + cnt[kv] : ''), attrs: a });
        });
        m.lists[path] = { attrs: ca, items: items };
        return;
      }
      m.other.push(k.name);
    });
    return m;
  }
  function cloneModel(m) {
    var c = { kind: m.kind, attrs: copyMap(m.attrs), els: {}, lists: {}, other: (m.other || []).slice() };
    Object.keys(m.els).forEach(function (p) { c.els[p] = copyMap(m.els[p]); });
    Object.keys(m.lists).forEach(function (p) {
      c.lists[p] = { attrs: copyMap(m.lists[p].attrs), items: m.lists[p].items.map(function (i) { return { k: i.k, attrs: copyMap(i.attrs) }; }) };
    });
    return c;
  }
  // Changed attributes between two maps: [{n, v}], v null = removed. a's order, then b's new names.
  function diffMaps(a, b) {
    var out = [];
    Object.keys(a).forEach(function (n) { var v = b[n] === undefined ? null : b[n]; if (a[n] !== v) out.push({ n: n, v: v }); });
    Object.keys(b).forEach(function (n) { if (a[n] === undefined && b[n] != null) out.push({ n: n, v: b[n] }); });
    return out;
  }
  function depth(p) { return p.split('/').length; }
  function union(a, b) { var o = {}; a.concat(b).forEach(function (x) { o[x] = 1; }); return Object.keys(o); }
  function itemIndex(list, k) { for (var i = 0; i < list.items.length; i++) if (list.items[i].k === k) return i; return -1; }
  // The edit from a to b as operations:
  //   {p:'attr', n, v}                          start-tag attribute (v null = removed)
  //   {p:'el', s, on, attrs: [[n, v]]}          single element s ('BladeData', 'BladeData/Thrust', ...) added/removed
  //   {p:'elAttr', s, n, v}                     its attribute
  //   {p:'list', l, on, attrs, items: [[k, [[n, v]]]]}   list container l added/removed
  //   {p:'listAttr', l, n, v}                   container attribute
  //   {p:'item', l, k, on, attrs}               list item added (at the end) / removed
  //   {p:'itemAttr', l, k, n, v}                list item attribute
  function diff(a, b) {
    var ops = [];
    diffMaps(a.attrs, b.attrs).forEach(function (d) { ops.push({ p: 'attr', n: d.n, v: d.v }); });
    union(Object.keys(a.els), Object.keys(b.els)).sort(function (x, y) { return depth(x) - depth(y) || (x < y ? -1 : 1); }).forEach(function (s) {
      var x = a.els[s], y = b.els[s];
      if (x && !y) ops.push({ p: 'el', s: s, on: false });
      else if (!x && y) ops.push({ p: 'el', s: s, on: true, attrs: pairs(y) });
      else diffMaps(x, y).forEach(function (d) { ops.push({ p: 'elAttr', s: s, n: d.n, v: d.v }); });
    });
    union(Object.keys(a.lists), Object.keys(b.lists)).sort().forEach(function (l) {
      var x = a.lists[l], y = b.lists[l];
      if (x && !y) { ops.push({ p: 'list', l: l, on: false }); return; }
      if (!x && y) { ops.push({ p: 'list', l: l, on: true, attrs: pairs(y.attrs), items: y.items.map(function (i) { return [i.k, pairs(i.attrs)]; }) }); return; }
      diffMaps(x.attrs, y.attrs).forEach(function (d) { ops.push({ p: 'listAttr', l: l, n: d.n, v: d.v }); });
      x.items.forEach(function (i) {
        var j = itemIndex(y, i.k);
        if (j < 0) ops.push({ p: 'item', l: l, k: i.k, on: false });
        else diffMaps(i.attrs, y.items[j].attrs).forEach(function (d) { ops.push({ p: 'itemAttr', l: l, k: i.k, n: d.n, v: d.v }); });
      });
      y.items.forEach(function (i) { if (itemIndex(x, i.k) < 0) ops.push({ p: 'item', l: l, k: i.k, on: true, attrs: pairs(i.attrs) }); });
    });
    return ops;
  }
  function sameModel(a, b) { return diff(a, b).length === 0; }
  // Applies diff() ops to a model (the page keeps its work in progress as ops, so it survives a data rebuild).
  // Returns the number of ops that no longer fit (an element or item that is gone).
  function applyOps(m, ops) {
    var missed = 0;
    (ops || []).forEach(function (o) {
      if (o.p === 'attr') { if (o.v == null) delete m.attrs[o.n]; else m.attrs[o.n] = o.v; return; }
      if (o.p === 'el') {
        if (o.on) m.els[o.s] = fromPairs(o.attrs);
        else Object.keys(m.els).forEach(function (s) { if (s === o.s || s.indexOf(o.s + '/') === 0) delete m.els[s]; });
        return;
      }
      if (o.p === 'elAttr') { var e = m.els[o.s]; if (!e) { missed++; return; } if (o.v == null) delete e[o.n]; else e[o.n] = o.v; return; }
      if (o.p === 'list') {
        if (o.on) m.lists[o.l] = { attrs: fromPairs(o.attrs), items: (o.items || []).map(function (i) { return { k: i[0], attrs: fromPairs(i[1]) }; }) };
        else delete m.lists[o.l];
        return;
      }
      var l = m.lists[o.l];
      if (!l) { missed++; return; }
      if (o.p === 'listAttr') { if (o.v == null) delete l.attrs[o.n]; else l.attrs[o.n] = o.v; return; }
      var j = itemIndex(l, o.k);
      if (o.p === 'item') {
        if (o.on) { if (j >= 0) { missed++; return; } l.items.push({ k: o.k, attrs: fromPairs(o.attrs) }); }
        else { if (j < 0) { missed++; return; } l.items.splice(j, 1); }
        return;
      }
      if (o.p === 'itemAttr') { if (j < 0) { missed++; return; } if (o.v == null) delete l.items[j].attrs[o.n]; else l.items[j].attrs[o.n] = o.v; return; }
      missed++;
    });
    return missed;
  }

  // Field paths, for the page: {el:'root'} | {el:'sub', s} | {el:'list', l} (container attributes) | {el:'item', l, k}.
  function mapAt(m, path) {
    if (path.el === 'root') return m.attrs;
    if (path.el === 'sub') return m.els[path.s] || null;
    if (path.el === 'list') return m.lists[path.l] ? m.lists[path.l].attrs : null;
    if (path.el === 'item') { var l = m.lists[path.l], j = l ? itemIndex(l, path.k) : -1; return j >= 0 ? l.items[j].attrs : null; }
    return null;
  }
  function getAt(m, path, n) { var o = mapAt(m, path); return o && o[n] !== undefined ? o[n] : null; }
  // Sets an attribute; a missing single element is created (BladeData before BladeData/Thrust).
  function setAt(m, path, n, v) {
    var o = mapAt(m, path);
    if (!o && path.el === 'sub' && v != null && v !== '') {
      var parts = path.s.split('/');
      for (var i = 1; i <= parts.length; i++) { var s = parts.slice(0, i).join('/'); if (!m.els[s]) m.els[s] = {}; }
      o = m.els[path.s];
    }
    if (!o) return false;
    if (v == null || v === '') delete o[n]; else o[n] = String(v);
    return true;
  }
  // The XSD path of an element of the model (vocab key): 'CraftingPiece/BladeData/Thrust', 'CraftingTemplate/StatsData/StatData', ...
  function vocabPath(kind, path) {
    if (path.el === 'root') return kind;
    if (path.el === 'sub') return kind + '/' + path.s;
    var spec = listSpec(kind, path.l);
    if (!spec) return '';
    return path.el === 'list' ? kind + '/' + spec.name : kind + '/' + spec.name + '/' + spec.item;
  }

  // ---------------------------------------------------------------- game enums and parsing rules

  function enumHas(list, v, ci) {
    if (v == null) return false;
    if (ci) { var lv = String(v).toLowerCase(); return (list || []).some(function (x) { return x.toLowerCase() === lv; }); }
    return (list || []).indexOf(v) >= 0;
  }
  function canon(list, v) {
    var lv = String(v).toLowerCase();
    for (var i = 0; i < (list || []).length; i++) if (list[i].toLowerCase() === lv) return list[i];
    return null;
  }
  // Names that are not real choices (counts, masks, sentinels) are left out of the pickers; the checks accept them.
  function cleanEnum(list) {
    return (list || []).filter(function (n) { return !/Mask$|^Num|^NumberOf|^Invalid$|^Undefined$/.test(n); });
  }
  // Attributes parsed into an enum: ci = Enum.Parse(ignoreCase: true); list: ':' separated.
  var ATTR_ENUMS = {
    'CraftingPiece@piece_type': { e: 'PieceTypes', ci: true },
    'CraftingPiece/BladeData/Thrust@damage_type': { e: 'DamageTypes', ci: true },
    'CraftingPiece/BladeData/Swing@damage_type': { e: 'DamageTypes', ci: true },
    'CraftingPiece/Materials/Material@id': { e: 'CraftingMaterials', soft: 'Enum.TryParse fails: read as IronOre (no tier weight)' },
    'CraftingPiece/Flags/Flag@type': { list0: ['WeaponFlags', 'ItemFlags'], soft: 'anything but "WeaponFlags" reads the name as an ItemFlags flag' },
    'CraftingTemplate@item_type': { e: 'ItemTypeEnum' },
    'CraftingTemplate@piece_type_to_scale_holster_with': { e: 'PieceTypes' },
    'CraftingTemplate@hidden_piece_types_on_holster': { e: 'PieceTypes', sep: ':' },
    'CraftingTemplate/PieceDatas/PieceData@piece_type': { e: 'PieceTypes' },
    'CraftingTemplate/StatsData/StatData@stat_type': { e: 'CraftingStatTypes' }
  };
  // Attributes the game parses with int.Parse / short.Parse (whatever the XSD says).
  var INT_ATTRS = {
    'CraftingPiece@tier': 1, 'CraftingPiece@CraftingCost': 1, 'CraftingPiece@required_skill_value': 1,
    'CraftingPiece/BladeData@stack_amount': 1, 'CraftingPiece/Materials/Material@count': 'soft',
    'CraftingPiece/StatContributions@armor_bonus': 1, 'CraftingPiece/StatContributions@handling_bonus': 1,
    'CraftingPiece/StatContributions@swing_damage_bonus': 1, 'CraftingPiece/StatContributions@swing_speed_bonus': 1,
    'CraftingPiece/StatContributions@thrust_damage_bonus': 1, 'CraftingPiece/StatContributions@thrust_speed_bonus': 1,
    'CraftingPiece/StatContributions@accuracy_bonus': 1, 'CraftingTemplate/PieceDatas/PieceData@build_order': 1
  };
  // Attributes read with Convert.ToBoolean (throws on anything but true/false, any case).
  var BOOL_ATTRS = {
    'CraftingPiece@is_unique': 1, 'CraftingPiece@is_default': 1, 'CraftingPiece@is_hidden': 1,
    'CraftingTemplate@use_weapon_as_holster_mesh': 1, 'CraftingTemplate@always_show_holster_with_weapon': 1, 'CraftingTemplate@rotate_weapon_in_holster': 1
  };
  // References: culture "Culture.x", modifier group, holsters (':' list), weapon description, piece.
  var ATTR_REFS = {
    'CraftingPiece@culture': { ref: 'culture', prefix: 'Culture.' },
    'CraftingTemplate@modifier_group': { ref: 'modgroup' },
    'CraftingTemplate@item_holsters': { ref: 'holsters', sep: ':' },
    'CraftingTemplate/WeaponDescriptions/WeaponDescription@id': { ref: 'desc' },
    'CraftingTemplate/StatsData@weapon_description': { ref: 'desc' },
    'CraftingTemplate/UsablePieces/UsablePiece@piece_id': { ref: 'piece' },
    'CraftingPiece/CraftingTemplates/CraftingTemplate@id': { ref: 'template' }
  };
  var PIECE_TYPE_INDEX = { Blade: 0, Guard: 1, Handle: 2, Pommel: 3 };
  var PIECE_TYPES = ['Blade', 'Guard', 'Handle', 'Pommel'];

  function isInt(v) { return /^\s*[+-]?\d+\s*$/.test(v); }
  function isDec(v) { return /^\s*[+-]?(\d+\.?\d*|\.\d+)([eE][+-]?\d+)?\s*$/.test(v); }
  function num(v) { if (v == null || v === '') return NaN; return isDec(String(v)) ? Number(String(v).trim()) : NaN; }
  // float.Parse: the nearest float; NaN when the game would throw.
  function pf(v) { var n = num(v); return isNaN(n) ? NaN : F(n); }
  function pi(v) { return isInt(String(v)) ? parseInt(String(v).trim(), 10) : NaN; }
  function toBool(v) { var s = String(v).trim().toLowerCase(); return s === 'true' ? true : s === 'false' ? false : null; }

  // ---------------------------------------------------------------- deserializing (the game's view of an id)
  // An id's definitions, each deserialized onto the same object in load order (MBObjectManager.LoadXml: RBM's files
  // are appended, so an id RBM defines again is read twice). Every start-tag attribute is set again each time (absent
  // = the default), while a child element only changes what it holds when it is present.

  // CraftingPiece.Deserialize, run on each model in order. Floats as the game stores them (float32).
  function effPiece(models, data) {
    var en = (data && data.enums) || {};
    var p = {
      valid: true, errors: [], name: '', type: null, mesh: null, culture: null, appearance: F(0.5), cost: 0, weight: 0, length: 0, dNext: 0, dPrev: 0,
      inertia: 0, com: 0, tier: 1, isUnique: false, isDefault: false, isHidden: false, fullScale: false, excluded: '', reqSkill: 0,
      armorBonus: 0, handlingBonus: 0, swingDamageBonus: 0, swingSpeedBonus: 0, thrustDamageBonus: 0, thrustSpeedBonus: 0, accuracyBonus: 0,
      blade: null, pieceOffset: 0, prevOffset: 0, nextOffset: 0, materials: [], materialCosts: [0, 0, 0, 0, 0, 0, 0, 0, 0],
      itemFlags: [], weaponFlags: [], templates: [], holsterShift: [0, 0, 0]
    };
    var err = function (t) { p.errors.push(t); };
    models.forEach(function (m) {
      var a = m.attrs;
      p.id = a.id;
      p.name = a.name;
      if (a.name == null) err('no name attribute (CraftingPiece.Deserialize throws)');
      var pt = a.piece_type != null ? canon(en.PieceTypes, a.piece_type) : null;
      if (!pt || PIECE_TYPE_INDEX[pt] == null) { err('piece_type "' + a.piece_type + '" is not Blade, Guard, Handle or Pommel'); pt = pt || null; }
      p.type = pt;
      if (a.mesh == null) err('no mesh attribute (CraftingPiece.Deserialize throws)');
      p.mesh = a.mesh;
      p.culture = a.mesh != null && a.culture != null ? String(a.culture).replace(/^Culture\./, '') : null;
      p.appearance = a.appearance != null ? pf(a.appearance) : F(0.5);
      p.cost = a.CraftingCost != null ? pi(a.CraftingCost) : 0;
      p.weight = a.weight != null ? pf(a.weight) : 0;
      if (a.length != null) {
        p.length = F(F(0.01) * pf(a.length));
        p.dNext = F(p.length / 2); p.dPrev = F(p.length / 2);
      } else {
        p.dNext = F(F(0.01) * pf(a.distance_to_next_piece));
        p.dPrev = F(F(0.01) * pf(a.distance_to_previous_piece));
        if (a.distance_to_next_piece == null || a.distance_to_previous_piece == null) err('no length and no distance_to_next_piece/distance_to_previous_piece (float.Parse throws)');
        p.length = F(p.dNext + p.dPrev);
      }
      p.inertia = F(F(F(F(1 / 12) * p.weight) * p.length) * p.length);
      var com = a.center_of_mass != null ? pf(a.center_of_mass) : F(0.5);
      p.com = F(p.length * com);
      p.holsterShift = [0, 0, 0];
      if (a.item_holster_pos_shift != null) {
        var hs = String(a.item_holster_pos_shift).split(',');
        if (hs.length === 3) p.holsterShift = hs.map(function (x) { var n = pf(x); return isNaN(n) ? 0 : n; });
      }
      p.tier = a.tier == null ? 1 : pi(a.tier);
      p.isUnique = a.is_unique != null && toBool(a.is_unique) === true;
      p.isDefault = a.is_default != null && toBool(a.is_default) === true;
      p.isHidden = a.is_hidden != null && toBool(a.is_hidden) === true;
      p.fullScale = a.full_scale != null ? a.full_scale === 'true' : (pt === 'Guard' || pt === 'Pommel');
      p.excluded = a.excluded_item_usage_features != null ? a.excluded_item_usage_features : '';
      p.reqSkill = a.required_skill_value != null ? pi(a.required_skill_value) : 0;
      var sc = m.els.StatContributions;
      if (sc) {
        var ib = function (n) { return sc[n] != null ? pi(sc[n]) : 0; };
        p.armorBonus = ib('armor_bonus'); p.handlingBonus = ib('handling_bonus'); p.swingDamageBonus = ib('swing_damage_bonus');
        p.swingSpeedBonus = ib('swing_speed_bonus'); p.thrustDamageBonus = ib('thrust_damage_bonus'); p.thrustSpeedBonus = ib('thrust_speed_bonus');
        p.accuracyBonus = ib('accuracy_bonus');
      }
      var bd = m.els.BladeData;
      if (bd) {
        // new BladeData(PieceType, Length) then BladeData.Deserialize.
        var b = { stack: bd.stack_amount == null ? 1 : pi(bd.stack_amount), thrustType: 'Invalid', thrustFactor: 0, swingType: 'Invalid', swingFactor: 0 };
        b.length = bd.blade_length != null ? F(F(0.01) * pf(bd.blade_length)) : p.length;
        b.width = bd.blade_width != null ? F(F(0.01) * pf(bd.blade_width)) : F(F(0.15) + F(b.length * F(0.3)));
        b.physics = bd.physics_material || null; b.body = bd.body_name || null;
        b.holsterMesh = bd.holster_mesh || null; b.holsterBody = bd.holster_body_name || null;
        b.holsterLength = F(F(0.01) * (bd.holster_mesh_length != null ? pf(bd.holster_mesh_length) : 0));
        var th = m.els['BladeData/Thrust'], sw = m.els['BladeData/Swing'];
        if (th) {
          b.thrustType = canon(en.DamageTypes, th.damage_type) || 'Invalid';
          if (!canon(en.DamageTypes, th.damage_type)) err('Thrust damage_type "' + th.damage_type + '" is not a DamageTypes value (Enum.Parse throws)');
          b.thrustFactor = pf(th.damage_factor);
        }
        if (sw) {
          b.swingType = canon(en.DamageTypes, sw.damage_type) || 'Invalid';
          if (!canon(en.DamageTypes, sw.damage_type)) err('Swing damage_type "' + sw.damage_type + '" is not a DamageTypes value (Enum.Parse throws)');
          b.swingFactor = pf(sw.damage_factor);
        }
        p.blade = b;
      }
      var bu = m.els.BuildData;
      if (bu) {
        p.pieceOffset = bu.piece_offset != null ? F(F(0.01) * pf(bu.piece_offset)) : 0;
        p.prevOffset = bu.previous_piece_offset != null ? F(F(0.01) * pf(bu.previous_piece_offset)) : 0;
        p.nextOffset = bu.next_piece_offset != null ? F(F(0.01) * pf(bu.next_piece_offset)) : 0;
      }
      var ml = m.lists.Materials;
      if (ml) {
        p.materials = [];
        ml.items.forEach(function (it) {
          var mats = en.CraftingMaterials || [], idx = mats.indexOf(it.attrs.id);
          if (idx < 0 || idx >= 9) idx = 0;   // Enum.TryParse failed: default(CraftingMaterials) = IronOre
          var c = isInt(String(it.attrs.count)) ? parseInt(it.attrs.count, 10) : 0;
          if (c > 0) p.materials.push({ mat: mats[idx], count: c });
          p.materialCosts[idx] = c;
        });
      }
      var fl = m.lists.Flags;
      if (fl) {
        p.itemFlags = []; p.weaponFlags = [];
        fl.items.forEach(function (it) {
          var isW = it.attrs.type == null || it.attrs.type === 'WeaponFlags', list = isW ? en.WeaponFlags : en.ItemFlags, c = canon(list, it.attrs.name);
          if (!c) { err('Flag "' + it.attrs.name + '" is not a ' + (isW ? 'WeaponFlags' : 'ItemFlags') + ' name (Enum.Parse throws)'); return; }
          (isW ? p.weaponFlags : p.itemFlags).push(c);
        });
      }
      var ct = m.lists.CraftingTemplates;
      if (ct) ct.items.forEach(function (it) { if (it.attrs.id != null) p.templates.push(it.attrs.id); });
    });
    return p;
  }

  // CraftingTemplate.Deserialize, run on each def in order. defs: [{ model, add: [piece ids an XSLT added] }].
  // hasPiece(id) -> piece type or null (UsablePieces only keeps pieces that exist); descIds: known descriptions.
  function effTemplate(defs, data, hasPiece, hasDesc) {
    var en = (data && data.enums) || {};
    var t = { itemType: null, modifierGroup: null, holsters: [], holsterShift: null, buildOrders: [], descs: [], statData: [], usable: [], usableFrom: {}, errors: [], flags: {} };
    var err = function (x) { t.errors.push(x); };
    defs.forEach(function (d, di) {
      var m = d.model, a = m.attrs;
      if (a.modifier_group != null) t.modifierGroup = a.modifier_group;
      if (!enumHas(en.ItemTypeEnum, a.item_type)) err('item_type "' + a.item_type + '" is not an ItemTypeEnum value (Enum.Parse, case-sensitive, throws)');
      t.itemType = a.item_type;
      if (a.item_holsters == null) err('no item_holsters (CraftingTemplate.Deserialize throws)');
      t.holsters = a.item_holsters != null ? String(a.item_holsters).split(':') : [];
      if (a.default_item_holster_position_offset == null) err('no default_item_holster_position_offset (CraftingTemplate.Deserialize throws)');
      t.flags = { weaponAsHolster: a.use_weapon_as_holster_mesh != null && toBool(a.use_weapon_as_holster_mesh) === true };
      var pd = m.lists.PieceDatas;
      if (pd) {
        t.buildOrders = [];
        pd.items.forEach(function (it) {
          var ty = enumHas(en.PieceTypes, it.attrs.piece_type) ? it.attrs.piece_type : null;
          var o = pi(it.attrs.build_order);
          if (!ty || PIECE_TYPE_INDEX[ty] == null) { err('PieceData piece_type "' + it.attrs.piece_type + '" is not a piece type (Enum.Parse throws)'); return; }
          if (isNaN(o)) { err('PieceData ' + ty + ' build_order "' + it.attrs.build_order + '" is not a whole number (int.Parse throws)'); return; }
          t.buildOrders.push({ type: ty, order: o });
        });
      }
      var wd = m.lists.WeaponDescriptions;
      if (wd) {
        t.descs = [];
        wd.items.forEach(function (it) { if (hasDesc(it.attrs.id)) t.descs.push(it.attrs.id); });
        t.statData = t.descs.map(function () { return null; });
      }
      var up = m.lists.UsablePieces, add = function (id, how) {
        if (id == null || !hasPiece(id) || t.usable.indexOf(id) >= 0) return;
        t.usable.push(id); t.usableFrom[id] = how;
      };
      if (up) up.items.forEach(function (it) { add(it.attrs.piece_id, d.label || ('definition ' + (di + 1))); });
      (d.add || []).forEach(function (id) { add(id, (d.label || ('definition ' + (di + 1))) + ' (XSLT)'); });
      Object.keys(m.lists).forEach(function (l) {
        if (l.indexOf('StatsData|') !== 0) return;
        var arr = {}, wdesc = m.lists[l].attrs.weapon_description;
        m.lists[l].items.forEach(function (it) { var mv = pf(it.attrs.max_value); if (enumHas(en.CraftingStatTypes, it.attrs.stat_type)) arr[it.attrs.stat_type] = mv; });
        if (wdesc != null) { var ix = t.descs.indexOf(wdesc); if (ix >= 0) t.statData[ix] = arr; else err('StatsData weapon_description "' + wdesc + '" is not one of the template\'s weapon descriptions (index -1: the game throws)'); }
        else for (var k = 0; k < t.statData.length; k++) t.statData[k] = arr;
      });
    });
    return t;
  }

  // ---------------------------------------------------------------- the calculator
  // Ports of the decompiled TaleWorlds.Core code: WeaponDesign (pivot distances, length), WeaponDesignElement (scaled
  // values), Crafting.CraftedItemGenerationHelper (GenerateCraftedItem, CraftingStats), CombatStatCalculator,
  // DefaultItemValueModel (tier, value); RBM's patches on that path: CombatModule/Magnitude/MagnitudeChanges.Thrust.cs
  // (CalculateStrikeMagnitudeForThrust), CombatModule/Items/ItemValuesTiers.Tiers.cs (CalculateTierMeleeWeapon) and
  // .Pricing.cs (CalculateValue), RBM/CraftingCoveragePatches.cs (pieces in no weapon description). Float math in
  // float32 (Math.fround) where the game uses float, double where it uses double.

  var RBM_CFG = { ThrustMagnitudeModifier: 0.05, armorMultiplier: 2, armorThresholdModifier: 1, bluntTraumaBonus: 0, bluntTraumaMultiplier: 1,
    maceBluntModifier: 1, armorEffectivenessMultiplier: 1, weaponPriceModifier: 1 };
  // RBMConfig/Utilities.cs createWeaponTypesFactors (defaults): [bluntCut, bluntPierce, thresholdPierce, thresholdCut].
  var WEAPON_TYPE_FACTORS = {
    Dagger: [0.25, 0.35, 3, 5], ThrowingKnife: [0.15, 0.15, 3, 5], OneHandedSword: [0.25, 0.35, 3.5, 5], TwoHandedSword: [0.25, 0.35, 3.5, 5],
    OneHandedBastardAxe: [0.3, 0.25, 2.5, 5], OneHandedAxe: [0.3, 0.25, 2.5, 5], TwoHandedAxe: [0.3, 0.3, 2.5, 5], OneHandedPolearm: [0.3, 0.35, 3, 5],
    TwoHandedPolearm: [0.3, 0.35, 3, 5], Mace: [0.1, 0.25, 4, 4], TwoHandedMace: [0.1, 0.25, 4, 4], Arrow: [0.15, 0.15, 2, 2.6], Bolt: [0.15, 0.15, 2, 2.6],
    Javelin: [0.05, 0.2, 3, 3], ThrowingAxe: [0.3, 0.2, 2.5, 4], SlingStone: [0.5, 0.6, 6, 10]
  };

  // System.Math.Round(double): half to even.
  function roundEven(x) {
    var r = Math.round(x);
    if (Math.abs(x - Math.trunc(x)) === 0.5) r = 2 * Math.round(x / 2);
    return r;
  }
  function round2(x) { return F(roundEven(x * 100) / 100); }   // MathF.Round(float, 2) = (float)Math.Round(f, 2)
  function clampF(x, a, b) { return x < a ? a : x > b ? b : x; }

  // CombatStatCalculator.CalculateStrikeMagnitudeForSwing (float).
  function strikeSwing(swingSpeed, impact, weight, length, inertia, com, extra) {
    var n1 = F(F(length * impact) - com);
    var n2 = F(F(swingSpeed * F(F(0.5) + com)) + extra);
    var n3 = F(F(F(F(0.5) * weight) * n2) * n2);
    var n4 = F(F(F(F(0.5) * inertia) * swingSpeed) * swingSpeed);
    var n5 = F(n3 + n4);
    var n6 = F(F(n2 + F(swingSpeed * n1)) / F(F(1 / weight) + F(F(n1 * n1) / inertia)));
    var n7 = F(n2 - F(n6 / weight));
    var n8 = F(swingSpeed - F(F(n6 * n1) / inertia));
    var n9 = F(F(F(F(0.5) * weight) * n7) * n7);
    var n10 = F(F(F(F(0.5) * inertia) * n8) * n8);
    var n11 = F(n9 + n10);
    var n12 = F(F(n5 - n11) + F(0.5));
    return F(F(0.067) * n12);
  }
  // CombatStatCalculator.CalculateBaseBlowMagnitudeForSwing (float).
  function baseBlowSwing(speed, reach, weight, inertia, com, impact, extra) {
    impact = Math.min(impact, F(0.93));
    var n = clampF(F(F(0.4) / reach), 0, 1), best = 0;
    for (var i = 0; i < 5; i++) {
      var n3 = F(impact + F(F(i / 4) * n));
      if (!(n3 < 1)) break;
      var n4 = strikeSwing(speed, n3, weight, reach, inertia, com, extra);
      if (best < n4) best = n4;
    }
    return best;
  }
  // CombatStatCalculator.CalculateStrikeMagnitudeForThrust: vanilla, or RBM's prefix (MagnitudeChanges.Thrust.cs),
  // which ignores the weapon's weight and isThrown: 0.5 × 8 × clamp(speed, 4, 6)² × ThrustMagnitudeModifier.
  function strikeThrust(speed, weight, extra, isThrown, rbm) {
    if (rbm) {
      var cs = F(clampF(speed, 4, 6) + extra);
      if (cs > 0) return F(F(F(F(F(0.5) * 8) * cs) * cs) * F(RBM_CFG.ThrustMagnitudeModifier));
      return 0;
    }
    var s = F(speed + extra);
    if (s > 0) {
      if (!isThrown) weight = F(weight + F(2.5));
      return F(F(0.125) * F(F(F(F(0.5) * weight) * s) * s));
    }
    return 0;
  }
  // Crafting...CraftingStats.SimulateSwingLayer / SimulateThrustLayer (double).
  function simSwing(angleSpan, usablePower, maxTorque, inertia, reachTerm) {
    var a = 0, v = 0.01, t = 0;
    while (a < angleSpan) {
      var tq = usablePower / v;
      if (tq > maxTorque) tq = maxTorque;
      tq -= v * reachTerm;
      v += 0.009999999776482582 * tq / inertia;
      a += v * 0.009999999776482582;
      t += 0.009999999776482582;
      if (t > 1e4) break;   // guard: a degenerate design never reaches the angle
    }
    return t;
  }
  function simThrust(distance, usablePower, maxForce, mass) {
    var d = 0, v = 0.01, t = 0;
    while (d < distance) {
      var f = usablePower / v;
      if (f > maxForce) f = maxForce;
      v += 0.01 * f / mass;
      d += v * 0.01;
      t += 0.01;
      if (t > 1e4) break;
    }
    return t;
  }
  function hasAll(flags, list) { return list.every(function (f) { return flags.indexOf(f) >= 0; }); }
  function hasAny(flags, list) { return list.some(function (f) { return flags.indexOf(f) >= 0; }); }

  // A WeaponDesignElement: the piece with its scale (ScaleFactor = scale × 0.01f; unscaled values when scale = 100).
  function element(pc, scale) {
    if (!pc) return { valid: false, pc: null, scale: 100, sf: 1, len: 0, weight: 0, com: 0, dNext: 0, dPrev: 0, offset: 0, prevOffset: 0, nextOffset: 0, inertia: 0 };
    var s = scale == null ? 100 : scale, sf = F(s * F(0.01)), sc = s !== 100;
    var mul = function (x) { return sc ? F(x * sf) : x; };
    return {
      valid: true, pc: pc, scale: s, sf: sf,
      len: mul(pc.length), com: mul(pc.com), dNext: mul(pc.dNext), dPrev: mul(pc.dPrev),
      offset: mul(pc.pieceOffset), prevOffset: mul(pc.prevOffset), nextOffset: mul(pc.nextOffset),
      weight: sc ? F(pc.weight * (pc.fullScale ? F(F(sf * sf) * sf) : sf)) : pc.weight,
      inertia: pc.inertia
    };
  }

  // The weapon a design makes, as the game builds it. design: { template (id), pieces: { Blade: {id, scale}, ... } }.
  // ctx: { piece(id) -> effPiece or null, template(id) -> effTemplate or null, desc(id) -> {cls, features, flags, rotated,
  //   useCom, avail}, rbm: RBM's formulas (thrust, tier, price), value (the item's value attribute), skill (RBM stats) }.
  // Returns { ok, errors: [{cls, text}], weight, appearance, length (cm), usages: [...], tier }.
  function calcDesign(design, ctx) {
    var res = { ok: true, errors: [], usages: [], weight: null };
    var bad = function (t) { res.errors.push({ cls: 'bad', text: t }); res.ok = false; };
    var warn = function (t) { res.errors.push({ cls: 'warn', text: t }); };
    var tpl = ctx.template(design.template);
    if (!tpl) { bad('unknown crafting template "' + design.template + '"'); return res; }
    var used = [];
    for (var i = 0; i < 4; i++) {
      var pd = design.pieces[PIECE_TYPES[i]];
      var pc = pd && pd.id ? ctx.piece(pd.id) : null;
      if (pd && pd.id && !pc) { bad(PIECE_TYPES[i] + ' piece "' + pd.id + '" does not exist'); return res; }
      if (pc && pc.type !== PIECE_TYPES[i]) warn(PIECE_TYPES[i] + ' slot holds ' + pd.id + ', a ' + pc.type + ' piece');
      used.push(element(pc, pd ? pd.scale : 100));
    }
    // GenerateCraftedItem: a valid piece the template does not list fails the whole item (it is unregistered).
    for (i = 0; i < 4; i++) {
      if (used[i].valid && tpl.usable.indexOf(used[i].pc.id) < 0) { bad(used[i].pc.id + ' is not usable by template ' + design.template + ': GenerateCraftedItem returns null and the item is not loaded'); }
    }
    if (!res.ok) return res;
    if (!used[0].valid || !used[0].pc.blade) { bad('the Blade slot has no piece with BladeData (Crafting reads UsedPieces[0].CraftingPiece.BladeData: it throws)'); return res; }
    // WeaponDesign.CalculatePivotDistances / CalculateWeaponLength.
    var pivots = [NaN, NaN, NaN, NaN], bottom = 0, top = 0;
    tpl.buildOrders.forEach(function (bo) {
      var e = used[PIECE_TYPE_INDEX[bo.type]];
      if (!e.valid) { pivots[PIECE_TYPE_INDEX[bo.type]] = NaN; return; }
      var sg = Math.sign(bo.order);
      if (sg === 0) { top = F(top + e.offset); bottom = F(bottom - e.offset); }
      else if (sg < 0) { bottom = F(F(F(bottom + e.dNext) + e.offset) - e.nextOffset); }
      else { top = F(F(F(top + e.dPrev) + e.offset) - e.prevOffset); }
      pivots[PIECE_TYPE_INDEX[bo.type]] = F(F(sg * (sg < 0 ? bottom : top)) + (sg === 0 ? e.offset : 0));
      if (sg === 0) { bottom = F(bottom + F(e.dPrev - e.prevOffset)); top = F(top + F(e.dNext - e.nextOffset)); }
      if (sg < 0) bottom = F(bottom + F(e.dPrev - e.prevOffset));
      if (sg > 0) top = F(top + F(e.dNext - e.nextOffset));
    });
    var lenA = F(pivots[0] + used[0].dNext), lenB = 0;
    used.forEach(function (e) { if (e.valid && e.dNext > lenB) lenB = F(e.dNext + e.offset); });
    var craftedLength = isNaN(lenA) ? lenB : Math.max(lenA, lenB);
    var designFlags = [];
    used.forEach(function (e) { if (e.valid) e.pc.weaponFlags.forEach(function (f) { if (designFlags.indexOf(f) < 0) designFlags.push(f); }); });
    // Crafting.GenerateItem: weight = round(Σ scaled weights, 2) (Enumerable.Sum<float> adds in double).
    var sum = 0;
    used.forEach(function (e) { sum += e.weight; });
    var weight = round2(F(sum));
    var appearance = used[3].valid ? used[3].pc.appearance : used[0].pc.appearance;
    res.weight = weight; res.appearance = appearance; res.craftedLength = craftedLength; res.pivots = pivots;
    res.designFlags = designFlags;
    // Which weapon descriptions become usages: every piece (or an invalid slot) must be in AvailablePieces.
    var isAlt = false;
    tpl.descs.forEach(function (did) {
      var desc = ctx.desc(did);
      if (!desc) return;
      var n = 4;
      used.forEach(function (e) { if (!e.valid) n--; });
      var avail = ctx.avail ? ctx.avail(did) : desc.avail;
      for (var j = 0; j < avail.length && n > 0; j++) {
        var ap = ctx.piece(avail[j]);
        if (!ap || PIECE_TYPE_INDEX[ap.type] == null) continue;
        var e = used[PIECE_TYPE_INDEX[ap.type]];
        if (e.valid && e.pc.id === avail[j]) n--;
      }
      if (n > 0) return;
      res.usages.push(calcUsage(used, tpl, desc, did, weight, craftedLength, pivots, designFlags, isAlt, ctx));
      isAlt = true;
    });
    if (!res.usages.length) bad('no weapon description of template ' + design.template + ' lists all its pieces: the item gets no weapon and is not loaded');
    if (!res.ok) return res;
    tierAndPrice(res, used, tpl, ctx);
    return res;
  }

  // CraftingStats.CalculateStats + SetWeaponData for one weapon description.
  function calcUsage(used, tpl, desc, did, weight, craftedLength, pivots, designFlags, isAlt, ctx) {
    var df = desc.flags, twoH = hasAll(df, ['MeleeWeapon', 'NotUsableWithOneHand']), wide = hasAll(df, ['MeleeWeapon', 'WideGrip']);
    var reach = round2(craftedLength);
    // CalculateCenterOfMass
    var sumM = 0, accP = 0, accN = 0;
    tpl.buildOrders.forEach(function (bo) {
      var e = used[PIECE_TYPE_INDEX[bo.type]];
      if (!e.valid) return;
      var v = 0;
      if (bo.order < 0) { v = F(v - F(F(accN + F(e.len - e.com)) * e.weight)); accN = F(accN + e.len); }
      else { v = F(v + F(F(accP + e.com) * e.weight)); accP = F(accP + e.len); }
      sumM = F(sumM + v);
    });
    var h = used[2];
    var com = F(F(sumM / weight) - F(h.dPrev - h.offset));
    // CalculateWeaponInertia
    var off = F(-com), inertia = 0;
    tpl.buildOrders.forEach(function (bo) {
      var e = used[PIECE_TYPE_INDEX[bo.type]];
      if (!e.valid) return;
      var o = F(off + e.pc.com);
      inertia = F(inertia + F(e.pc.inertia + F(F(e.weight * o) * o)));
      off = F(off + e.len);
    });
    var pax = function (I, m, o) { return F(I + F(F(m * o) * o)); };
    var iShoulder = pax(inertia, weight, F(F(0.5) + com)), iGrip = pax(inertia, weight, com);
    // CalculateSwingSpeed (double)
    var n = 1.0 * iShoulder + 0.9, p2 = 170.0, p3 = 90.0, t4 = 27.0, t5 = 15.0, t6 = 7.0;
    if (twoH) {
      if (hasAny(df, ['WideGrip'])) { n += 1.5; t6 *= 4.0; t5 *= 1.7; p3 *= 1.3; p2 *= 1.15; }
      else { n += 1.0; t6 *= 2.4; t5 *= 1.3; p3 *= 1.35; p2 *= 1.15; }
    }
    t4 = Math.max(1.0, t4 - n); t5 = Math.max(1.0, t5 - n); t6 = Math.max(1.0, t6 - n);
    var reachTerm = 3.9 * reach * (wide ? 1.0 : 0.3);
    var st = 0.33 * (simSwing(1.5, 200.0, t4, 2.0 + n, reachTerm) + simSwing(1.5, p2, t5, 1.0 + n, reachTerm) + simSwing(1.5, p3, t6, 0.5 + n, reachTerm));
    var swingSpeed = F(20.8 / st);
    // CalculateThrustSpeed (double)
    var tm = 1.8 + weight + iGrip * 0.2, q2 = 170.0, q3 = 90.0, q4 = 24.0, q5 = 15.0;
    if (twoH && !hasAny(df, ['WideGrip'])) { tm += 0.6; q5 *= 1.9; q4 *= 1.1; q3 *= 1.2; q2 *= 1.05; }
    else if (hasAll(df, ['MeleeWeapon', 'NotUsableWithOneHand', 'WideGrip'])) { tm += 0.9; q5 *= 2.1; q4 *= 1.2; q3 *= 1.2; q2 *= 1.05; }
    var tt = 0.33 * (simThrust(0.6, 250.0, 48.0, 4.0 + tm) + simThrust(0.6, q2, q4, 2.0 + tm) + simThrust(0.6, q3, q5, 0.5 + tm));
    var thrustSpeed = F(3.8500000000000005 / tt);
    // CalculateAgility
    var g = iGrip;
    if (twoH) { g = F(g * F(0.5)); g = F(g + F(0.9)); }
    else if (wide) { g = F(g * F(0.4)); g = F(g + 1); }
    else g = F(g + F(0.7));
    var handling = roundEven(F(F(100) * F(Math.pow(F(1 / g), F(0.55)))));
    // CalculateWeaponTier: the average piece tier, truncated.
    var tsum = 0, tn = 0;
    used.forEach(function (e) { if (e.valid) { tsum += e.pc.tier; tn++; } });
    var weaponTier = Math.trunc(F(tsum / tn));
    var blade = used[0].pc.blade, sf = blade.swingFactor, tf = blade.thrustFactor;
    var cls = desc.cls, thrown = cls === 'ThrowingAxe' || cls === 'ThrowingKnife' || cls === 'Javelin';
    var swingBase = function () {
      var best = 0;
      for (var ip = F(0.93); ip > 0.5; ip = F(ip - F(0.05))) {
        var mg = baseBlowSwing(swingSpeed, reach, weight, inertia, com, ip, 0);
        if (mg > best) best = mg;
      }
      return F(best * sf);
    };
    var thrustBase = function (isThrown) { return F(strikeThrust(thrustSpeed, weight, 0, isThrown, ctx.rbm) * tf); };
    var swingDmg = 0, thrustDmg = 0;
    if (thrown) {
      if (cls === 'ThrowingAxe') thrustDmg = F(swingBase() * 2);
      else if (cls === 'ThrowingKnife') thrustDmg = F(thrustBase(true) * F(3.3));
      else thrustDmg = F(thrustBase(true) * 9);
    } else { swingDmg = swingBase(); thrustDmg = thrustBase(false); }
    // CalculateSweetSpot
    var bestM = -1, sweet = -1;
    for (var k = 0; k < 100; k++) {
      var at = F(F(0.01) * k), mm = strikeSwing(swingSpeed, at, weight, reach, inertia, com, 0);
      if (bestM < mm) { bestM = mm; sweet = at; }
    }
    // SetWeaponData
    var stack = 0, accuracy = 0, missileSpeed = 0;
    if (thrown) {
      stack = isAlt ? 1 : blade.stack;
      accuracy = cls === 'Javelin' ? 92 : cls === 'ThrowingAxe' ? 93 : 95;
      missileSpeed = Math.floor(F(thrustSpeed * F(cls === 'ThrowingAxe' ? 3.2 : cls === 'ThrowingKnife' ? 3.9 : 3.6)));
    }
    var features = (desc.features || '').split(':');
    used.forEach(function (e) {
      if (!e.valid) return;
      e.pc.excluded.split(':').forEach(function (x) { if (x) { var ix = features.indexOf(x); if (ix >= 0) features.splice(ix, 1); } });
    });
    var flags = df.slice();
    designFlags.forEach(function (f) { if (flags.indexOf(f) < 0) flags.push(f); });
    var consumable = flags.indexOf('Consumable') >= 0;
    var u = {
      desc: did, cls: cls, flags: flags, itemUsage: features.join('_'), physics: blade.physics,
      length: Math.trunc(F(reach * 100)), reach: reach,
      balance: round2(clampF(F(F(F(swingSpeed * F(4.5454545)) - 70) / 30), 0, 1)),
      inertia: inertia, totalInertia: F(inertia * (consumable ? stack : 1)), com: com, handling: Math.floor(handling),
      swingFactor: round2(sf), thrustFactor: round2(tf), swingType: blade.swingType, thrustType: blade.thrustType,
      swingSpeed: Math.floor(F(swingSpeed * F(4.5454545))), swingDamage: roundEven(swingDmg),
      thrustSpeed: Math.floor(F(thrustSpeed * F(11.764706))), thrustDamage: roundEven(thrustDmg),
      sweetSpot: sweet, stack: stack, accuracy: accuracy, missileSpeed: missileSpeed, weaponTier: weaponTier,
      armorBonus: used[1].valid ? used[1].pc.armorBonus : 0, useCom: !!desc.useCom, rotated: !!desc.rotated,
      melee: flags.indexOf('MeleeWeapon') >= 0, thrown: thrown,
      raw: { swingSpeed: swingSpeed, thrustSpeed: thrustSpeed, iShoulder: iShoulder, iGrip: iGrip, swingDamage: swingDmg, thrustDamage: thrustDmg }
    };
    if (ctx.rbm && ctx.skill != null) u.rbmStats = rbmMeleeStats(u, weight, ctx.skill);
    return u;
  }

  // DefaultItemValueModel.CalculateTierCraftedWeapon (vanilla; RBM keeps it).
  function pieceTier(used) {
    var n = 0, n2 = 0, n3 = 0, n4 = 0;
    var W = { Iron6: 6, Iron5: 5, Iron4: 4, Iron3: 3, Iron2: 2, Iron1: 1 };
    used.forEach(function (e) {
      if (!e.valid) return;
      n += e.pc.tier; n2++;
      e.pc.materials.forEach(function (m) { var w = W[m.mat]; if (w != null) { n3 += m.count * w; n4 += m.count; } });
    });
    if (n4 > 0 && n2 > 0) return F(F(F(0.4) * F(F(F(1.25) * n) / n2)) + F(F(0.6) * F(F(F(n3 * F(1.3)) / F(n4 + F(0.6))) - F(1.3))));
    if (n2 > 0) return F(n / n2);
    return F(0.1);
  }
  // RBM's CalculateTierMeleeWeapon prefix (Weapons[0] only).
  function rbmMeleeTier(w) {
    var fac = w.swingType === 'Pierce' ? F(1.15) : w.swingType === 'Blunt' ? F(1.2) : 1;
    var val = F(F(F(w.thrustFactor * F(15.4)) - F(11.55)) * F(w.thrustSpeed * F(0.01)));
    var sw = F(F(F(F(w.swingFactor * F(15.4)) - F(11.55)) * fac) * F(w.swingSpeed * F(0.01)));
    var mace = F(F(F(w.swingFactor * F(10.9)) - F(6.54)) * F(w.swingSpeed * F(0.01)));
    if (val < 0) val = 0; if (sw < 0) sw = 0; if (mace < 0) mace = 0;
    var r;
    switch (w.cls) {
      case 'OneHandedSword': case 'TwoHandedSword': r = F(F(val + sw) * F(0.5)); break;
      case 'Dagger': case 'ThrowingKnife': r = F(F(val * F(0.7)) + F(sw * F(0.3))); break;
      case 'TwoHandedPolearm': case 'LowGripPolearm': case 'OneHandedPolearm': r = F(val + F(sw * F(0.3))); break;
      case 'TwoHandedAxe': case 'OneHandedAxe': case 'Pick': case 'ThrowingAxe': r = sw; break;
      case 'TwoHandedMace': case 'Mace': r = F(mace + F(val * F(0.3))); break;
      case 'Javelin': r = val; break;
      default: r = F(F(val + sw) * F(0.5));
    }
    return clampF(r, F(0.3), F(6.5));
  }
  // Vanilla DefaultItemValueModel.CalculateTierMeleeWeapon (every usage).
  function vanillaMeleeTier(usages) {
    var gf = function (t) { return t === 'Pierce' ? F(1.15) : t === 'Blunt' ? F(1.45) : 1; };
    var best = -Infinity, second = -Infinity;
    usages.forEach(function (w) {
      var a = F(F(w.thrustDamage * gf(w.thrustType)) * F(Math.pow(F(w.thrustSpeed * F(0.01)), F(1.5))));
      var b = F(F(w.swingDamage * gf(w.swingType)) * F(Math.pow(F(w.swingSpeed * F(0.01)), F(1.5))));
      var n4 = Math.max(a, F(b * F(1.1)));
      if (w.flags.indexOf('NotUsableWithOneHand') >= 0) n4 = F(n4 * F(0.8));
      if (w.cls === 'ThrowingKnife' || w.cls === 'ThrowingAxe') n4 = F(n4 * F(1.2));
      if (w.cls === 'Javelin') n4 = F(n4 * F(0.6));
      var n6 = F(F(F(0.06) * F(n4 * F(1 + F(w.length * F(0.01))))) - F(3.5));
      if (n6 > second) { if (n6 >= best) { second = best; best = n6; } else second = n6; }
    });
    best = clampF(best, F(-1.5), F(7.5));
    if (second !== -Infinity) second = clampF(second, F(-1.5), F(7.5));
    if (usages.length <= 1) return best;
    return F(best * F(Math.pow(F(1 + F(F(second + F(1.5)) / F(best + F(2.5)))), F(0.2))));
  }
  function tierAndPrice(res, used, tpl, ctx) {
    var pt = pieceTier(used), mt = ctx.rbm ? rbmMeleeTier(res.usages[0]) : vanillaMeleeTier(res.usages);
    var tierf = F(F(F(0.6) * mt) + F(F(0.4) * pt));
    res.pieceTier = pt; res.meleeTier = mt; res.tierf = tierf;
    res.tier = clampF(roundEven(tierf), 0, 6);
    var tv = F(Math.pow(F(2.75), clampF(tierf, -1, F(7.5))));
    res.tierValue = tv;
    var type = tpl.itemType, price;
    if (ctx.rbm) {
      price = F(F(30 + F(tv * 24)) * RBM_CFG.weaponPriceModifier);
      if (type === 'Polearm') price = F(F(30 + F(tv * 16)) * RBM_CFG.weaponPriceModifier);
      if (type === 'Sling') price = F(price * F(0.25));
      if (type === 'TwoHandedWeapon') price = F(price * F(1.5));
      res.priceText = '(30 + tier value × ' + (type === 'Polearm' ? 16 : 24) + ')' + (type === 'TwoHandedWeapon' ? ' × 1.5' : '') + ' (RBM campaign pricing)';
    } else {
      var ap = res.appearance;
      price = F(F(F(100 * tv) * F(1 + F(F(0.2) * F(ap - 1)))) + F(100 * Math.max(0, F(ap - 1))));
      res.priceText = '100 × tier value × (1 + 0.2 × (appearance − 1)) (vanilla)';
    }
    res.computedPrice = Math.trunc(price);
    res.price = ctx.value != null && isInt(String(ctx.value)) ? parseInt(ctx.value, 10) : res.computedPrice;
    res.priceSrc = ctx.value != null && isInt(String(ctx.value)) ? 'value' : 'computed';
  }

  // ---------------------------------------------------------------- RBM's tooltip numbers
  // CombatModule/Magnitude/Tooltips/MagnitudeChanges.WeaponTooltip.cs GetRBMMeleeWeaponStats for a character with the
  // given weapon skill and no item modifier: swing/thrust speed (Utilities.CalculateVisualSpeeds), the swing sweet
  // spot (MagnitudeChanges.CalculateSweetSpotSwingMagnitude), the thrust magnitude (CalculateThrustMagnitude), then
  // RBMConfig/Shared/SkillDamage.GetSkillBasedDamage and BlowDamage.RBMComputeDamage at armor 0..100.
  function effSkillDR(s) { return F(F(F(600) / F(600 + s)) * s); }
  function skillMod(s) { return clampF(F(s / 250), 0, 1); }
  // Utilities.CalculateThrustSpeed (RBMCombat/Utilities/Utilities.Physics.cs).
  function rbmThrustSpeed(w, inertia, com) {
    var ig = F(inertia + F(F(w * com) * com));
    var n = 1.8 + w + ig * 0.2;
    var t = 0.33 * (simThrust(0.6, 250.0, 48.0, 4.0 + n) + simThrust(0.6, 170.0, 24.0, 2.0 + n) + simThrust(0.6, 90.0, 15.0, 0.5 + n));
    return F(3.8500000000000005 / t);
  }
  function thrustMag1H(w, dr, speed, extra) {
    if (speed > 9) speed = F(9);
    var cs = F(speed + extra), sm = F(skillMod(dr) * 2);
    var spear = F(F(F(0.5) * w) * F(cs * cs));
    var str = F(w + F(F(2.5) * F(1 + sm)));
    var cap = clampF(F(F(F(F(0.5) * str) * F(speed * speed)) * F(1.5)), 0, 180);
    var e = F(F(F(F(0.5) * str) * F(speed * speed)) + F(F(F(0.5) * w) * F(cs * cs)));
    if (e > cap) e = cap;
    var mg = e;
    if (spear > mg) mg = spear;
    if (mg > cap) mg = cap;
    return F(mg * F(RBM_CFG.ThrustMagnitudeModifier));
  }
  function thrustMag2H(w, dr, speed, extra) {
    if (speed > 6) speed = F(6);
    var cs = F(speed + extra), sm = F(skillMod(dr) * 2);
    var spear = F(F(F(0.5) * w) * F(cs * cs));
    var str = F(F(5) * F(1 + sm));
    var cap = clampF(F(F(F(F(0.5) * str) * F(speed * speed)) * F(1.5)), 0, 250);
    var e = F(F(F(F(0.5) * str) * F(speed * speed)) + F(F(F(0.5) * w) * F(cs * cs)));
    if (e > cap) e = cap;
    var mg = e;
    if (spear > mg) mg = spear;
    if (mg > cap) mg = cap;
    return F(mg * F(RBM_CFG.ThrustMagnitudeModifier));
  }
  // RBMConfig/Shared/SkillDamage.GetSkillBasedDamage (not passive usage, not a handle hit).
  function skillBasedDamage(mag, cls, dt, es, sm, isSwing) {
    var c = function (v, a, b) { return clampF(F(v), F(a), F(b)); };
    var sk = 0, lo, hi;
    switch (cls) {
      case 'Dagger': case 'OneHandedSword': case 'ThrowingKnife':
        if (dt === 'Cut') sk = F(c(mag + F(es * F(0.133)), 5 * (1 + sm), 15 * (1 + 2 * sm)) * F(4.6));
        else if (dt === 'Blunt') sk = F(F(c(mag + F(es * F(0.075)), 15 * (1 + sm), 20 * (1 + 2 * sm)) * 4) * F(0.4));
        else sk = isSwing ? F(F(c(mag + F(es * F(0.133)), 5 * (1 + sm), 15 * (1 + 2 * sm)) * 4) * F(RBM_CFG.ThrustMagnitudeModifier)) : mag;
        return mag > 1 ? sk : mag;
      case 'TwoHandedSword':
        if (dt === 'Cut') sk = F(c(mag + F(es * F(0.199)), 12 * (1 + sm), 20 * (1 + 2 * sm)) * F(4.6));
        else if (dt === 'Blunt') sk = F(F(c(mag + F(es * F(0.112)), 20 * (1 + sm), 26 * (1 + 2 * sm)) * 4) * F(0.4));
        else sk = isSwing ? F(F(c(mag + F(es * F(0.199)), 12 * (1 + sm), 20 * (1 + 2 * sm)) * 4) * F(RBM_CFG.ThrustMagnitudeModifier)) : mag;
        return mag > 1 ? sk : mag;
      case 'OneHandedAxe': case 'ThrowingAxe':
        sk = F(c(mag + F(es * F(0.1)), 10 * (1 + sm), 18 * (1 + 2 * sm)) * F(4.6));
        if (dt === 'Blunt') sk = F(F(c(mag + F(es * F(0.075)), 15 * (1 + sm), 20 * (1 + 2 * sm)) * 4) * F(0.3));
        return mag > 1 ? sk : mag;
      case 'TwoHandedAxe':
        sk = F(c(mag + F(es * F(0.15)), 15 * (1 + sm), 24 * (1 + 2 * sm)) * F(4.6));
        if (dt === 'Blunt') sk = F(F(c(mag + F(es * F(0.112)), 20 * (1 + sm), 26 * (1 + 2 * sm)) * 4) * F(0.3));
        return mag > 1 ? sk : mag;
      case 'Mace':
        sk = dt === 'Pierce' ? mag : F(c(mag + F(es * F(0.075)), 10 * (1 + sm), 15 * (1 + 2 * sm)) * F(4.6));
        return mag > 1 ? sk : mag;
      case 'TwoHandedMace':
        sk = dt === 'Pierce' ? mag : F(c(mag + F(es * F(0.1125)), 15 * (1 + sm), 22 * (1 + 2 * sm)) * F(4.6));
        return mag > 1 ? sk : mag;
      case 'OneHandedPolearm':
        if (dt === 'Cut') sk = F(c(mag + F(es * F(0.1)), 15 * (1 + sm), 24 * (1 + 2 * sm)) * 4);
        else if (dt === 'Blunt') sk = F(F(c(mag + F(es * F(0.075)), 15 * (1 + sm), 20 * (1 + 2 * sm)) * 4) * F(0.3));
        else sk = mag;
        return mag > F(0.15) ? sk : mag;
      case 'TwoHandedPolearm':
        if (dt === 'Cut') sk = F(c(mag + F(es * F(0.1495)), 18 * (1 + sm), 28 * (1 + 2 * sm)) * 4);
        else if (dt === 'Blunt') sk = F(F(c(mag + F(es * F(0.0975)), 20 * (1 + sm), 26 * (1 + 2 * sm)) * 4) * F(0.3));
        else sk = mag;
        return mag > F(0.15) ? sk : mag;
    }
    return mag;
  }
  // RBMConfig/Shared/BlowDamage.RBMComputeDamage (absorbedDamageRatio 1, no armor material).
  function computeDamage(cls, dt, mag, armor, wdf) {
    armor = F(armor * F(RBM_CFG.armorEffectivenessMultiplier));
    var red = F(F(100) / F(F(100) + F(armor * F(RBM_CFG.armorMultiplier))));
    var tbonus = 1 / RBM_CFG.ThrustMagnitudeModifier;
    var m = mag;
    if (dt === 'Pierce' && ['Dagger', 'ThrowingKnife', 'OneHandedSword', 'TwoHandedSword', 'OneHandedPolearm', 'TwoHandedPolearm', 'Mace', 'TwoHandedMace', 'Javelin', 'ThrowingAxe'].indexOf(cls) >= 0) m = F(mag * F(tbonus));
    var fac = WEAPON_TYPE_FACTORS[cls] || [1, 1, 1, 1];
    var thr = F(F(RBM_CFG.armorThresholdModifier) / wdf), pen = 0, blunt = 0, dmg = 0;
    if (dt === 'Blunt') {
      pen = Math.max(0, F(m - F(F(F(armor * 5) * thr))));
      var bf = m > 0 ? F(F(m - pen) / m) : 0;
      dmg = pen;
      var bt = F(F(F(F(m * F(F(0.7) * F(RBM_CFG.maceBluntModifier))) * bf) * F(RBM_CFG.bluntTraumaMultiplier)));
      blunt = Math.max(0, F(bt * red)); dmg = F(dmg + blunt);
    } else if (dt === 'Cut' || dt === 'Pierce') {
      var cut = dt === 'Cut';
      pen = Math.max(0, F(m - F(F(armor * F(cut ? fac[3] : fac[2])) * thr)));
      var bf2 = m > 0 ? F(F(m - pen) / m) : 0;
      dmg = pen;
      var bt2 = F(F(F(m * F(F(cut ? fac[0] : fac[1]) + F(RBM_CFG.bluntTraumaBonus))) * bf2) * F(RBM_CFG.bluntTraumaMultiplier));
      blunt = Math.max(0, F(bt2 * red)); dmg = F(dmg + blunt);
    }
    return { d: dmg, p: pen, b: blunt };
  }
  function damageRows(cls, dt, mag, wdf) {
    var rows = [];
    for (var a = 0; a <= 100; a += 10) {
      var r = computeDamage(cls, dt, mag, a, wdf);
      rows.push({ a: a, d: clampF(Math.floor(r.d), 0, 2000), p: Math.floor(r.p), b: Math.floor(r.b) });
    }
    return rows;
  }
  var ONE_H_FAST = ['LowGripPolearm', 'Mace', 'OneHandedAxe', 'OneHandedPolearm'];
  var SWORDS = ['OneHandedSword', 'Dagger', 'TwoHandedSword'];
  function rbmMeleeStats(u, weight, skill) {
    if (!u.melee) return null;
    var dr = effSkillDR(skill), sm = skillMod(skill), cls = u.cls, out = { skill: skill, dr: dr };
    // Utilities.CalculateVisualSpeeds (EquipmentElement overload): m/s = speed / 4.5454545 (swing), / 11.7647057 (thrust).
    var cts = function () { return Math.floor(F(rbmThrustSpeed(weight, u.totalInertia, u.com) * F(11.7647057))); };
    var sw = -1, th = -1, hd = -1;
    if (ONE_H_FAST.indexOf(cls) >= 0) {
      sw = Math.ceil(F(F(u.swingSpeed * F(0.83)) * F(1 + F(dr / 800)))); th = Math.ceil(F(F(cts() * F(1.1)) * F(1 + F(dr / 900)))); hd = Math.ceil(F(F(u.handling * F(0.83)) * F(1 + F(dr / 600))));
    } else if (cls === 'TwoHandedPolearm' || cls === 'TwoHandedMace') {
      sw = Math.ceil(F(F(u.swingSpeed * F(0.83)) * F(1 + F(dr / 500)))); th = Math.ceil(F(F(cts() * F(1.05)) * F(1 + F(dr / 800)))); hd = Math.ceil(F(F(u.handling * 5) * F(1 + F(dr / 450))));
    } else if (cls === 'TwoHandedAxe') {
      sw = Math.ceil(F(F(u.swingSpeed * F(0.75)) * F(1 + F(dr / 450)))); th = Math.ceil(F(F(u.thrustSpeed * F(0.9)) * F(1 + F(dr / 900)))); hd = Math.ceil(F(F(u.handling * F(0.83)) * F(1 + F(dr / 400))));
    } else if (SWORDS.indexOf(cls) >= 0) {
      sw = Math.ceil(F(F(u.swingSpeed * F(0.83)) * F(1 + F(dr / 650)))); th = Math.ceil(F(F(cts() * F(1.15)) * F(1 + F(dr / 700)))); hd = Math.ceil(F(F(u.handling * F(0.9)) * F(1 + F(dr / 600))));
    }
    out.swingMs = sw < 0 ? null : F(sw / F(4.5454545)); out.thrustMs = th < 0 ? null : F(th / F(11.7647057)); out.handling = hd < 0 ? null : hd;
    // CalculateSweetSpotSwingMagnitude
    if (u.swingDamage > 0) {
      var ss = F(F(u.swingSpeed / F(4.5454545)) * 1);
      if (ONE_H_FAST.indexOf(cls) >= 0 || cls === 'TwoHandedMace' || cls === 'TwoHandedPolearm') ss = F(F(F(ss * F(0.83)) * F(1 + F(dr / 1000))) * 1);
      else if (cls === 'TwoHandedAxe') ss = F(F(F(ss * F(0.75)) * F(1 + F(dr / 800))) * 1);
      else if (SWORDS.indexOf(cls) >= 0) ss = F(F(F(ss * F(0.83)) * F(1 + F(dr / 800))) * 1);
      // GetRealWeaponLength: weapon_length × 0.01 + Frame.rotation.u · Frame.origin (−CoM with use_center_of_mass_as_hand_base).
      var realLen = F(F(u.length * F(0.01)) + (u.useCom ? F(-u.com) : 0));
      var best = -1, spot = -1;
      for (var cur = F(1); cur > F(0.35); cur = F(cur - F(0.01))) {
        var mg = strikeSwing(ss, cur, weight, realLen, u.totalInertia, u.com, 0);
        if (mg > best) { best = mg; spot = cur; }
      }
      var sbd = skillBasedDamage(best, cls, u.swingType, dr, sm, true), wdf = F(Math.sqrt(u.swingFactor));
      out.swing = { magnitude: best, sweetSpot: spot, skillDamage: sbd, factor: wdf, rows: damageRows(cls, u.swingType, sbd, wdf) };
    }
    if (u.thrustDamage > 0) {
      var ts = F(F(u.thrustSpeed / F(11.7647057)) * 1), tsp = ts;
      if (ONE_H_FAST.indexOf(cls) >= 0 || cls === 'TwoHandedMace') tsp = F(F(F(rbmThrustSpeed(weight, u.totalInertia, u.com) * F(0.75)) * F(1 + F(dr / 1000))) * 1);
      else if (cls === 'TwoHandedPolearm') tsp = F(F(F(rbmThrustSpeed(weight, u.totalInertia, u.com) * F(0.65)) * F(1 + F(dr / 800))) * 1);
      else if (cls === 'TwoHandedAxe') tsp = F(F(F(rbmThrustSpeed(weight, u.totalInertia, u.com) * F(0.9)) * F(1 + F(dr / 1000))) * 1);
      else if (SWORDS.indexOf(cls) >= 0) tsp = F(F(F(rbmThrustSpeed(weight, u.totalInertia, u.com) * F(0.7)) * F(1 + F(dr / 800))) * 1);
      var tm = -1;
      if (['OneHandedPolearm', 'OneHandedSword', 'Dagger', 'Mace', 'LowGripPolearm'].indexOf(cls) >= 0) tm = thrustMag1H(weight, dr, tsp, 0);
      else if (['TwoHandedPolearm', 'TwoHandedSword', 'TwoHandedMace'].indexOf(cls) >= 0) tm = thrustMag2H(weight, dr, tsp, 0);
      var tbd = skillBasedDamage(tm, cls, u.thrustType, dr, sm, false), twdf = F(Math.sqrt(u.thrustFactor));
      out.thrust = { magnitude: tm, speed: tsp, skillDamage: tbd, factor: twdf, rows: damageRows(cls, u.thrustType, tbd, twdf) };
    }
    return out;
  }

  // RBM/CraftingCoveragePatches.cs HealUncoveredPieces (combat on): a template piece no weapon description lists is
  // added to the description that covers most of the template's pieces. Mutates nothing: returns desc id -> avail.
  function healedAvail(templateOrder, templateOf, descOf, pieceOf) {
    var avail = {};
    var get = function (d) { if (!avail[d]) { var x = descOf(d); avail[d] = x ? x.avail.slice() : []; } return avail[d]; };
    var healed = [];
    templateOrder.forEach(function (tid) {
      var t = templateOf(tid);
      if (!t || !t.descs.length) return;
      t.usable.forEach(function (pid) {
        var pc = pieceOf(pid);
        if (!pc || !t.buildOrders.some(function (b) { return b.type === pc.type; })) return;
        if (t.descs.some(function (d) { return get(d).indexOf(pid) >= 0; })) return;
        var best = t.descs[0], bestCount = -1;
        t.descs.forEach(function (d) {
          var av = get(d), c = t.usable.filter(function (p) { return av.indexOf(p) >= 0; }).length;
          if (c > bestCount) { best = d; bestCount = c; }
        });
        get(best).push(pid);
        healed.push({ template: tid, piece: pid, desc: best });
      });
    });
    return { avail: avail, healed: healed };
  }

  // The design a CraftedItem element describes (ItemObject.Deserialize: one piece per Type, the last wins).
  function parseCrafted(text) {
    var ci = childInfo(text), a = attrsOf(ci.st.text), d = { id: a.id, template: a.crafting_template, value: a.value, pieces: {}, attrs: a };
    elKids(ci.kids, 'Pieces').slice(0, 1).forEach(function (pk) {
      var pt = text.slice(pk.start, pk.end);
      elKids(childInfo(pt).kids, 'Piece').forEach(function (p) {
        var pa = attrsOf(T.startTagOf(pt.slice(p.start, p.end)).text);
        if (PIECE_TYPE_INDEX[pa.Type] == null) return;
        d.pieces[pa.Type] = { id: pa.id, scale: pa.scale_factor != null && isInt(pa.scale_factor) ? parseInt(pa.scale_factor, 10) : 100 };
      });
    });
    // Crafting.GetXmlCodeForCurrentItem's comment, when the item has one: what the smithy computed when it was written.
    var cm = /<!--\s*Length:\s*([\d.]+)\s*Weight:\s*([\d.]+)(?:\s*SwingSpeed:\s*(\d+))?(?:\s*ThrustSpeed:\s*(\d+))?/.exec(text);
    if (cm) d.truth = { length: +cm[1], weight: +cm[2], swingSpeed: cm[3] != null ? +cm[3] : null, thrustSpeed: cm[4] != null ? +cm[4] : null };
    return d;
  }

  // ---------------------------------------------------------------- states: the game's objects for a set of files
  // mode 'file': the files as generated; 'cur': the files with the work in progress (work.models: "kind|id" -> edited
  // model; work.news: [{ kind, id, model }]); 'van': without RBM's files (templates and weapon descriptions from the
  // pipeline without RBM, pieces from their non-RBM definitions). opts: { rbm: RBM's formulas (default: mode !== 'van'),
  // heal: RBM/CraftingCoveragePatches (default: mode !== 'van'), skill: weapon skill for RBM's tooltip numbers,
  // campaign: false to let pieces that only multiplayer files define exist (default: a campaign, where they do not) }.

  function modelOfDef(d) { return d._model || (d._model = parseModel(d.text)); }
  function createState(data, mode, work, opts) {
    work = work || {}; opts = opts || {};
    var models = work.models || {}, news = (mode === 'cur' && work.news) || [];
    var rbm = opts.rbm != null ? opts.rbm : mode !== 'van', heal = opts.heal != null ? opts.heal : mode !== 'van';
    var newModel = {};
    news.forEach(function (n) { newModel[n.kind + '|' + n.id] = n.model; });
    var pieceCache = {}, tplCache = {}, healed = null, extra = null;
    var editedOf = function (key) { return mode === 'cur' ? models[key] : null; };
    // The definitions of an id in this state, as models in load order.
    function pieceModels(id) {
      var key = 'CraftingPiece|' + id;
      if (newModel[key] && !data.pieces[id]) return [newModel[key]];
      var rec = data.pieces[id];
      // A piece only multiplayer files define does not exist in a campaign (opts.campaign false: it does).
      if (!rec || (rec.nc && opts.campaign !== false)) return null;
      var defs = mode === 'van' ? rec.defs.filter(function (d) { return !d.rbm; }) : rec.defs;
      if (!defs.length) return null;
      var ed = editedOf(key);
      if (!ed) return defs.map(modelOfDef);
      var ent = entityOf(data, 'CraftingPiece', id);
      if (!ent.rbmPaths.length) return defs.map(modelOfDef).concat([ed]);
      var ops = diff(parseModel(baseText(ent).text), ed);
      return defs.map(function (d) { if (!d.rbm) return modelOfDef(d); var m = cloneModel(modelOfDef(d)); applyOps(m, ops); return m; });
    }
    function templateDefs(id) {
      var key = 'CraftingTemplate|' + id;
      if (newModel[key] && !data.templates[id]) return [{ model: newModel[key], label: 'new' }];
      var rec = data.templates[id];
      if (!rec) return null;
      var defs = mode === 'van' ? (rec.vdefs || []) : rec.defs;
      if (!defs.length) return null;
      var lab = function (d) { return d.src; };
      var ed = editedOf(key);
      if (!ed) return defs.map(function (d) { return { model: modelOfDef(d), add: d.xsltAdd, label: lab(d) }; });
      var ent = entityOf(data, 'CraftingTemplate', id);
      if (!ent.rbmPaths.length) return defs.map(function (d) { return { model: modelOfDef(d), add: d.xsltAdd, label: lab(d) }; }).concat([{ model: ed, label: 'edited copy' }]);
      var ops = diff(parseModel(baseText(ent).text), ed);
      return defs.map(function (d) {
        if (!d.rbm) return { model: modelOfDef(d), add: d.xsltAdd, label: lab(d) };
        var m = cloneModel(modelOfDef(d)); applyOps(m, ops);
        return { model: m, add: d.xsltAdd, label: lab(d) + ' (edited)' };
      });
    }
    function piece(id) {
      if (id == null) return null;
      if (Object.prototype.hasOwnProperty.call(pieceCache, id)) return pieceCache[id];
      var ms = pieceModels(id), p = ms ? effPiece(ms, data) : null;
      if (p) p.id = id;
      pieceCache[id] = p;
      return p;
    }
    // Pieces that add themselves to templates (a CraftingPiece's <CraftingTemplates> child, read before the templates).
    function extras() {
      if (extra) return extra;
      extra = {};
      var ids = Object.keys(data.pieces).filter(function (id) { return data.pieces[id].defs.some(function (d) { return d.text.indexOf('<CraftingTemplates') >= 0; }); });
      Object.keys(models).concat(Object.keys(newModel)).forEach(function (k) {
        if (k.indexOf('CraftingPiece|') === 0) { var id = k.slice(14); if (ids.indexOf(id) < 0) ids.push(id); }
      });
      ids.forEach(function (id) { var p = piece(id); if (p) p.templates.forEach(function (t) { (extra[t] = extra[t] || []).push(id); }); });
      return extra;
    }
    function template(id) {
      if (id == null) return null;
      if (Object.prototype.hasOwnProperty.call(tplCache, id)) return tplCache[id];
      var defs = templateDefs(id), t = null;
      if (defs) {
        t = effTemplate(defs, data, function (pid) { return !!piece(pid); }, function (did) { return !!desc(did); });
        // Those pieces are added while the pieces load, before any template definition: first in the list.
        var pre = (extras()[id] || []).filter(function (pid) { return t.usable.indexOf(pid) < 0 && piece(pid); });
        pre.forEach(function (pid) { t.usableFrom[pid] = 'piece ' + pid + '\'s <CraftingTemplates>'; });
        t.usable = pre.concat(t.usable);
        t.id = id;
      }
      tplCache[id] = t;
      return t;
    }
    function desc(id) {
      var r = data.descriptions && data.descriptions[id];
      if (!r) return null;
      return mode === 'van' ? r.van : r.rbm;
    }
    function templateOrder() {
      var ids = Object.keys(data.templates || {});
      Object.keys(newModel).forEach(function (k) { if (k.indexOf('CraftingTemplate|') === 0 && ids.indexOf(k.slice(17)) < 0) ids.push(k.slice(17)); });
      return ids;
    }
    function healInfo() {
      if (healed) return healed;
      healed = heal ? healedAvail(templateOrder(), template, desc, piece) : { avail: {}, healed: [] };
      return healed;
    }
    function avail(did) { var h = healInfo().avail[did]; if (h) return h; var d = desc(did); return d ? d.avail : []; }
    function calcItem(item) {
      var text = mode === 'van' && item.vanText ? item.vanText : item.text;
      var key = mode === 'van' ? '_vdesign' : '_design';
      var d = item[key] || (item[key] = parseCrafted(text));
      return calcDesign(d, ctxFor(d.value));
    }
    function ctxFor(value) { return { piece: piece, template: template, desc: desc, avail: avail, rbm: rbm, skill: opts.skill, value: value }; }
    return {
      mode: mode, rbm: rbm, heal: heal, piece: piece, template: template, desc: desc, avail: avail, healInfo: healInfo, templateOrder: templateOrder,
      pieceModels: pieceModels, templateDefs: templateDefs, calcItem: calcItem,
      calc: function (design, value) { return calcDesign(design, ctxFor(value)); }
    };
  }

  // ---------------------------------------------------------------- problem checks

  var REF_WORD = { culture: 'culture', modgroup: 'item modifier group', holsters: 'item holster', desc: 'weapon description', piece: 'crafting piece', template: 'crafting template' };
  function refExists(spec, v, data) {
    var id = spec.prefix ? v.slice(spec.prefix.length) : v;
    switch (spec.ref) {
      case 'culture': return !!(data.cultures && data.cultures[id]);
      case 'modgroup': return (data.modifierGroups || []).indexOf(id) >= 0;
      case 'holsters': return !!(data.holsters && data.holsters[id]);
      case 'desc': return !!(data.descriptions && data.descriptions[id]);
      case 'piece': return !!(data.pieces && data.pieces[id]) || !!(data.newPieces && data.newPieces[id]);
      case 'template': return !!(data.templates && data.templates[id]);
    }
    return true;
  }
  var DEC_TYPES = { 'xs:decimal': 1, 'xs:float': 1, 'xs:double': 1 };
  var UINT_TYPES = { 'xs:unsignedInt': 1, 'xs:unsignedShort': 1, 'xs:integer': 1 };
  function attrKind(vkey, info) {
    if (INT_ATTRS[vkey]) return 'int';
    if (BOOL_ATTRS[vkey]) return 'bool';
    if (!info) return 'str';
    if (UINT_TYPES[info.type]) return 'int';
    if (DEC_TYPES[info.type]) return 'dec';
    if (info.type === 'xs:boolean') return 'bool';
    return 'str';
  }
  // Checks one element's attributes against the XSD and the game's parsers.
  function checkAttrs(add, data, vpath, attrs, where) {
    var vocab = (data.vocab && data.vocab[vpath]) || null;
    Object.keys(attrs).forEach(function (n) {
      var v = attrs[n], key = vpath + '@' + n, info = vocab ? vocab[n] : null;
      if (vocab && !info) { if (n !== '_replaceWhileMerging') add('warn', where + ': attribute ' + n + ' is not in the XSD for <' + vpath.split('/').pop() + '> (the game ignores it)'); return; }
      var k = attrKind(key, info);
      if (k === 'int' && !isInt(v)) add(INT_ATTRS[key] === 'soft' ? 'warn' : 'bad', where + ': ' + n + '="' + v + '" is not a whole number' + (INT_ATTRS[key] === 'soft' ? ' (int.TryParse fails: count 0)' : ' (int.Parse throws)'));
      else if (k === 'dec' && !isDec(v)) add('bad', where + ': ' + n + '="' + v + '" is not a number (float.Parse throws)');
      else if (k === 'bool' && toBool(v) == null) add('bad', where + ': ' + n + '="' + v + '" is not true/false (Convert.ToBoolean throws)');
      var es = ATTR_ENUMS[key];
      if (es) {
        var list = es.list0 || (data.enums && data.enums[es.e]) || [];
        var vals = es.sep ? String(v).split(es.sep) : [v];
        vals.forEach(function (x) {
          if (!enumHas(list, x, es.ci)) add(es.soft ? 'warn' : 'bad', where + ': ' + n + '="' + x + '" is not a ' + (es.e || 'valid') + ' value' + (es.soft ? ' (' + es.soft + ')' : ' (Enum.Parse' + (es.ci ? '' : ', case-sensitive,') + ' throws)'));
        });
      }
      var rs = ATTR_REFS[key];
      if (rs && v !== '') {
        if (rs.prefix && v.indexOf(rs.prefix) !== 0) { add('bad', where + ': ' + n + '="' + v + '" must start with "' + rs.prefix + '"'); return; }
        (rs.sep ? v.split(rs.sep) : [v]).forEach(function (x) {
          if (x !== '' && !refExists(rs, x, data)) add('warn', where + ': unknown ' + REF_WORD[rs.ref] + ' "' + (rs.prefix ? x.slice(rs.prefix.length) : x) + '"');
        });
      }
    });
    if (vocab) Object.keys(vocab).forEach(function (n) {
      if (vocab[n].use === 'required' && attrs[n] == null) add('warn', where + ': required attribute ' + n + ' (XSD) is missing');
    });
  }
  function checkModel(add, data, m) {
    checkAttrs(add, data, m.kind, m.attrs, m.kind);
    Object.keys(m.els).forEach(function (s) { checkAttrs(add, data, m.kind + '/' + s, m.els[s], s); });
    Object.keys(m.lists).forEach(function (l) {
      var spec = listSpec(m.kind, l), lab = l.replace('|', ' ');
      if (!spec) return;
      checkAttrs(add, data, m.kind + '/' + spec.name, m.lists[l].attrs, lab);
      m.lists[l].items.forEach(function (it) {
        checkAttrs(add, data, m.kind + '/' + spec.name + '/' + spec.item, it.attrs, lab + ' ' + it.k);
        if (/#\d+$/.test(it.k)) add('warn', lab + ': ' + it.k.replace(/#\d+$/, '') + ' is listed more than once');
      });
    });
  }
  // What a CraftingPiece start-tag attribute is when absent (CraftingPiece.Deserialize), for the "lost vanilla
  // attribute" check: RBM's definition is deserialized after vanilla's and sets every start-tag attribute again.
  function pieceDefault(n, m) {
    switch (n) {
      case 'appearance': case 'center_of_mass': return '0.5';
      case 'CraftingCost': case 'weight': case 'required_skill_value': return '0';
      case 'tier': return '1';
      case 'is_unique': case 'is_default': case 'is_hidden': return 'false';
      case 'full_scale': return (canon(['Guard', 'Pommel'], m.attrs.piece_type || '') ? 'true' : 'false');
      case 'excluded_item_usage_features': return '';
      case 'item_holster_pos_shift': return '0,0,0';
      case 'culture': return '(none)';
    }
    return null;
  }
  function sameValue(n, a, b) {
    if (a === b) return true;
    if (isDec(String(a)) && isDec(String(b))) return +a === +b;
    if (n === 'item_holster_pos_shift') return String(a).split(',').map(Number).join() === String(b).split(',').map(Number).join();
    var ba = toBool(a), bb = toBool(b);
    return ba != null && ba === bb;
  }
  // Notes on what the game makes of this definition after the vanilla one(s): lost start-tag attributes (absent =
  // the default) and kept child elements (absent = vanilla's stays). v0: the vanilla definition's model.
  function replayNotes(m, v0) {
    var out = [];
    if (!v0 || v0.kind !== m.kind) return out;
    if (m.kind === 'CraftingPiece') {
      var lost = Object.keys(v0.attrs).filter(function (n) {
        if (m.attrs[n] != null || n === 'id') return false;
        if (n === 'length' && m.attrs.distance_to_next_piece != null) return false;
        if ((n === 'distance_to_next_piece' || n === 'distance_to_previous_piece') && m.attrs.length != null) return false;
        var d = pieceDefault(n, v0);
        return d == null || !sameValue(n, v0.attrs[n], d);
      });
      if (lost.length) out.push({ cls: 'warn', text: 'vanilla sets ' + lost.map(function (n) { return n + '="' + v0.attrs[n] + '"'; }).join(', ') +
        ' but this definition does not: the game deserializes it after vanilla\'s and resets ' + (lost.length > 1 ? 'them' : 'it') + ' to the default (' +
        lost.map(function (n) { var d = pieceDefault(n, v0); return n + ' ' + (d == null ? 'unset' : d === '' ? '""' : d); }).join(', ') + ')' });
      ['BladeData', 'BuildData', 'StatContributions'].forEach(function (s) {
        if (v0.els[s] && !m.els[s]) out.push({ cls: 'info', text: 'no <' + s + '>: vanilla\'s is kept (the game only re-reads child elements that are present)' });
      });
      ['Materials', 'Flags'].forEach(function (l) {
        if (v0.lists[l] && !m.lists[l]) out.push({ cls: 'info', text: 'no <' + l + '>: vanilla\'s is kept' });
      });
    } else if (m.kind === 'CraftingTemplate') {
      if (v0.attrs.modifier_group != null && m.attrs.modifier_group == null) out.push({ cls: 'info', text: 'no modifier_group: vanilla\'s (' + v0.attrs.modifier_group + ') is kept' });
      ['piece_type_to_scale_holster_with', 'hidden_piece_types_on_holster', 'use_weapon_as_holster_mesh', 'always_show_holster_with_weapon', 'rotate_weapon_in_holster'].forEach(function (n) {
        if (v0.attrs[n] != null && m.attrs[n] == null && !(toBool(v0.attrs[n]) === false)) out.push({ cls: 'warn', text: 'vanilla sets ' + n + '="' + v0.attrs[n] + '" but this definition does not: the game resets it' });
      });
      ['PieceDatas', 'WeaponDescriptions'].forEach(function (l) {
        if (v0.lists[l] && !m.lists[l]) out.push({ cls: 'info', text: 'no <' + l + '>: vanilla\'s is kept' });
      });
    }
    return out;
  }

  // Problems of an entity's current state: [{cls: 'bad'|'warn'|'info', text}].
  // info: { ent (entityOf, null for a new entity), isNew, edited, targets (append targets), allIds ("kind|id" -> true),
  //   newIds ("kind|id" -> count), vanModel (the vanilla definition's model), eff (effPiece/effTemplate in the edited
  //   state), pieceType(id) -> type (edited state), vanUsable (template: piece ids vanilla lists) }.
  function problems(data, model, info) {
    var out = [], add = function (cls, text) { out.push({ cls: cls, text: text }); };
    var ent = info.ent, kind = model.kind, id = model.attrs.id, inRbm = !!(ent && ent.rbmPaths.length);
    if (!id) add('bad', 'No id');
    if (info.isNew) {
      if (id && info.allIds && info.allIds[kind + '|' + id]) add('bad', 'The id "' + id + '" already exists: pick a new one');
      if (id && info.newIds && info.newIds[kind + '|' + id] > 1) add('bad', 'Two new entries share the id "' + id + '"');
      if (id && !/^[\w.-]+$/.test(id)) add('warn', 'The id "' + id + '" has characters other than letters, digits, _ . -');
    } else if (ent && id !== ent.id) add('bad', 'The id was changed: use "Duplicate" instead');
    if ((info.isNew || (!inRbm && info.edited)) && !(info.targets && info.targets.length)) add('bad', 'No RBM file to write it to: pick a target file');
    checkModel(add, data, model);
    (info.eff && info.eff.errors || []).forEach(function (t) { add('bad', t); });
    if (kind === 'CraftingPiece') {
      var e = info.eff;
      if (e && e.type === 'Blade' && !e.blade) add('bad', 'A Blade piece without <BladeData>: Crafting reads its BladeData and throws');
      if (e && e.blade && e.type === 'Blade') {
        if (e.blade.swingType === 'Invalid' && e.blade.thrustType === 'Invalid') add('warn', 'No Swing and no Thrust in <BladeData>: no damage at all');
        if (!e.blade.physics) add('warn', '<BladeData> has no physics_material');
      }
      if (model.attrs.length == null && (model.attrs.distance_to_next_piece == null || model.attrs.distance_to_previous_piece == null)) add('bad', 'Needs length, or distance_to_next_piece and distance_to_previous_piece');
      if (model.attrs.length != null && (model.attrs.distance_to_next_piece != null || model.attrs.distance_to_previous_piece != null)) add('info', 'length is set, so distance_to_next_piece/distance_to_previous_piece are ignored (each is length / 2)');
      if (model.attrs.full_scale != null && model.attrs.full_scale !== 'true' && model.attrs.full_scale !== 'false') add('warn', 'full_scale="' + model.attrs.full_scale + '": only the exact text "true" turns it on');
    } else if (kind === 'CraftingTemplate') {
      var t = info.eff, types = t ? t.buildOrders.map(function (b) { return b.type; }) : [];
      var up = model.lists.UsablePieces;
      if (up && t) up.items.forEach(function (it) {
        var pt = info.pieceType ? info.pieceType(it.attrs.piece_id) : null;
        if (pt && types.length && types.indexOf(pt) < 0) add('warn', 'UsablePiece ' + it.attrs.piece_id + ' is a ' + pt + ' piece, a type this template does not build (Crafting.Init never offers it)');
      });
      if (info.vanUsable && up) {
        var mine = up.items.map(function (i) { return i.attrs.piece_id; });
        var gone = (info.baseUsable || []).filter(function (p) { return mine.indexOf(p) < 0 && info.vanUsable.indexOf(p) >= 0; });
        if (gone.length) add('info', 'Removed from this list but vanilla lists ' + (gone.length > 1 ? 'them' : 'it') + ' too (' + gone.join(', ') + '): usable pieces only accumulate, so ' + (gone.length > 1 ? 'they stay' : 'it stays') + ' usable');
      }
      Object.keys(model.lists).forEach(function (l) {
        if (l.indexOf('StatsData|') !== 0) return;
        var wd = model.lists[l].attrs.weapon_description, descs = model.lists.WeaponDescriptions ? model.lists.WeaponDescriptions.items.map(function (i) { return i.attrs.id; }) : (t ? t.descs : []);
        if (wd != null && descs.indexOf(wd) < 0) add('bad', 'StatsData for ' + wd + ', which is not one of the template\'s weapon descriptions (the game throws)');
      });
    }
    if (inRbm || (info.edited && !info.isNew) || info.isNew) replayNotes(model, info.vanModel).forEach(function (n) { out.push(n); });
    if (ent && ent.rbmPaths.length > 1) add('info', 'Defined in ' + ent.rbmPaths.length + ' RBM files (' + ent.rbmPaths.join(', ') + '): the export patches the changed attributes in each');
    if (ent && ent.rbmDefs.length > ent.rbmPaths.length) add('warn', 'Defined more than once in the same RBM file: the game reads each in turn; the export patches every copy');
    return out;
  }

  // ---------------------------------------------------------------- export: patching an element's text

  // The token of the single element s ('BladeData' or 'BladeData/Thrust') in elText: {start, end} or null (the last
  // one of that name, as the game's last read wins).
  function findSingle(elText, s) {
    var parts = s.split('/'), base = 0, text = elText, tok = null;
    for (var i = 0; i < parts.length; i++) {
      var kk = elKids(childInfo(text).kids, parts[i]);
      if (!kk.length) return null;
      tok = kk[kk.length - 1];
      if (i < parts.length - 1) { base += tok.start; text = text.slice(tok.start, tok.end); }
    }
    return { start: base + tok.start, end: base + tok.end, parentStart: parts.length > 1 ? base : 0 };
  }
  function findList(elText, kind, l) {
    var spec = listSpec(kind, l);
    if (!spec) return null;
    var hit = null;
    elKids(childInfo(elText).kids, spec.name).forEach(function (k) {
      if (spec.keyAttr) {
        var a = attrsOf(T.startTagOf(elText.slice(k.start, k.end)).text), kv = a[spec.keyAttr] == null ? '' : a[spec.keyAttr];
        if (kv !== spec.kv) return;
      }
      hit = k;
    });
    return hit ? { start: hit.start, end: hit.end, spec: spec } : null;
  }
  function findItem(listText, spec, k) {
    var m = /^(.*?)(?:#(\d+))?$/.exec(k), key = m[1], nth = m[2] ? +m[2] : 1, seen = 0, hit = null;
    elKids(childInfo(listText).kids, spec.item).forEach(function (it) {
      if (hit) return;
      var a = attrsOf(T.startTagOf(listText.slice(it.start, it.end)).text), kv = a[spec.key] == null ? '' : a[spec.key];
      if (kv === key && ++seen === nth) hit = it;
    });
    return hit;
  }
  function sampleFor(ctx, name, ownText) { return sampleIn(ownText || '', name) || ctx.samples[name] || null; }
  // Applies ops (see diff) to an element's text. indent: the element's indentation in its file. Returns { text, notes }.
  function patchEntity(text, kind, ops, ctx, indent) {
    var notes = [], own = text;
    text = patchStartTag(text, ops.filter(function (o) { return o.p === 'attr'; }));
    var newEl = function (name, attrsPairs, parentText, parentIndent, open) {
      var ci = childInfo(parentText), childIndent = childIndentOf(parentText, ci.kids, parentIndent, ctx);
      return { indent: childIndent, text: makeTag(name, attrsPairs, sampleFor(ctx, name, own), childIndent, defaultShape(ci.st.text, childIndent, ctx), open) };
    };
    var parentOf = function (s) {
      var i = s.lastIndexOf('/');
      if (i < 0) return { start: 0, end: text.length, indent: indent };
      var sp = findSingle(text, s.slice(0, i));
      return sp ? { start: sp.start, end: sp.end, indent: T.indentAt(text, sp.start) || indent + ctx.unit } : null;
    };
    var addSingle = function (s, attrsPairs) {
      var par = parentOf(s);
      if (!par) { notes.push(['bad', 'has no <' + s.split('/')[0] + '>: ' + s + ' not written']); return; }
      var pt = text.slice(par.start, par.end), name = s.split('/').pop();
      var ne = newEl(name, attrsPairs, pt, par.indent, false);
      // Root singles go after the other singles (BladeData, BuildData, StatContributions), else first.
      var after = null;
      if (s.indexOf('/') < 0) elKids(childInfo(pt).kids).forEach(function (k) { if (schemaOf(kind).singles.indexOf(k.name) >= 0) after = k; });
      pt = insertChild(pt, ne.text, ctx, par.indent, after, s.indexOf('/') < 0 && !after);
      text = text.slice(0, par.start) + pt + text.slice(par.end);
    };
    ops.forEach(function (o) {
      if (o.p === 'attr') return;
      if (o.p === 'el') {
        var sp = findSingle(text, o.s);
        if (o.on) { if (sp) { notes.push(['warn', o.s + ' already exists: attributes set on it']); var t0 = text.slice(sp.start, sp.end); text = text.slice(0, sp.start) + patchStartTag(t0, o.attrs.map(function (a) { return { n: a[0], v: a[1] }; })) + text.slice(sp.end); } else addSingle(o.s, o.attrs); }
        else if (sp) {
          var par = parentOf(o.s), pt = text.slice(par.start, par.end);
          text = text.slice(0, par.start) + removeChildTok(pt, { start: sp.start - par.start, end: sp.end - par.start }) + text.slice(par.end);
        }
        return;
      }
      if (o.p === 'elAttr') {
        var sp2 = findSingle(text, o.s);
        if (!sp2) { if (o.v != null) addSingle(o.s, [[o.n, o.v]]); return; }
        var et = text.slice(sp2.start, sp2.end);
        text = text.slice(0, sp2.start) + patchStartTag(et, [o]) + text.slice(sp2.end);
        return;
      }
      if (o.p === 'list') {
        var ls = findList(text, kind, o.l);
        if (!o.on) { if (ls) text = removeChildTok(text, ls); return; }
        if (ls) { notes.push(['warn', o.l + ' already exists']); return; }
        var spec = listSpec(kind, o.l);
        var cont = newEl(spec.name, o.attrs, text, indent, true);
        var itemIndent = cont.indent + ctx.unit, lines = [cont.text];
        o.items.forEach(function (i) {
          lines.push(itemIndent + makeTag(spec.item, i[1], sampleFor(ctx, spec.item, own), itemIndent, { sep1: ' ', sepN: ' ', tail: ctx.selfClose }, false));
        });
        lines.push(cont.indent + '</' + spec.name + '>');
        text = insertChild(text, lines.join(ctx.eol), ctx, indent, null, false);
        return;
      }
      var lsp = findList(text, kind, o.l);
      if (!lsp) { notes.push(['bad', 'has no <' + o.l.replace('|', ' ') + '>: change not written']); return; }
      var lt = text.slice(lsp.start, lsp.end), lIndent = T.indentAt(text, lsp.start) || indent + ctx.unit, spec2 = lsp.spec;
      if (o.p === 'listAttr') lt = patchStartTag(lt, [o]);
      else if (o.p === 'item' && !o.on) {
        var it = findItem(lt, spec2, o.k);
        if (it) lt = removeChildTok(lt, it); else notes.push(['warn', o.l + ' ' + o.k + ' not found: not removed']);
      } else if (o.p === 'item') {
        var kids = elKids(childInfo(lt).kids, spec2.item), last = kids[kids.length - 1];
        var ci2 = childInfo(lt), cIndent = childIndentOf(lt, ci2.kids, lIndent, ctx);
        var sample = last ? { tag: T.startTagOf(lt.slice(last.start, last.end)).text, indent: T.indentAt(lt, last.start) || cIndent } : sampleFor(ctx, spec2.item, own);
        var nt = makeTag(spec2.item, o.attrs, sample, cIndent, defaultShape(ci2.st.text, cIndent, ctx), false);
        lt = insertChild(lt, nt, ctx, lIndent, last || null, false);
      } else if (o.p === 'itemAttr') {
        var it2 = findItem(lt, spec2, o.k);
        if (!it2) { notes.push(['bad', o.l + ' ' + o.k + ' not found: ' + o.n + ' not written']); return; }
        var itt = lt.slice(it2.start, it2.end);
        lt = lt.slice(0, it2.start) + patchStartTag(itt, [o]) + lt.slice(it2.end);
      }
      text = text.slice(0, lsp.start) + lt + text.slice(lsp.end);
    });
    return { text: text, notes: notes };
  }

  // ---------------------------------------------------------------- export: files

  var ctxCache = {};
  var SAMPLE_NAMES = ['BladeData', 'BuildData', 'StatContributions', 'Thrust', 'Swing', 'Materials', 'Material', 'Flags', 'Flag', 'PieceDatas', 'PieceData',
    'WeaponDescriptions', 'WeaponDescription', 'StatsData', 'StatData', 'UsablePieces', 'UsablePiece', 'CraftingTemplates', 'CraftingTemplate'];
  function fileCtx(f) {
    if (ctxCache[f.path] && ctxCache[f.path].src === f.text) return ctxCache[f.path];
    var text = f.text, eol = f.eol || '\n';
    var m = /^([ \t]*)<(CraftingPiece|CraftingTemplate)\b/m.exec(text);
    var entIndent = m ? m[1] : '  ';
    var unit = T.unitFor(entIndent);
    if (m) {
      var at = m.index + m[1].length, node = T.parseChildren(text, at, Math.min(text.length, at + 400000))[0];
      if (node && node.kind === 'el') unit = detectUnit(text.slice(node.start, node.end).replace(/\r/g, ''), entIndent);
    } else {
      // An empty file (RBMCombat_sword_pieces.xml): the indentation of its comments.
      var c = /^([ \t]+)<!--/m.exec(text);
      if (c) { entIndent = c[1]; unit = T.unitFor(entIndent); }
    }
    var spaced = (text.match(/ \/>/g) || []).length, tight = (text.match(/[^ ]\/>/g) || []).length;
    var ctx = { src: text, eol: eol, entIndent: entIndent, unit: unit, selfClose: spaced >= tight ? ' />' : '/>', samples: {} };
    SAMPLE_NAMES.forEach(function (n) { ctx.samples[n] = sampleIn(text, n); });
    ctxCache[f.path] = ctx;
    return ctx;
  }
  // "kind|id" -> [{start, end}] of the top-level entities of a file.
  function entitySpans(text) {
    var r = T.rootRange(text), map = {};
    T.parseChildren(text, r.innerStart, r.innerEnd).forEach(function (n) {
      if (n.kind !== 'el') return;
      var id = attrsOf(text.slice(n.start, n.tagEnd)).id;
      if (id == null) return;
      (map[n.name + '|' + id] = map[n.name + '|' + id] || []).push({ start: n.start, end: n.end });
    });
    return map;
  }

  // An entity: { kind, id, defs (data defs), rbmDefs: [defs in RBM files], rbmPaths: [unique paths] }.
  function entityOf(data, kind, id) {
    var rec = kind === 'CraftingPiece' ? data.pieces[id] : data.templates[id];
    if (!rec) return null;
    var rbmDefs = rec.defs.filter(function (d) { return d.rbm; }), paths = [];
    rbmDefs.forEach(function (d) { if (paths.indexOf(d.rel) < 0) paths.push(d.rel); });
    return { kind: kind, id: id, rec: rec, defs: rec.defs, rbmDefs: rbmDefs, rbmPaths: paths };
  }
  // What an edit is measured against: the last RBM definition, else the text a copy starts from (the last vanilla
  // definition, before any XSLT).
  function baseText(ent) {
    if (ent.rbmDefs.length) { var d = ent.rbmDefs[ent.rbmDefs.length - 1]; return { text: d.text, indent: d.indent || '', src: d.src, rbm: true }; }
    var v = ent.defs[ent.defs.length - 1];
    return { text: v.srcText || v.text, indent: (v.srcText ? v.srcIndent : v.indent) || '', src: v.src + (v.reformatted && !v.srcText ? ' (merged)' : ''), rbm: false };
  }

  // piece id -> the templates whose definitions list it in UsablePieces (any definition, any pipeline).
  function pieceTemplateIndex(data) {
    if (data._ptIndex) return data._ptIndex;
    var idx = {};
    Object.keys(data.templates || {}).forEach(function (tid) {
      var rec = data.templates[tid];
      rec.defs.concat(rec.vdefs || []).forEach(function (d) {
        var re = /\bpiece_id\s*=\s*"([^"]*)"/g, m;
        while ((m = re.exec(d.text))) { var l = idx[m[1]] || (idx[m[1]] = []); if (l.indexOf(tid) < 0) l.push(tid); }
        (d.xsltAdd || []).forEach(function (p) { var l = idx[p] || (idx[p] = []); if (l.indexOf(tid) < 0) l.push(tid); });
      });
    });
    data._ptIndex = idx;
    return idx;
  }
  // Default file for a piece or template not yet in an RBM file. Pieces: War Sails pieces go to RBM_WS_XML's piece
  // file; others to the RBM piece file whose pieces are used by the same templates most often; templates: the RBM
  // template file. usedBy: the templates using the piece (default: those whose XML lists it).
  function defaultTarget(data, kind, id, model, usedBy) {
    var files = (data.rbmFiles || []).filter(function (f) { return f.kind === kind; });
    if (kind === 'CraftingTemplate') return files.length ? [files[0].path] : [];
    var rec = data.pieces[id], origin = rec && rec.defs.length ? rec.defs[0].module : '';
    if (origin === 'NavalDLC') { var ws = files.filter(function (f) { return f.naval; })[0]; if (ws) return [ws.path]; }
    var idx = pieceTemplateIndex(data), mine = usedBy || idx[id] || [], best = null, bestScore = 0;
    var spansOf = data._fileIds || (data._fileIds = {});
    files.forEach(function (f) {
      if (f.naval || !f.count) return;
      var score = 0;
      if (!spansOf[f.path]) spansOf[f.path] = Object.keys(entitySpans(f.text));
      spansOf[f.path].forEach(function (k) {
        (idx[k.slice(k.indexOf('|') + 1)] || []).forEach(function (t) { if (mine.indexOf(t) >= 0) score++; });
      });
      if (score > bestScore) { best = f; bestScore = score; }
    });
    return best ? [best.path] : [];
  }

  // The element text appended for a copy of ent with model cur, in file f.
  function appendText(base, cur, f, newId) {
    var ctx = fileCtx(f);
    var raw = base.text.replace(/\r\n/g, '\n'), srcIndent = base.indent || '';
    var el = restyle(raw, srcIndent, ctx.entIndent, detectUnit(raw, srcIndent), ctx.unit);
    var orig = parseModel(base.text);
    var r = patchEntity(el, orig.kind, diff(orig, cur), Object.assign({}, ctx, { eol: '\n' }), ctx.entIndent);
    return { text: T.normEol(r.text, ctx.eol), notes: r.notes };
  }

  // edits: "kind|id" -> { model, target } for existing entities (only those that differ are exported);
  // news: [{ kind, id, from (source id), model, target }]. Returns { files: [{ path, bom, eol, text, original,
  // items: [{ key, kind, id, mode: 'patch'|'append'|'new', summary, notes }] }], messages: [[cls, text]] }.
  // opts.all: also return files without changes (tests).
  function buildExport(data, edits, news, opts) {
    opts = opts || {};
    var byFile = {}, messages = [];
    var job = function (path) { return byFile[path] || (byFile[path] = { patch: [], append: [] }); };
    Object.keys(edits || {}).forEach(function (key) {
      var kind = key.split('|')[0], id = key.slice(kind.length + 1), ent = entityOf(data, kind, id), e = edits[key];
      if (!ent) { messages.push(['bad', 'Unknown ' + kind + ' "' + id + '" in the work in progress: skipped']); return; }
      var base = baseText(ent), orig = parseModel(base.text), ops = diffWithOld(orig, e.model);
      if (!ops.length) return;
      if (ent.rbmPaths.length) ent.rbmPaths.forEach(function (p) { job(p).patch.push({ key: key, kind: kind, id: id, ops: ops }); });
      else {
        var targets = e.target && e.target.length ? e.target : defaultTarget(data, kind, id, e.model, e.usedBy);
        if (!targets.length) { messages.push(['bad', id + ': no RBM file to append it to (pick a target file): not exported']); return; }
        targets.forEach(function (p) { job(p).append.push({ key: key, kind: kind, id: id, base: base, model: e.model, mode: 'append', ops: ops }); });
      }
    });
    (news || []).forEach(function (n) {
      var src = entityOf(data, n.kind, n.from);
      if (!src) { messages.push(['bad', 'New ' + n.kind + ' ' + n.id + ': its source "' + n.from + '" no longer exists: skipped']); return; }
      var targets = n.target && n.target.length ? n.target : (src.rbmPaths.length ? [src.rbmPaths[src.rbmPaths.length - 1]] : defaultTarget(data, n.kind, n.from, n.model, n.usedBy));
      if (!targets.length) { messages.push(['bad', 'New ' + n.kind + ' ' + n.id + ': no RBM file to append it to (pick a target file): not exported']); return; }
      var base = baseText(src), ops = diffWithOld(parseModel(base.text), n.model);
      targets.forEach(function (p) { job(p).append.push({ key: n.kind + '|' + n.id, kind: n.kind, id: n.id, from: n.from, base: base, model: n.model, mode: 'new', ops: ops }); });
    });
    var files = [];
    (data.rbmFiles || []).forEach(function (f) {
      var j = byFile[f.path];
      if (!j && !opts.all) return;
      j = j || { patch: [], append: [] };
      var ctx = fileCtx(f), text = f.text, out = { path: f.path, bom: !!f.bom, eol: f.eol, original: f.text, items: [] };
      var spans = entitySpans(text), work = [];
      j.patch.forEach(function (p) {
        var list = spans[p.kind + '|' + p.id] || [];
        if (!list.length) { messages.push(['bad', p.id + ': not found in ' + f.path + ' any more (re-run the build script)']); return; }
        list.forEach(function (sp) { work.push({ sp: sp, p: p }); });
      });
      work.sort(function (a, b) { return b.sp.start - a.sp.start; });
      var notesOf = {};
      work.forEach(function (w) {
        var r = patchEntity(text.slice(w.sp.start, w.sp.end), w.p.kind, w.p.ops, ctx, T.indentAt(text, w.sp.start) || ctx.entIndent);
        text = text.slice(0, w.sp.start) + r.text + text.slice(w.sp.end);
        notesOf[w.p.key] = (notesOf[w.p.key] || []).concat(r.notes);
      });
      j.patch.forEach(function (p) {
        if (!(spans[p.kind + '|' + p.id] || []).length) return;
        out.items.push({ key: p.key, kind: p.kind, id: p.id, mode: 'patch', summary: summarize(p.ops), notes: notesOf[p.key] || [] });
      });
      if (j.append.length) {
        var r0 = T.rootRange(text), close = text.lastIndexOf('</' + r0.name);
        var ls = text.lastIndexOf('\n', close - 1) + 1, block = '';
        if (!T.isWs(text.slice(ls, close))) ls = close;
        j.append.forEach(function (a) {
          var at = appendText(a.base, a.model, f);
          var what = a.mode === 'new' ? a.id + ': duplicated from ' + a.from + ' (' + a.base.src + ')' : a.id + ': copied from ' + a.base.src;
          block += ctx.entIndent + '<!-- ' + what.replace(/--/g, '- -') + ' by the crafting editor -->' + ctx.eol + ctx.entIndent + at.text + ctx.eol;
          out.items.push({ key: a.key, kind: a.kind, id: a.id, mode: a.mode, summary: a.mode === 'new' ? ['new ' + a.kind + ' from ' + a.from].concat(summarize(a.ops)) : summarize(a.ops), notes: at.notes });
        });
        if (ls === close) block = ctx.eol + block;
        text = text.slice(0, ls) + block + text.slice(ls);
      }
      out.items.sort(function (a, b) { return a.key < b.key ? -1 : a.key > b.key ? 1 : 0; });
      out.text = text;
      if (opts.all || out.items.length) files.push(out);
    });
    return { files: files, messages: messages };
  }

  // The element as the export will write it (for the page's preview): the first RBM file's patched element, or the
  // appended copy. Returns { path, text, mode, notes, original } or null.
  function previewElement(data, ent, model, targets, isNew) {
    var fileOf = {};
    (data.rbmFiles || []).forEach(function (f) { fileOf[f.path] = f; });
    var base = baseText(ent);
    if (!isNew && ent.rbmPaths.length) {
      var f = fileOf[ent.rbmPaths[0]];
      if (!f) return null;
      var sp = (entitySpans(f.text)[ent.kind + '|' + ent.id] || []).slice(-1)[0];
      if (!sp) return null;
      var ctx = fileCtx(f), el = f.text.slice(sp.start, sp.end);
      var r = patchEntity(el, ent.kind, diff(parseModel(base.text), model), ctx, T.indentAt(f.text, sp.start) || ctx.entIndent);
      return { path: f.path, text: r.text, mode: 'patch', notes: r.notes, original: el };
    }
    var f2 = fileOf[(targets || [])[0]];
    if (!f2) return null;
    var at = appendText(base, model, f2);
    return { path: f2.path, text: at.text, mode: isNew ? 'new' : 'append', notes: at.notes, original: null };
  }

  // ---------------------------------------------------------------- change summary

  function q(v) { return v == null ? '(none)' : v; }
  function summarize(ops) {
    var out = [];
    ops.forEach(function (o) {
      if (o.p === 'attr') out.push(o.n + ' ' + q(o.old) + ' → ' + q(o.v));
      else if (o.p === 'el') out.push('<' + o.s + '> ' + (o.on ? 'added' + (o.attrs.length ? ' (' + o.attrs.map(function (a) { return a[0] + '=' + a[1]; }).join(' ') + ')' : '') : 'removed'));
      else if (o.p === 'elAttr') out.push(o.s + ' ' + o.n + ' ' + q(o.old) + ' → ' + q(o.v));
      else if (o.p === 'list') out.push('<' + o.l.replace('|', ' ') + '> ' + (o.on ? 'added (' + o.items.length + ' entries)' : 'removed'));
      else if (o.p === 'listAttr') out.push(o.l.replace('|', ' ') + ' ' + o.n + ' ' + q(o.old) + ' → ' + q(o.v));
      else if (o.p === 'item') out.push(o.l.replace('|', ' ') + ': ' + (o.on ? '+ ' : '− ') + o.k + (o.on && o.attrs.length > 1 ? ' (' + o.attrs.map(function (a) { return a[0] + '=' + a[1]; }).join(' ') + ')' : ''));
      else if (o.p === 'itemAttr') out.push(o.l.replace('|', ' ') + ' ' + o.k + ' ' + o.n + ' ' + q(o.old) + ' → ' + q(o.v));
    });
    return out;
  }
  // diff() with the old values attached (for summaries that show "a → b").
  function diffWithOld(a, b) {
    return diff(a, b).map(function (o) {
      if (o.p === 'attr') o.old = getAt(a, { el: 'root' }, o.n);
      else if (o.p === 'elAttr') o.old = getAt(a, { el: 'sub', s: o.s }, o.n);
      else if (o.p === 'listAttr') o.old = getAt(a, { el: 'list', l: o.l }, o.n);
      else if (o.p === 'itemAttr') o.old = getAt(a, { el: 'item', l: o.l, k: o.k }, o.n);
      return o;
    });
  }

  var api = {
    SCHEMA: SCHEMA, ATTR_ENUMS: ATTR_ENUMS, ATTR_REFS: ATTR_REFS, INT_ATTRS: INT_ATTRS, BOOL_ATTRS: BOOL_ATTRS, PIECE_TYPES: PIECE_TYPES,
    RBM_CFG: RBM_CFG, WEAPON_TYPE_FACTORS: WEAPON_TYPE_FACTORS,
    attrsOf: attrsOf, parseModel: parseModel, cloneModel: cloneModel, diff: diff, diffWithOld: diffWithOld, sameModel: sameModel,
    applyOps: applyOps, mapAt: mapAt, getAt: getAt, setAt: setAt, listSpec: listSpec, vocabPath: vocabPath, attrKind: attrKind,
    cleanEnum: cleanEnum, canon: canon, enumHas: enumHas, isInt: isInt, isDec: isDec,
    effPiece: effPiece, effTemplate: effTemplate, calcDesign: calcDesign, healedAvail: healedAvail, parseCrafted: parseCrafted,
    createState: createState, modelOfDef: modelOfDef, pieceTemplateIndex: pieceTemplateIndex,
    rbmMeleeStats: rbmMeleeStats, computeDamage: computeDamage, skillBasedDamage: skillBasedDamage, roundEven: roundEven,
    checkModel: checkModel, checkAttrs: checkAttrs, replayNotes: replayNotes, problems: problems,
    patchEntity: patchEntity, fileCtx: fileCtx, entitySpans: entitySpans, entityOf: entityOf, baseText: baseText,
    defaultTarget: defaultTarget, appendText: appendText, buildExport: buildExport, previewElement: previewElement, summarize: summarize
  };
  if (typeof module !== 'undefined' && module.exports) module.exports = api;
  else root.CraftingEditorCore = api;
})(this);
