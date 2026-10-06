<#
.SYNOPSIS
    Builds troop-loadout-data.js for the troop skills / loadout editor (index.html in this folder).

.DESCRIPTION
    Collects, without running the game:
      - the skills (ids, names, order) from the decompiled DefaultSkills.cs (and War Sails' NavalSkills.cs);
      - every non-hero NPCCharacter from the XML of Native, SandBoxCore, SandBox, StoryMode, CustomBattle,
        NavalDLC (when installed) and RBM's own XML in this repo (RBMXML, plus RBM_WS_XML when NavalDLC is
        installed), found through each module's SubModule.xml, merged in module order the way the game does it
        (see "merging" below; a file not loaded in campaigns never changes a campaign definition). Per troop:
        level, explicit and template skills, every EquipmentRoster, EquipmentSet reference and loose <equipment>
        override, upgrade targets and upgrade_requires, flags, the defining files, and - for troops not defined in
        an RBM unit overhaul file - the element's XML text (merged and re-formatted when several files define it),
        for the export;
      - the ids of the hero characters (skipped above), the campaign cultures' troop references (basic_troop,
        elite_basic_troop, militia, caravan guard, basic_mercenary_troops, ...) and which campaign party
        templates spawn each troop, for the upgrade tree editor (../TroopUpgradeTreeEditor);
      - the equipment sets (EquipmentRosters XML) that troops reference;
      - every item (Item and CraftedItem) from the Items XML, RBM's own item XML replacing by id the way
        RBM/XmlLoadingPatches.cs MergeTwoXmlsPatch does. Assumes RBM combat AND campaign on (so RBM_COMBAT_ONLY
        files are skipped and RBM_ECONOMY_COMBAT files load) and the troop overhaul on;
      - the raw text of RBMXML/RBMCombat_unit_overhaul.xml and RBM_WS_XML/RBMCombat_WS_unit_overhaul.xml, which
        the page splices on export.

    The output contains TaleWorlds text and is gitignored. Windows PowerShell 5.1 compatible.

.PARAMETER ModulesRoot
    The game's Modules folder. Defaults to the folder the repo sits in.

.EXAMPLE
    powershell -ExecutionPolicy Bypass -File tools\TroopLoadoutEditor\Build-TroopLoadoutData.ps1
#>
[CmdletBinding()]
param(
    [string]$ModulesRoot = ''
)

Set-StrictMode -Version 2.0
$ErrorActionPreference = 'Stop'

$Inv = [System.Globalization.CultureInfo]::InvariantCulture
$RepoRoot = (Resolve-Path (Join-Path $PSScriptRoot '..\..')).Path
if ($ModulesRoot -eq '') {
    $ModulesRoot = (Resolve-Path (Join-Path $RepoRoot '..')).Path
}
$Decompiled = Join-Path $RepoRoot 'decompiled'
$OutFile = Join-Path $PSScriptRoot 'troop-loadout-data.js'

$script:Warnings = New-Object System.Collections.Generic.List[string]
function Add-Warning([string]$message) {
    $script:Warnings.Add($message)
    Write-Warning $message
}

# ---------------------------------------------------------------- JSON writer
# Hand-rolled (copied from the troop perk editor's script): PS 5.1's ConvertTo-Json is slow, wraps arrays in
# {value,Count} under some type data, and formats numbers with the current culture.

function Write-JsonString([System.Text.StringBuilder]$sb, [string]$s) {
    [void]$sb.Append('"')
    foreach ($ch in $s.ToCharArray()) {
        $code = [int]$ch
        if ($ch -eq '"') { [void]$sb.Append('\"') }
        elseif ($ch -eq '\') { [void]$sb.Append('\\') }
        elseif ($code -eq 10) { [void]$sb.Append('\n') }
        elseif ($code -eq 13) { [void]$sb.Append('\r') }
        elseif ($code -eq 9) { [void]$sb.Append('\t') }
        elseif ($code -lt 32 -or $code -eq 0x2028 -or $code -eq 0x2029) { [void]$sb.Append('\u' + $code.ToString('x4')) }
        elseif ($code -eq 60) { [void]$sb.Append('<') }  # '<': keeps "</script>" out of the generated file
        else { [void]$sb.Append($ch) }
    }
    [void]$sb.Append('"')
}

function Write-JsonValue([System.Text.StringBuilder]$sb, $value) {
    if ($null -eq $value) { [void]$sb.Append('null'); return }
    if ($value -is [string]) { Write-JsonString $sb $value; return }
    if ($value -is [bool]) { if ($value) { [void]$sb.Append('true') } else { [void]$sb.Append('false') }; return }
    if ($value -is [int] -or $value -is [long]) { [void]$sb.Append($value.ToString($Inv)); return }
    if ($value -is [double] -or $value -is [single] -or $value -is [decimal]) {
        [void]$sb.Append(([double]$value).ToString('0.######', $Inv)); return
    }
    if ($value -is [System.Collections.IDictionary]) {
        [void]$sb.Append('{')
        $first = $true
        foreach ($key in $value.Keys) {
            if (-not $first) { [void]$sb.Append(',') }
            $first = $false
            Write-JsonString $sb ([string]$key)
            [void]$sb.Append(':')
            Write-JsonValue $sb $value[$key]
        }
        [void]$sb.Append('}')
        return
    }
    if ($value -is [System.Collections.IEnumerable]) {
        [void]$sb.Append('[')
        $first = $true
        foreach ($item in $value) {
            if (-not $first) { [void]$sb.Append(',') }
            $first = $false
            Write-JsonValue $sb $item
        }
        [void]$sb.Append(']')
        return
    }
    Write-JsonString $sb ([string]$value)
}

# ---------------------------------------------------------------- helpers

function Remove-LocKey([string]$text) {
    if ($null -eq $text) { return '' }
    return ($text -replace '^\{=[^}]*\}', '')
}

function Get-Attr($node, [string]$name) {
    if ($null -eq $node -or $null -eq $node.Attributes) { return $null }
    $a = $node.Attributes[$name]
    if ($null -eq $a) { return $null }
    return $a.Value
}

function Test-True($value) { return ($null -ne $value -and $value.Trim() -ieq 'true') }

function Get-Number($value) {
    # Invariant-culture number, or $null.
    if ($null -eq $value) { return $null }
    $d = 0.0
    if ([double]::TryParse($value.Trim(), [System.Globalization.NumberStyles]::Float, $Inv, [ref]$d)) { return $d }
    return $null
}

function Get-RefId([string]$value) {
    # Equipment.DeserializeNode: "Item.foo" -> "foo" (the part after the first '.'), else the value itself.
    if ($null -eq $value) { return '' }
    if ($value.Contains('.')) { return $value.Split('.')[1] }
    return $value
}

function Get-AttrMap($node) {
    $m = [ordered]@{}
    if ($null -eq $node -or $null -eq $node.Attributes) { return $m }
    foreach ($a in $node.Attributes) { $m[$a.Name] = $a.Value }
    return $m
}

# Equipment.EquipmentType of an <EquipmentRoster> (MBEquipmentRoster.InitEquipment) or an <EquipmentSet> reference
# (BasicCharacterObject.Deserialize): equipmentType="Battle|Civilian|Stealth" (Enum.TryParse, case-sensitive; a bad
# value stays Battle), else civilian="true" (bool.Parse) -> Civilian, else Battle.
function Get-EquipmentType($node) {
    $et = Get-Attr $node 'equipmentType'
    if ($null -ne $et) {
        if (@('Battle', 'Civilian', 'Stealth') -ccontains $et) { return $et }
        return 'Battle'
    }
    if (Test-True (Get-Attr $node 'civilian')) { return 'Civilian' }
    return 'Battle'
}

# Top-level child elements of the root, by id, with their exact source text. Comment, CDATA and PI aware.
$script:TokenRe = New-Object System.Text.RegularExpressions.Regex('<!--[\s\S]*?-->|<!\[CDATA\[[\s\S]*?\]\]>|<\?[\s\S]*?\?>|<!DOCTYPE[^>]*>|<(/?)([A-Za-z_][\w.:-]*)((?:[^>"'']|"[^"]*"|''[^'']*'')*?)(/?)>')
function Get-TopLevelSpans([string]$text, [string]$elementName) {
    $spans = New-Object System.Collections.Generic.List[object]
    $depth = 0
    $start = -1
    $startAttrs = ''
    foreach ($m in $script:TokenRe.Matches($text)) {
        if (-not $m.Groups[2].Success) { continue }
        $closing = ($m.Groups[1].Value -eq '/')
        $selfClose = ($m.Groups[4].Value -eq '/')
        if ($closing) {
            $depth--
            if ($depth -eq 1 -and $start -ge 0) {
                $end = $m.Index + $m.Length
                $spans.Add(@{ start = $start; end = $end; attrs = $startAttrs })
                $start = -1
            }
            continue
        }
        if ($depth -eq 1 -and $m.Groups[2].Value -eq $elementName) {
            if ($selfClose) { $spans.Add(@{ start = $m.Index; end = $m.Index + $m.Length; attrs = $m.Groups[3].Value }) }
            else { $start = $m.Index; $startAttrs = $m.Groups[3].Value }
        }
        if (-not $selfClose) { $depth++ }
    }
    $byId = @{}
    foreach ($s in $spans) {
        $im = [regex]::Match($s.attrs, '(?:^|\s)id\s*=\s*"([^"]*)"')
        if (-not $im.Success) { continue }
        $lineStart = $text.LastIndexOf("`n", [Math]::Max(0, $s.start - 1)) + 1
        if ($s.start -eq 0) { $lineStart = 0 }
        $indent = $text.Substring($lineStart, $s.start - $lineStart)
        if ($indent.Trim() -ne '') { $indent = '' }
        $byId[$im.Groups[1].Value] = @{ text = $text.Substring($s.start, $s.end - $s.start); indent = $indent }
    }
    return $byId
}

# ---------------------------------------------------------------- skills

$skillMap = New-Object System.Collections.Specialized.OrderedDictionary
function Read-SkillClass([string]$path, [string]$className) {
    if (-not (Test-Path -LiteralPath $path)) { Add-Warning "skills file not found: $path"; return }
    $src = [System.IO.File]::ReadAllText($path)
    $fieldToId = @{}
    foreach ($m in [regex]::Matches($src, '(_\w+)\s*=\s*Create\("(\w+)"\)')) { $fieldToId[$m.Groups[1].Value] = $m.Groups[2].Value }
    $fieldToName = @{}
    foreach ($m in [regex]::Matches($src, '(_\w+)\.Initialize\(new TextObject\("([^"]*)"\)')) { $fieldToName[$m.Groups[1].Value] = Remove-LocKey $m.Groups[2].Value }
    foreach ($m in [regex]::Matches($src, 'public static SkillObject (\w+)\s*=>\s*Instance\.(_\w+);')) {
        $field = $m.Groups[2].Value
        if (-not $fieldToId.ContainsKey($field)) { continue }
        $id = $fieldToId[$field]
        $name = $id
        if ($fieldToName.ContainsKey($field)) { $name = $fieldToName[$field] }
        if (-not $skillMap.Contains($id)) { $skillMap[$id] = [ordered]@{ id = $id; name = $name; source = $className } }
    }
}
Read-SkillClass (Join-Path $Decompiled 'TaleWorlds.Core\TaleWorlds.Core\DefaultSkills.cs') 'DefaultSkills'
$navalSkillsPath = Join-Path $Decompiled 'NavalDLC\NavalDLC.CharacterDevelopment\NavalSkills.cs'
if (Test-Path -LiteralPath $navalSkillsPath) { Read-SkillClass $navalSkillsPath 'NavalSkills' }
if ($skillMap.Count -eq 0) { Add-Warning 'no skills read from decompiled/ (run tools\Decompile-Bannerlord.ps1)' }

# ---------------------------------------------------------------- modules and their XML

$navalInstalled = Test-Path -LiteralPath (Join-Path $ModulesRoot 'NavalDLC\SubModule.xml')
$moduleList = @(
    @{ id = 'Native'; sub = (Join-Path $ModulesRoot 'Native\SubModule.xml'); data = (Join-Path $ModulesRoot 'Native\ModuleData'); rbm = $false },
    @{ id = 'SandBoxCore'; sub = (Join-Path $ModulesRoot 'SandBoxCore\SubModule.xml'); data = (Join-Path $ModulesRoot 'SandBoxCore\ModuleData'); rbm = $false },
    @{ id = 'SandBox'; sub = (Join-Path $ModulesRoot 'SandBox\SubModule.xml'); data = (Join-Path $ModulesRoot 'SandBox\ModuleData'); rbm = $false },
    @{ id = 'StoryMode'; sub = (Join-Path $ModulesRoot 'StoryMode\SubModule.xml'); data = (Join-Path $ModulesRoot 'StoryMode\ModuleData'); rbm = $false },
    @{ id = 'CustomBattle'; sub = (Join-Path $ModulesRoot 'CustomBattle\SubModule.xml'); data = (Join-Path $ModulesRoot 'CustomBattle\ModuleData'); rbm = $false }
)
if ($navalInstalled) {
    $moduleList += @{ id = 'NavalDLC'; sub = (Join-Path $ModulesRoot 'NavalDLC\SubModule.xml'); data = (Join-Path $ModulesRoot 'NavalDLC\ModuleData'); rbm = $false }
}
# RBM's own XML straight from the repo (RBMXML is deployed as the RBM module's ModuleData).
$moduleList += @{ id = 'RBM'; sub = (Join-Path $RepoRoot 'RBMXML\SubModule.xml'); data = (Join-Path $RepoRoot 'RBMXML'); rbm = $true; repoDir = 'RBMXML' }
if ($navalInstalled) {
    $moduleList += @{ id = 'RBM_WS'; sub = (Join-Path $RepoRoot 'RBM_WS_XML\SubModule.xml'); data = (Join-Path $RepoRoot 'RBM_WS_XML'); rbm = $true; repoDir = 'RBM_WS_XML' }
}

# The two files the editor exports (RBM's troop overhaul, RBM_COMBAT_OVERHAUL_XML_TAG).
$rbmTroopFiles = @('RBMXML/RBMCombat_unit_overhaul.xml')
if ($navalInstalled) { $rbmTroopFiles += 'RBM_WS_XML/RBMCombat_WS_unit_overhaul.xml' }

function Get-XmlFiles($module, [string]$xmlId, [switch]$WithXslt) {
    # Like MBObjectManager.GetMergedXmlForManaged: "<path>.xml", else every *.xml in the folder "<path>".
    # With -WithXslt, a "<path>.xslt" entry is returned too, in registration order, as @{ xslt = <file> }.
    $result = @()
    if (-not (Test-Path -LiteralPath $module.sub)) { Add-Warning "$($module.id): SubModule.xml not found ($($module.sub))"; return ,$result }
    $doc = New-Object System.Xml.XmlDocument
    $doc.Load($module.sub)
    foreach ($node in $doc.SelectNodes('//Xmls/XmlNode')) {
        $nameNode = $node.SelectSingleNode('XmlName')
        if ($null -eq $nameNode -or $null -eq $nameNode.Attributes['id'] -or $nameNode.Attributes['id'].Value -ne $xmlId) { continue }
        $path = $nameNode.Attributes['path'].Value
        $types = @()
        $included = $node.SelectSingleNode('IncludedGameTypes')
        if ($null -ne $included) {
            foreach ($child in $included.ChildNodes) {
                if ($child.NodeType -eq [System.Xml.XmlNodeType]::Element -and $null -ne $child.Attributes['value']) { $types += $child.Attributes['value'].Value.Trim() }
            }
        }
        # XmlResource: no IncludedGameTypes = every game type.
        $campaign = ($types.Count -eq 0)
        foreach ($t in $types) { if ($t -ieq 'Campaign' -or $t -ieq 'CampaignStoryMode') { $campaign = $true } }
        $file = Join-Path $module.data ($path + '.xml')
        # A "<path>.xsl(t)" (e.g. XSLT/NavalDLC_SandBoxCore_SPCultures) transforms the XML merged so far, before this
        # entry's own file is merged (MBObjectManager.CreateMergedXmlFile / HandleXsltList). Only Collect-Merged applies
        # them. (A folder entry's per-file transforms are not handled; no installed module has one.)
        # The repo keeps RBM_WS_XML's transforms at its root (the build puts them in XSLT/), hence the leaf name.
        $xslt = $null
        $leaf = ($path -split '/')[-1]
        foreach ($x in @(($path + '.xsl'), ($path + '.xslt'), ($leaf + '.xsl'), ($leaf + '.xslt'))) {
            $xf = Join-Path $module.data $x
            if ($null -eq $xslt -and (Test-Path -LiteralPath $xf)) { $xslt = $xf }
        }
        if ($null -ne $xslt -and $WithXslt) {
            $result += @{ xslt = $xslt; name = ($path + [System.IO.Path]::GetExtension($xslt)); campaign = $campaign; types = ($types -join ',') }
        }
        if (Test-Path -LiteralPath $file) {
            $result += @{ file = $file; name = ($path + '.xml'); campaign = $campaign; types = ($types -join ',') }
            continue
        }
        $dir = Join-Path $module.data $path
        if (Test-Path -LiteralPath $dir -PathType Container) {
            foreach ($f in (Get-ChildItem -LiteralPath $dir -Filter '*.xml' | Sort-Object Name)) {
                $result += @{ file = $f.FullName; name = ($path + '/' + $f.Name); campaign = $campaign; types = ($types -join ',') }
            }
            continue
        }
        if ($null -ne $xslt) { continue }
        Add-Warning "$($module.id): $xmlId file not found: $file"
    }
    return ,$result
}

function Get-RbmTags([System.Xml.XmlDocument]$doc) {
    $tags = @{}
    foreach ($c in $doc.SelectNodes('//comment()')) {
        foreach ($t in @('RBM_XML_TAG', 'RBM_COMBAT_XML_TAG', 'RBM_CAMPAIGN_XML_TAG', 'RBM_ECONOMY_COMBAT_XML_TAG', 'RBM_COMBAT_ONLY_XML_TAG', 'RBM_COMBAT_OVERHAUL_XML_TAG')) {
            if ($c.Value.Contains($t)) { $tags[$t] = $true }
        }
    }
    return $tags
}

# MergeTwoXmlsPatch with combat + campaign + troop overhaul on: only RBM_COMBAT_ONLY files are dropped.
function Test-RbmFileLoads($tags) {
    if ($tags.ContainsKey('RBM_COMBAT_ONLY_XML_TAG')) { return $false }
    return $true
}

$fileLog = New-Object System.Collections.Generic.List[object]
function Add-FileLog($module, $f, [string]$kind, [int]$count, [string]$note) {
    $fileLog.Add([ordered]@{ module = $module.id; file = $f.name; kind = $kind; campaign = [bool]$f.campaign; types = $f.types; count = $count; note = $note })
    Write-Host ("  {0,-12} {1,-12} {2,-44} {3,5}{4}" -f $module.id, $kind, $f.name, $count, $(if ($note) { '  (' + $note + ')' } else { '' }))
}

# ---------------------------------------------------------------- merging, as the game does it
# MBObjectManager.CreateMergedXmlFile merges the files of one XML id in module order. Plain files go through
# MergeTwoXmls -> MergeElements: a same-id element is merged into the earlier one (attributes overlaid, children
# merged by the XSD's AlwaysPreferMerge / xs:unique keys, other children appended); NavalDLC uses that to add bits to
# vanilla troops and items. RBM's files (RBM_XML_TAG) bypass it in MergeTwoXmlsPatch: an RBM Item/CraftedItem
# replaces the same-kind same-id element, an RBM NPCCharacter replaces the earlier one only when both have an
# <Equipments><EquipmentRoster>, and is appended next to it otherwise.

$GameRoot = (Resolve-Path (Join-Path $ModulesRoot '..')).Path
$XsNs = 'http://www.w3.org/2001/XMLSchema'
function Get-XsdPath($node) {
    # XmlResource.GetFullXPathOfElement(isXsd: true): the names of the enclosing xs:element nodes.
    $parts = New-Object System.Collections.Generic.List[string]
    $n = $node
    while ($null -ne $n -and $n.NodeType -eq [System.Xml.XmlNodeType]::Element) {
        if ($n.LocalName -eq 'element' -and $n.NamespaceURI -eq $XsNs) {
            $nm = $n.GetAttribute('name')
            if ($nm -eq '') { $nm = $n.GetAttribute('ref') }
            $parts.Insert(0, $nm)
        }
        $n = $n.ParentNode
    }
    return '/' + ($parts -join '/')
}
function Read-Schema([string]$xmlId) {
    # XmlResource.ReadXsdFileAndExtractInformation: path -> { always (AlwaysPreferMerge), unique (key attributes) }.
    $schema = @{}
    $p = Join-Path $GameRoot ('XmlSchemas\' + $xmlId + '.xsd')
    if (-not (Test-Path -LiteralPath $p)) { Add-Warning "XSD not found: $p (overrides from plain files are treated as appends)"; return $schema }
    $doc = New-Object System.Xml.XmlDocument
    $doc.Load($p)
    $ns = New-Object System.Xml.XmlNamespaceManager($doc.NameTable)
    $ns.AddNamespace('xs', $XsNs)
    foreach ($e in $doc.SelectNodes('//xs:element', $ns)) {
        $always = $false
        $note = $e.SelectSingleNode('xs:annotation/xs:appinfo/appSpecificNote', $ns)
        if ($null -ne $note -and $note.InnerText.Trim() -eq 'AlwaysPreferMerge') { $always = $true }
        $schema[(Get-XsdPath $e)] = @{ always = $always; unique = (New-Object System.Collections.Generic.List[string]) }
    }
    foreach ($u in $doc.SelectNodes('//xs:unique|//xs:key', $ns)) {
        $sel = $u.SelectSingleNode('xs:selector', $ns)
        if ($null -eq $sel) { continue }
        $path = (Get-XsdPath $u) + '/' + $sel.GetAttribute('xpath')
        if (-not $schema.ContainsKey($path)) { continue }
        foreach ($fld in $u.SelectNodes('xs:field', $ns)) { $schema[$path].unique.Add($fld.GetAttribute('xpath').Substring(1)) }
    }
    return $schema
}
function Get-ElPath($el) {
    # XmlResource.GetFullXPathOfElement(isXsd: false)
    $parts = New-Object System.Collections.Generic.List[string]
    $n = $el
    while ($null -ne $n -and $n.NodeType -eq [System.Xml.XmlNodeType]::Element) { $parts.Insert(0, $n.LocalName); $n = $n.ParentNode }
    return '/' + ($parts -join '/')
}
function Get-UniqueKey($el, $info) {
    if ($null -eq $info) { return '' }
    $k = ''
    foreach ($u in $info.unique) { $v = Get-Attr $el $u; if ($null -ne $v) { $k += $v } }
    return $k
}
function Merge-Elements($e1, $e2, $schema) {
    # MBObjectManager.MergeElements / MergeElementAttributes.
    $replace = ((Get-Attr $e2 '_replaceWhileMerging') -eq 'true')
    if ($replace) { $e1.Attributes.RemoveAll() }
    foreach ($a in @($e2.Attributes)) { [void]$e1.SetAttribute($a.Name, $a.Value) }
    if ($replace) { foreach ($c in @($e1.ChildNodes)) { if ($c.NodeType -eq [System.Xml.XmlNodeType]::Element) { [void]$e1.RemoveChild($c) } } }
    $groups = @{}
    foreach ($c in @($e1.ChildNodes)) {
        if ($c.NodeType -ne [System.Xml.XmlNodeType]::Element) { continue }
        $nm = $c.LocalName
        if (-not $groups.ContainsKey($nm)) { $groups[$nm] = @{ first = $c; keys = @{}; info = $schema[(Get-ElPath $c)] } }
        $g = $groups[$nm]
        $g.keys[(Get-UniqueKey $c $g.info)] = $c
    }
    foreach ($c2 in @($e2.ChildNodes)) {
        if ($c2.NodeType -ne [System.Xml.XmlNodeType]::Element) { continue }
        $nm = $c2.LocalName
        if ($groups.ContainsKey($nm)) {
            $g = $groups[$nm]
            if ($null -ne $g.info -and $g.info.always) { Merge-Elements $g.first $c2 $schema; continue }
            $key = Get-UniqueKey $c2 $g.info
            if ($key -ne '' -and $g.keys.ContainsKey($key)) { Merge-Elements $g.keys[$key] $c2 $schema }
            elseif ((Get-Attr $c2 '_replaceWhileMerging') -eq 'true') { Merge-Elements $g.first $c2 $schema }
            else { [void]$e1.AppendChild($e1.OwnerDocument.ImportNode($c2, $true)) }
        }
        else { [void]$e1.AppendChild($e1.OwnerDocument.ImportNode($c2, $true)) }
    }
}
function Test-HasRoster($n) {
    return ($null -ne $n.SelectSingleNode('Equipments/EquipmentRoster|equipments/EquipmentRoster|Equipments/equipmentRoster|equipments/equipmentRoster'))
}
function Format-Element($el) {
    # Pretty-prints a merged element in the vanilla style (tabs, one attribute per line).
    $settings = New-Object System.Xml.XmlWriterSettings
    $settings.Indent = $true
    $settings.IndentChars = "`t"
    $settings.NewLineOnAttributes = $true
    $settings.OmitXmlDeclaration = $true
    $settings.NewLineChars = "`n"
    $settings.ConformanceLevel = [System.Xml.ConformanceLevel]::Fragment
    # The merge-only flag has done its job; it means nothing in an RBM file (MergeTwoXmlsPatch never merges).
    $copy = $el.CloneNode($true)
    foreach ($x in @($copy.SelectNodes('descendant-or-self::*[@_replaceWhileMerging]'))) { $x.RemoveAttribute('_replaceWhileMerging') }
    $sw = New-Object System.IO.StringWriter
    $w = [System.Xml.XmlWriter]::Create($sw, $settings)
    $copy.WriteTo($w)
    $w.Flush()
    return $sw.ToString()
}

# Collects the elements of one XML id from every module: key "kind|id" -> entry
# { kind, id, el (merged XmlElement), module, file, campaign, sources, alsoIn, merged, raw, rawIndent, rbm, rel, dupOf, order }.
# A file not loaded in campaigns never changes a campaign definition (it only adds ids no campaign file has).
$script:Order = 0
function Invoke-XsltOnStore([System.Xml.XmlDocument]$store, [string]$xsltPath, $entries, [string]$src) {
    # MBObjectManager.ApplyXslt over the definitions merged so far; then re-point every entry (and the definitions an
    # RBM troop sits next to, dupOf) at its transformed element, matched by element name and id in document order.
    $xsl = New-Object System.Xml.Xsl.XslCompiledTransform
    $xsl.Load($xsltPath)
    $out = New-Object System.Xml.XmlDocument
    $w = $out.CreateNavigator().AppendChild()
    $xsl.Transform([System.Xml.XPath.IXPathNavigable]$store, $w)
    $w.Close()
    $queues = @{}
    foreach ($node in $out.DocumentElement.ChildNodes) {
        if ($node.NodeType -ne [System.Xml.XmlNodeType]::Element) { continue }
        $k = $node.LocalName + '|' + (Get-Attr $node 'id')
        if (-not $queues.ContainsKey($k)) { $queues[$k] = New-Object System.Collections.Generic.Queue[object] }
        $queues[$k].Enqueue($node)
    }
    $map = New-Object 'System.Collections.Generic.Dictionary[object,object]'
    foreach ($node in $store.DocumentElement.ChildNodes) {
        if ($node.NodeType -ne [System.Xml.XmlNodeType]::Element) { continue }
        $k = $node.LocalName + '|' + (Get-Attr $node 'id')
        if ($queues.ContainsKey($k) -and $queues[$k].Count -gt 0) { $map[$node] = $queues[$k].Dequeue() }
    }
    $changed = 0
    $removed = @()
    foreach ($key in @($entries.Keys)) {
        $e = $entries[$key]
        while ($null -ne $e) {
            if ($null -ne $e.el) {
                if ($map.ContainsKey($e.el)) {
                    $new = $map[$e.el]
                    if ($new.OuterXml -ne $e.el.OuterXml) { $e.sources += $src; $e.merged = $true; $e.raw = $null; $changed++ }
                    $e.el = $new
                }
                elseif ($e -eq $entries[$key]) { $removed += $key }
            }
            $e = $e.dupOf
        }
    }
    foreach ($key in $removed) { Add-Warning "${src}: removes $key; the editor drops it"; $entries.Remove($key) }
    foreach ($q in $queues.Values) {
        foreach ($node in $q) { Add-Warning "${src}: adds $($node.LocalName) '$(Get-Attr $node 'id')'; not read by the editor" }
    }
    $script:XsltChanged = $changed
    return ,$out   # the comma: an XmlDocument is enumerable and would be unrolled into its child nodes
}

function Collect-Merged([string]$xmlId, [string[]]$kinds, [bool]$keepRaw) {
    $schema = Read-Schema $xmlId
    $store = New-Object System.Xml.XmlDocument
    $root = $store.AppendChild($store.CreateElement($xmlId))
    $entries = New-Object System.Collections.Specialized.OrderedDictionary
    foreach ($module in $moduleList) {
        if (-not (Test-Path -LiteralPath $module.sub)) { continue }
        foreach ($f in (Get-XmlFiles $module $xmlId -WithXslt)) {
            if ($f.ContainsKey('xslt')) {
                if (-not $f.campaign) { Add-FileLog $module $f $xmlId 0 'XSLT skipped: not loaded in campaigns'; continue }
                $store = Invoke-XsltOnStore $store $f.xslt $entries ($module.id + '/' + $f.name)
                $root = $store.DocumentElement
                Add-FileLog $module $f $xmlId $script:XsltChanged 'XSLT: definitions changed'
                continue
            }
            $text = [System.IO.File]::ReadAllText($f.file)
            $doc = New-Object System.Xml.XmlDocument
            $doc.LoadXml($text)
            $rbmXml = $false
            if ($module.rbm) {
                $tags = Get-RbmTags $doc
                if (-not (Test-RbmFileLoads $tags)) { Add-FileLog $module $f $xmlId 0 'skipped: RBM_COMBAT_ONLY (campaign on)'; continue }
                $rbmXml = $tags.ContainsKey('RBM_XML_TAG')
            }
            $rel = ''
            if ($module.rbm) { $rel = $module.repoDir + '/' + $f.name }
            $spans = @{}
            if ($keepRaw) { foreach ($k in $kinds) { $s = Get-TopLevelSpans $text $k; foreach ($sk in $s.Keys) { $spans[$k + '|' + $sk] = $s[$sk] } } }
            $n = 0
            $merged = 0
            $seen = @{}
            foreach ($node in $doc.DocumentElement.ChildNodes) {
                if ($node.NodeType -ne [System.Xml.XmlNodeType]::Element) { continue }
                # LocalName, not Name: PowerShell's XML adapter turns .Name into the element's name="" attribute.
                $kind = $node.LocalName
                if ($kinds -notcontains $kind) { continue }
                $id = Get-Attr $node 'id'
                if ([string]::IsNullOrEmpty($id)) { continue }
                $key = $kind + '|' + $id
                $src = $module.id + '/' + $f.name
                if ($seen.ContainsKey($key)) { Add-Warning "${src}: $kind '$id' is defined twice in the file; both load, the editor uses the last one" }
                $prev = $null
                if ($entries.Contains($key)) { $prev = $entries[$key] }
                if ($null -ne $prev -and $prev.campaign -and -not $f.campaign) { $prev.alsoIn += $src; continue }
                $n++
                $script:Order++
                $raw = $null
                $rawIndent = ''
                if ($spans.ContainsKey($key)) { $raw = $spans[$key].text; $rawIndent = $spans[$key].indent }
                $new = @{ kind = $kind; id = $id; el = $null; module = $module.id; file = $f.name; campaign = [bool]$f.campaign
                    sources = @($src); alsoIn = @(); merged = $false; raw = $raw; rawIndent = $rawIndent; rbm = [bool]$module.rbm; rel = $rel
                    dupOf = $null; order = $script:Order }
                if ($null -ne $prev -and -not $seen.ContainsKey($key) -and -not $rbmXml -and ($prev.campaign -or -not $f.campaign)) {
                    # Plain file over an earlier definition: keyed merge into it.
                    Merge-Elements $prev.el $node $schema
                    $prev.merged = $true
                    $prev.sources += $src
                    $prev.module = $module.id
                    $prev.file = $f.name
                    $prev.order = $script:Order
                    $prev.raw = $null
                    $merged++
                    continue
                }
                $new.el = $store.ImportNode($node, $true)
                [void]$root.AppendChild($new.el)
                if ($null -ne $prev) {
                    $new.sources = @($prev.sources) + $src
                    $new.alsoIn = @($prev.alsoIn)
                    if ($rbmXml -and $kind -eq 'NPCCharacter' -and $f.campaign -and -not ((Test-HasRoster $prev.el) -and (Test-HasRoster $new.el))) {
                        $new.dupOf = $prev
                    }
                    else { [void]$root.RemoveChild($prev.el) }
                    $entries.Remove($key)
                }
                $entries[$key] = $new
                $seen[$key] = $true
            }
            $note = ''
            if ($merged -gt 0) { $note = "$merged merged into earlier definitions" }
            if (-not $f.campaign) { $note = (@($note, 'not loaded in campaigns') | Where-Object { $_ }) -join '; ' }
            Add-FileLog $module $f $xmlId $n $note
        }
    }
    return $entries
}

# ---------------------------------------------------------------- monsters (horse base hit points)

$monsterHp = @{}
foreach ($module in $moduleList) {
    if ($module.rbm -or -not (Test-Path -LiteralPath $module.sub)) { continue }
    foreach ($f in (Get-XmlFiles $module 'Monsters')) {
        $doc = New-Object System.Xml.XmlDocument
        $doc.Load($f.file)
        foreach ($mn in $doc.SelectNodes('//Monster')) {
            $mid = Get-Attr $mn 'id'
            $hp = Get-Number (Get-Attr $mn 'hit_points')
            if ($null -ne $mid -and $null -ne $hp) { $monsterHp[$mid] = [int]$hp }
        }
    }
}

# ---------------------------------------------------------------- crafting templates (item type of crafted items)

$templateType = @{}
foreach ($module in $moduleList) {
    if (-not (Test-Path -LiteralPath $module.sub)) { continue }
    foreach ($f in (Get-XmlFiles $module 'CraftingTemplates')) {
        $doc = New-Object System.Xml.XmlDocument
        $doc.Load($f.file)
        foreach ($ct in $doc.SelectNodes('//CraftingTemplate')) {
            $cid = Get-Attr $ct 'id'
            $it = Get-Attr $ct 'item_type'
            if ($null -ne $cid -and $null -ne $it) { $templateType[$cid] = $it }
        }
    }
}

# ---------------------------------------------------------------- items

$items = New-Object System.Collections.Specialized.OrderedDictionary
# ItemObject.ItemTypeEnum.
$itemTypes = @('Invalid', 'Horse', 'OneHandedWeapon', 'TwoHandedWeapon', 'Polearm', 'Arrows', 'Bolts', 'SlingStones', 'Shield', 'Bow',
    'Crossbow', 'Sling', 'Thrown', 'Goods', 'HeadArmor', 'BodyArmor', 'LegArmor', 'HandArmor', 'Pistol', 'Musket', 'Bullets', 'Animal',
    'Book', 'ChestArmor', 'Cape', 'HorseHarness', 'Banner')
Write-Host 'Reading items...'
$itemEntries = Collect-Merged 'Items' @('Item', 'CraftedItem') $false
# An id defined as both an Item and a CraftedItem: both load; the editor keeps the later one.
$byItemId = @{}
foreach ($e in $itemEntries.Values) {
    if ($byItemId.ContainsKey($e.id)) {
        $o = $byItemId[$e.id]
        Add-Warning "item '$($e.id)' is both a $($o.kind) ($($o.module)/$($o.file)) and a $($e.kind) ($($e.module)/$($e.file)); both load, the editor uses the later one"
        if ($o.order -gt $e.order) { continue }
    }
    $byItemId[$e.id] = $e
}
foreach ($e in ($itemEntries.Values | Sort-Object { $_.order })) {
    if ($byItemId[$e.id] -ne $e) { continue }
    $node = $e.el; $kind = $e.kind; $id = $e.id
    $where = $e.sources -join ' > '
    $culture = Get-Attr $node 'culture'
    if ($null -ne $culture) { $culture = $culture -replace '^Culture\.', '' } else { $culture = '' }
    $name = Remove-LocKey (Get-Attr $node 'name')
    if ($name -eq '') { $name = $id }
    $item = [ordered]@{ id = $id; name = $name; kind = $kind; type = ''; culture = $culture }
    # The game computes the tier (ItemValueModel.CalculateTier) unless tier_override is set; the editor shows
    # tier_override, else the "_tN" in the id (marked as such), else nothing.
    $to = Get-Number (Get-Attr $node 'tier_override')
    $tm = [regex]::Match($id, '_t(\d)(?:_|$)')
    if ($null -ne $to) { $item.tier = [int]$to }
    elseif ($tm.Success) { $item.tier = [int]$tm.Groups[1].Value; $item.tierFromId = $true }
    foreach ($a in @('value', 'weight', 'difficulty', 'appearance')) {
        $v = Get-Number (Get-Attr $node $a)
        if ($null -ne $v) { $item[$a] = $v }
    }
    if ((Get-Attr $node 'is_merchandise') -eq 'false') { $item.notMerch = $true }
    $flags = @()
    foreach ($fl in $node.SelectNodes('Flags')) { foreach ($a in $fl.Attributes) { if (Test-True $a.Value) { $flags += $a.Name } } }
    if ($flags.Count -gt 0) { $item.flags = $flags }

    if ($kind -eq 'CraftedItem') {
        $tpl = Get-Attr $node 'crafting_template'
        if ($null -eq $tpl) { $tpl = '' }
        if ($templateType.ContainsKey($tpl)) { $item.type = $templateType[$tpl] }
        else { Add-Warning "crafted item '$id' ($where): unknown crafting_template '$tpl'" }
        $pieces = @()
        foreach ($p in $node.SelectNodes('Pieces/Piece')) {
            $pieces += [ordered]@{ id = (Get-Attr $p 'id'); type = (Get-Attr $p 'Type'); scale = (Get-Attr $p 'scale_factor') }
        }
        $item.crafted = [ordered]@{ template = $tpl; pieces = $pieces }
    }
    else {
        $t = Get-Attr $node 'Type'
        if ($null -eq $t) { $t = '' }
        # ItemObject.Deserialize: Enum.Parse(ItemTypeEnum, ignoreCase: true) - "headArmor" is HeadArmor.
        foreach ($canon in $itemTypes) { if ($canon -ieq $t) { $t = $canon; break } }
        if ($itemTypes -cnotcontains $t) { Add-Warning "item '$id' ($where): unknown Type '$t'" }
        $item.type = $t
        $weapons = @()
        foreach ($w in $node.SelectNodes('ItemComponent/Weapon')) {
            $wd = [ordered]@{ cls = (Get-Attr $w 'weapon_class') }
            foreach ($pair in @(@('swing_damage', 'sw'), @('thrust_damage', 'th'), @('speed_rating', 'spd'), @('thrust_speed', 'tspd'),
                    @('weapon_length', 'len'), @('missile_speed', 'ms'), @('stack_amount', 'stack'), @('accuracy', 'acc'),
                    @('handling', 'hand'), @('body_armor', 'arm'), @('hit_points', 'hp'), @('weapon_balance', 'bal'))) {
                $v = Get-Number (Get-Attr $w $pair[0])
                if ($null -ne $v) { $wd[$pair[1]] = $v }
            }
            $st = Get-Attr $w 'swing_damage_type'; if ($null -ne $st) { $wd.swt = $st }
            $tt = Get-Attr $w 'thrust_damage_type'; if ($null -ne $tt) { $wd.tht = $tt }
            $wf = @()
            foreach ($fl in $w.SelectNodes('WeaponFlags')) { foreach ($a in $fl.Attributes) { if (Test-True $a.Value) { $wf += $a.Name } } }
            if ($wf.Count -gt 0) { $wd.flags = $wf }
            $weapons += $wd
        }
        if ($weapons.Count -gt 0) { $item.weapons = $weapons }
        $ar = $node.SelectSingleNode('ItemComponent/Armor')
        if ($null -ne $ar) {
            $ad = [ordered]@{}
            foreach ($pair in @(@('head_armor', 'head'), @('body_armor', 'body'), @('arm_armor', 'arm'), @('leg_armor', 'leg'))) {
                $v = Get-Number (Get-Attr $ar $pair[0])
                if ($null -ne $v) { $ad[$pair[1]] = $v }
            }
            $mt = Get-Attr $ar 'material_type'; if ($null -ne $mt) { $ad.mat = $mt }
            $item.armor = $ad
        }
        $hn = $node.SelectSingleNode('ItemComponent/Horse')
        if ($null -ne $hn) {
            $hd = [ordered]@{}
            foreach ($pair in @(@('speed', 'spd'), @('maneuver', 'man'), @('charge_damage', 'chg'), @('extra_health', 'extraHp'), @('body_length', 'len'))) {
                $v = Get-Number (Get-Attr $hn $pair[0])
                if ($null -ne $v) { $hd[$pair[1]] = $v }
            }
            $mon = (Get-Attr $hn 'monster')
            if ($null -ne $mon) {
                $mon = $mon -replace '^Monster\.', ''
                $hd.monster = $mon
                if ($monsterHp.ContainsKey($mon)) {
                    $extra = 0
                    if ($hd.Contains('extraHp')) { $extra = $hd.extraHp }
                    $hd.hp = $monsterHp[$mon] + $extra
                }
            }
            if (Test-True (Get-Attr $hn 'is_mountable')) { $hd.mount = $true }
            $item.horse = $hd
        }
    }
    $item.module = $e.module
    $item.file = $e.file
    if ($e.merged) { $item.from = $where }
    if (-not $e.campaign) { $item.nc = $true }
    $items[$id] = $item
}

# ---------------------------------------------------------------- skill sets

$skillSetsCampaign = @{}
$skillSetsAll = @{}
function Merge-SkillSet($map, [string]$id, $node) {
    # MBCharacterSkills.Deserialize -> PropertyOwner.Deserialize: values overlay, nothing is cleared.
    if (-not $map.ContainsKey($id)) { $map[$id] = [ordered]@{} }
    foreach ($s in $node.SelectNodes('skill')) {
        if ($null -eq $s.Attributes['id'] -or $null -eq $s.Attributes['value']) { continue }
        $map[$id][$s.Attributes['id'].Value] = [int]$s.Attributes['value'].Value
    }
}
Write-Host 'Reading skill sets, equipment sets and troops...'
foreach ($module in $moduleList) {
    if (-not (Test-Path -LiteralPath $module.sub)) { continue }
    foreach ($f in (Get-XmlFiles $module 'SkillSets')) {
        $doc = New-Object System.Xml.XmlDocument
        $doc.Load($f.file)
        foreach ($set in $doc.SelectNodes('//SkillSet')) {
            if ($null -eq $set.Attributes['id']) { continue }
            $sid = $set.Attributes['id'].Value
            Merge-SkillSet $skillSetsAll $sid $set
            if ($f.campaign) { Merge-SkillSet $skillSetsCampaign $sid $set }
        }
    }
}

# ---------------------------------------------------------------- equipment sets (EquipmentRosters XML)

function Read-Slots($node) {
    # <equipment slot="..." id="..."/> children, in order. Returns a list of [slot, id, rawId].
    $slots = @()
    foreach ($e in $node.ChildNodes) {
        if ($e.NodeType -ne [System.Xml.XmlNodeType]::Element -or $e.LocalName -ne 'equipment') { continue }
        $slot = Get-Attr $e 'slot'
        $raw = Get-Attr $e 'id'
        if ($null -eq $slot) { $slot = '' }
        if ($null -eq $raw) { $raw = '' }
        $slots += , @($slot, (Get-RefId $raw))
    }
    return , $slots
}

$equipSets = @{}
foreach ($e in (Collect-Merged 'EquipmentRosters' @('EquipmentRoster') $false).Values) {
    $r = $e.el
    $culture = Get-Attr $r 'culture'
    if ($null -ne $culture) { $culture = $culture -replace '^Culture\.', '' } else { $culture = '' }
    $sets = @()
    foreach ($s in $r.SelectNodes('EquipmentSet')) {
        $sets += [ordered]@{ type = (Get-EquipmentType $s); slots = (Read-Slots $s) }
    }
    $equipSets[$e.id] = [ordered]@{ id = $e.id; culture = $culture; sets = $sets; module = $e.module; file = $e.file; from = ($e.sources -join ' > ') }
}

# ---------------------------------------------------------------- troops

$rbmFileText = @{}
$rbmFileBom = @{}
foreach ($rel in $rbmTroopFiles) {
    $p = Join-Path $RepoRoot ($rel -replace '/', '\')
    if (-not (Test-Path -LiteralPath $p)) { Add-Warning "RBM troop file not found: $p"; continue }
    $bytes = [System.IO.File]::ReadAllBytes($p)
    $bom = ($bytes.Length -ge 3 -and $bytes[0] -eq 0xEF -and $bytes[1] -eq 0xBB -and $bytes[2] -eq 0xBF)
    $offset = 0
    if ($bom) { $offset = 3 }
    $rbmFileText[$rel] = (New-Object System.Text.UTF8Encoding($false, $true)).GetString($bytes, $offset, $bytes.Length - $offset)
    $rbmFileBom[$rel] = $bom
}

function Read-Equipments($n) {
    # BasicCharacterObject.Deserialize, <Equipments>: EquipmentRoster children, EquipmentSet references and loose
    # <equipment> (applied over every equipment of the troop by AddOverriddenEquipments).
    $r = @{ rosters = @(); sets = @(); loose = @() }
    foreach ($eq in $n.ChildNodes) {
        if ($eq.NodeType -ne [System.Xml.XmlNodeType]::Element -or ($eq.LocalName -ne 'Equipments' -and $eq.LocalName -ne 'equipments')) { continue }
        foreach ($c in $eq.ChildNodes) {
            if ($c.NodeType -ne [System.Xml.XmlNodeType]::Element) { continue }
            $cn = $c.LocalName
            if ($cn -eq 'EquipmentRoster' -or $cn -eq 'equipmentRoster') {
                $r.rosters += [ordered]@{ type = (Get-EquipmentType $c); attrs = (Get-AttrMap $c); slots = (Read-Slots $c) }
            }
            elseif ($cn -eq 'EquipmentSet' -or $cn -eq 'equipmentSet') {
                $sid = Get-Attr $c 'id'
                if ($null -eq $sid) { $sid = '' }
                $r.sets += [ordered]@{ id = $sid; type = (Get-EquipmentType $c); attrs = (Get-AttrMap $c) }
            }
            elseif ($cn -eq 'equipment') {
                $slot = Get-Attr $c 'slot'
                if ($null -eq $slot) { $slot = '' }
                $r.loose += , @($slot, (Get-RefId (Get-Attr $c 'id')))
            }
        }
    }
    return $r
}

function Get-Upgrades($n) {
    # CharacterObject.Deserialize: every <upgrade_targets>/<upgrade_target id="NPCCharacter.x"> child, in order.
    $list = @()
    foreach ($u in $n.SelectNodes('upgrade_targets/upgrade_target')) {
        $uid = Get-Attr $u 'id'
        if ($null -ne $uid) { $list += ($uid -replace '^NPCCharacter\.', '') }
    }
    return , $list
}

$troops = New-Object System.Collections.Specialized.OrderedDictionary
$heroCount = 0
$heroIds = New-Object System.Collections.Generic.List[string]
$mountedClasses = @('Cavalry', 'HorseArcher', 'LightCavalry', 'HeavyCavalry')
$troopEntries = Collect-Merged 'NPCCharacters' @('NPCCharacter') $true
foreach ($e in $troopEntries.Values) {
    $n = $e.el
    $id = $e.id
    if (Test-True (Get-Attr $n 'is_hero')) { $heroCount++; $heroIds.Add($id); continue }
    $isRbmTroopFile = ($rbmTroopFiles -contains $e.rel)
    $notes = @()
    $dupFrom = $null
    if ($null -ne $e.dupOf) {
        # MergeTwoXmlsPatch removes the earlier node only when BOTH have an <Equipments><EquipmentRoster>.
        $prev = $e.dupOf
        $why = $(if (-not (Test-HasRoster $n)) { 'the RBM definition has no EquipmentRoster' } else { 'the earlier definition (' + ($prev.sources -join ' > ') + ') has no EquipmentRoster' })
        $notes += "Not replaced: $why, so RBM/XmlLoadingPatches keeps both definitions. The game deserializes both: the later one sets level and skills, the equipment rosters and sets of both are added."
        $pe = Read-Equipments $prev.el
        # upgrades: the earlier definition's targets. Each Deserialize sets UpgradeTargets anew, so the later
        # (RBM) definition's list is the one the game keeps.
        $dupFrom = [ordered]@{ file = ($prev.sources -join ' > '); rosters = $pe.rosters; sets = $pe.sets; loose = $pe.loose
            upgrades = (Get-Upgrades $prev.el) }
    }
    $level = 1
    $lv = Get-Attr $n 'level'
    if ($null -ne $lv) { [void][int]::TryParse($lv, [ref]$level) }
    # DefaultCharacterStatsModel.GetTier: clamp(ceil((level - 5) / 5), 0, 6)
    $tier = [int][Math]::Ceiling(($level - 5) / 5.0)
    if ($tier -lt 0) { $tier = 0 }
    if ($tier -gt 6) { $tier = 6 }
    $culture = Get-Attr $n 'culture'
    if ($null -eq $culture) { $culture = '' }
    $culture = $culture -replace '^Culture\.', ''
    $group = Get-Attr $n 'default_group'
    if ($null -eq $group) { $group = '' }
    $occupation = Get-Attr $n 'occupation'
    if ($null -eq $occupation) { $occupation = '' }

    # Skills: BasicCharacterObject.Deserialize. skill_template -> the template's values; a <skills> child
    # (even an empty one) copies the template and overlays its <skill> entries.
    $templateId = ''
    $templateSkills = [ordered]@{}
    $template = Get-Attr $n 'skill_template'
    if ($null -ne $template) {
        $templateId = $template -replace '^SkillSet\.', ''
        $sets = $skillSetsAll
        if ($e.campaign) { $sets = $skillSetsCampaign }
        if ($sets.ContainsKey($templateId)) { foreach ($k in $sets[$templateId].Keys) { $templateSkills[$k] = $sets[$templateId][$k] } }
        else { Add-Warning "$id : skill template '$templateId' not found" }
    }
    $explicit = [ordered]@{}
    $hasSkillsNode = $false
    foreach ($sn in $n.ChildNodes) {
        if ($sn.NodeType -ne [System.Xml.XmlNodeType]::Element -or ($sn.LocalName -ne 'skills' -and $sn.LocalName -ne 'Skills')) { continue }
        $hasSkillsNode = $true
        foreach ($s in $sn.ChildNodes) {
            if ($s.NodeType -ne [System.Xml.XmlNodeType]::Element) { continue }
            $sid = Get-Attr $s 'id'
            $sv = Get-Attr $s 'value'
            if ($null -eq $sid -or $null -eq $sv) { continue }
            $explicit[$sid] = [int]$sv
            if ($skillMap.Count -gt 0 -and -not $skillMap.Contains($sid)) { Add-Warning "$id : unknown skill id '$sid'" }
        }
    }

    $upgrades = Get-Upgrades $n
    $eqs = Read-Equipments $n

    $name = Remove-LocKey (Get-Attr $n 'name')
    if ($name -eq '') { $name = $id }
    $troop = [ordered]@{
        id = $id; name = $name; culture = $culture; level = $level; tier = $tier
        occupation = $occupation; group = $group
        mounted = ($mountedClasses -contains $group)
        upgrades = $upgrades
        skillTemplate = $templateId; templateSkills = $templateSkills; explicitSkills = $explicit; hasSkillsNode = $hasSkillsNode
        rosters = $eqs.rosters; sets = $eqs.sets; loose = $eqs.loose
        module = $e.module; file = $e.file; sources = @($e.sources); alsoIn = @($e.alsoIn)
        campaign = [bool]$e.campaign
        naval = ($e.module -eq 'NavalDLC' -or $e.module -eq 'RBM_WS')
        rbmFile = $(if ($isRbmTroopFile) { $e.rel } else { '' })
        template = (Test-True (Get-Attr $n 'is_template'))
        obsolete = (Test-True (Get-Attr $n 'is_obsolete'))
        hidden = (Test-True (Get-Attr $n 'is_hidden_encyclopedia'))
        basic = (Test-True (Get-Attr $n 'is_basic_troop'))
        notes = $notes
    }
    if ($null -ne $dupFrom) { $troop.dupFrom = $dupFrom }
    # CharacterObject.Deserialize: upgrade_requires="ItemCategory.x", the item category a party needs to upgrade into it.
    $ur = Get-Attr $n 'upgrade_requires'
    if ($null -ne $ur) { $troop.upgradeRequires = ($ur -replace '^ItemCategory\.', '') }
    if ($null -ne (Get-Attr $n 'default_equipment_set')) { $troop.notes += "Has default_equipment_set=""$(Get-Attr $n 'default_equipment_set')"": the first equipment is filled from that game default set." }
    if (-not $isRbmTroopFile) {
        # The export appends a copy: the element's own text when one file defines it, else the merged element
        # (vanilla style) since no single file holds what the game loads.
        if ($e.merged) {
            $troop.raw = (Format-Element $n) -replace "`n", "`n`t"
            $troop.rawIndent = "`t"
            $troop.rawMerged = $true
        }
        elseif ($null -ne $e.raw) { $troop.raw = $e.raw; $troop.rawIndent = $e.rawIndent }
        else { Add-Warning "$($e.module)/$($e.file): could not locate the raw text of '$id' (export cannot append it)" }
    }
    $troops[$id] = $troop
}

# ---------------------------------------------------------------- cultures and party templates (upgrade tree roots)
# CultureObject.Deserialize reads its troops from attributes (basic_troop, elite_basic_troop, melee_militia_troop,
# caravan_guard, ...: every "NPCCharacter.x" value is kept, keyed by attribute name) and from
# <basic_mercenary_troops><template name="NPCCharacter.x"/> (tavern mercenaries are drawn from these trees).
# PartyTemplateObject.Deserialize: <stacks><PartyTemplateStack troop="NPCCharacter.x"/> spawns the troop directly.
# Campaign files only. The upgrade tree editor uses these to tell a tree root from a troop nobody upgrades into.

Write-Host 'Reading cultures and party templates...'
$culturesOut = [ordered]@{}
foreach ($e in (Collect-Merged 'SPCultures' @('Culture') $false).Values) {
    if (-not $e.campaign) { continue }
    $c = $e.el
    $roots = [ordered]@{}
    foreach ($a in $c.Attributes) {
        if ($a.Value.StartsWith('NPCCharacter.')) { $roots[$a.Name] = $a.Value.Substring('NPCCharacter.'.Length) }
    }
    $mercs = @()
    foreach ($m in $c.SelectNodes('basic_mercenary_troops/template')) {
        $mn = Get-Attr $m 'name'
        if ($null -ne $mn) { $mercs += ($mn -replace '^NPCCharacter\.', '') }
    }
    $cname = Remove-LocKey (Get-Attr $c 'name')
    if ($cname -eq '') { $cname = $e.id }
    $culturesOut[$e.id] = [ordered]@{ id = $e.id; name = $cname; bandit = (Test-True (Get-Attr $c 'is_bandit'))
        main = (Test-True (Get-Attr $c 'is_main_culture')); roots = $roots; mercenaries = $mercs; module = $e.module }
}
$templateTroops = @{}
foreach ($module in $moduleList) {
    if (-not (Test-Path -LiteralPath $module.sub)) { continue }
    foreach ($f in (Get-XmlFiles $module 'partyTemplates')) {
        if (-not $f.campaign) { continue }
        $doc = New-Object System.Xml.XmlDocument
        $doc.Load($f.file)
        if ($module.rbm -and -not (Test-RbmFileLoads (Get-RbmTags $doc))) { continue }
        foreach ($pt in $doc.SelectNodes('//MBPartyTemplate')) {
            $ptid = Get-Attr $pt 'id'
            if ($null -eq $ptid) { continue }
            foreach ($st in $pt.SelectNodes('stacks/PartyTemplateStack')) {
                $tr = Get-Attr $st 'troop'
                if ($null -eq $tr) { continue }
                $tr = $tr -replace '^NPCCharacter\.', ''
                if (-not $templateTroops.ContainsKey($tr)) { $templateTroops[$tr] = New-Object System.Collections.Generic.List[string] }
                if (-not $templateTroops[$tr].Contains($ptid)) { $templateTroops[$tr].Add($ptid) }
            }
        }
    }
}
$templateTroopsOut = [ordered]@{}
foreach ($k in ($templateTroops.Keys | Sort-Object)) { $templateTroopsOut[$k] = @($templateTroops[$k]) }

# ---------------------------------------------------------------- checks

$slotNames = @('Item0', 'Item1', 'Item2', 'Item3', 'Item4', 'Head', 'Body', 'Leg', 'Gloves', 'Cape', 'Horse', 'HorseHarness')
$weaponTypes = @('OneHandedWeapon', 'TwoHandedWeapon', 'Polearm', 'Arrows', 'Bolts', 'SlingStones', 'Shield', 'Bow', 'Crossbow', 'Sling', 'Thrown', 'Pistol', 'Musket', 'Bullets', 'Banner')
function Test-Fits([string]$slot, $item) {
    # Equipment.IsItemFitsToSlot.
    $t = $item.type
    if ($weaponTypes -contains $t) {
        $drop = $false
        if ($item.Contains('flags')) { $drop = ($item.flags -contains 'DropOnWeaponChange' -or $item.flags -contains 'DropOnAnyAction') }
        if ($drop) { return ($slot -eq 'Item4') }
        return (@('Item0', 'Item1', 'Item2', 'Item3') -contains $slot)
    }
    switch ($t) {
        'Horse' { return ($slot -eq 'Horse') }
        'Animal' { return ($slot -eq 'Horse') }
        'HeadArmor' { return ($slot -eq 'Head') }
        'BodyArmor' { return ($slot -eq 'Body') }
        'LegArmor' { return ($slot -eq 'Leg') }
        'HandArmor' { return ($slot -eq 'Gloves') }
        'Cape' { return ($slot -eq 'Cape') }
        'HorseHarness' { return ($slot -eq 'HorseHarness') }
    }
    return $false
}

$unknownItems = @{}
$badSlots = New-Object System.Collections.Generic.List[string]
$usedSets = @{}
function Test-ShoulderBase([string]$itemId) {
    # MergeTwoXmlsPatch with passiveShoulderShields off.
    if (-not ($itemId.Contains('shield') -and $itemId.EndsWith('_shoulder'))) { return $null }
    $b = $itemId.Substring(0, $itemId.Length - '_shoulder'.Length)
    foreach ($s in @('_kalkan', '_cataphract')) { if ($b.EndsWith($s)) { $b = $b.Substring(0, $b.Length - $s.Length); break } }
    return $b
}
function Check-Slots([string]$troopId, $slots, [string]$where) {
    foreach ($pair in $slots) {
        $slot = $pair[0]; $iid = $pair[1]
        if ($slotNames -notcontains $slot) { Add-Warning "$troopId ($where): unknown slot '$slot'"; continue }
        if ($iid -eq '') { continue }
        if (-not $items.Contains($iid)) {
            if (-not $unknownItems.ContainsKey($iid)) { $unknownItems[$iid] = New-Object System.Collections.Generic.List[string] }
            if (-not $unknownItems[$iid].Contains($troopId)) { $unknownItems[$iid].Add($troopId) }
            continue
        }
        if (-not (Test-Fits $slot $items[$iid])) { $badSlots.Add("$troopId ($where): $iid ($($items[$iid].type)) does not fit slot $slot") }
    }
}
foreach ($t in $troops.Values) {
    $i = 0
    foreach ($r in $t.rosters) { $i++; Check-Slots $t.id $r.slots ("roster " + $i) }
    if ($t.loose.Count -gt 0) { Check-Slots $t.id $t.loose 'loose <equipment> override' }
    foreach ($s in $t.sets) {
        if ($s.id -eq '') { Add-Warning "$($t.id): an <EquipmentSet> has no id"; continue }
        if (-not $equipSets.ContainsKey($s.id)) { Add-Warning "$($t.id): unknown equipment set '$($s.id)'"; continue }
        $usedSets[$s.id] = $true
    }
    if ($null -ne $t['dupFrom']) { foreach ($s in $t.dupFrom.sets) { if ($equipSets.ContainsKey($s.id)) { $usedSets[$s.id] = $true } } }
    foreach ($u in $t.upgrades) { if (-not $troops.Contains($u)) { Add-Warning "$($t.id): unknown upgrade target '$u'" } }
}
foreach ($sid in $usedSets.Keys) {
    $k = 0
    foreach ($s in $equipSets[$sid].sets) { $k++; Check-Slots ('equipment set ' + $sid) $s.slots ("set " + $k) }
}
foreach ($iid in ($unknownItems.Keys | Sort-Object)) {
    $users = $unknownItems[$iid]
    $base = Test-ShoulderBase $iid
    $extra = ''
    if ($null -ne $base) { $extra = " (shoulder variant of '$base'" + $(if ($items.Contains($base)) { ', which exists' } else { ', also unknown' }) + ')' }
    $list = (@($users | Select-Object -First 6) -join ', ')
    if ($users.Count -gt 6) { $list += ", ... ($($users.Count) in all)" }
    Add-Warning "unknown item '$iid'$extra used by $list"
}
$maxBad = 40
for ($i = 0; $i -lt [Math]::Min($badSlots.Count, $maxBad); $i++) { Add-Warning $badSlots[$i] }
if ($badSlots.Count -gt $maxBad) { Add-Warning "... and $($badSlots.Count - $maxBad) more items that do not fit their slot" }

# Keep only the equipment sets troops reference.
$setsOut = [ordered]@{}
foreach ($sid in ($usedSets.Keys | Sort-Object)) { $setsOut[$sid] = $equipSets[$sid] }

# Drop always-empty fields to keep the file small.
foreach ($t in $troops.Values) {
    if ($t.alsoIn.Count -eq 0) { $t.Remove('alsoIn') }
    if ($t.notes.Count -eq 0) { $t.Remove('notes') }
    if ($t.loose.Count -eq 0) { $t.Remove('loose') }
}

# ---------------------------------------------------------------- write

$gameVersion = ''
$nativeSub = Join-Path $ModulesRoot 'Native\SubModule.xml'
if (Test-Path -LiteralPath $nativeSub) {
    $d = New-Object System.Xml.XmlDocument
    $d.Load($nativeSub)
    $v = $d.SelectSingleNode('/Module/Version')
    if ($null -ne $v -and $null -ne $v.Attributes['value']) { $gameVersion = $v.Attributes['value'].Value.Trim() }
}

$skillNames = [ordered]@{}
$skillOrder = @()
foreach ($s in $skillMap.Values) { $skillNames[$s.id] = $s.name; $skillOrder += $s.id }

$rbmFilesOut = @()
foreach ($rel in $rbmTroopFiles) {
    if (-not $rbmFileText.ContainsKey($rel)) { continue }
    $txt = $rbmFileText[$rel]
    $eol = "`n"
    if ($txt.Contains("`r`n")) { $eol = "`r`n" }
    $rbmFilesOut += [ordered]@{ path = $rel; bom = $rbmFileBom[$rel]; eol = $eol; naval = $rel.StartsWith('RBM_WS_XML'); text = $txt }
}

$data = [ordered]@{
    generated = (Get-Date).ToString('yyyy-MM-dd HH:mm:ss', $Inv)
    gameVersion = $gameVersion
    navalDlc = $navalInstalled
    assumptions = 'RBM combat, campaign and troop overhaul on; passiveShoulderShields off (default). Campaign game type.'
    skills = $skillNames
    skillOrder = $skillOrder
    troops = @($troops.Values)
    equipmentSets = $setsOut
    items = @($items.Values)
    rbmFiles = $rbmFilesOut
    cultures = $culturesOut
    partyTemplateTroops = $templateTroopsOut
    heroIds = @($heroIds | Sort-Object)
    files = $fileLog
    warnings = $script:Warnings
}

$sb = New-Object System.Text.StringBuilder
[void]$sb.Append("// Generated by Build-TroopLoadoutData.ps1 - do not edit, do not commit (contains TaleWorlds text).`n")
[void]$sb.Append('window.TROOP_LOADOUT_DATA = ')
Write-JsonValue $sb $data
[void]$sb.Append(";`n")
[System.IO.File]::WriteAllText($OutFile, $sb.ToString(), (New-Object System.Text.UTF8Encoding($false)))

$campaignTroops = 0
$rbmTroops = 0
foreach ($t in $troops.Values) { if ($t.campaign) { $campaignTroops++ }; if ($t.rbmFile -ne '') { $rbmTroops++ } }
Write-Host ''
Write-Host ("Skills:  {0}" -f $skillMap.Count)
Write-Host ("Troops:  {0} non-hero ({1} loaded in campaigns, {2} defined in an RBM unit overhaul file; {3} hero entries skipped)" -f $troops.Count, $campaignTroops, $rbmTroops, $heroCount)
Write-Host ("Items:   {0}; equipment sets referenced: {1}" -f $items.Count, $setsOut.Count)
Write-Host ("Game:    {0}{1}" -f $gameVersion, $(if ($navalInstalled) { ' + War Sails' } else { '' }))
Write-Host ("Warnings: {0}" -f $script:Warnings.Count)
Write-Host ("Wrote {0} ({1:N0} KB)" -f $OutFile, ((Get-Item -LiteralPath $OutFile).Length / 1024))
