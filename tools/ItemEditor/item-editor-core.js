// Item editor: the item model, problem checks, the RBM tier/price preview and the text-splicing export. No DOM;
// index.html uses it, and it runs under node (module.exports) to test an export. The XML text helpers (tokenizer,
// getAttr/setAttr/removeAttr, reindent, ...) come from ../TroopLoadoutEditor/troop-loadout-core.js, which a page must
// load first. See README.md.
(function (root) {
  'use strict';
  var T = (typeof module !== 'undefined' && module.exports) ? require('../TroopLoadoutEditor/troop-loadout-core.js') : root.TroopLoadoutCore;

  // ---------------------------------------------------------------- game knowledge

  // WeaponComponentData.GetItemTypeFromWeaponClass. ItemObject.Deserialize overrides an Item's Type with the first
  // weapon's class's type (WeaponComponent.GetItemType) whenever it has a Type attribute and a weapon.
  var CLASS_TYPE = {
    Dagger: 'OneHandedWeapon', OneHandedSword: 'OneHandedWeapon', OneHandedAxe: 'OneHandedWeapon', Mace: 'OneHandedWeapon',
    TwoHandedSword: 'TwoHandedWeapon', TwoHandedAxe: 'TwoHandedWeapon', Pick: 'TwoHandedWeapon', TwoHandedMace: 'TwoHandedWeapon',
    OneHandedPolearm: 'Polearm', TwoHandedPolearm: 'Polearm', LowGripPolearm: 'Polearm',
    Arrow: 'Arrows', Bolt: 'Bolts', SlingStone: 'SlingStones', Cartridge: 'Bullets', Bow: 'Bow', Crossbow: 'Crossbow', Sling: 'Sling',
    Stone: 'Thrown', Boulder: 'Thrown', ThrowingAxe: 'Thrown', ThrowingKnife: 'Thrown', Javelin: 'Thrown', BallistaBoulder: 'Thrown',
    BallistaStone: 'Thrown', Pistol: 'Pistol', Musket: 'Musket', SmallShield: 'Shield', LargeShield: 'Shield', Banner: 'Banner'
  };
  var ARMOR_TYPES = ['HeadArmor', 'BodyArmor', 'LegArmor', 'HandArmor', 'Cape', 'HorseHarness'];
  var LAUNCHER_TYPES = ['Bow', 'Crossbow', 'Sling', 'Pistol', 'Musket'];
  var AMMO_TYPES = ['Arrows', 'Bolts', 'SlingStones', 'Bullets'];
  var SIEGE_CLASSES = ['Boulder', 'BallistaBoulder', 'BallistaStone'];

  // Attributes the game parses into an enum (decompiled TaleWorlds.Core: ItemObject, WeaponComponentData, ArmorComponent,
  // ItemObject.Deserialize for CraftedItem pieces). ci: Enum.Parse(ignoreCase: true). soft: any other value is ignored
  // (ArmorComponent compares body_mesh_type/body_deform_type to two strings), so it is not an error.
  var ATTR_ENUMS = {
    'Item@Type': { e: 'ItemTypeEnum', ci: true },
    'Weapon@weapon_class': { e: 'WeaponClass' }, 'Weapon@ammo_class': { e: 'WeaponClass' },
    'Weapon@swing_damage_type': { e: 'DamageTypes' }, 'Weapon@thrust_damage_type': { e: 'DamageTypes' },
    'Banner@weapon_class': { e: 'WeaponClass' }, 'Banner@ammo_class': { e: 'WeaponClass' },
    'Banner@swing_damage_type': { e: 'DamageTypes' }, 'Banner@thrust_damage_type': { e: 'DamageTypes' },
    'Armor@material_type': { e: 'ArmorMaterialTypes' },
    'Armor@hair_cover_type': { e: 'HairCoverTypes', ci: true }, 'Armor@beard_cover_type': { e: 'BeardCoverTypes', ci: true },
    'Armor@mane_cover_type': { e: 'HorseHarnessCoverTypes', ci: true }, 'Armor@tail_cover_type': { e: 'HorseTailCoverTypes', ci: true },
    'Armor@body_mesh_type': { list: ['upperbody', 'shoulders'], soft: true },
    'Armor@body_deform_type': { list: ['large', 'skinny'], soft: true },
    'Piece@Type': { e: 'PieceTypes' }
  };
  // Attributes that reference another object (prefix: the "Type." the game's reference reader expects).
  var ATTR_REFS = {
    'Item@culture': { ref: 'culture', prefix: 'Culture.' }, 'CraftedItem@culture': { ref: 'culture', prefix: 'Culture.' },
    'Item@item_category': { ref: 'category' },
    'Armor@modifier_group': { ref: 'modgroup' }, 'Weapon@modifier_group': { ref: 'modgroup' }, 'Horse@modifier_group': { ref: 'modgroup' },
    'Banner@modifier_group': { ref: 'modgroup' }, 'CraftedItem@modifier_group': { ref: 'modgroup' },
    'Item@item_holsters': { ref: 'holsters', list: ':' },
    'Horse@monster': { ref: 'monster', prefix: 'Monster.' },
    'CraftedItem@crafting_template': { ref: 'template' },
    'Weapon@item_usage': { ref: 'usage' },
    'Piece@id': { ref: 'piece' }
  };
  // Names that are not real choices (counts, masks, sentinels) are left out of the pickers; the checks accept them.
  function cleanEnum(list) {
    return (list || []).filter(function (n) { return !/Mask$|^Num|^NumberOf|^Invalid$|^Undefined$|^BodyMeshTypesNum$/.test(n); });
  }

  // ---------------------------------------------------------------- XML text helpers

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

  // ---------------------------------------------------------------- model

  // An item element's text as the editor edits it:
  //   { kind, attrs, comps: [{ el, attrs, wflags }], flags, pieces: [{ attrs }], other: [names of other children] }
  // comps: the element children of every <ItemComponent>, in order (ItemObject.Deserialize: an Armor/Horse/Trade/
  // Banner child makes the item's component, every Weapon child adds a usage). wflags: the attributes of a weapon's
  // <WeaponFlags> (WeaponComponentData.Deserialize reads attributes only; a <WeaponFlag name=""/> child is ignored).
  // flags: the attributes of <Flags> (every Flags child is read). All values are strings as written.
  function parseItem(text) {
    var ci = childInfo(text);
    var m = { kind: ci.st.name, attrs: attrsOf(ci.st.text), comps: [], flags: {}, pieces: [], other: [] };
    elKids(ci.kids).forEach(function (k) {
      var kt = text.slice(k.start, k.end);
      if (k.name === 'ItemComponent') {
        var cc = childInfo(kt);
        elKids(cc.kids).forEach(function (c) {
          var ct = kt.slice(c.start, c.end), cci = childInfo(ct);
          var comp = { el: c.name, attrs: attrsOf(cci.st.text), wflags: {} };
          elKids(cci.kids, 'WeaponFlags').forEach(function (w) {
            var wa = attrsOf(T.startTagOf(ct.slice(w.start, w.end)).text);
            Object.keys(wa).forEach(function (n) { comp.wflags[n] = wa[n]; });
          });
          m.comps.push(comp);
        });
      } else if (k.name === 'Flags') {
        var fa = attrsOf(T.startTagOf(kt).text);
        Object.keys(fa).forEach(function (n) { m.flags[n] = fa[n]; });
      } else if (k.name === 'Pieces') {
        var pc = childInfo(kt);
        elKids(pc.kids, 'Piece').forEach(function (p) { m.pieces.push({ attrs: attrsOf(T.startTagOf(kt.slice(p.start, p.end)).text) }); });
      } else m.other.push(k.name);
    });
    return m;
  }
  function copyMap(o) { var c = {}; Object.keys(o).forEach(function (k) { c[k] = o[k]; }); return c; }
  function cloneModel(m) {
    return {
      kind: m.kind, attrs: copyMap(m.attrs), flags: copyMap(m.flags), other: (m.other || []).slice(),
      comps: m.comps.map(function (c) { return { el: c.el, attrs: copyMap(c.attrs), wflags: copyMap(c.wflags) }; }),
      pieces: m.pieces.map(function (p) { return { attrs: copyMap(p.attrs) }; })
    };
  }

  // Changed attributes between two maps: [{n, v}], v null = removed. a's order, then b's new names.
  function diffMaps(a, b) {
    var out = [];
    Object.keys(a).forEach(function (n) { var v = b[n] === undefined ? null : b[n]; if (a[n] !== v) out.push({ n: n, v: v }); });
    Object.keys(b).forEach(function (n) { if (a[n] === undefined && b[n] != null) out.push({ n: n, v: b[n] }); });
    return out;
  }
  function pieceMap(m) { var o = {}; m.pieces.forEach(function (p) { o[p.attrs.Type || ''] = p; }); return o; }
  // The edit from a to b as operations on the element:
  //   {p:'item', n, v} start-tag attribute; {p:'comp', i, el, n, v} component i's attribute; {p:'wf', i, el, n, v}
  //   its WeaponFlags attribute; {p:'flag', n, v} a <Flags> attribute; {p:'piece', t, n, v} the attribute of the piece
  //   with Type t; {p:'pieceAdd', t, attrs: [[n, v]]}; {p:'pieceDel', t}; {p:'struct'} components added/removed
  //   (not supported).
  function diff(a, b) {
    var ops = [];
    diffMaps(a.attrs, b.attrs).forEach(function (d) { ops.push({ p: 'item', n: d.n, v: d.v }); });
    for (var i = 0; i < Math.max(a.comps.length, b.comps.length); i++) {
      var ac = a.comps[i], bc = b.comps[i];
      if (!ac || !bc || ac.el !== bc.el) { ops.push({ p: 'struct', i: i }); continue; }
      diffMaps(ac.attrs, bc.attrs).forEach(function (d) { ops.push({ p: 'comp', i: i, el: bc.el, n: d.n, v: d.v }); });
      diffMaps(ac.wflags, bc.wflags).forEach(function (d) { ops.push({ p: 'wf', i: i, el: bc.el, n: d.n, v: d.v }); });
    }
    diffMaps(a.flags, b.flags).forEach(function (d) { ops.push({ p: 'flag', n: d.n, v: d.v }); });
    var pa = pieceMap(a), pb = pieceMap(b);
    Object.keys(pa).forEach(function (t) {
      if (!pb[t]) { ops.push({ p: 'pieceDel', t: t }); return; }
      diffMaps(pa[t].attrs, pb[t].attrs).forEach(function (d) { ops.push({ p: 'piece', t: t, n: d.n, v: d.v }); });
    });
    Object.keys(pb).forEach(function (t) {
      if (pa[t]) return;
      ops.push({ p: 'pieceAdd', t: t, attrs: Object.keys(pb[t].attrs).map(function (n) { return [n, pb[t].attrs[n]]; }) });
    });
    return ops;
  }
  function sameModel(a, b) { return diff(a, b).length === 0; }
  // Applies diff() ops to a model (the page keeps its work in progress as ops, so it survives a data rebuild).
  // Returns the number of ops that no longer fit the model (a component or piece that is gone).
  function applyOps(m, ops) {
    var missed = 0;
    (ops || []).forEach(function (o) {
      if (o.p === 'item') { if (o.v == null) delete m.attrs[o.n]; else m.attrs[o.n] = o.v; return; }
      if (o.p === 'flag') { if (o.v == null) delete m.flags[o.n]; else m.flags[o.n] = o.v; return; }
      if (o.p === 'comp' || o.p === 'wf') {
        var c = m.comps[o.i];
        if (!c || c.el !== o.el) { missed++; return; }
        var tgt = o.p === 'comp' ? c.attrs : c.wflags;
        if (o.v == null) delete tgt[o.n]; else tgt[o.n] = o.v;
        return;
      }
      var pm = pieceMap(m);
      if (o.p === 'piece') { if (!pm[o.t]) { missed++; return; } if (o.v == null) delete pm[o.t].attrs[o.n]; else pm[o.t].attrs[o.n] = o.v; return; }
      if (o.p === 'pieceDel') { m.pieces = m.pieces.filter(function (p) { return (p.attrs.Type || '') !== o.t; }); return; }
      if (o.p === 'pieceAdd') {
        if (pm[o.t]) { missed++; return; }
        var at = {};
        o.attrs.forEach(function (a) { at[a[0]] = a[1]; });
        m.pieces.push({ attrs: at });
        return;
      }
      missed++;
    });
    return missed;
  }

  // Model paths, for the page: {el: 'item'} | {el: 'comp', i} | {el: 'wf', i} | {el: 'flag'} | {el: 'piece', t}.
  function mapAt(m, path) {
    if (path.el === 'item') return m.attrs;
    if (path.el === 'comp') return m.comps[path.i] && m.comps[path.i].attrs;
    if (path.el === 'wf') return m.comps[path.i] && m.comps[path.i].wflags;
    if (path.el === 'flag') return m.flags;
    if (path.el === 'piece') { var p = pieceMap(m)[path.t]; return p && p.attrs; }
    return null;
  }
  function getAt(m, path, n) { var o = mapAt(m, path); return o && o[n] !== undefined ? o[n] : null; }
  function setAt(m, path, n, v) {
    var o = mapAt(m, path);
    if (!o) return false;
    if (v == null || v === '') delete o[n]; else o[n] = String(v);
    return true;
  }

  // ---------------------------------------------------------------- the item's type

  function canonType(t, data) {
    var list = (data && data.enums && data.enums.ItemTypeEnum) || [];
    for (var i = 0; i < list.length; i++) if (list[i].toLowerCase() === String(t).toLowerCase()) return list[i];
    return null;
  }
  // ItemObject.Type as the game sets it: a CraftedItem's template item_type; an Item's Type attribute, overridden by its
  // first weapon's class when it has one.
  function typeOf(m, data) {
    if (m.kind === 'CraftedItem') {
      var tpl = data && data.templates && data.templates[m.attrs.crafting_template];
      return tpl ? tpl.itemType : '';
    }
    if (m.attrs.Type == null) return 'Invalid';
    var t = canonType(m.attrs.Type, data) || 'Invalid';
    var w = m.comps.filter(function (c) { return c.el === 'Weapon'; })[0];
    if (w && CLASS_TYPE[w.attrs.weapon_class]) t = CLASS_TYPE[w.attrs.weapon_class];
    return t;
  }

  // ---------------------------------------------------------------- tier and price (RBM)

  function num(v) { if (v == null || v === '') return NaN; var n = Number(String(v).trim()); return n; }
  function int0(v) { var n = num(v); return isNaN(n) ? 0 : Math.trunc(n); }
  // System.Math.Round(double): to even at .5 (TaleWorlds MathF.Round).
  function roundEven(x) {
    var r = Math.round(x);
    if (Math.abs(x - Math.trunc(x)) === 0.5) r = 2 * Math.round(x / 2);
    return r;
  }
  function clamp(x, a, b) { return Math.max(a, Math.min(b, x)); }
  function f2(x) { return (Math.round(x * 100) / 100).toString(); }
  // The component ItemObject.ItemComponent ends up with: the last <ItemComponent> child (a Weapon child reuses the
  // weapon component, so a weapon item keeps all its usages).
  function mainComp(m) { return m.comps.length ? m.comps[m.comps.length - 1] : null; }
  function weapons(m) { return m.comps.filter(function (c) { return c.el === 'Weapon'; }); }
  // WeaponComponentData.Deserialize: MaxDataValue = ammo_limit, else stack_amount, else hit_points, else 0.
  function maxDataValue(w) {
    var a = w.attrs;
    if (a.ammo_limit != null) return int0(a.ammo_limit);
    if (a.stack_amount != null) return int0(a.stack_amount);
    if (a.hit_points != null) return int0(a.hit_points);
    return 0;
  }

  // ItemObject.Tierf with RBM's DefaultItemValueModel patches (RealisticBattleCombatModule/CombatModule/Items/
  // ItemValuesTiers.Tiers.cs) and the vanilla code they leave alone (decompiled DefaultItemValueModel.CalculateTier,
  // CalculateTierNonCraftedWeapon, CalculateBannerTier). Returns { tierf (null = not previewed), tier (what the game
  // shows: clamp(round(Tierf), 0, 6); ItemObject.Tier is that minus 1), src, text }.
  function computeTier(m, data) {
    var res = function (tierf, src, text) {
      return { tierf: tierf, tier: tierf == null ? null : clamp(roundEven(tierf), 0, 6), src: src, text: text };
    };
    var to = num(m.attrs.tier_override);
    // ItemObject: TierfOverride = tier_override + 1, used when >= 1.
    if (!isNaN(to) && to + 1 >= 1) return res(to, 'override', 'tier_override = ' + m.attrs.tier_override);
    if (m.kind === 'CraftedItem') {
      return res(null, 'crafted', 'Computed in game from the weapon design: 0.6 × RBM melee tier (damage factors and speeds from the pieces) + 0.4 × piece tier. Not previewed.');
    }
    var type = typeOf(m, data), c = mainComp(m);
    if (!c) return res(0, 'none', 'No ItemComponent: the game shows Tier 1 (ItemObject.Tier), the value formula uses tier 1.');
    if (c.el === 'Armor') {
      var a = c.attrs, H = int0(a.head_armor), B = int0(a.body_armor), L = int0(a.leg_armor), A = int0(a.arm_armor), t;
      if (type === 'LegArmor') { t = L * 0.10; return res(Math.max(0, t), 'armor', 'leg ' + L + ' × 0.10'); }
      if (type === 'HandArmor') { t = A * 0.10; return res(Math.max(0, t), 'armor', 'arm ' + A + ' × 0.10'); }
      if (type === 'HeadArmor') { t = H * 0.06; return res(Math.max(0, t), 'armor', 'head ' + H + ' × 0.06'); }
      if (type === 'Cape') { t = (B + A) * 0.15; return res(Math.max(0, t), 'armor', '(body ' + B + ' + arm ' + A + ') × 0.15'); }
      if (type === 'BodyArmor') { t = B * 0.05 + L * 0.035 + A * 0.025; return res(Math.max(0, t), 'armor', 'body ' + B + ' × 0.05 + leg ' + L + ' × 0.035 + arm ' + A + ' × 0.025'); }
      if (type === 'HorseHarness') { t = B * 0.02 + L * 0.04 + A * 0.02 + H * 0.02; return res(Math.max(0, t), 'armor', 'body ' + B + ' × 0.02 + leg ' + L + ' × 0.04 + arm ' + A + ' × 0.02 + head ' + H + ' × 0.02'); }
      return res(0, 'armor', 'RBM gives other armor types tier 0');
    }
    if (c.el === 'Horse') {
      var h = c.attrs;
      var pack = h.is_pack_animal != null && String(h.is_pack_animal).trim().toLowerCase() === 'true';
      if (pack) return res(1, 'horse', 'pack animal: 1');
      var hp = int0(h.extra_health), man = int0(h.maneuver), spd = int0(h.speed);
      return res(0.009 * hp + 0.030 * man + 0.030 * spd, 'horse', 'extra_health ' + hp + ' × 0.009 + maneuver ' + man + ' × 0.03 + speed ' + spd + ' × 0.03');
    }
    if (c.el === 'Banner') {
      var lvl = int0(c.attrs.banner_level), lb = lvl === 3 ? 5 : lvl === 2 ? 3 : 1, cb = m.attrs.culture != null ? 1 : 0;
      return res(cb + lb, 'banner', 'culture ' + cb + ' + banner level ' + lvl + ' → ' + lb);
    }
    if (c.el === 'Weapon') {
      var w = weapons(m)[0], wa = w.attrs;
      if (LAUNCHER_TYPES.indexOf(type) >= 0) {
        var dw = int0(wa.missile_speed);
        if (type === 'Crossbow') return res((dw - 80) * 0.031 + 1, 'ranged', '(draw weight ' + dw + ' − 80) × 0.031 + 1');
        return res((dw - 60) * 0.049 + 1, 'ranged', '(draw weight ' + dw + ' − 60) × 0.049 + 1');
      }
      if (AMMO_TYPES.indexOf(type) >= 0) {
        var dmg = int0(wa.thrust_damage), wt = isNaN(num(m.attrs.weight)) ? 1 : num(m.attrs.weight);
        var at = dmg * 0.01 * ((wt * 100 - 4 + 0.01) * 0.8);
        return res(Math.min(6, at), 'ammo', 'damage ' + dmg + ' × 0.01 × ((weight ' + wt + ' × 100 − 4 + 0.01) × 0.8), at most 6');
      }
      if (type === 'Shield') {
        var shp = maxDataValue(w), arm = int0(wa.body_armor), len = int0(wa.weapon_length);
        var st = ((shp - 400) * 0.005 + arm * 0.2) * (len / 60);
        return res(Math.min(6.5, st), 'shield', '((hit points ' + shp + ' − 400) × 0.005 + armor ' + arm + ' × 0.2) × (length ' + len + ' / 60), at most 6.5');
      }
      return res(0, 'weapon', 'A non-crafted ' + (type || 'weapon') + ' gets tier 0 (DefaultItemValueModel.CalculateTierNonCraftedWeapon)');
    }
    return res(0, 'other', c.el + ' component: tier 0');
  }

  // RBM config defaults (RBMConfig/Config/RBMConfig.Core.cs PriceModifiers).
  var DEFAULT_MULT = { armor: 1, weapon: 1, horse: 0.2, trade: 1 };
  // ItemObject.Value: the value attribute, else DefaultItemValueModel.CalculateValue as RBM patches it
  // (CombatModule/Items/ItemValuesTiers.Pricing.cs): CalculateCampaignValue with RBM campaign on, CalculateLegacyValue
  // without; tier = 2.75^clamp(Tierf, -1, 7.5) (GetEquipmentValueFromTier), 1 without a component; (int) truncates.
  // Returns { value, src: 'value'|'computed'|'unknown', campaign, legacy, text }.
  function computePrice(m, data, tierInfo, mult) {
    mult = mult || DEFAULT_MULT;
    if (m.attrs.value != null) return { value: int0(m.attrs.value), src: 'value', text: 'value attribute' };
    if (!tierInfo || tierInfo.tierf == null) return { value: null, src: 'unknown', text: 'computed in game (needs the tier)' };
    var c = mainComp(m), type = typeOf(m, data);
    var tier = c ? Math.pow(2.75, clamp(tierInfo.tierf, -1, 7.5)) : 1;
    var camp = 1, leg = 1, how = '';
    if (c && c.el === 'Armor') {
      var a = c.attrs, H = int0(a.head_armor), B = int0(a.body_armor), L = int0(a.leg_armor), A = int0(a.arm_armor);
      var mat = a.material_type;
      var mc = mat === 'Cloth' ? 0.4 * clamp(tier - 1, 0, 4) : mat === 'Leather' ? 0.6 * clamp(tier - 1, 0, 6) :
        mat === 'Chainmail' ? 1.6 * clamp(tier - 3, 1, 6) : mat === 'Plate' ? 1.7 * clamp(tier - 3, 1, 6) : 50;
      var ml = mat === 'Cloth' ? 5 : mat === 'Leather' ? 15 : mat === 'Chainmail' ? 80 : mat === 'Plate' ? 120 : 50;
      if (type === 'LegArmor') { camp = 50 + 4 * (L * mc); leg = 75 + L * ml; how = '50 + 4 × leg × material'; }
      else if (type === 'HandArmor') { camp = 50 + 5 * (A * mc * 0.8); leg = 50 + A * ml * 0.8; how = '50 + 5 × arm × material × 0.8'; }
      else if (type === 'HeadArmor') { camp = 70 + 3 * (H * mc * 1.2 + B * mc * 0.6); leg = 100 + (H * ml * 1.2 + B * ml * 0.6); how = '70 + 3 × (head × material × 1.2 + body × material × 0.6)'; }
      else if (type === 'Cape') { camp = 50 + 5 * (B * mc * 0.8 + A * mc * 0.8); leg = 50 + (B * ml * 0.8 + A * ml * 0.8); how = '50 + 5 × (body + arm) × material × 0.8'; }
      else if (type === 'BodyArmor') { camp = 150 + 5 * (B * mc * 2.5 + L * mc + A * mc * 0.8); leg = 200 + (B * ml * 2.5 + L * ml + A * ml * 0.8); how = '150 + 5 × (body × 2.5 + leg + arm × 0.8) × material'; }
      else if (type === 'HorseHarness') { camp = 600 + 70 * mc * (B * 0.2 + A * 0.2 + L * 0.4 + H * 0.2); leg = 100 + (B * 0.2 + A * 0.2 + L * 0.4 + H * 0.2 * 450); how = '600 + 70 × material × (body × 0.2 + arm × 0.2 + leg × 0.4 + head × 0.2)'; }
      camp *= mult.armor; leg *= mult.armor;
      how += '; material factor ' + f2(mc) + ' (' + (mat || 'None') + ', from tier value ' + f2(tier) + '); × armor multiplier ' + mult.armor;
    } else if (c && c.el === 'Weapon') {
      camp = (30 + tier * 24) * mult.weapon; how = '(30 + tier value × 24)';
      leg = (50 + tier * 100) * 0.7 * mult.weapon;
      if (type === 'Polearm') { camp = (30 + tier * 16) * mult.weapon; how = '(30 + tier value × 16)'; leg *= 0.3; }
      if (type === 'Thrown') leg *= 0.25;
      if (type === 'Sling') { camp *= 0.25; leg *= 0.25; how += ' × 0.25'; }
      if (type === 'TwoHandedWeapon') { camp *= 1.5; leg *= 1.5; how += ' × 1.5'; }
      if (type === 'Shield') { camp = (120 + tier * 2) * mult.weapon; how = '(120 + tier value × 2)'; leg *= 0.3; }
      if (AMMO_TYPES.slice(0, 3).indexOf(type) >= 0) { camp = (20 + tier) * mult.weapon; leg = (30 + tier * 10) * mult.weapon; how = '(20 + tier value)'; }
      how += ' × weapon multiplier ' + mult.weapon + '; tier value ' + f2(tier);
    } else if (c && c.el === 'Horse') {
      var ap = isNaN(num(m.attrs.appearance)) ? 0.5 : num(m.attrs.appearance);
      camp = 600 + 1000 * tier * mult.horse; how = '600 + 1000 × tier value ' + f2(tier) + ' × horse multiplier ' + mult.horse;
      leg = 200 * tier * mult.horse * (1 + 0.2 * (ap - 1)) + 100 * Math.max(0, ap - 1);
    } else {
      camp = 1; leg = 1; how = 'no armor, weapon or horse component: 1 (trade goods are priced by RBMCampaign\'s market)';
    }
    return { value: Math.trunc(camp), src: 'computed', campaign: Math.trunc(camp), legacy: Math.trunc(leg), text: how };
  }

  // RBMConfig/Shared/MissileBallistics.CalculateMissileSpeed (float steps as in C#). drawWeight = the launcher's
  // missile_speed, which RBM reads as draw weight.
  var fr = Math.fround || function (x) { return x; };
  function missileSpeed(ammoWeight, usage, dw) {
    ammoWeight = ammoWeight > 0 && isFinite(ammoWeight) ? ammoWeight : 0.07;
    var bowLike = function (stroke, eff, virt) {
      var pe = fr(fr(fr(0.5 * fr(dw * fr(4.448))) * fr(stroke * fr(0.0254))) * fr(eff));
      var w = fr(ammoWeight + fr(dw * fr(virt)));
      return Math.floor(Math.sqrt(pe * 2 / w));
    };
    switch (usage) {
      case 'bow': return bowLike(25, 0.9, 0.00015);
      case 'long_bow': return bowLike(25, 0.835, 0.00018);
      case 'crossbow': case 'crossbow_fast': case 'crossbow_light': return bowLike(20, 0.88, 0.00015);
      case 'osa_sling': return Math.floor(Math.sqrt(fr(0.5 * (dw * dw) * fr(0.12)) * 2 / fr(ammoWeight + fr(0.04))));
      case 'cla_musket': case 'cla_flint_rifle': case 'cla_pistol': case 'cla_revolver': case 'cla_cannon': case 'cla_bolt_rifle':
        return Math.floor(Math.sqrt(dw * 2 / ammoWeight));
      case 'cla_bomb': return Math.floor(Math.sqrt(150 * 2 / ammoWeight));
      default: return dw;
    }
  }
  // CombatModule/Magnitude/Tooltips/MagnitudeChanges.WeaponTooltip.cs GetLauncherTooltipStats (no item modifier).
  // Returns null for anything but a bow or crossbow usage.
  function launcherInfo(m) {
    var out = null;
    weapons(m).some(function (w, i) {
      var cls = w.attrs.weapon_class;
      if (cls !== 'Bow' && cls !== 'Crossbow') return false;
      var dw = int0(w.attrs.missile_speed), usage = w.attrs.item_usage || '';
      var ideal = cls === 'Bow' ? dw / (usage === 'bow' ? 1600 : 1400) : clamp(dw / 1750, 0, 0.15);
      var speeds = [0.03, 0.05, 0.07, 0.09, 0.12].map(function (g) { return { weight: g, speed: missileSpeed(g, usage, dw) }; });
      out = { usage: i, cls: cls, drawWeight: dw, itemUsage: usage, idealWeight: ideal, idealSpeed: missileSpeed(ideal, usage, dw), speeds: speeds,
        text: cls === 'Bow' ? 'ideal ammo weight = draw weight / ' + (usage === 'bow' ? 1600 : 1400) + ' (item_usage ' + (usage === 'bow' ? '"bow"' : 'not "bow"') + ')'
          : 'ideal ammo weight = draw weight / 1750, at most 150 g' };
      return true;
    });
    return out;
  }

  // ---------------------------------------------------------------- problem checks

  var INT_TYPES = { 'xs:int': 1, 'xs:integer': 1, 'xs:short': 1, 'xs:long': 1 };
  var UINT_TYPES = { 'xs:unsignedInt': 1, 'xs:unsignedShort': 1, 'xs:unsignedLong': 1, 'xs:unsignedByte': 1, 'xs:nonNegativeInteger': 1 };
  var DEC_TYPES = { 'xs:decimal': 1, 'xs:float': 1, 'xs:double': 1 };
  function attrKind(info) {
    if (!info) return 'str';
    if (INT_TYPES[info.type]) return 'int';
    if (UINT_TYPES[info.type]) return 'uint';
    if (DEC_TYPES[info.type]) return 'dec';
    if (info.type === 'xs:boolean') return 'bool';
    return 'str';
  }
  // The game parses these with int.Parse even where the XSD says decimal (ItemObject.Deserialize: CraftedItem value).
  var FORCE_INT = { 'CraftedItem@value': 1, 'Item@value': 1 };
  function isInt(v) { return /^\s*[+-]?\d+\s*$/.test(v); }
  function isDec(v) { return /^\s*[+-]?(\d+\.?\d*|\.\d+)([eE][+-]?\d+)?\s*$/.test(v); }

  function enumValues(spec, data) { return spec.list || (data.enums && data.enums[spec.e]) || []; }
  function inEnum(v, spec, data) {
    var list = enumValues(spec, data);
    if (spec.ci) { var lv = String(v).toLowerCase(); return list.some(function (x) { return x.toLowerCase() === lv; }); }
    return list.indexOf(v) >= 0;
  }
  function flagNames(data, which) {
    var xsd = Object.keys((data.vocab && data.vocab[which]) || {});
    var en = cleanEnum((data.enums && data.enums[which === 'Flags' ? 'ItemFlags' : 'WeaponFlags']) || []);
    en.forEach(function (n) { if (xsd.indexOf(n) < 0) xsd.push(n); });
    return xsd;
  }
  function refExists(spec, v, data) {
    var id = v;
    if (spec.prefix) id = v.slice(spec.prefix.length);
    switch (spec.ref) {
      case 'culture': return !!(data.cultures && data.cultures[id]);
      case 'category': return !!(data.itemCategories && data.itemCategories[id]);
      case 'modgroup': return (data.modifierGroups || []).indexOf(id) >= 0;
      case 'holsters': return !!(data.holsters && data.holsters[id]);
      case 'monster': return !!(data.monsters && id in data.monsters);
      case 'template': return !!(data.templates && data.templates[id]);
      case 'usage': return (data.itemUsages || []).indexOf(id) >= 0;
      case 'piece': return !!(data.pieces && data.pieces[id]);
    }
    return true;
  }
  var REF_WORD = { culture: 'culture', category: 'item category', modgroup: 'item modifier group', holsters: 'item holster',
    monster: 'monster', template: 'crafting template', usage: 'item usage set', piece: 'crafting piece' };

  // Checks one element's attributes against the XSD and the game's parsers. where: label; el: XSD element name.
  function checkAttrs(add, data, el, attrs, where) {
    var vocab = (data.vocab && data.vocab[el]) || null;
    Object.keys(attrs).forEach(function (n) {
      var v = attrs[n], key = el + '@' + n, info = vocab ? vocab[n] : null;
      if (vocab && !info) {
        if (n !== '_replaceWhileMerging') add('warn', where + ': attribute ' + n + ' is not in Items.xsd for <' + el + '> (the game ignores it)');
        return;
      }
      var k = FORCE_INT[key] ? 'int' : attrKind(info);
      if (k === 'int' && !isInt(v)) add('bad', where + ': ' + n + '="' + v + '" is not a whole number (the game\'s int.Parse throws)');
      else if (k === 'uint' && (!isInt(v) || Number(v) < 0)) add('bad', where + ': ' + n + '="' + v + '" is not a whole number ≥ 0');
      else if (k === 'dec' && !isDec(v)) add('bad', where + ': ' + n + '="' + v + '" is not a number');
      else if (k === 'bool' && !/^\s*(true|false)\s*$/i.test(v)) add('warn', where + ': ' + n + '="' + v + '" is not true/false');
      if (info && info.values && info.values.indexOf(v) < 0) add('bad', where + ': ' + n + '="' + v + '" is not one of ' + info.values.join(', '));
      var es = ATTR_ENUMS[key];
      if (es && !inEnum(v, es, data)) {
        add(es.soft ? 'warn' : 'bad', where + ': ' + n + '="' + v + '" is not a valid value' + (es.soft ? ' (ignored: the game uses its default)' : ' (Enum.Parse throws)') +
          '; one of ' + cleanEnum(enumValues(es, data)).join(', '));
      }
      var rs = ATTR_REFS[key];
      // An empty reference is skipped by the game (item_category: string.IsNullOrEmpty; modifier_group="": no group).
      if (rs && v !== '') {
        if (rs.prefix && v.indexOf(rs.prefix) !== 0) { add('bad', where + ': ' + n + '="' + v + '" must start with "' + rs.prefix + '"'); return; }
        var vals = rs.list ? v.split(rs.list).filter(function (x) { return x !== ''; }) : [v];
        vals.forEach(function (x) {
          if (rs.ref === 'usage' && x === '') return;
          if (!refExists(rs, x, data)) add('warn', where + ': unknown ' + REF_WORD[rs.ref] + ' "' + (rs.prefix ? x.slice(rs.prefix.length) : x) + '"');
        });
      }
    });
    if (vocab) {
      Object.keys(vocab).forEach(function (n) {
        if (vocab[n].use === 'required' && attrs[n] == null && !(el === 'Item' && n === 'name') && !(el === 'CraftedItem' && n === 'name')) {
          add('warn', where + ': required attribute ' + n + ' (Items.xsd) is missing');
        }
      });
    }
  }
  function checkFlags(add, data, which, flags, where) {
    var names = flagNames(data, which), all = (data.enums && data.enums[which === 'Flags' ? 'ItemFlags' : 'WeaponFlags']) || [];
    Object.keys(flags).forEach(function (n) {
      if (names.indexOf(n) < 0 && all.indexOf(n) < 0) add('warn', where + ': ' + n + ' is not a ' + (which === 'Flags' ? 'ItemFlags' : 'WeaponFlags') + ' name (ignored)');
    });
  }

  // Problems of an item's current state: [{cls: 'bad'|'warn'|'info', text}].
  // info: { item (data entry, null for a new item), isNew, edited, targets (append targets), allIds (id -> true),
  // newIds (id -> count) }.
  // Whether an <Armor> attribute value equals what ArmorComponent.Deserialize uses when the attribute is absent
  // (decompiled/TaleWorlds.Core/TaleWorlds.Core/ArmorComponent.cs).
  var ARMOR_ZERO = ['head_armor', 'body_armor', 'leg_armor', 'arm_armor', 'family_type', 'maneuver_bonus', 'speed_bonus', 'charge_bonus', 'stealth_factor'];
  function isArmorDefault(n, v) {
    var s = String(v).trim().toLowerCase();
    if (ARMOR_ZERO.indexOf(n) >= 0) return isFinite(+s) && +s === 0;
    if (n === 'has_gender_variations') return s === 'true';
    if (/^covers_/.test(n)) return s === 'false';
    if (n === 'material_type' || /_cover_type$/.test(n)) return s === 'none';
    if (n === 'body_mesh_type') return v !== 'upperbody' && v !== 'shoulders';
    if (n === 'body_deform_type') return v !== 'large' && v !== 'skinny';
    if (n === 'reins_mesh') return s === '';
    return false;
  }
  function armorDefaultText(n) {
    if (ARMOR_ZERO.indexOf(n) >= 0) return '0';
    if (n === 'has_gender_variations') return 'true';
    if (/^covers_/.test(n)) return 'false';
    if (n === 'material_type' || /_cover_type$/.test(n)) return 'none';
    if (n === 'body_mesh_type') return 'normal';
    if (n === 'body_deform_type') return 'medium';
    if (n === 'reins_mesh') return 'none';
    return 'not set';
  }
  // RBM replaces the whole element (no attribute merge), so an attribute vanilla sets and the item's definition m
  // leaves out falls back to the game's default; only a vanilla value that differs from that default is lost.
  // v0 is the definition before RBM replaced it. Checked: every <Armor> attribute, and modifier_group on <Weapon>
  // (absent = no modifier group: ItemComponent.Deserialize) and on <CraftedItem> (absent = the crafting template's
  // group: ItemObject.Deserialize). Returns one message per element.
  function lostVanillaAttrs(m, v0, data) {
    var out = [];
    if (!v0 || v0 === m || v0.kind !== m.kind) return out;
    // A vanilla group that does not exist (vanilla's shield_wood) resolves to null in game too: nothing is lost.
    var groups = data && data.modifierGroups, realGroup = function (g) { return !groups || groups.indexOf(g) >= 0; };
    var vWpn = weapons(v0), mWpn = weapons(m);
    vWpn.forEach(function (vc, i) {
      var mg = vc.attrs.modifier_group, mc = mWpn[i], where = 'Weapon' + (vWpn.length > 1 ? ' usage ' + (i + 1) : '');
      if (mg == null || mg === '' || !realGroup(mg) || !mc || mc.attrs.modifier_group != null) return;
      out.push(where + ': vanilla sets modifier_group="' + mg + '" but this definition does not, so RBM\'s full replacement leaves the weapon without a modifier group (no quality modifiers)');
    });
    if (m.kind === 'CraftedItem') {
      var vg = v0.attrs.modifier_group, tpl = data && data.templates && data.templates[m.attrs.crafting_template];
      var fallback = tpl && tpl.modifierGroup ? tpl.modifierGroup : '';
      if (vg != null && vg !== '' && realGroup(vg) && m.attrs.modifier_group == null && String(m.attrs.has_modifier).toLowerCase() !== 'false' && vg !== fallback) {
        out.push('CraftedItem: vanilla sets modifier_group="' + vg + '" but this definition does not, so RBM\'s full replacement uses template ' +
          (m.attrs.crafting_template || '?') + '\'s group (' + (fallback || 'none') + ')');
      }
    }
    var vArm = v0.comps.filter(function (c) { return c.el === 'Armor'; }), mArm = m.comps.filter(function (c) { return c.el === 'Armor'; });
    vArm.forEach(function (vc, i) {
      var mc = mArm[i], where = 'Armor' + (vArm.length > 1 ? ' ' + (i + 1) : '');
      if (!mc) { out.push(where + ': vanilla has an <Armor> component, this definition has none'); return; }
      var lost = Object.keys(vc.attrs).filter(function (n) { return vc.attrs[n] != null && vc.attrs[n] !== '' && mc.attrs[n] == null && !isArmorDefault(n, vc.attrs[n]); });
      if (lost.length) out.push(where + ': vanilla sets ' + lost.map(function (n) { return n + '="' + vc.attrs[n] + '"'; }).join(', ') +
        ' but this definition does not, so RBM\'s full replacement makes ' + (lost.length > 1 ? 'them' : 'it') + ' the game default (' +
        lost.map(function (n) { return n + ' ' + armorDefaultText(n); }).join(', ') + ')');
    });
    return out;
  }
  function problems(m, data, info) {
    var out = [], add = function (cls, text) { out.push({ cls: cls, text: text }); };
    var it = info.item || null, inRbm = !!(it && it.rbm && it.rbm.length);
    var id = m.attrs.id;
    if (!id) add('bad', 'No id');
    if (m.attrs.name == null) add('bad', 'No name attribute (ItemObject.Deserialize reads it unconditionally)');
    if (m.kind === 'Item' && m.attrs.Type == null) {
      add('bad', 'No Type attribute: with betterArrowVisuals on (the default) RBM/XmlLoadingPatches.cs reads Attribute("Type").Value of every Item in an RBM file, so loading throws');
    }
    if (info.isNew) {
      if (id && info.allIds && info.allIds[id]) add('bad', 'The id "' + id + '" already exists: pick a new one');
      if (id && info.newIds && info.newIds[id] > 1) add('bad', 'Two new items share the id "' + id + '"');
      if (id && !/^[\w.-]+$/.test(id)) add('warn', 'The id "' + id + '" has characters other than letters, digits, _ . -');
    } else if (it && id !== it.id) add('bad', 'The id was changed: use "Duplicate as new item" instead');
    if ((info.isNew || (!inRbm && info.edited)) && !(info.targets && info.targets.length)) add('bad', 'No RBM file to write it to: pick a target file');
    checkAttrs(add, data, m.kind, m.attrs, m.kind);
    var type = typeOf(m, data);
    if (m.kind === 'Item' && m.attrs.Type != null) {
      var ct = canonType(m.attrs.Type, data);
      var w = weapons(m)[0];
      if (ct && w && CLASS_TYPE[w.attrs.weapon_class] && CLASS_TYPE[w.attrs.weapon_class] !== ct) {
        add('warn', 'Type ' + ct + ' is replaced in game by ' + CLASS_TYPE[w.attrs.weapon_class] + ' (the first weapon\'s class ' + w.attrs.weapon_class + ')');
      }
      if (ct && ARMOR_TYPES.indexOf(ct) >= 0 && !m.comps.some(function (c) { return c.el === 'Armor'; })) add('warn', ct + ' without an <Armor> component');
    }
    m.comps.forEach(function (c, i) {
      var where = c.el + (m.comps.length > 1 ? ' ' + (i + 1) : '');
      if (['Armor', 'Weapon', 'Horse', 'Trade', 'Banner'].indexOf(c.el) < 0) add('bad', '<ItemComponent> child <' + c.el + '>: ItemObject.Deserialize throws "Wrong ItemComponent type"');
      checkAttrs(add, data, c.el, c.attrs, where);
      if (c.el === 'Weapon' || c.el === 'Banner') checkFlags(add, data, 'WeaponFlags', c.wflags, where + ' WeaponFlags');
    });
    checkFlags(add, data, 'Flags', m.flags, 'Flags');
    lostVanillaAttrs(m, info.vanilla, data).forEach(function (t) { add('warn', t); });
    if (m.kind === 'CraftedItem') {
      var tpl = data.templates && data.templates[m.attrs.crafting_template];
      if (!m.pieces.length) add('bad', 'No pieces');
      m.pieces.forEach(function (p) {
        var pt = p.attrs.Type, pid = p.attrs.id, where = 'Piece ' + (pt || '?');
        checkAttrs(add, data, 'Piece', p.attrs, where);
        var pc = data.pieces && data.pieces[pid];
        if (pc && pt && pc.type && pc.type !== pt) add('bad', where + ': ' + pid + ' is a ' + pc.type + ' piece');
        if (tpl && pt && tpl.pieceTypes.length && tpl.pieceTypes.indexOf(pt) < 0) add('warn', where + ': template ' + m.attrs.crafting_template + ' has no ' + pt + ' piece');
        if (tpl && pid && tpl.usable.length && tpl.usable.indexOf(pid) < 0) add('warn', where + ': ' + pid + ' is not in template ' + m.attrs.crafting_template + '\'s UsablePieces (the smithy cannot build it)');
      });
      if (tpl && m.attrs.modifier_group == null && !tpl.modifierGroup) add('warn', 'No modifier_group and the template has none');
    }
    if (it && inRbm) {
      var files = it.rbm, loaded = (data.rbmFiles || []).filter(function (f) { return files.indexOf(f.path) >= 0 && f.loadsInCampaign; });
      if (files.length > 1) add('info', 'Defined in ' + files.length + ' RBM files (' + files.join(', ') + '): the export patches the changed attributes in each');
      if (!loaded.length) add('warn', 'Only defined in RBM files that do not load in campaigns (' + files.join(', ') + ')');
    }
    if (type === 'Invalid' && m.kind === 'Item' && m.attrs.Type != null && !canonType(m.attrs.Type, data)) { /* reported by the enum check */ }
    return out;
  }

  // ---------------------------------------------------------------- export: patching an element's text

  // The indentation unit of an element's text: a tab if any deeper line starts with one, else the smallest run of
  // spaces deeper lines add to baseIndent (attribute alignment adds more, child elements add one unit).
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
  function makeTag(name, list, sample, indent, dflt) {
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
    var t = '<' + name + list.map(function (a, i) { return (i ? sepN : sep1) + a[0] + eq + '"' + T.escAttr(a[1]) + '"'; }).join('') + tail;
    if (!/\/>$/.test(t)) t += '</' + name + '>';
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
  // Inserts newText as a child of the element elText (indent elIndent), after the last child named after (or at the
  // end), converting a self-closing element to an open one.
  function insertChild(elText, newText, after, ctx, elIndent) {
    var ci = childInfo(elText), childIndent = childIndentOf(elText, ci.kids, elIndent, ctx);
    if (ci.st.self) {
      return ci.st.text.replace(/\s*\/>$/, '>') + ctx.eol + childIndent + newText + ctx.eol + elIndent + '</' + ci.st.name + '>' + elText.slice(ci.st.end);
    }
    var anchor = null;
    if (after) elKids(ci.kids, after).forEach(function (k) { anchor = k; });
    if (anchor) return elText.slice(0, anchor.end) + ctx.eol + childIndent + newText + elText.slice(anchor.end);
    var cs = ci.close, pre = elText.slice(0, cs).replace(/\s*$/, '');
    return pre + ctx.eol + childIndent + newText + ctx.eol + elIndent + elText.slice(cs);
  }
  function removeChildTok(text, tok) {
    var before = text.slice(0, tok.start), ws = /\s*$/.exec(before)[0];
    return before.slice(0, before.length - ws.length) + text.slice(tok.end);
  }
  // Applies attribute ops to the first child <childName> of elText (created when missing, removed when it ends up
  // with no attributes and no children).
  function patchAttrChild(elText, childName, ops, ctx, elIndent, after, ownText) {
    if (!ops.length) return elText;
    var ci = childInfo(elText), kids = elKids(ci.kids, childName);
    if (kids.length) {
      var k = kids[0], kt = elText.slice(k.start, k.end), st = T.startTagOf(kt), tag = st.text;
      ops.forEach(function (o) { tag = setOrRemove(tag, o.n, o.v); });
      var empty = !Object.keys(attrsOf(tag)).length && (st.self || !elKids(childInfo(kt).kids).length);
      if (empty) return removeChildTok(elText, k);
      return elText.slice(0, k.start) + tag + kt.slice(st.end) + elText.slice(k.end);
    }
    var adds = ops.filter(function (o) { return o.v != null; }).map(function (o) { return [o.n, o.v]; });
    if (!adds.length) return elText;
    var childIndent = childIndentOf(elText, ci.kids, elIndent, ctx);
    var sample = sampleIn(ownText || '', childName) || ctx.samples[childName] || null;
    var tag2 = makeTag(childName, adds, sample, childIndent, defaultShape(ci.st.text, childIndent, ctx));
    return insertChild(elText, tag2, after, ctx, elIndent);
  }
  function patchStartTag(text, ops) {
    if (!ops.length) return text;
    var st = T.startTagOf(text), tag = st.text;
    ops.forEach(function (o) { tag = setOrRemove(tag, o.n, o.v); });
    return tag + text.slice(st.end);
  }
  // The component children of every <ItemComponent>, as absolute spans in text.
  function compSpans(text) {
    var out = [], ci = childInfo(text);
    elKids(ci.kids, 'ItemComponent').forEach(function (ic) {
      var it = text.slice(ic.start, ic.end), cc = childInfo(it);
      elKids(cc.kids).forEach(function (c) { out.push({ start: ic.start + c.start, end: ic.start + c.end, name: c.name }); });
    });
    return out;
  }

  // Applies ops (see diff) to an item element's text. indent: the element's indentation in its file. Returns
  // { text, notes: [[cls, text]] }.
  function patchElement(text, ops, ctx, indent) {
    var notes = [], own = text;
    // The element's own indentation unit (a component's indent minus its <ItemComponent>'s), else the file's: some
    // files mix tabs and spaces from item to item.
    var ic = elKids(childInfo(text).kids, 'ItemComponent')[0];
    if (ic) {
      var icIndent = T.indentAt(text, ic.start), cs0 = compSpans(text)[0], cIndent0 = cs0 ? T.indentAt(text, cs0.start) : '';
      if (icIndent && cIndent0.length > icIndent.length && cIndent0.indexOf(icIndent) === 0) {
        ctx = Object.assign({}, ctx, { unit: cIndent0.slice(icIndent.length) });
      }
    }
    text = patchStartTag(text, ops.filter(function (o) { return o.p === 'item'; }));
    var byComp = {};
    ops.forEach(function (o) { if (o.p === 'comp' || o.p === 'wf') (byComp[o.i] = byComp[o.i] || []).push(o); });
    Object.keys(byComp).forEach(function (i) {
      var cops = byComp[i], spans = compSpans(text), sp = spans[+i];
      if (!sp) { notes.push(['bad', 'has no component ' + (+i + 1) + ' (' + cops[0].el + '): ' + cops.length + ' change(s) not written']); return; }
      if (sp.name !== cops[0].el) { notes.push(['bad', 'component ' + (+i + 1) + ' is <' + sp.name + '>, not <' + cops[0].el + '>: ' + cops.length + ' change(s) not written']); return; }
      var ct = text.slice(sp.start, sp.end), cIndent = T.indentAt(text, sp.start) || indent + ctx.unit + ctx.unit;
      ct = patchStartTag(ct, cops.filter(function (o) { return o.p === 'comp'; }));
      ct = patchAttrChild(ct, 'WeaponFlags', cops.filter(function (o) { return o.p === 'wf'; }), ctx, cIndent, null, own);
      text = text.slice(0, sp.start) + ct + text.slice(sp.end);
    });
    text = patchAttrChild(text, 'Flags', ops.filter(function (o) { return o.p === 'flag'; }), ctx, indent, 'ItemComponent', own);
    var pops = ops.filter(function (o) { return o.p === 'piece' || o.p === 'pieceAdd' || o.p === 'pieceDel'; });
    if (pops.length) {
      var ci = childInfo(text), pk = elKids(ci.kids, 'Pieces')[0];
      if (!pk) notes.push(['bad', 'has no <Pieces>: piece changes not written']);
      else {
        var pt = text.slice(pk.start, pk.end), pIndent = T.indentAt(text, pk.start) || indent + ctx.unit;
        pops.forEach(function (o) {
          var pc = childInfo(pt), pieces = elKids(pc.kids, 'Piece');
          var find = function (t) { return pieces.filter(function (p) { return attrsOf(T.startTagOf(pt.slice(p.start, p.end)).text).Type === t; }); };
          if (o.p === 'pieceDel') { find(o.t).reverse().forEach(function (p) { pt = removeChildTok(pt, p); }); return; }
          if (o.p === 'piece') {
            var hit = find(o.t);
            if (!hit.length) { notes.push(['bad', 'has no ' + o.t + ' piece: ' + o.n + ' not written']); return; }
            hit.reverse().forEach(function (p) {
              var s = pt.slice(p.start, p.end), st = T.startTagOf(s);
              pt = pt.slice(0, p.start) + setOrRemove(st.text, o.n, o.v) + s.slice(st.end) + pt.slice(p.end);
            });
            return;
          }
          var last = pieces[pieces.length - 1], childIndent = childIndentOf(pt, pc.kids, pIndent, ctx);
          var sample = last ? { tag: T.startTagOf(pt.slice(last.start, last.end)).text, indent: T.indentAt(pt, last.start) || childIndent } :
            (sampleIn(own, 'Piece') || ctx.samples.Piece || null);
          var nt = makeTag('Piece', o.attrs, sample, childIndent, defaultShape(pc.st.text, childIndent, ctx));
          pt = insertChild(pt, nt, 'Piece', ctx, pIndent);
        });
        text = text.slice(0, pk.start) + pt + text.slice(pk.end);
      }
    }
    if (ops.some(function (o) { return o.p === 'struct'; })) notes.push(['bad', 'components were added or removed: not supported, not written']);
    return { text: text, notes: notes };
  }

  // ---------------------------------------------------------------- export: files

  var ctxCache = {};
  function fileCtx(f) {
    if (ctxCache[f.path] && ctxCache[f.path].src === f.text) return ctxCache[f.path];
    var text = f.text, eol = f.eol || '\n';
    var m = /^([ \t]*)<(Item|CraftedItem)\b/m.exec(text);
    var itemIndent = m ? m[1] : '\t';
    var unit = '\t';
    if (m) {
      var at = m.index + m[1].length, node = T.parseChildren(text, at, Math.min(text.length, at + 200000))[0];
      if (node && node.kind === 'el') unit = detectUnit(text.slice(node.start, node.end).replace(/\r/g, ''), itemIndent);
    }
    var spaced = (text.match(/ \/>/g) || []).length, tight = (text.match(/[^ ]\/>/g) || []).length;
    var ctx = {
      src: text, eol: eol, itemIndent: itemIndent, unit: unit, selfClose: spaced >= tight ? ' />' : '/>',
      samples: { Flags: sampleIn(text, 'Flags'), WeaponFlags: sampleIn(text, 'WeaponFlags'), Piece: sampleIn(text, 'Piece') }
    };
    ctxCache[f.path] = ctx;
    return ctx;
  }
  // "kind|id" -> [{start, end}] of the top-level items of a file.
  function itemSpans(text) {
    var r = T.rootRange(text), map = {};
    T.parseChildren(text, r.innerStart, r.innerEnd).forEach(function (n) {
      if (n.kind !== 'el' || (n.name !== 'Item' && n.name !== 'CraftedItem')) return;
      var id = attrsOf(text.slice(n.start, n.tagEnd)).id;
      if (id == null) return;
      (map[n.name + '|' + id] = map[n.name + '|' + id] || []).push({ start: n.start, end: n.end });
    });
    return map;
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

  // Which RBM file(s) an item not yet in one is appended to, by its type. Ranged launchers, thrown weapons and sling
  // stones go to both ranged twins (RBMCombat_ranged.xml loads with RBM campaign off, RBMEconomyCombat_ranged.xml with
  // it on); War Sails items to RBM_WS_XML.
  var TYPE_FILE = {
    BodyArmor: 'RBMXML/RBMCombat_body_armors.xml', HeadArmor: 'RBMXML/RBMCombat_head_armors.xml', HandArmor: 'RBMXML/RBMCombat_arm_armors.xml',
    LegArmor: 'RBMXML/RBMCombat_leg_armors.xml', Cape: 'RBMXML/RBMCombat_shoulder_armors.xml', HorseHarness: 'RBMXML/RBMCombat_horse_armors.xml',
    Horse: 'RBMXML/RBMCombat_horses.xml', Shield: 'RBMXML/RBMCombat_shields.xml', Arrows: 'RBMXML/RBMCombat_arrow_visuals.xml',
    Bolts: 'RBMXML/RBMCombat_arrow_visuals.xml', Goods: 'RBMXML/RBMEconomy_trade_goods.xml', Animal: 'RBMXML/RBMEconomy_trade_goods.xml',
    OneHandedWeapon: 'RBMXML/RBMCombat_gladius.xml', TwoHandedWeapon: 'RBMXML/RBMCombat_gladius.xml', Polearm: 'RBMXML/RBMCombat_lances.xml'
  };
  var RANGED_TWINS = ['RBMXML/RBMCombat_ranged.xml', 'RBMXML/RBMEconomyCombat_ranged.xml'];
  function defaultTargets(m, it, data) {
    var have = {};
    (data.rbmFiles || []).forEach(function (f) { have[f.path] = true; });
    var pick = function (list) { return list.filter(function (p) { return have[p]; }); };
    var type = typeOf(m, data), naval = it && it.naval && have['RBM_WS_XML/RBMCombat_WS_items.xml'];
    if (m.kind === 'CraftedItem') {
      if (naval && have['RBM_WS_XML/RBMCombat_WS_Weapons.xml']) return ['RBM_WS_XML/RBMCombat_WS_Weapons.xml'];
      return pick([type === 'Polearm' ? 'RBMXML/RBMCombat_lances.xml' : 'RBMXML/RBMCombat_gladius.xml']);
    }
    if (naval) return ['RBM_WS_XML/RBMCombat_WS_items.xml'];
    var w = weapons(m)[0];
    if (w && SIEGE_CLASSES.indexOf(w.attrs.weapon_class) >= 0) return pick(['RBMXML/RBMCombat_siege_ranged.xml']);
    if (['Bow', 'Crossbow', 'Sling', 'SlingStones', 'Thrown'].indexOf(type) >= 0) return pick(RANGED_TWINS);
    return TYPE_FILE[type] ? pick([TYPE_FILE[type]]) : [];
  }

  // Where an item lives in RBM's files: { inPlace: [paths that define it] } or { append: [target paths] }.
  function placement(it, m, data, target) {
    if (it && it.rbm && it.rbm.length) return { inPlace: it.rbm.slice() };
    return { append: target && target.length ? target.slice() : defaultTargets(m, it, data) };
  }

  // The element text appended for item it (or, for a new item, the source item) with model cur, in file f.
  function appendText(it, cur, f, data) {
    var ctx = fileCtx(f);
    var raw = it.raw.replace(/\r\n/g, '\n');
    var srcIndent = it.rawIndent || '';
    var srcUnit = detectUnit(raw, srcIndent);
    var el = restyle(raw, srcIndent, ctx.itemIndent, srcUnit, ctx.unit);
    var orig = parseItem(it.raw);
    var r = patchElement(el, diff(orig, cur), Object.assign({}, ctx, { eol: '\n' }), ctx.itemIndent);
    return { text: T.normEol(r.text, ctx.eol), notes: r.notes };
  }
  function sourceLabel(it) { return it.rawMerged ? (it.sources || []).join(' + ') + ' (merged)' : it.module + '/' + it.file; }

  // edits: id -> { model, target } for existing items (only those that differ are exported);
  // news: [{ id, from (source item id), model, target }] for new items.
  // Returns { files: [{ path, bom, eol, text, original, items: [{ id, mode: 'patch'|'append'|'new', summary, notes }] }],
  // messages: [[cls, text]] }. opts.all: also return files without changes (tests).
  function buildExport(data, edits, news, opts) {
    opts = opts || {};
    var byId = {}, byFile = {}, messages = [];
    (data.items || []).forEach(function (it) { byId[it.id] = it; });
    var fileOf = {};
    (data.rbmFiles || []).forEach(function (f) { fileOf[f.path] = f; });
    var job = function (path) { return byFile[path] || (byFile[path] = { patch: [], append: [] }); };
    Object.keys(edits || {}).forEach(function (id) {
      var it = byId[id], e = edits[id];
      if (!it) { messages.push(['bad', 'Unknown item "' + id + '" in the work in progress: skipped']); return; }
      var orig = parseItem(it.raw), ops = diffWithOld(orig, e.model);
      if (!ops.length) return;
      var pl = placement(it, e.model, data, e.target);
      if (pl.inPlace) pl.inPlace.forEach(function (p) { job(p).patch.push({ id: id, kind: it.kind, ops: ops }); });
      else if (!pl.append.length) messages.push(['bad', id + ': no RBM file to append it to (pick a target file): not exported']);
      else pl.append.forEach(function (p) { job(p).append.push({ id: id, it: it, model: e.model, mode: 'append', ops: ops }); });
    });
    (news || []).forEach(function (n) {
      var src = byId[n.from];
      if (!src) { messages.push(['bad', 'New item ' + n.id + ': its source "' + n.from + '" no longer exists: skipped']); return; }
      var targets = n.target && n.target.length ? n.target : defaultTargets(n.model, src, data);
      if (!targets.length) { messages.push(['bad', 'New item ' + n.id + ': no RBM file to append it to (pick a target file): not exported']); return; }
      var ops = diffWithOld(parseItem(src.raw), n.model);
      targets.forEach(function (p) { job(p).append.push({ id: n.id, it: src, model: n.model, mode: 'new', ops: ops }); });
    });
    var files = [];
    (data.rbmFiles || []).forEach(function (f) {
      var j = byFile[f.path];
      if (!j && !opts.all) return;
      j = j || { patch: [], append: [] };
      var ctx = fileCtx(f), text = f.text, out = { path: f.path, bom: !!f.bom, eol: f.eol, original: f.text, items: [] };
      var spans = itemSpans(text), work = [];
      j.patch.forEach(function (p) {
        var list = spans[p.kind + '|' + p.id] || [];
        if (!list.length) { messages.push(['bad', p.id + ': not found in ' + f.path + ' any more (re-run the build script)']); return; }
        list.forEach(function (sp) { work.push({ sp: sp, p: p }); });
      });
      work.sort(function (a, b) { return b.sp.start - a.sp.start; });
      var notesOf = {};
      work.forEach(function (w) {
        var r = patchElement(text.slice(w.sp.start, w.sp.end), w.p.ops, ctx, T.indentAt(text, w.sp.start) || ctx.itemIndent);
        text = text.slice(0, w.sp.start) + r.text + text.slice(w.sp.end);
        notesOf[w.p.id] = (notesOf[w.p.id] || []).concat(r.notes);
      });
      j.patch.forEach(function (p) {
        if (!(spans[p.kind + '|' + p.id] || []).length) return;
        out.items.push({ id: p.id, mode: 'patch', summary: summarize(p.ops), notes: notesOf[p.id] || [] });
      });
      if (j.append.length) {
        var r0 = T.rootRange(text), close = text.lastIndexOf('</' + r0.name);
        var ls = text.lastIndexOf('\n', close - 1) + 1;
        var block = '';
        if (!T.isWs(text.slice(ls, close))) ls = close;
        j.append.forEach(function (a) {
          var at = appendText(a.it, a.model, f, data);
          var what = a.mode === 'new' ? a.id + ': duplicated from ' + a.it.id + ' (' + sourceLabel(a.it) + ')' : a.id + ': copied from ' + sourceLabel(a.it);
          block += ctx.itemIndent + '<!-- ' + what.replace(/--/g, '- -') + ' by the item editor -->' + ctx.eol + ctx.itemIndent + at.text + ctx.eol;
          out.items.push({ id: a.id, mode: a.mode, summary: a.mode === 'new' ? ['new item from ' + a.it.id].concat(summarize(a.ops)) : summarize(a.ops), notes: at.notes });
        });
        if (ls === close) block = ctx.eol + block;
        text = text.slice(0, ls) + block + text.slice(ls);
      }
      out.items.sort(function (a, b) { return a.id < b.id ? -1 : a.id > b.id ? 1 : 0; });
      out.text = text;
      if (opts.all || out.items.length) files.push(out);
    });
    return { files: files, messages: messages };
  }

  // The element as the export will write it (for the page's preview): the first RBM file's patched element, or the
  // appended copy. Returns { path, text, mode } or null.
  function previewElement(data, it, m, target, isNew) {
    var src = it;
    var pl = isNew ? { append: target && target.length ? target : defaultTargets(m, src, data) } : placement(it, m, data, target);
    var fileOf = {};
    (data.rbmFiles || []).forEach(function (f) { fileOf[f.path] = f; });
    if (pl.inPlace) {
      var f = fileOf[pl.inPlace[0]];
      if (!f) return null;
      var sp = (itemSpans(f.text)[it.kind + '|' + it.id] || []).slice(-1)[0];
      if (!sp) return null;
      var ctx = fileCtx(f), el = f.text.slice(sp.start, sp.end);
      var r = patchElement(el, diff(parseItem(it.raw), m), ctx, T.indentAt(f.text, sp.start) || ctx.itemIndent);
      return { path: f.path, text: r.text, mode: 'patch', notes: r.notes };
    }
    var f2_ = fileOf[pl.append[0]];
    if (!f2_) return null;
    var at = appendText(src, m, f2_, data);
    return { path: f2_.path, text: at.text, mode: isNew ? 'new' : 'append', notes: at.notes };
  }

  // ---------------------------------------------------------------- change summary

  function q(v) { return v == null ? '(none)' : v; }
  function summarize(ops) {
    var out = [], flags = { flag: { on: [], off: [] } }, wf = {};
    ops.forEach(function (o) {
      var isOn = function (v) { return v != null && String(v).toLowerCase() !== 'false'; };
      if (o.p === 'item') out.push(o.n + ' ' + q(o.old) + ' → ' + q(o.v));
      else if (o.p === 'comp') out.push(o.el + (o.i ? ' ' + (o.i + 1) : '') + ' ' + o.n + ' ' + q(o.old) + ' → ' + q(o.v));
      else if (o.p === 'flag') (isOn(o.v) ? flags.flag.on : flags.flag.off).push(o.n + (o.v != null && !isOn(o.v) ? '="' + o.v + '"' : ''));
      else if (o.p === 'wf') { var k = o.el + (o.i ? ' ' + (o.i + 1) : ''); wf[k] = wf[k] || { on: [], off: [] }; (isOn(o.v) ? wf[k].on : wf[k].off).push(o.n); }
      else if (o.p === 'piece') out.push('piece ' + o.t + ' ' + o.n + ' ' + q(o.old) + ' → ' + q(o.v));
      else if (o.p === 'pieceAdd') out.push('piece ' + o.t + ' added (' + o.attrs.map(function (a) { return a[0] + '=' + a[1]; }).join(' ') + ')');
      else if (o.p === 'pieceDel') out.push('piece ' + o.t + ' removed');
    });
    var fl = function (label, x) {
      var p = [];
      if (x.on.length) p.push('+' + x.on.join(' +'));
      if (x.off.length) p.push('−' + x.off.join(' −'));
      if (p.length) out.push(label + ': ' + p.join(' '));
    };
    fl('Flags', flags.flag);
    Object.keys(wf).forEach(function (k) { fl(k + ' WeaponFlags', wf[k]); });
    return out;
  }
  // diff() with the old values attached (for summaries that show "a → b").
  function diffWithOld(a, b) {
    return diff(a, b).map(function (o) {
      var path = o.p === 'item' ? { el: 'item' } : o.p === 'comp' ? { el: 'comp', i: o.i } : o.p === 'wf' ? { el: 'wf', i: o.i } :
        o.p === 'flag' ? { el: 'flag' } : o.p === 'piece' ? { el: 'piece', t: o.t } : null;
      if (path && o.n) o.old = getAt(a, path, o.n);
      return o;
    });
  }

  var api = {
    CLASS_TYPE: CLASS_TYPE, ARMOR_TYPES: ARMOR_TYPES, LAUNCHER_TYPES: LAUNCHER_TYPES, AMMO_TYPES: AMMO_TYPES,
    ATTR_ENUMS: ATTR_ENUMS, ATTR_REFS: ATTR_REFS, DEFAULT_MULT: DEFAULT_MULT, RANGED_TWINS: RANGED_TWINS,
    cleanEnum: cleanEnum, enumValues: enumValues, flagNames: flagNames, attrKind: attrKind,
    attrsOf: attrsOf, decode: decode, parseItem: parseItem, cloneModel: cloneModel, diff: diff, diffWithOld: diffWithOld, sameModel: sameModel,
    applyOps: applyOps,
    lostVanillaAttrs: lostVanillaAttrs, getAt: getAt, setAt: setAt, mapAt: mapAt, typeOf: typeOf, canonType: canonType, weapons: weapons, mainComp: mainComp,
    computeTier: computeTier, computePrice: computePrice, missileSpeed: missileSpeed, launcherInfo: launcherInfo, roundEven: roundEven,
    problems: problems, patchElement: patchElement, fileCtx: fileCtx, itemSpans: itemSpans, restyle: restyle,
    defaultTargets: defaultTargets, placement: placement, appendText: appendText, buildExport: buildExport,
    previewElement: previewElement, summarize: summarize
  };
  if (typeof module !== 'undefined' && module.exports) module.exports = api;
  else root.ItemEditorCore = api;
})(this);
