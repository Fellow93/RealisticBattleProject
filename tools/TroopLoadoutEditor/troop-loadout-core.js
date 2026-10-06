// Troop loadout editor: the data model and the text-splicing export. No DOM; index.html uses it, and it can be
// run under node (module.exports) to test an export. See README.md.
(function (root) {
  'use strict';

  // EquipmentIndex names as the XML writes them (Equipment.GetEquipmentIndexFromOldEquipmentIndexName).
  var SLOTS = ['Item0', 'Item1', 'Item2', 'Item3', 'Item4', 'Head', 'Body', 'Leg', 'Gloves', 'Cape', 'Horse', 'HorseHarness'];
  var SLOT_ORDER = {};
  SLOTS.forEach(function (s, i) { SLOT_ORDER[s] = i; });
  var WEAPON_SLOTS = ['Item0', 'Item1', 'Item2', 'Item3'];
  var WEAPON_TYPES = ['OneHandedWeapon', 'TwoHandedWeapon', 'Polearm', 'Arrows', 'Bolts', 'SlingStones', 'Shield', 'Bow',
    'Crossbow', 'Sling', 'Thrown', 'Pistol', 'Musket', 'Bullets', 'Banner'];
  var ARMOR_SLOT_TYPE = { Head: 'HeadArmor', Body: 'BodyArmor', Leg: 'LegArmor', Gloves: 'HandArmor', Cape: 'Cape', HorseHarness: 'HorseHarness' };

  // Equipment.IsItemFitsToSlot.
  function fits(slot, item) {
    if (!item) return true;
    var t = item.type;
    if (WEAPON_TYPES.indexOf(t) >= 0) {
      var fl = item.flags || [];
      if (fl.indexOf('DropOnWeaponChange') >= 0 || fl.indexOf('DropOnAnyAction') >= 0) return slot === 'Item4';
      return WEAPON_SLOTS.indexOf(slot) >= 0;
    }
    if (t === 'Horse' || t === 'Animal') return slot === 'Horse';
    for (var s in ARMOR_SLOT_TYPE) if (ARMOR_SLOT_TYPE[s] === t) return slot === s;
    return false;
  }

  // RBM/XmlLoadingPatches.cs MergeTwoXmlsPatch, passiveShoulderShields off: "<shield>[_kalkan|_cataphract]_shoulder"
  // is loaded as "<shield>". Returns the base id, or null.
  function shoulderBase(id) {
    if (!id || id.indexOf('shield') < 0 || !/_shoulder$/.test(id)) return null;
    var b = id.slice(0, -'_shoulder'.length);
    ['_kalkan', '_cataphract'].some(function (s) {
      if (b.slice(-s.length) === s) { b = b.slice(0, -s.length); return true; }
      return false;
    });
    return b;
  }

  // DefaultCharacterStatsModel.GetTier.
  function tierOf(level) { return Math.max(0, Math.min(6, Math.ceil((level - 5) / 5))); }

  // ---------------------------------------------------------------- model

  function pairsToMap(pairs) {
    var m = {};
    (pairs || []).forEach(function (p) { if (p[1]) m[p[0]] = p[1]; else delete m[p[0]]; });
    return m;
  }
  // The troop as the data file describes it. skills = effective values (template overlaid with explicit ones).
  function origModel(t) {
    var skills = {};
    Object.keys(t.templateSkills || {}).forEach(function (k) { skills[k] = t.templateSkills[k]; });
    Object.keys(t.explicitSkills || {}).forEach(function (k) { skills[k] = t.explicitSkills[k]; });
    return {
      level: t.level,
      skills: skills,
      rosters: (t.rosters || []).map(function (r, i) { return { type: r.type, slots: pairsToMap(r.slots), o: i }; }),
      loose: pairsToMap(t.loose)
    };
  }
  function cloneModel(m) {
    return {
      level: m.level,
      skills: Object.assign({}, m.skills),
      rosters: m.rosters.map(function (r) { return { type: r.type, slots: Object.assign({}, r.slots), o: r.o }; }),
      loose: Object.assign({}, m.loose)
    };
  }
  function sameMap(a, b) {
    var ka = Object.keys(a).filter(function (k) { return a[k]; }), kb = Object.keys(b).filter(function (k) { return b[k]; });
    if (ka.length !== kb.length) return false;
    return ka.every(function (k) { return a[k] === b[k]; });
  }
  function sameSkills(a, b) {
    var keys = {};
    Object.keys(a).concat(Object.keys(b)).forEach(function (k) { keys[k] = true; });
    return Object.keys(keys).every(function (k) { return (a[k] || 0) === (b[k] || 0); });
  }
  function sameRoster(a, b) { return a.type === b.type && sameMap(a.slots, b.slots); }
  function sameRosters(a, b) {
    if (a.length !== b.length) return false;
    for (var i = 0; i < a.length; i++) { if (a[i].o !== b[i].o || !sameRoster(a[i], b[i])) return false; }
    return true;
  }
  function sameModel(a, b) {
    return a.level === b.level && sameSkills(a.skills, b.skills) && sameRosters(a.rosters, b.rosters) && sameMap(a.loose, b.loose);
  }

  // ---------------------------------------------------------------- XML text scanning

  var TOKEN_SRC = '<!--[\\s\\S]*?-->|<!\\[CDATA\\[[\\s\\S]*?\\]\\]>|<\\?[\\s\\S]*?\\?>|<!DOCTYPE[^>]*>|<(\\/?)([A-Za-z_][\\w.:-]*)((?:[^>"\']|"[^"]*"|\'[^\']*\')*?)(\\/?)>';

  // Direct children of the range [from, to) as {kind: 'el'|'comment'|'other'|'text', start, end, name, tagEnd, closeStart, self}.
  function parseChildren(text, from, to) {
    var re = new RegExp(TOKEN_SRC, 'g');
    re.lastIndex = from;
    var out = [], depth = 0, cur = null, pos = from, m;
    while ((m = re.exec(text)) && m.index < to) {
      var end = m.index + m[0].length;
      if (!m[2]) {
        if (depth === 0) {
          if (m.index > pos) out.push({ kind: 'text', start: pos, end: m.index });
          out.push({ kind: m[0].slice(0, 4) === '<!--' ? 'comment' : 'other', start: m.index, end: end });
          pos = end;
        }
        continue;
      }
      var closing = m[1] === '/', self = m[4] === '/';
      if (closing) {
        depth--;
        if (depth === 0 && cur) { cur.end = end; cur.closeStart = m.index; out.push(cur); cur = null; pos = end; }
        continue;
      }
      if (depth === 0) {
        if (m.index > pos) out.push({ kind: 'text', start: pos, end: m.index });
        cur = { kind: 'el', name: m[2], start: m.index, tagEnd: end, self: self };
        if (self) { cur.end = end; cur.closeStart = end; out.push(cur); cur = null; pos = end; continue; }
      }
      if (!self) depth++;
    }
    if (pos < to) out.push({ kind: 'text', start: pos, end: to });
    return out;
  }
  // The root element's inner range.
  function rootRange(text) {
    var re = new RegExp(TOKEN_SRC, 'g'), m;
    while ((m = re.exec(text))) {
      if (m[2] && m[1] !== '/') {
        var close = text.lastIndexOf('</' + m[2]);
        return { name: m[2], innerStart: m.index + m[0].length, innerEnd: close };
      }
    }
    throw new Error('no root element');
  }
  // id -> span of the top-level <NPCCharacter> (the last one when an id repeats, as it deserializes last).
  function troopSpans(text) {
    var r = rootRange(text), map = {};
    parseChildren(text, r.innerStart, r.innerEnd).forEach(function (n) {
      if (n.kind !== 'el' || n.name !== 'NPCCharacter') return;
      var id = getAttr(text.slice(n.start, n.tagEnd), 'id');
      if (id != null) map[id] = { start: n.start, end: n.end };
    });
    return map;
  }
  // Tokens with their text, for splicing.
  function tokens(text, from, to) {
    return parseChildren(text, from, to).map(function (n) { n.text = text.slice(n.start, n.end); return n; });
  }
  function startTagOf(text) {
    var re = new RegExp(TOKEN_SRC, 'g'), m = re.exec(text);
    return { text: m[0], name: m[2], self: m[4] === '/', end: m.index + m[0].length };
  }
  function isWs(s) { return /^\s*$/.test(s); }
  function indentAt(text, pos) {
    var ls = text.lastIndexOf('\n', pos - 1) + 1;
    var s = text.slice(ls, pos);
    return isWs(s) ? s : '';
  }
  function unitFor(indent) { return indent.indexOf('\t') >= 0 ? '\t' : '  '; }
  function escAttr(s) { return String(s).replace(/&/g, '&amp;').replace(/</g, '&lt;').replace(/>/g, '&gt;').replace(/"/g, '&quot;'); }
  function reEsc(s) { return s.replace(/[.*+?^${}()|[\]\\]/g, '\\$&'); }

  function attrRe(name) { return new RegExp('(\\s)(' + reEsc(name) + ')(\\s*=\\s*)("([^"]*)"|\'([^\']*)\')'); }
  function getAttr(tag, name) {
    var m = attrRe(name).exec(tag);
    if (!m) return null;
    return m[5] != null ? m[5] : m[6];
  }
  // Sets an attribute in a start tag, keeping everything else byte-identical. A new attribute goes after the last one,
  // with the same separator the last one has (a newline plus alignment in RBM's files).
  function setAttr(tag, name, value) {
    var re = attrRe(name), m = re.exec(tag);
    if (m) {
      var q = m[4].charAt(0);
      return tag.slice(0, m.index) + m[1] + m[2] + m[3] + q + escAttr(value) + q + tag.slice(m.index + m[0].length);
    }
    var all = /(\s+)([\w.:-]+)\s*=\s*("[^"]*"|'[^']*')/g, last = null, x;
    while ((x = all.exec(tag))) last = x;
    if (last) {
      var at = last.index + last[0].length;
      return tag.slice(0, at) + last[1] + name + '="' + escAttr(value) + '"' + tag.slice(at);
    }
    var nm = /^<[\w.:-]+/.exec(tag)[0];
    return nm + ' ' + name + '="' + escAttr(value) + '"' + tag.slice(nm.length);
  }
  function removeAttr(tag, name) {
    return tag.replace(new RegExp('\\s+' + reEsc(name) + '\\s*=\\s*("[^"]*"|\'[^\']*\')'), '');
  }
  // Re-indents a multi-line snippet taken from another place.
  function reindent(snippet, oldIndent, newIndent) {
    var lines = snippet.split('\n');
    for (var i = 1; i < lines.length; i++) {
      if (lines[i].indexOf(oldIndent) === 0) lines[i] = newIndent + lines[i].slice(oldIndent.length);
    }
    return lines.join('\n');
  }

  // ---------------------------------------------------------------- entry lists (<equipment .../>, <skill .../>)

  // Edits the entries (elements named opts.entry) among tokens so that key -> value equals want, touching only
  // the entries whose value changes. opts: entry, keyAttr, valAttr, order(key), fmtVal(value, oldRaw),
  // remove (bool: drop keys missing from want), sample() -> {text, ws} for a new entry when the list has none,
  // emptyWs: whitespace before a new entry when the list has none.
  // Entries are self-closing (<equipment .../>, <skill .../>), so the whole text is the tag.
  function entryKey(tok, opts) { return getAttr(tok.text, opts.keyAttr); }
  function editEntries(toks, want, opts) {
    var isEntry = function (t) { return t.kind === 'el' && t.name === opts.entry; };
    var have = {};
    toks.forEach(function (t) {
      if (!isEntry(t)) return;
      var k = entryKey(t, opts), v = getAttr(t.text, opts.valAttr);
      if (k != null) have[k] = opts.parseVal(v);
    });
    var keys = {};
    Object.keys(have).concat(Object.keys(want)).forEach(function (k) { keys[k] = true; });
    var sorted = Object.keys(keys).sort(function (a, b) { return opts.order(a) - opts.order(b); });
    sorted.forEach(function (k) {
      var o = have[k], n = want[k];
      if (n === undefined || n === null || n === '') n = null;
      if (o === undefined || o === null || o === '') o = null;
      if (o === n) return;
      var idxs = [];
      toks.forEach(function (t, i) { if (isEntry(t) && entryKey(t, opts) === k) idxs.push(i); });
      if (n === null) {
        if (!opts.remove) return;
        for (var j = idxs.length - 1; j >= 0; j--) {
          var i = idxs[j];
          if (i > 0 && toks[i - 1].kind === 'text' && isWs(toks[i - 1].text)) toks.splice(i - 1, 2);
          else toks.splice(i, 1);
        }
        return;
      }
      if (idxs.length) {
        var t = toks[idxs[idxs.length - 1]];
        t.text = setAttr(t.text, opts.valAttr, opts.fmtVal(n, getAttr(t.text, opts.valAttr)));
        return;
      }
      // New entry: shaped like an existing sibling (same whitespace before it), else like opts.sample().
      var entries = [];
      toks.forEach(function (t, i) { if (isEntry(t)) entries.push(i); });
      var sampleText, ws;
      if (entries.length) {
        var si = entries[0];
        sampleText = toks[si].text;
        ws = si > 0 && toks[si - 1].kind === 'text' && isWs(toks[si - 1].text) ? toks[si - 1].text : opts.emptyWs;
      } else {
        var s = opts.sample();
        sampleText = s.text; ws = opts.emptyWs;
      }
      var text = setAttr(setAttr(sampleText, opts.keyAttr, k), opts.valAttr, opts.fmtVal(n, null));
      var nt = { kind: 'el', name: opts.entry, start: 0, tagEnd: text.length, text: text };
      var wt = { kind: 'text', text: ws };
      var anchor = -1;
      entries.forEach(function (i) { if (opts.order(entryKey(toks[i], opts)) < opts.order(k)) anchor = i; });
      if (anchor >= 0) toks.splice(anchor + 1, 0, wt, nt);
      else if (entries.length) toks.splice(entries[0], 0, nt, wt);
      else {
        // No entry yet: after the last non-text token, else at the start.
        var last = -1;
        toks.forEach(function (t, i) { if (t.kind !== 'text') last = i; });
        toks.splice(last + 1, 0, wt, nt);
      }
    });
    return toks;
  }
  function join(toks) { return toks.map(function (t) { return t.text; }).join(''); }

  function equipOpts(ctx, indent) {
    return {
      entry: 'equipment', keyAttr: 'slot', valAttr: 'id', remove: true,
      order: function (k) { return SLOT_ORDER[k] != null ? SLOT_ORDER[k] : 99; },
      parseVal: function (v) { return v == null ? null : (v.indexOf('.') >= 0 ? v.split('.')[1] : v); },
      fmtVal: function (n, oldRaw) { return (oldRaw == null || oldRaw.indexOf('.') >= 0 ? 'Item.' : '') + n; },
      sample: function () { return { text: reindent(ctx.sampleEquip.text, ctx.sampleEquip.indent, indent) }; },
      emptyWs: ctx.eol + indent
    };
  }

  // ---------------------------------------------------------------- per-element patches

  function setRosterType(tag, type) {
    var hasEt = getAttr(tag, 'equipmentType') != null;
    if (type === 'Battle') return removeAttr(removeAttr(tag, 'civilian'), 'equipmentType');
    if (type === 'Civilian') {
      if (hasEt) return removeAttr(setAttr(tag, 'equipmentType', 'Civilian'), 'civilian');
      return setAttr(tag, 'civilian', 'true');
    }
    return setAttr(removeAttr(tag, 'civilian'), 'equipmentType', type);
  }
  function rosterTypeOfTag(tag) {
    var et = getAttr(tag, 'equipmentType');
    if (et != null) return ['Battle', 'Civilian', 'Stealth'].indexOf(et) >= 0 ? et : 'Battle';
    var c = getAttr(tag, 'civilian');
    return c != null && c.trim().toLowerCase() === 'true' ? 'Civilian' : 'Battle';
  }

  // A roster element's text with the wanted type and slots. indent: the roster's own indentation.
  function patchRoster(text, want, ctx, indent) {
    var st = startTagOf(text);
    var tag = st.text;
    if (rosterTypeOfTag(tag) !== want.type) tag = setRosterType(tag, want.type);
    var entryIndent = indent + unitFor(indent);
    if (st.self) {
      if (!Object.keys(want.slots).some(function (k) { return want.slots[k]; })) return tag;
      tag = tag.replace(/\s*\/>$/, '>');
      var toks0 = editEntries([], want.slots, equipOpts(ctx, entryIndent));
      return tag + join(toks0) + ctx.eol + indent + '</' + st.name + '>';
    }
    var close = text.lastIndexOf('</');
    var toks = tokens(text, st.end, close);
    var wasEmpty = !toks.some(function (t) { return t.kind !== 'text'; });
    editEntries(toks, want.slots, equipOpts(ctx, entryIndent));
    var inner = join(toks);
    if (wasEmpty && !isWs(inner)) inner = inner.replace(/\s*$/, '') + ctx.eol + indent;
    return tag + inner + text.slice(close);
  }
  function stripComments(text) {
    var st = startTagOf(text), close = text.lastIndexOf('</');
    if (st.self || close < st.end) return text;
    var toks = tokens(text, st.end, close), out = [];
    toks.forEach(function (t) {
      if (t.kind === 'comment') { if (out.length && out[out.length - 1].kind === 'text' && isWs(out[out.length - 1].text)) out.pop(); return; }
      out.push(t);
    });
    return text.slice(0, st.end) + join(out) + text.slice(close);
  }

  // <Equipments> element text with the wanted rosters (order, type, slots) and loose overrides.
  function patchEquipments(text, cur, ctx, indent) {
    var st = startTagOf(text);
    var childIndent = indent + unitFor(indent);
    var close = text.lastIndexOf('</');
    var toks = st.self ? [] : tokens(text, st.end, close);
    var origRosterTexts = [];
    var rosterIdx = [];
    toks.forEach(function (t, i) {
      if (t.kind === 'el' && (t.name === 'EquipmentRoster' || t.name === 'equipmentRoster')) { rosterIdx.push(i); origRosterTexts.push(t.text); }
    });
    // Indentation of the children (rosters): the first element child's, else one unit in.
    var firstEl = toks.filter(function (t) { return t.kind !== 'text'; })[0];
    if (firstEl) {
      var fi = toks.indexOf(firstEl);
      if (fi > 0 && toks[fi - 1].kind === 'text') {
        var w = toks[fi - 1].text, nl = w.lastIndexOf('\n');
        if (nl >= 0) childIndent = w.slice(nl + 1);
      }
    }
    var sample = null;
    if (origRosterTexts.length) sample = { text: stripComments(origRosterTexts[0]), indent: childIndent };
    else if (ctx.sampleRoster) sample = { text: reindent(stripComments(ctx.sampleRoster.text), ctx.sampleRoster.indent, childIndent), indent: childIndent };
    var rosterText = function (r) {
      if (r.o != null && origRosterTexts[r.o] != null) return patchRoster(origRosterTexts[r.o], r, ctx, childIndent);
      if (sample) return patchRoster(sample.text, r, ctx, childIndent);
      var bare = '<EquipmentRoster' + (r.type === 'Civilian' ? ' civilian="true"' : r.type === 'Stealth' ? ' equipmentType="Stealth"' : '') + '></EquipmentRoster>';
      return patchRoster(bare, r, ctx, childIndent);
    };
    var k = rosterIdx.length, m = cur.rosters.length;
    var newTexts = cur.rosters.map(rosterText);
    var remove = [];
    for (var j = 0; j < k; j++) {
      if (j < m) toks[rosterIdx[j]].text = newTexts[j];
      else remove.push(rosterIdx[j]);
    }
    var ws = ctx.eol + childIndent;
    if (m > k) {
      var extra = [];
      for (var e = k; e < m; e++) extra.push(newTexts[e]);
      if (k > 0) {
        var li = rosterIdx[k - 1];
        var lws = li > 0 && toks[li - 1].kind === 'text' && isWs(toks[li - 1].text) ? toks[li - 1].text : ws;
        var ins = [];
        extra.forEach(function (x) { ins.push({ kind: 'text', text: lws }, { kind: 'el', name: 'EquipmentRoster', text: x }); });
        Array.prototype.splice.apply(toks, [li + 1, 0].concat(ins));
      } else {
        var f = -1;
        toks.forEach(function (t, i) { if (f < 0 && t.kind !== 'text') f = i; });
        var ins2 = [];
        if (f >= 0) {
          var fws = f > 0 && toks[f - 1].kind === 'text' && isWs(toks[f - 1].text) ? toks[f - 1].text : ws;
          extra.forEach(function (x) { ins2.push({ kind: 'el', name: 'EquipmentRoster', text: x }, { kind: 'text', text: fws }); });
          Array.prototype.splice.apply(toks, [f, 0].concat(ins2));
        } else {
          extra.forEach(function (x) { ins2.push({ kind: 'text', text: ws }, { kind: 'el', name: 'EquipmentRoster', text: x }); });
          toks = ins2.concat([{ kind: 'text', text: ctx.eol + indent }]);
        }
      }
    }
    for (var r = remove.length - 1; r >= 0; r--) {
      var ri = remove[r];
      if (ri > 0 && toks[ri - 1].kind === 'text' && isWs(toks[ri - 1].text)) toks.splice(ri - 1, 2);
      else toks.splice(ri, 1);
    }
    var hadLoose = toks.some(function (t) { return t.kind === 'el' && t.name === 'equipment'; });
    var lo = equipOpts(ctx, childIndent);
    if (!hadLoose) {
      // A loose entry goes after the last child, with the whitespace the last child has before it.
      var lastEl = -1;
      toks.forEach(function (t, i) { if (t.kind !== 'text') lastEl = i; });
      if (lastEl > 0 && toks[lastEl - 1].kind === 'text' && isWs(toks[lastEl - 1].text)) lo.emptyWs = toks[lastEl - 1].text;
    }
    editEntries(toks, cur.loose, lo);
    if (!toks.some(function (t) { return t.kind !== 'text'; })) toks = [{ kind: 'text', text: ctx.eol + indent }];
    var inner = join(toks);
    if (st.self) {
      if (isWs(inner)) return text;
      return st.text.replace(/\s*\/>$/, '>') + inner.replace(/\s*$/, '') + ctx.eol + indent + '</' + st.name + '>';
    }
    return st.text + inner + text.slice(close);
  }

  // <skills> element text with the wanted explicit entries.
  function patchSkills(text, wantExplicit, ctx, indent, skillOrder) {
    var st = startTagOf(text);
    var entryIndent = indent + unitFor(indent);
    var opts = {
      entry: 'skill', keyAttr: 'id', valAttr: 'value', remove: false,
      order: function (k) { var i = skillOrder.indexOf(k); return i < 0 ? 999 : i; },
      parseVal: function (v) { return v == null ? null : parseInt(v, 10); },
      fmtVal: function (n) { return String(n); },
      sample: function () { return { text: reindent(ctx.sampleSkill.text, ctx.sampleSkill.indent, entryIndent) }; },
      emptyWs: ctx.eol + entryIndent
    };
    // A wanted 0 must still be written when the entry exists (editEntries treats null/'' as absent, not 0).
    var want = {};
    Object.keys(wantExplicit).forEach(function (k) { want[k] = wantExplicit[k]; });
    if (st.self) {
      var t0 = editEntries([], want, opts);
      if (!t0.length) return text;
      return st.text.replace(/\s*\/>$/, '>') + join(t0) + ctx.eol + indent + '</' + st.name + '>';
    }
    var close = text.lastIndexOf('</');
    var toks = tokens(text, st.end, close);
    var wasEmpty = !toks.some(function (t) { return t.kind !== 'text'; });
    editEntries(toks, want, opts);
    var inner = join(toks);
    if (wasEmpty && !isWs(inner)) inner = inner.replace(/\s*$/, '') + ctx.eol + indent;
    return st.text + inner + text.slice(close);
  }

  // The explicit <skill> entries that give the wanted effective skills: existing entries keep their place with the
  // new value; a skill differing from the template (or from 0 without one) gets a new entry. Entries are never
  // removed: BasicCharacterObject.Deserialize copies the template and overlays every <skill> entry.
  function wantedExplicit(t, cur) {
    var want = {};
    var tpl = t.templateSkills || {}, ex = t.explicitSkills || {};
    Object.keys(ex).forEach(function (k) { want[k] = cur.skills[k] || 0; });
    Object.keys(cur.skills).forEach(function (k) {
      if (k in want) return;
      var v = cur.skills[k] || 0;
      if (v !== (tpl[k] || 0)) want[k] = v;
    });
    // Template skills the editor zeroed out (removed from cur.skills).
    Object.keys(tpl).forEach(function (k) { if (!(k in want) && !(k in cur.skills) && tpl[k]) want[k] = 0; });
    return want;
  }

  // The whole <NPCCharacter> element text with level, skills, rosters and loose overrides as in cur.
  function patchTroop(elText, t, orig, cur, ctx) {
    var text = elText;
    var st = startTagOf(text);
    if (cur.level !== orig.level) {
      var tag = setAttr(st.text, 'level', String(cur.level));
      text = tag + text.slice(st.end);
      st = startTagOf(text);
    }
    var children = function () { return tokens(text, st.end, text.lastIndexOf('</')); };
    var replaceTok = function (tok, newText) { text = text.slice(0, tok.start) + newText + text.slice(tok.end); };
    var elIndent = indentAt(text, 0) || ctx.troopIndent || '';
    var findChild = function (names) {
      return children().filter(function (c) { return c.kind === 'el' && names.indexOf(c.name) >= 0; });
    };
    var childIndentOf = function () {
      var c = children().filter(function (x) { return x.kind !== 'text'; })[0];
      if (c) { var i = indentAt(text, c.start); if (i) return i; }
      return elIndent + unitFor(elIndent || ctx.troopIndent || '  ');
    };
    if (!sameSkills(cur.skills, orig.skills)) {
      var want = wantedExplicit(t, cur);
      var sk = findChild(['skills', 'Skills']);
      if (sk.length) {
        var s = sk[sk.length - 1];
        replaceTok(s, patchSkills(s.text, want, ctx, indentAt(text, s.start), ctx.skillOrder));
      } else if (Object.keys(want).length) {
        var ci = childIndentOf();
        var skText = patchSkills('<skills></skills>', want, ctx, ci, ctx.skillOrder);
        var anchor = findChild(['upgrade_targets', 'Equipments', 'equipments'])[0];
        if (anchor) text = text.slice(0, anchor.start) + skText + ctx.eol + indentAt(text, anchor.start) + text.slice(anchor.start);
        else {
          var cs = text.lastIndexOf('</');
          var pre = text.slice(0, cs).replace(/\s*$/, '');
          text = pre + ctx.eol + ci + skText + ctx.eol + elIndent + text.slice(cs);
        }
      }
    }
    if (!sameRosters(cur.rosters, orig.rosters) || !sameMap(cur.loose, orig.loose)) {
      var eqs = findChild(['Equipments', 'equipments']);
      if (eqs.length) {
        var e = eqs[eqs.length - 1];
        replaceTok(e, patchEquipments(e.text, cur, ctx, indentAt(text, e.start)));
      } else if (cur.rosters.length || Object.keys(cur.loose).some(function (k) { return cur.loose[k]; })) {
        var ci2 = childIndentOf();
        var eqText = patchEquipments('<Equipments></Equipments>', cur, ctx, ci2);
        var cs2 = text.lastIndexOf('</');
        var pre2 = text.slice(0, cs2).replace(/\s*$/, '');
        text = pre2 + ctx.eol + ci2 + eqText + ctx.eol + elIndent + text.slice(cs2);
      }
    }
    return text;
  }

  // ---------------------------------------------------------------- export

  function fileCtx(file, skillOrder) {
    var text = file.text, eol = file.eol || '\n';
    var ctx = { eol: eol, skillOrder: skillOrder };
    var m = /^([ \t]*)<equipment\b[^>]*\/>/m.exec(text);
    ctx.sampleEquip = m ? { text: m[0].slice(m[1].length), indent: m[1] } : { text: '<equipment slot="Item0" id="Item.x" />', indent: '' };
    m = /^([ \t]*)<skill\b[^>]*\/>/m.exec(text);
    ctx.sampleSkill = m ? { text: m[0].slice(m[1].length), indent: m[1] } : { text: '<skill id="x" value="0" />', indent: '' };
    m = /^([ \t]*)<EquipmentRoster\b/m.exec(text);
    if (m) {
      var at = m.index + m[1].length;
      var node = parseChildren(text, at, Math.min(text.length, at + 50000))[0];
      if (node && node.kind === 'el') ctx.sampleRoster = { text: text.slice(node.start, node.end), indent: m[1] };
    }
    m = /^([ \t]*)<NPCCharacter\b/m.exec(text);
    ctx.troopIndent = m ? m[1] : '  ';
    return ctx;
  }
  function normEol(text, eol) { return text.replace(/\r\n/g, '\n').replace(/\n/g, eol); }

  // Which RBM file a troop's edits go to.
  function targetFile(t, data) {
    if (t.rbmFile) return t.rbmFile;
    var ws = (data.rbmFiles || []).filter(function (f) { return f.naval; })[0];
    if (t.naval && ws) return ws.path;
    var main = (data.rbmFiles || []).filter(function (f) { return !f.naval; })[0];
    return main ? main.path : null;
  }

  // models: troop id -> current model (only troops that differ from the data are exported).
  // Returns { files: [{path, bom, eol, text, original, troops: [{id, mode, summary}]}], messages: [[cls, text]] }.
  function buildExport(data, models, troopById) {
    var byFile = {}, messages = [];
    var skillOrder = data.skillOrder || [];
    Object.keys(models).forEach(function (id) {
      var t = troopById[id];
      if (!t) { messages.push(['bad', 'Unknown troop "' + id + '" in the work in progress: skipped']); return; }
      var orig = origModel(t), cur = models[id];
      if (sameModel(orig, cur)) return;
      var path = targetFile(t, data);
      if (!path) { messages.push(['bad', id + ': no RBM troop file to write to']); return; }
      (byFile[path] = byFile[path] || []).push(id);
    });
    var files = [];
    (data.rbmFiles || []).forEach(function (f) {
      var ids = byFile[f.path];
      if (!ids || !ids.length) return;
      var ctx = fileCtx(f, skillOrder);
      var text = f.text;
      var spans = troopSpans(text);
      var replaced = [], appended = [];
      ids.forEach(function (id) { if (troopById[id].rbmFile === f.path && spans[id]) replaced.push(id); else appended.push(id); });
      replaced.sort(function (a, b) { return spans[b].start - spans[a].start; });
      var out = { path: f.path, bom: !!f.bom, eol: f.eol, original: f.text, troops: [] };
      replaced.forEach(function (id) {
        var t = troopById[id], sp = spans[id];
        var el = text.slice(sp.start, sp.end);
        var c = Object.assign({}, ctx, { troopIndent: indentAt(text, sp.start) || ctx.troopIndent });
        text = text.slice(0, sp.start) + patchTroop(el, t, origModel(t), models[id], c) + text.slice(sp.end);
        out.troops.push({ id: id, mode: 'replace', summary: summarize(origModel(t), models[id], data) });
      });
      appended.forEach(function (id) {
        var t = troopById[id];
        if (!t.raw) { messages.push(['bad', id + ': the data has no raw XML for this troop; it cannot be appended']); return; }
        var el = normEol(t.raw, ctx.eol);
        var ind = t.rawIndent || ctx.troopIndent;
        var c = Object.assign({}, ctx, { troopIndent: ind });
        var patched = patchTroop(el, t, origModel(t), models[id], c);
        var close = text.lastIndexOf('</NPCCharacters>');
        var ls = text.lastIndexOf('\n', close - 1) + 1;
        if (!isWs(text.slice(ls, close))) ls = close;
        var from = t.rawMerged ? (t.sources || []).join(' + ') + ' (merged)' : t.module + '/' + t.file;
        var block = ind + '<!-- ' + t.id + ': copied from ' + from + ' by the troop loadout editor -->' + ctx.eol +
          ind + patched + ctx.eol;
        text = text.slice(0, ls) + block + text.slice(ls);
        out.troops.push({ id: id, mode: 'append', summary: summarize(origModel(t), models[id], data) });
      });
      out.troops.sort(function (a, b) { return a.id < b.id ? -1 : 1; });
      out.text = text;
      files.push(out);
    });
    return { files: files, messages: messages };
  }

  // ---------------------------------------------------------------- change summary

  function itemLabel(id, data) { return id || '(empty)'; }
  function summarize(orig, cur, data) {
    var parts = [];
    if (orig.level !== cur.level) parts.push('level ' + orig.level + ' → ' + cur.level);
    var sk = [];
    var keys = {};
    Object.keys(orig.skills).concat(Object.keys(cur.skills)).forEach(function (k) { keys[k] = true; });
    var names = (data && data.skills) || {};
    Object.keys(keys).forEach(function (k) {
      var a = orig.skills[k] || 0, b = cur.skills[k] || 0;
      if (a !== b) sk.push((names[k] || k) + ' ' + a + ' → ' + b);
    });
    if (sk.length) parts.push('skills: ' + sk.join(', '));
    if (!sameRosters(orig.rosters, cur.rosters)) {
      if (orig.rosters.length !== cur.rosters.length) parts.push('rosters ' + orig.rosters.length + ' → ' + cur.rosters.length);
      var order = cur.rosters.map(function (r) { return r.o; });
      var kept = order.filter(function (o) { return o != null; });
      if (kept.some(function (o, i) { return i > 0 && o < kept[i - 1]; })) parts.push('rosters reordered');
      cur.rosters.forEach(function (r, i) {
        if (r.o == null) { parts.push('roster ' + (i + 1) + ' new (' + r.type + ', ' + Object.keys(r.slots).filter(function (s) { return r.slots[s]; }).length + ' items)'); return; }
        var o = orig.rosters[r.o], ch = [];
        if (o.type !== r.type) ch.push(o.type + ' → ' + r.type);
        SLOTS.forEach(function (s) {
          var a = o.slots[s] || '', b = r.slots[s] || '';
          if (a !== b) ch.push(s + ': ' + itemLabel(a, data) + ' → ' + itemLabel(b, data));
        });
        if (ch.length) parts.push('roster ' + (i + 1) + (r.o !== i ? ' (was ' + (r.o + 1) + ')' : '') + ': ' + ch.join(', '));
        else if (r.o !== i) parts.push('roster ' + (i + 1) + ' is the file\'s roster ' + (r.o + 1));
      });
      var gone = orig.rosters.filter(function (o, i) { return !cur.rosters.some(function (r) { return r.o === i; }); });
      if (gone.length) parts.push(gone.length + ' roster(s) deleted');
    }
    if (!sameMap(orig.loose, cur.loose)) {
      var lc = [];
      SLOTS.forEach(function (s) {
        var a = orig.loose[s] || '', b = cur.loose[s] || '';
        if (a !== b) lc.push(s + ': ' + itemLabel(a, data) + ' → ' + itemLabel(b, data));
      });
      parts.push('shared slots: ' + lc.join(', '));
    }
    return parts;
  }

  var api = {
    SLOTS: SLOTS, SLOT_ORDER: SLOT_ORDER, WEAPON_SLOTS: WEAPON_SLOTS, WEAPON_TYPES: WEAPON_TYPES,
    fits: fits, shoulderBase: shoulderBase, tierOf: tierOf,
    origModel: origModel, cloneModel: cloneModel, sameModel: sameModel, sameRoster: sameRoster, sameSkills: sameSkills,
    sameMap: sameMap, sameRosters: sameRosters,
    parseChildren: parseChildren, rootRange: rootRange, troopSpans: troopSpans, getAttr: getAttr, setAttr: setAttr,
    patchTroop: patchTroop, buildExport: buildExport, summarize: summarize, targetFile: targetFile, wantedExplicit: wantedExplicit
  };
  if (typeof module !== 'undefined' && module.exports) module.exports = api;
  else root.TroopLoadoutCore = api;
})(this);
