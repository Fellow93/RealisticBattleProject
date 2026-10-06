<#
.SYNOPSIS
    Builds crafting-data.js for the crafting editor (index.html in this folder).

.DESCRIPTION
    Collects, without running the game:
      - every CraftingPiece and CraftingTemplate definition (and the WeaponDescriptions the calculator needs) from
        Native, SandBoxCore, SandBox, StoryMode, CustomBattle, NavalDLC (when installed) and RBM's own XML in this repo
        (RBMXML, plus RBM_WS_XML when NavalDLC is installed), found through each module's SubModule.xml and merged in
        module order the way the game does it: per module its XSLT transforms over the XML merged so far, then its XML;
        plain files through MBObjectManager.MergeElements (keyed merge), RBM's files through RBM/XmlLoadingPatches.cs
        MergeTwoXmlsPatch, which APPENDS them (it only removes earlier Item/CraftedItem/ItemModifier/NPCCharacter
        elements). So an id RBM defines again is in the merged XML twice, and MBObjectManager.LoadXml deserializes the
        same object twice, in load order. The data keeps every definition of an id in that order (the page replays
        the deserializers on them). Templates and weapon descriptions are also collected without RBM ("vanilla").
        Assumes RBM combat on (the RBM crafting files carry RBM_COMBAT_XML_TAG).
      - every CraftedItem (all modules' Items XML, RBM's item files replacing by id as for the item editor), for the
        weapon-stat calculator;
      - the full text of every RBM crafting file (BOM and line ends recorded), which the page splices on export;
      - the attribute vocabulary of XmlSchemas/CraftingPieces.xsd and CraftingTemplates.xsd and the enums the game
        parses those attributes with (from decompiled/);
      - lookups: cultures, item modifier groups, item holsters.

    The output contains TaleWorlds text and is gitignored. Windows PowerShell 5.1 compatible.

.PARAMETER ModulesRoot
    The game's Modules folder. Defaults to the folder the repo sits in.

.EXAMPLE
    powershell -ExecutionPolicy Bypass -File tools\CraftingEditor\Build-CraftingData.ps1
#>
[CmdletBinding()]
param(
    [string]$ModulesRoot = ''
)

Set-StrictMode -Version 2.0
$ErrorActionPreference = 'Stop'

$Clock = [System.Diagnostics.Stopwatch]::StartNew()
$Inv = [System.Globalization.CultureInfo]::InvariantCulture
$RepoRoot = (Resolve-Path (Join-Path $PSScriptRoot '..\..')).Path
if ($ModulesRoot -eq '') {
    $ModulesRoot = (Resolve-Path (Join-Path $RepoRoot '..')).Path
}
$Decompiled = Join-Path $RepoRoot 'decompiled'
$OutFile = Join-Path $PSScriptRoot 'crafting-data.js'
$GameRoot = (Resolve-Path (Join-Path $ModulesRoot '..')).Path

$script:Warnings = New-Object System.Collections.Generic.List[string]
function Add-Warning([string]$message) {
    $script:Warnings.Add($message)
    Write-Warning $message
}

# ---------------------------------------------------------------- JSON writer
# Hand-rolled like the other editors' scripts (PS 5.1's ConvertTo-Json is slow and culture-dependent).

$script:CtrlRe = New-Object System.Text.RegularExpressions.Regex('[\x00-\x08\x0B\x0C\x0E-\x1F]')
$script:LtEsc = '<'
$script:Ls = [string][char]0x2028
$script:Ps = [string][char]0x2029
function Write-JsonString([System.Text.StringBuilder]$sb, [string]$s) {
    $e = $s.Replace('\', '\\').Replace('"', '\"').Replace("`r", '\r').Replace("`n", '\n').Replace("`t", '\t')
    # '<' escaped keeps "</script>" out of the generated file; U+2028/9 are line ends in older JS parsers.
    $e = $e.Replace('<', $script:LtEsc).Replace($script:Ls, ' ').Replace($script:Ps, ' ')
    if ($script:CtrlRe.IsMatch($e)) {
        $e = $script:CtrlRe.Replace($e, { param($m) '\u' + ([int][char]$m.Value).ToString('x4') })
    }
    [void]$sb.Append('"').Append($e).Append('"')
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

function Read-TextFile([string]$path) {
    # @{ text; bom; eol } - the text without the BOM, decoded as strict UTF-8.
    $bytes = [System.IO.File]::ReadAllBytes($path)
    $bom = ($bytes.Length -ge 3 -and $bytes[0] -eq 0xEF -and $bytes[1] -eq 0xBB -and $bytes[2] -eq 0xBF)
    $offset = 0
    if ($bom) { $offset = 3 }
    $text = (New-Object System.Text.UTF8Encoding($false, $true)).GetString($bytes, $offset, $bytes.Length - $offset)
    $eol = "`n"
    if ($text.Contains("`r`n")) { $eol = "`r`n" }
    return @{ text = $text; bom = $bom; eol = $eol }
}

# Top-level child elements of the root named in $names, in document order, with their exact source text and
# indentation: list of @{ kind; id; text; indent }. Comment, CDATA and PI aware (a commented-out piece is not a piece).
# Elements without an id are left out (the XmlDocument side skips them the same way).
$script:TokenRe = New-Object System.Text.RegularExpressions.Regex('<!--[\s\S]*?-->|<!\[CDATA\[[\s\S]*?\]\]>|<\?[\s\S]*?\?>|<!DOCTYPE[^>]*>|<(/?)([A-Za-z_][\w.:-]*)((?:[^>"'']|"[^"]*"|''[^'']*'')*?)(/?)>')
function Get-TopLevelSpanList([string]$text, [string[]]$names) {
    $spans = New-Object System.Collections.Generic.List[object]
    $depth = 0
    $start = -1
    $startAttrs = ''
    $startName = ''
    foreach ($m in $script:TokenRe.Matches($text)) {
        if (-not $m.Groups[2].Success) { continue }
        $closing = ($m.Groups[1].Value -eq '/')
        $selfClose = ($m.Groups[4].Value -eq '/')
        if ($closing) {
            $depth--
            if ($depth -eq 1 -and $start -ge 0) {
                $spans.Add(@{ start = $start; end = ($m.Index + $m.Length); attrs = $startAttrs; kind = $startName })
                $start = -1
            }
            continue
        }
        if ($depth -eq 1 -and $names -contains $m.Groups[2].Value) {
            if ($selfClose) { $spans.Add(@{ start = $m.Index; end = ($m.Index + $m.Length); attrs = $m.Groups[3].Value; kind = $m.Groups[2].Value }) }
            else { $start = $m.Index; $startAttrs = $m.Groups[3].Value; $startName = $m.Groups[2].Value }
        }
        if (-not $selfClose) { $depth++ }
    }
    $out = New-Object System.Collections.Generic.List[object]
    foreach ($s in $spans) {
        $im = [regex]::Match($s.attrs, '(?:^|\s)id\s*=\s*"([^"]*)"')
        if (-not $im.Success) { continue }
        $lineStart = $text.LastIndexOf("`n", [Math]::Max(0, $s.start - 1)) + 1
        if ($s.start -eq 0) { $lineStart = 0 }
        $indent = $text.Substring($lineStart, $s.start - $lineStart)
        if ($indent.Trim() -ne '') { $indent = '' }
        $out.Add(@{ kind = $s.kind; id = $im.Groups[1].Value; text = $text.Substring($s.start, $s.end - $s.start); indent = $indent })
    }
    return ,$out
}

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
$vanillaModules = @($moduleList | Where-Object { -not $_.rbm })

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
        # A registered "<path>.xsl(t)" transforms the XML merged so far, before this entry's own file is merged
        # (MBObjectManager.CreateMergedXmlFile). The repo keeps RBM_WS_XML's transforms at its root, hence the leaf name.
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

$RbmTagNames = @('RBM_XML_TAG', 'RBM_COMBAT_XML_TAG', 'RBM_CAMPAIGN_XML_TAG', 'RBM_ECONOMY_COMBAT_XML_TAG', 'RBM_COMBAT_ONLY_XML_TAG', 'RBM_COMBAT_OVERHAUL_XML_TAG', 'RBM_WS_XML_TAG')
function Get-RbmTags([System.Xml.XmlDocument]$doc) {
    $tags = [ordered]@{}
    foreach ($c in $doc.SelectNodes('//comment()')) {
        foreach ($t in $RbmTagNames) {
            if ($c.Value.Contains($t)) { $tags[$t] = $true }
        }
    }
    return $tags
}

# MergeTwoXmlsPatch with combat + campaign on: only RBM_COMBAT_ONLY files are dropped.
function Test-RbmFileLoads($tags) {
    if ($tags.Contains('RBM_COMBAT_ONLY_XML_TAG')) { return $false }
    return $true
}

$fileLog = New-Object System.Collections.Generic.List[object]
$script:LogQuiet = $false
function Add-FileLog($module, $f, [string]$kind, [int]$count, [string]$note) {
    if ($script:LogQuiet) { return }
    $fileLog.Add([ordered]@{ module = $module.id; file = $f.name; kind = $kind; campaign = [bool]$f.campaign; types = $f.types; count = $count; note = $note })
    Write-Host ("  {0,-12} {1,-18} {2,-46} {3,5}{4}" -f $module.id, $kind, $f.name, $count, $(if ($note) { '  (' + $note + ')' } else { '' }))
}

# ---------------------------------------------------------------- merging, as the game does it
# (MergeElements and the schema reader are copied from ../ItemEditor/Build-ItemData.ps1; see its comments)

$XsNs = 'http://www.w3.org/2001/XMLSchema'
function Get-XsdPath($node) {
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
function Format-Element($el) {
    # Pretty-prints a merged element in the vanilla style (tabs, one attribute per line).
    $settings = New-Object System.Xml.XmlWriterSettings
    $settings.Indent = $true
    $settings.IndentChars = "`t"
    $settings.NewLineOnAttributes = $true
    $settings.OmitXmlDeclaration = $true
    $settings.NewLineChars = "`n"
    $settings.ConformanceLevel = [System.Xml.ConformanceLevel]::Fragment
    $copy = $el.CloneNode($true)
    foreach ($x in @($copy.SelectNodes('descendant-or-self::*[@_replaceWhileMerging]'))) { $x.RemoveAttribute('_replaceWhileMerging') }
    $sw = New-Object System.IO.StringWriter
    $w = [System.Xml.XmlWriter]::Create($sw, $settings)
    $copy.WriteTo($w)
    $w.Flush()
    return $sw.ToString()
}

# Piece ids under UsablePieces/UsablePiece@piece_id and AvailablePieces/AvailablePiece@id (the only children the
# game's and RBM's XSLTs add), as a list with repeats.
function Get-ListedPieceIds($el) {
    $ids = New-Object System.Collections.Generic.List[string]
    foreach ($n in $el.SelectNodes('UsablePieces/UsablePiece')) { $v = Get-Attr $n 'piece_id'; if ($null -ne $v) { $ids.Add($v) } }
    foreach ($n in $el.SelectNodes('AvailablePieces/AvailablePiece')) { $v = Get-Attr $n 'id'; if ($null -ne $v) { $ids.Add($v) } }
    return ,$ids
}
function Get-WithoutListedPieces($el) {
    $c = $el.CloneNode($true)
    foreach ($n in @($c.SelectNodes('UsablePieces/UsablePiece|AvailablePieces/AvailablePiece'))) { [void]$n.ParentNode.RemoveChild($n) }
    foreach ($n in @($c.SelectNodes('UsablePieces|AvailablePieces'))) {
        foreach ($t in @($n.ChildNodes)) { if ($t.NodeType -ne [System.Xml.XmlNodeType]::Element) { [void]$n.RemoveChild($t) } }
    }
    foreach ($t in @($c.ChildNodes)) { if ($t.NodeType -eq [System.Xml.XmlNodeType]::Whitespace -or $t.NodeType -eq [System.Xml.XmlNodeType]::SignificantWhitespace) { [void]$c.RemoveChild($t) } }
    return $c.OuterXml -replace '>\s+<', '><'
}

$script:Order = 0
$script:XsltCheck = $true
# Collects every definition of one XML id, in the order the game deserializes them. Returns a list of defs
# { kind, id, el (XmlElement in the merged store), module, src, campaign, rbm, rel, raw (file text, null once a plain
# file merged into it), rawIndent, merged, xslt (sources), xsltChanged, xsltAdd (piece ids an XSLT added to an RBM
# definition), sources, alsoIn, before (the def an RBM Item/CraftedItem replaced), order }.
#   $replaceRbm: an RBM_XML_TAG file REPLACES earlier definitions of the same kind and id (Items); otherwise it is
#   appended and the id is deserialized again (CraftingPieces, CraftingTemplates, WeaponDescriptions).
# A file not loaded in campaigns never changes a campaign definition (it only adds ids no campaign file has).
function Collect-Defs([string]$xmlId, [string[]]$kinds, $modules, [bool]$replaceRbm, [bool]$keepRaw) {
    $schema = Read-Schema $xmlId
    $store = New-Object System.Xml.XmlDocument
    $root = $store.AppendChild($store.CreateElement($xmlId))
    $defs = New-Object System.Collections.Generic.List[object]
    foreach ($module in $modules) {
        if (-not (Test-Path -LiteralPath $module.sub)) { continue }
        foreach ($f in (Get-XmlFiles $module $xmlId -WithXslt)) {
            $src = $module.id + '/' + $f.name
            if ($f.ContainsKey('xslt')) {
                if (-not $f.campaign) { Add-FileLog $module $f $xmlId 0 'XSLT skipped: not loaded in campaigns'; continue }
                # MBObjectManager.ApplyXslt over the definitions merged so far; then re-point every def at its
                # transformed element, matched by element name and id in document order.
                $xsl = New-Object System.Xml.Xsl.XslCompiledTransform
                $xsl.Load($f.xslt)
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
                $gone = @()
                foreach ($d in $defs) {
                    if (-not $map.ContainsKey($d.el)) { $gone += $d; continue }
                    $new = $map[$d.el]
                    # Whitespace-insensitive: the identity template re-copies every whitespace node.
                    if (($new.OuterXml -replace '>\s+<', '><') -ne ($d.el.OuterXml -replace '>\s+<', '><')) {
                        $changed++
                        $d.xslt += $src
                        $d.xsltChanged = $true
                        if ($script:XsltCheck -and (Get-WithoutListedPieces $new) -ne (Get-WithoutListedPieces $d.el)) {
                            Add-Warning "${src}: changes $($d.kind) '$($d.id)' ($($d.src)) beyond adding usable/available pieces; the editor shows the transformed definition but cannot replay the change on an edited RBM copy"
                        }
                        if ($d.rbm) {
                            # Added ids = new list minus old list (with repeats).
                            $old = Get-ListedPieceIds $d.el
                            foreach ($pieceId in (Get-ListedPieceIds $new)) {
                                if ($old.Contains($pieceId)) { [void]$old.Remove($pieceId) } else { $d.xsltAdd += $pieceId }
                            }
                        }
                    }
                    $d.el = $new
                }
                foreach ($d in $gone) { Add-Warning "${src}: removes $($d.kind) '$($d.id)' ($($d.src)); the editor drops it"; [void]$defs.Remove($d) }
                foreach ($q in $queues.Values) {
                    foreach ($node in $q) { Add-Warning "${src}: adds $($node.LocalName) '$(Get-Attr $node 'id')'; not read by the editor" }
                }
                $store = $out
                $root = $store.DocumentElement
                Add-FileLog $module $f $xmlId $changed 'XSLT: definitions changed'
                continue
            }
            $tf = Read-TextFile $f.file
            $doc = New-Object System.Xml.XmlDocument
            $doc.LoadXml($tf.text)
            $rbmXml = $false
            if ($module.rbm) {
                $tags = Get-RbmTags $doc
                if (-not (Test-RbmFileLoads $tags)) { Add-FileLog $module $f $xmlId 0 'skipped: RBM_COMBAT_ONLY (campaign on)'; continue }
                $rbmXml = $tags.Contains('RBM_XML_TAG')
            }
            $rel = ''
            if ($module.rbm) { $rel = $module.repoDir + '/' + $f.name }
            $spans = $null
            if ($keepRaw) { $spans = Get-TopLevelSpanList $tf.text $kinds }
            $si = 0
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
                $raw = $null
                $rawIndent = ''
                if ($keepRaw) {
                    if ($si -lt $spans.Count -and $spans[$si].id -eq $id -and $spans[$si].kind -eq $kind) { $raw = $spans[$si].text; $rawIndent = $spans[$si].indent }
                    else { Add-Warning "${src}: could not match $kind '$id' to its source text; it is shown re-formatted" }
                    $si++
                }
                $key = $kind + '|' + $id
                if ($seen.ContainsKey($key)) { Add-Warning "${src}: $kind '$id' is defined twice in the file; the game deserializes both, in order" }
                $prev = $null
                for ($i = $defs.Count - 1; $i -ge 0; $i--) { if ($defs[$i].kind -eq $kind -and $defs[$i].id -eq $id) { $prev = $defs[$i]; break } }
                if ($null -ne $prev -and $prev.campaign -and -not $f.campaign) { $prev.alsoIn += $src; continue }
                $n++
                $script:Order++
                if ($null -ne $prev -and -not $seen.ContainsKey($key) -and -not $rbmXml -and ($prev.campaign -or -not $f.campaign)) {
                    # Plain file over an earlier definition: keyed merge into it.
                    Merge-Elements $prev.el $node $schema
                    $prev.merged = $true
                    $prev.sources += $src
                    $prev.module = $module.id
                    $prev.src = $src
                    $prev.raw = $null
                    $merged++
                    continue
                }
                $new = @{ kind = $kind; id = $id; el = $store.ImportNode($node, $true); module = $module.id; src = $src; campaign = [bool]$f.campaign
                    rbm = [bool]$module.rbm; rel = $rel; raw = $raw; rawIndent = $rawIndent; merged = $false; xslt = @(); xsltChanged = $false; xsltAdd = @()
                    sources = @($src); alsoIn = @(); before = $null; order = $script:Order }
                [void]$root.AppendChild($new.el)
                if ($null -ne $prev -and $replaceRbm -and $rbmXml) {
                    # MergeTwoXmlsPatch: an RBM Item/CraftedItem removes every earlier element of its kind and id.
                    $new.before = $prev
                    $new.sources = @($prev.sources) + $src
                    $new.alsoIn = @($prev.alsoIn)
                    foreach ($d in $defs.ToArray()) {
                        if ($d.kind -eq $kind -and $d.id -eq $id) { if ($null -ne $d.el.ParentNode) { [void]$d.el.ParentNode.RemoveChild($d.el) }; [void]$defs.Remove($d) }
                    }
                }
                $defs.Add($new)
                $seen[$key] = $true
            }
            $note = ''
            if ($merged -gt 0) { $note = "$merged merged into earlier definitions" }
            if (-not $f.campaign) { $note = (@($note, 'not loaded in campaigns') | Where-Object { $_ }) -join '; ' }
            if ($rbmXml -and -not $replaceRbm) { $note = (@($note, 'RBM_XML_TAG: appended, ids deserialized again') | Where-Object { $_ }) -join '; ' }
            Add-FileLog $module $f $xmlId $n $note
        }
    }
    return ,$defs
}

# A def as written to the data: the exact text the game deserializes (re-formatted when a plain file or an XSLT
# changed it), and for a re-formatted non-RBM def the text before any XSLT (what a copy appended to an RBM file starts
# from: the transforms run before RBM's files and must not be baked into them).
function Get-DefOut($d) {
    $o = [ordered]@{ src = $d.src; module = $d.module }
    if ($d.rbm) { $o.rbm = $true; $o.rel = $d.rel }
    if (-not $d.campaign) { $o.nc = $true }
    $reformat = ($d.merged -or $null -eq $d.raw -or ($d.xsltChanged -and -not $d.rbm))
    if ($reformat) {
        $o.text = ((Format-Element $d.el) -replace "`n", "`n`t")
        $o.indent = "`t"
        $o.reformatted = $true
        if ($null -ne $d.raw -and -not $d.merged) { $o.srcText = $d.raw; $o.srcIndent = $d.rawIndent }
    }
    else { $o.text = $d.raw; $o.indent = $d.rawIndent }
    if ($d.xslt.Count -gt 0) { $o.xslt = @($d.xslt) }
    if ($d.rbm -and $d.xsltAdd.Count -gt 0) { $o.xsltAdd = @($d.xsltAdd) }
    if ($d.sources.Count -gt 1) { $o.sources = @($d.sources) }
    if ($d.alsoIn.Count -gt 0) { $o.alsoIn = @($d.alsoIn) }
    return $o
}

function Group-Defs($defs) {
    $byId = [ordered]@{}
    foreach ($d in $defs) {
        if (-not $byId.Contains($d.id)) { $byId[$d.id] = New-Object System.Collections.Generic.List[object] }
        $byId[$d.id].Add($d)
    }
    return $byId
}

# ---------------------------------------------------------------- enums from the decompiled sources

function Read-Enum([string]$relPath, [string]$enumName) {
    $p = Join-Path $Decompiled $relPath
    if (-not (Test-Path -LiteralPath $p)) { Add-Warning "enum source not found: $p (run tools\Decompile-Bannerlord.ps1)"; return @() }
    $src = [System.IO.File]::ReadAllText($p)
    $m = [regex]::Match($src, 'enum\s+' + $enumName + '\b[^{]*\{([^}]*)\}')
    if (-not $m.Success) { Add-Warning "enum $enumName not found in $relPath"; return @() }
    $names = @()
    foreach ($line in ($m.Groups[1].Value -split ',')) {
        $nm = [regex]::Match($line, '^\s*([A-Za-z_]\w*)')
        if ($nm.Success) { $names += $nm.Groups[1].Value }
    }
    return ,$names
}

$core = 'TaleWorlds.Core\TaleWorlds.Core\'
$enums = [ordered]@{
    PieceTypes = (Read-Enum ($core + 'CraftingPiece.cs') 'PieceTypes')
    DamageTypes = (Read-Enum ($core + 'DamageTypes.cs') 'DamageTypes')
    WeaponFlags = (Read-Enum ($core + 'WeaponFlags.cs') 'WeaponFlags')
    ItemFlags = (Read-Enum ($core + 'ItemFlags.cs') 'ItemFlags')
    ItemTypeEnum = (Read-Enum ($core + 'ItemObject.cs') 'ItemTypeEnum')
    WeaponClass = (Read-Enum ($core + 'WeaponClass.cs') 'WeaponClass')
    CraftingMaterials = (Read-Enum ($core + 'CraftingMaterials.cs') 'CraftingMaterials')
    CraftingStatTypes = (Read-Enum ($core + 'CraftingTemplate.cs') 'CraftingStatTypes')
}

# ---------------------------------------------------------------- crafting pieces, templates, weapon descriptions

Write-Host 'Reading crafting pieces...'
$pieceDefs = Collect-Defs 'CraftingPieces' @('CraftingPiece') $moduleList $false $true
Write-Host 'Reading crafting templates...'
$templateDefs = Collect-Defs 'CraftingTemplates' @('CraftingTemplate') $moduleList $false $true
Write-Host 'Reading weapon descriptions...'
$descDefs = Collect-Defs 'WeaponDescriptions' @('WeaponDescription') $moduleList $false $false
Write-Host 'Reading templates and weapon descriptions without RBM (vanilla)...'
$script:LogQuiet = $true
$vTemplateDefs = Collect-Defs 'CraftingTemplates' @('CraftingTemplate') $vanillaModules $false $true
$vDescDefs = Collect-Defs 'WeaponDescriptions' @('WeaponDescription') $vanillaModules $false $false
$script:LogQuiet = $false

$piecesOut = [ordered]@{}
$rbmPieceFiles = @{}
foreach ($entry in (Group-Defs $pieceDefs).GetEnumerator()) {
    $list = $entry.Value
    $rec = [ordered]@{ defs = @($list | ForEach-Object { Get-DefOut $_ }) }
    $campaign = $false
    foreach ($d in $list) { if ($d.campaign) { $campaign = $true }; if ($d.xsltChanged) { Add-Warning "piece '$($entry.Key)' was changed by an XSLT ($($d.xslt -join ', ')); the editor's vanilla view ignores that" } }
    if (-not $campaign) { $rec.nc = $true }
    $files = @($list | Where-Object { $_.rbm } | ForEach-Object { $_.rel } | Select-Object -Unique)
    if ($files.Count -gt 1) { Add-Warning "piece '$($entry.Key)' is defined in $($files.Count) RBM files ($($files -join ', ')); the export patches each" }
    $piecesOut[$entry.Key] = $rec
}

$templatesOut = [ordered]@{}
$vTemplates = Group-Defs $vTemplateDefs
foreach ($entry in (Group-Defs $templateDefs).GetEnumerator()) {
    $rec = [ordered]@{ defs = @($entry.Value | ForEach-Object { Get-DefOut $_ }) }
    if ($vTemplates.Contains($entry.Key)) { $rec.vdefs = @($vTemplates[$entry.Key] | ForEach-Object { Get-DefOut $_ }) } else { $rec.vdefs = @() }
    $templatesOut[$entry.Key] = $rec
}

# WeaponDescription.Deserialize on each definition in order: weapon_class / item_usage_features / rotated_in_hand /
# use_center_of_mass_as_hand_base are set again every time; WeaponFlags only ever accumulate (|=); AvailablePieces is
# replaced by the last definition that has one.
function Get-Descriptions($defs) {
    $out = [ordered]@{}
    foreach ($entry in (Group-Defs $defs).GetEnumerator()) {
        $flags = New-Object System.Collections.Generic.List[string]
        $d = [ordered]@{ cls = 'Undefined'; features = ''; rotated = $false; useCom = $false; flags = $flags; avail = $null; src = @() }
        foreach ($x in $entry.Value) {
            $el = $x.el
            $c = Get-Attr $el 'weapon_class'; if ($null -ne $c) { $d.cls = $c } else { $d.cls = 'Undefined' }
            $fe = Get-Attr $el 'item_usage_features'; if ($null -ne $fe) { $d.features = $fe } else { $d.features = '' }
            $d.rotated = ((Get-Attr $el 'rotated_in_hand') -ieq 'true')
            $d.useCom = ((Get-Attr $el 'use_center_of_mass_as_hand_base') -ieq 'true')
            foreach ($wf in $el.SelectNodes('WeaponFlags/*')) { $v = Get-Attr $wf 'value'; if ($null -ne $v -and -not $flags.Contains($v)) { $flags.Add($v) } }
            $ap = $el.SelectSingleNode('AvailablePieces')
            if ($null -ne $ap) {
                $list = New-Object System.Collections.Generic.List[string]
                foreach ($a in $ap.ChildNodes) { if ($a.NodeType -eq [System.Xml.XmlNodeType]::Element) { $v = Get-Attr $a 'id'; if ($null -ne $v) { $list.Add($v) } } }
                $d.avail = $list
            }
            $d.src += $x.src
        }
        if ($null -eq $d.avail) { $d.avail = @() }
        $out[$entry.Key] = $d
    }
    return $out
}
$descOut = [ordered]@{}
$descR = Get-Descriptions $descDefs
$descV = Get-Descriptions $vDescDefs
foreach ($k in $descR.Keys) {
    $rec = [ordered]@{ rbm = $descR[$k] }
    if ($descV.Contains($k)) { $rec.van = $descV[$k] } else { $rec.van = $null }
    $descOut[$k] = $rec
}

# ---------------------------------------------------------------- crafted items

Write-Host 'Reading crafted items...'
$itemDefs = Collect-Defs 'Items' @('CraftedItem') $moduleList $true $true
$rbmItemDefs = @{}
foreach ($module in $moduleList) {
    if (-not $module.rbm -or -not (Test-Path -LiteralPath $module.sub)) { continue }
    foreach ($f in (Get-XmlFiles $module 'Items')) {
        $rel = $module.repoDir + '/' + $f.name
        $text = [System.IO.File]::ReadAllText($f.file)
        foreach ($s in (Get-TopLevelSpanList $text @('CraftedItem'))) {
            if (-not $rbmItemDefs.ContainsKey($s.id)) { $rbmItemDefs[$s.id] = New-Object System.Collections.Generic.List[string] }
            if (-not $rbmItemDefs[$s.id].Contains($rel)) { $rbmItemDefs[$s.id].Add($rel) }
        }
    }
}
$items = New-Object System.Collections.Generic.List[object]
foreach ($d in $itemDefs) {
    $node = $d.el
    $culture = Get-Attr $node 'culture'
    if ($null -ne $culture) { $culture = $culture -replace '^Culture\.', '' } else { $culture = '' }
    $name = Remove-LocKey (Get-Attr $node 'name')
    if ($name -eq '') { $name = $d.id }
    $o = Get-DefOut $d
    $rec = [ordered]@{ id = $d.id; name = $name; culture = $culture; module = $d.module; src = $d.src; text = $o.text; indent = $o.indent }
    if ($d.sources.Count -gt 1) { $rec.sources = @($d.sources) }
    if (-not $d.campaign) { $rec.nc = $true }
    $b = $d.before
    while ($null -ne $b -and $b.rbm) { $b = $b.before }
    if ($d.rbm -and $null -ne $b) { $bo = Get-DefOut $b; $rec.vanText = $bo.text; $rec.vanFrom = $b.src }
    if ($rbmItemDefs.ContainsKey($d.id)) { $rec.rbmFiles = @($rbmItemDefs[$d.id]) }
    $origin = $d.sources[0].Split('/')[0]
    if ($origin -eq 'NavalDLC' -or $origin -eq 'RBM_WS') { $rec.naval = $true }
    $items.Add($rec)
}

# ---------------------------------------------------------------- RBM crafting files

$rbmFilesOut = @()
foreach ($module in $moduleList) {
    if (-not $module.rbm -or -not (Test-Path -LiteralPath $module.sub)) { continue }
    foreach ($xmlId in @('CraftingPieces', 'CraftingTemplates')) {
        foreach ($f in (Get-XmlFiles $module $xmlId)) {
            $rel = $module.repoDir + '/' + $f.name
            $tf = Read-TextFile $f.file
            $doc = New-Object System.Xml.XmlDocument
            $doc.LoadXml($tf.text)
            $tags = Get-RbmTags $doc
            $kind = 'CraftingPiece'
            if ($xmlId -eq 'CraftingTemplates') { $kind = 'CraftingTemplate' }
            $count = (Get-TopLevelSpanList $tf.text @($kind)).Count
            if (-not $tags.Contains('RBM_XML_TAG')) { Add-Warning "${rel}: no RBM_XML_TAG comment, so the game merges it like a plain file (the editor assumes RBM's append)" }
            $rbmFilesOut += [ordered]@{ path = $rel; xmlId = $xmlId; kind = $kind; module = $module.id; bom = $tf.bom; eol = $tf.eol; tags = @($tags.Keys)
                loads = ($f.campaign -and (Test-RbmFileLoads $tags)); naval = ($module.id -eq 'RBM_WS'); count = $count; text = $tf.text }
        }
    }
}

# ---------------------------------------------------------------- XSD vocabulary

Write-Host 'Reading XmlSchemas/CraftingPieces.xsd and CraftingTemplates.xsd...'
$vocab = [ordered]@{}
foreach ($xsdName in @('CraftingPieces', 'CraftingTemplates')) {
    $xsdPath = Join-Path $GameRoot ('XmlSchemas\' + $xsdName + '.xsd')
    if (-not (Test-Path -LiteralPath $xsdPath)) { Add-Warning "$xsdName.xsd not found: $xsdPath (no attribute vocabulary; every attribute is reported unknown)"; continue }
    $xd = New-Object System.Xml.XmlDocument
    $xd.Load($xsdPath)
    $ns = New-Object System.Xml.XmlNamespaceManager($xd.NameTable)
    $ns.AddNamespace('xs', $XsNs)
    foreach ($el in $xd.SelectNodes('//xs:element[@name]', $ns)) {
        # '/CraftingPieces/CraftingPiece/BladeData' -> 'CraftingPiece/BladeData'
        $path = (Get-XsdPath $el) -replace '^/[^/]+/', ''
        if ($path -match '^/') { continue }
        if (-not $vocab.Contains($path)) { $vocab[$path] = [ordered]@{} }
        foreach ($a in $el.SelectNodes('xs:complexType/xs:attribute', $ns)) {
            $an = $a.GetAttribute('name')
            if ($an -eq '' -or $vocab[$path].Contains($an)) { continue }
            $info = [ordered]@{ type = $a.GetAttribute('type'); use = $a.GetAttribute('use') }
            if ($info.type -eq '') {
                $r = $a.SelectSingleNode('xs:simpleType/xs:restriction', $ns)
                if ($null -ne $r) {
                    $info.type = $r.GetAttribute('base')
                    $pat = $r.SelectSingleNode('xs:pattern', $ns)
                    if ($null -ne $pat) { $info.pattern = $pat.GetAttribute('value') }
                }
            }
            if ($info.type -eq '') { $info.type = 'xs:string' }
            $vocab[$path][$an] = $info
        }
    }
}

# ---------------------------------------------------------------- lookups

Write-Host 'Reading cultures, modifier groups, holsters...'
function Collect-Simple([string]$xmlId, [string]$kind) {
    # Lookups only need the ids and names: no file log, no XSLT diff warnings.
    $script:LogQuiet = $true
    $script:XsltCheck = $false
    $r = Collect-Defs $xmlId @($kind) $moduleList $true $false
    $script:LogQuiet = $false
    $script:XsltCheck = $true
    return ,$r
}
$culturesOut = [ordered]@{}
foreach ($e in (Collect-Simple 'SPCultures' 'Culture')) {
    $cname = Remove-LocKey (Get-Attr $e.el 'name'); if ($cname -eq '') { $cname = $e.id }
    $culturesOut[$e.id] = [ordered]@{ name = $cname; campaign = [bool]$e.campaign }
}
foreach ($e in (Collect-Simple 'BasicCultures' 'Culture')) {
    if ($culturesOut.Contains($e.id)) { continue }
    $cname = Remove-LocKey (Get-Attr $e.el 'name'); if ($cname -eq '') { $cname = $e.id }
    $culturesOut[$e.id] = [ordered]@{ name = $cname; campaign = $false }
}

$modGroups = New-Object System.Collections.Generic.SortedSet[string]
foreach ($module in $moduleList) {
    if (-not (Test-Path -LiteralPath $module.sub)) { continue }
    foreach ($f in (Get-XmlFiles $module 'ItemModifierGroups')) {
        $doc = New-Object System.Xml.XmlDocument; $doc.Load($f.file)
        foreach ($g in $doc.SelectNodes('//ItemModifierGroup')) { $gid = Get-Attr $g 'id'; if ($null -ne $gid) { [void]$modGroups.Add($gid) } }
    }
    foreach ($f in (Get-XmlFiles $module 'ItemModifiers')) {
        # ItemModifier modifier_group="ItemModifierGroup.x" registers x as a presumed object too.
        $doc = New-Object System.Xml.XmlDocument; $doc.Load($f.file)
        foreach ($g in $doc.SelectNodes('//ItemModifier')) { $gid = Get-Attr $g 'modifier_group'; if ($null -ne $gid) { [void]$modGroups.Add(($gid -replace '^ItemModifierGroup\.', '')) } }
    }
}

$holsters = [ordered]@{}
$holsterFiles = @()
foreach ($mod in @('Native', 'SandBoxCore', 'SandBox', 'NavalDLC')) {
    $d = Join-Path $ModulesRoot ($mod + '\ModuleData')
    if (Test-Path -LiteralPath $d) { foreach ($hf in (Get-ChildItem -LiteralPath $d -Filter 'item_holsters*.xml')) { $holsterFiles += @{ path = $hf.FullName; src = $mod } } }
}
$holsterFiles += @{ path = (Join-Path $RepoRoot 'RBMXML\RBMCombat_item_holsters.xml'); src = 'RBM' }
foreach ($hf in $holsterFiles) {
    if (-not (Test-Path -LiteralPath $hf.path)) { Add-Warning "item holsters file not found: $($hf.path)"; continue }
    $doc = New-Object System.Xml.XmlDocument; $doc.Load($hf.path)
    foreach ($h in $doc.SelectNodes('//item_holster')) { $hid = Get-Attr $h 'id'; if ($null -ne $hid) { $holsters[$hid] = $hf.src } }
}

# ---------------------------------------------------------------- ground truth for the calculator check
# Vanilla crafted items sometimes carry the comment Crafting.GetXmlCodeForCurrentItem writes ("Length: 143 Weight:
# 1.68 ..."), the values the smithy computed when the item was authored. They are in the item text; the page and the
# node test compare the calculator against them (they may be stale when TaleWorlds changed a piece later).

# ---------------------------------------------------------------- write

$gameVersion = ''
$nativeSub = Join-Path $ModulesRoot 'Native\SubModule.xml'
if (Test-Path -LiteralPath $nativeSub) {
    $d = New-Object System.Xml.XmlDocument
    $d.Load($nativeSub)
    $v = $d.SelectSingleNode('/Module/Version')
    if ($null -ne $v -and $null -ne $v.Attributes['value']) { $gameVersion = $v.Attributes['value'].Value.Trim() }
}

$data = [ordered]@{
    generated = (Get-Date).ToString('yyyy-MM-dd HH:mm:ss', $Inv)
    gameVersion = $gameVersion
    navalDlc = $navalInstalled
    assumptions = 'RBM combat on (the RBM crafting files carry RBM_COMBAT_XML_TAG), RBM campaign on for the price. Campaign game type. RBM config defaults (ThrustMagnitudeModifier 0.05, armorMultiplier 2, weapon type factors, price multipliers).'
    pieces = $piecesOut
    templates = $templatesOut
    descriptions = $descOut
    items = $items
    rbmFiles = $rbmFilesOut
    vocab = $vocab
    enums = $enums
    cultures = $culturesOut
    modifierGroups = @($modGroups)
    holsters = $holsters
    files = $fileLog
    warnings = $script:Warnings
}

Write-Host 'Writing...'
$sb = New-Object System.Text.StringBuilder
[void]$sb.Append("// Generated by Build-CraftingData.ps1 - do not edit, do not commit (contains TaleWorlds text).`n")
[void]$sb.Append('window.CRAFTING_EDITOR_DATA = ')
Write-JsonValue $sb $data
[void]$sb.Append(";`n")
[System.IO.File]::WriteAllText($OutFile, $sb.ToString(), (New-Object System.Text.UTF8Encoding($false)))

$rbmPieces = 0
foreach ($k in $piecesOut.Keys) { foreach ($d in $piecesOut[$k].defs) { if ($d.Contains('rbm')) { $rbmPieces++; break } } }
$rbmTemplates = 0
foreach ($k in $templatesOut.Keys) { foreach ($d in $templatesOut[$k].defs) { if ($d.Contains('rbm')) { $rbmTemplates++; break } } }
$campaignItems = 0
foreach ($it in $items) { if (-not $it.Contains('nc')) { $campaignItems++ } }
Write-Host ''
Write-Host ("Pieces:    {0} ({1} defined again in an RBM file)" -f $piecesOut.Count, $rbmPieces)
Write-Host ("Templates: {0} ({1} defined again in an RBM file)" -f $templatesOut.Count, $rbmTemplates)
Write-Host ("Weapon descriptions: {0}" -f $descOut.Count)
Write-Host ("Crafted items: {0} ({1} loaded in campaigns)" -f $items.Count, $campaignItems)
Write-Host ("RBM crafting files: {0}" -f $rbmFilesOut.Count)
Write-Host ("Lookups:   {0} cultures, {1} modifier groups, {2} holsters" -f $culturesOut.Count, $modGroups.Count, $holsters.Count)
Write-Host ("Game:      {0}{1}" -f $gameVersion, $(if ($navalInstalled) { ' + War Sails' } else { '' }))
Write-Host ("Warnings:  {0}" -f $script:Warnings.Count)
Write-Host ("Wrote {0} ({1:N0} KB) in {2:N1} s" -f $OutFile, ((Get-Item -LiteralPath $OutFile).Length / 1024), $Clock.Elapsed.TotalSeconds)
