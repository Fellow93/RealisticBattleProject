<#
.SYNOPSIS
    Builds item-editor-data.js for the item editor (index.html in this folder).

.DESCRIPTION
    Collects, without running the game:
      - every item (Item and CraftedItem) from the Items XML of Native, SandBoxCore, SandBox, StoryMode, CustomBattle,
        NavalDLC (when installed) and RBM's own XML in this repo (RBMXML, plus RBM_WS_XML when NavalDLC is installed),
        found through each module's SubModule.xml and merged in module order the way the game does it: plain files
        through MBObjectManager.MergeElements (keyed merge), RBM's files through RBM/XmlLoadingPatches.cs
        MergeTwoXmlsPatch (an RBM Item/CraftedItem REPLACES the earlier one of the same kind and id). Assumes RBM combat
        AND campaign on, so RBM_COMBAT_ONLY files (RBMCombat_ranged.xml) are skipped and RBM_ECONOMY_COMBAT ones load.
        Per item: the final element's exact text (re-formatted when several plain files merged into it), the
        definition before any RBM file replaced it, the merge chain and every RBM file that defines the id;
      - the full text of every RBM item file (BOM and line ends recorded), which the page splices on export;
      - the attribute vocabulary of XmlSchemas/Items.xsd and the enums the game parses those attributes with
        (from decompiled/), for the "All attributes" editor and the problem checks;
      - lookups: cultures, crafting pieces, crafting templates (piece types, usable pieces), item modifier groups,
        item categories (created in code), item holsters, monsters, item usage sets.

    The output contains TaleWorlds text and is gitignored. Windows PowerShell 5.1 compatible.

.PARAMETER ModulesRoot
    The game's Modules folder. Defaults to the folder the repo sits in.

.EXAMPLE
    powershell -ExecutionPolicy Bypass -File tools\ItemEditor\Build-ItemData.ps1
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
$OutFile = Join-Path $PSScriptRoot 'item-editor-data.js'
$GameRoot = (Resolve-Path (Join-Path $ModulesRoot '..')).Path

$script:Warnings = New-Object System.Collections.Generic.List[string]
function Add-Warning([string]$message) {
    $script:Warnings.Add($message)
    Write-Warning $message
}

# ---------------------------------------------------------------- JSON writer
# Hand-rolled like the troop editors' scripts (PS 5.1's ConvertTo-Json is slow and culture-dependent). Strings are
# escaped with String.Replace instead of a per-character loop: the item text is several MB.

$script:CtrlRe = New-Object System.Text.RegularExpressions.Regex('[\x00-\x08\x0B\x0C\x0E-\x1F]')
function Write-JsonString([System.Text.StringBuilder]$sb, [string]$s) {
    $e = $s.Replace('\', '\\').Replace('"', '\"').Replace("`r", '\r').Replace("`n", '\n').Replace("`t", '\t')
    # '<' as < keeps "</script>" out of the generated file; U+2028/9 are line ends in older JS parsers.
    $e = $e.Replace('<', '<').Replace([string][char]0x2028, ' ').Replace([string][char]0x2029, ' ')
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

function Test-True($value) { return ($null -ne $value -and $value.Trim() -ieq 'true') }

function Get-Number($value) {
    if ($null -eq $value) { return $null }
    $d = 0.0
    if ([double]::TryParse($value.Trim(), [System.Globalization.NumberStyles]::Float, $Inv, [ref]$d)) { return $d }
    return $null
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

# Top-level child elements of the root named $elementName, by id (the last one when an id repeats), with their exact
# source text and indentation. Comment, CDATA and PI aware (a commented-out item is not an item).
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

# MergeTwoXmlsPatch with combat + campaign on: only RBM_COMBAT_ONLY files are dropped (RBM_COMBAT, RBM_CAMPAIGN and
# RBM_ECONOMY_COMBAT files load; no item file carries RBM_COMBAT_OVERHAUL).
function Test-RbmFileLoads($tags) {
    if ($tags.Contains('RBM_COMBAT_ONLY_XML_TAG')) { return $false }
    return $true
}

$fileLog = New-Object System.Collections.Generic.List[object]
function Add-FileLog($module, $f, [string]$kind, [int]$count, [string]$note) {
    $fileLog.Add([ordered]@{ module = $module.id; file = $f.name; kind = $kind; campaign = [bool]$f.campaign; types = $f.types; count = $count; note = $note })
    Write-Host ("  {0,-12} {1,-18} {2,-44} {3,5}{4}" -f $module.id, $kind, $f.name, $count, $(if ($note) { '  (' + $note + ')' } else { '' }))
}

# ---------------------------------------------------------------- merging, as the game does it
# (copied from ../TroopLoadoutEditor/Build-TroopLoadoutData.ps1; see its comments)

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
    # The merge-only flag has done its job; it means nothing in an RBM file (MergeTwoXmlsPatch never merges).
    $copy = $el.CloneNode($true)
    foreach ($x in @($copy.SelectNodes('descendant-or-self::*[@_replaceWhileMerging]'))) { $x.RemoveAttribute('_replaceWhileMerging') }
    $sw = New-Object System.IO.StringWriter
    $w = [System.Xml.XmlWriter]::Create($sw, $settings)
    $copy.WriteTo($w)
    $w.Flush()
    return $sw.ToString()
}

$script:Order = 0
function Invoke-XsltOnStore([System.Xml.XmlDocument]$store, [string]$xsltPath, $entries, [string]$src) {
    # MBObjectManager.ApplyXslt over the definitions merged so far; then re-point every entry at its transformed
    # element, matched by element name and id in document order.
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
        if ($null -ne $e.el) {
            if ($map.ContainsKey($e.el)) {
                $new = $map[$e.el]
                if ($new.OuterXml -ne $e.el.OuterXml) { $e.sources += $src; $e.merged = $true; $e.raw = $null; $changed++ }
                $e.el = $new
            }
            else { $removed += $key }
        }
    }
    foreach ($key in $removed) { Add-Warning "${src}: removes $key; the editor drops it"; $entries.Remove($key) }
    foreach ($q in $queues.Values) {
        foreach ($node in $q) { Add-Warning "${src}: adds $($node.LocalName) '$(Get-Attr $node 'id')'; not read by the editor" }
    }
    $script:XsltChanged = $changed
    return ,$out   # the comma: an XmlDocument is enumerable and would be unrolled into its child nodes
}

# Collects the elements of one XML id from every module: key "kind|id" -> entry
# { kind, id, el (merged XmlElement), module, file, campaign, sources, alsoIn, merged, raw, rawIndent, rbm, rel, order,
#   before (the entry an RBM definition replaced) }.
# A file not loaded in campaigns never changes a campaign definition (it only adds ids no campaign file has).
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
                $rbmXml = $tags.Contains('RBM_XML_TAG')
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
                    order = $script:Order; before = $null }
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
                    # An RBM file (or a second definition in one file) replaces the earlier element outright.
                    $new.sources = @($prev.sources) + $src
                    $new.alsoIn = @($prev.alsoIn)
                    $new.before = $prev
                    if ($null -ne $prev.el -and $null -ne $prev.el.ParentNode) { [void]$root.RemoveChild($prev.el) }
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

# The exact text of an entry's element (its source text, or the merged element re-formatted) and its indentation.
function Get-EntryText($e) {
    if ($e.merged -or $null -eq $e.raw) {
        return @{ text = ((Format-Element $e.el) -replace "`n", "`n`t"); indent = "`t"; merged = $true }
    }
    return @{ text = $e.raw; indent = $e.rawIndent; merged = $false }
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
    ItemTypeEnum = (Read-Enum ($core + 'ItemObject.cs') 'ItemTypeEnum')
    WeaponClass = (Read-Enum ($core + 'WeaponClass.cs') 'WeaponClass')
    DamageTypes = (Read-Enum ($core + 'DamageTypes.cs') 'DamageTypes')
    ArmorMaterialTypes = (Read-Enum ($core + 'ArmorComponent.cs') 'ArmorMaterialTypes')
    HairCoverTypes = (Read-Enum ($core + 'ArmorComponent.cs') 'HairCoverTypes')
    BeardCoverTypes = (Read-Enum ($core + 'ArmorComponent.cs') 'BeardCoverTypes')
    HorseHarnessCoverTypes = (Read-Enum ($core + 'ArmorComponent.cs') 'HorseHarnessCoverTypes')
    HorseTailCoverTypes = (Read-Enum ($core + 'ArmorComponent.cs') 'HorseTailCoverTypes')
    PieceTypes = (Read-Enum ($core + 'CraftingPiece.cs') 'PieceTypes')
    ItemFlags = (Read-Enum ($core + 'ItemFlags.cs') 'ItemFlags')
    WeaponFlags = (Read-Enum ($core + 'WeaponFlags.cs') 'WeaponFlags')
}
# WeaponComponentData.GetItemTypeFromWeaponClass: an Item with a Type attribute and a weapon gets the first weapon's
# class's type (ItemObject.Deserialize overrides Type with WeaponComponent.GetItemType()).
$ClassType = @{
    Dagger = 'OneHandedWeapon'; OneHandedSword = 'OneHandedWeapon'; OneHandedAxe = 'OneHandedWeapon'; Mace = 'OneHandedWeapon'
    TwoHandedSword = 'TwoHandedWeapon'; TwoHandedAxe = 'TwoHandedWeapon'; Pick = 'TwoHandedWeapon'; TwoHandedMace = 'TwoHandedWeapon'
    OneHandedPolearm = 'Polearm'; TwoHandedPolearm = 'Polearm'; LowGripPolearm = 'Polearm'
    Arrow = 'Arrows'; Bolt = 'Bolts'; SlingStone = 'SlingStones'; Cartridge = 'Bullets'; Bow = 'Bow'; Crossbow = 'Crossbow'; Sling = 'Sling'
    Stone = 'Thrown'; Boulder = 'Thrown'; ThrowingAxe = 'Thrown'; ThrowingKnife = 'Thrown'; Javelin = 'Thrown'; BallistaBoulder = 'Thrown'; BallistaStone = 'Thrown'
    Pistol = 'Pistol'; Musket = 'Musket'; SmallShield = 'Shield'; LargeShield = 'Shield'; Banner = 'Banner'
}

# ---------------------------------------------------------------- crafting templates (item type of crafted items)

Write-Host 'Reading crafting templates and pieces...'
$templateEntries = Collect-Merged 'CraftingTemplates' @('CraftingTemplate') $false
$templatesOut = [ordered]@{}
$usable = @{}
foreach ($module in $moduleList) {
    # A template defined again (RBM's RBMCombat_no_bastard_axes) is deserialized again: its usable pieces accumulate.
    if (-not (Test-Path -LiteralPath $module.sub)) { continue }
    foreach ($f in (Get-XmlFiles $module 'CraftingTemplates')) {
        $doc = New-Object System.Xml.XmlDocument
        $doc.Load($f.file)
        foreach ($ct in $doc.SelectNodes('//CraftingTemplate')) {
            $cid = Get-Attr $ct 'id'
            if ($null -eq $cid) { continue }
            if (-not $usable.ContainsKey($cid)) { $usable[$cid] = New-Object System.Collections.Generic.HashSet[string] }
            foreach ($up in $ct.SelectNodes('UsablePieces/UsablePiece')) { $pieceId = Get-Attr $up 'piece_id'; if ($null -ne $pieceId) { [void]$usable[$cid].Add($pieceId) } }
        }
    }
}
foreach ($e in $templateEntries.Values) {
    $ct = $e.el
    if (-not $usable.ContainsKey($e.id)) { $usable[$e.id] = New-Object System.Collections.Generic.HashSet[string] }
    foreach ($up in $ct.SelectNodes('UsablePieces/UsablePiece')) { $pieceId = Get-Attr $up 'piece_id'; if ($null -ne $pieceId) { [void]$usable[$e.id].Add($pieceId) } }
    $ptypes = @()
    foreach ($pd in $ct.SelectNodes('PieceDatas/PieceData')) { $pt = Get-Attr $pd 'piece_type'; if ($null -ne $pt) { $ptypes += $pt } }
    $it = Get-Attr $ct 'item_type'; if ($null -eq $it) { $it = '' }
    $mg = Get-Attr $ct 'modifier_group'; if ($null -eq $mg) { $mg = '' }
    $templatesOut[$e.id] = [ordered]@{ itemType = $it; modifierGroup = $mg; pieceTypes = $ptypes; usable = @($usable[$e.id] | Sort-Object) }
}

$piecesOut = [ordered]@{}
foreach ($e in (Collect-Merged 'CraftingPieces' @('CraftingPiece') $false).Values) {
    $p = $e.el
    $pc = Get-Attr $p 'culture'; if ($null -ne $pc) { $pc = $pc -replace '^Culture\.', '' } else { $pc = '' }
    $rec = [ordered]@{ type = (Get-Attr $p 'piece_type'); name = (Remove-LocKey (Get-Attr $p 'name')); culture = $pc }
    $len = Get-Number (Get-Attr $p 'length'); if ($null -ne $len) { $rec.len = $len }
    $wt = Get-Number (Get-Attr $p 'weight'); if ($null -ne $wt) { $rec.weight = $wt }
    $tr = Get-Number (Get-Attr $p 'tier'); if ($null -ne $tr) { $rec.tier = $tr }
    if (-not $e.campaign) { $rec.nc = $true }
    if (Test-True (Get-Attr $p 'is_hidden')) { $rec.hidden = $true }
    $piecesOut[$e.id] = $rec
}

# ---------------------------------------------------------------- items

Write-Host 'Reading items...'
$itemEntries = Collect-Merged 'Items' @('Item', 'CraftedItem') $true

# RBM item files: every file registered as Items in RBMXML/RBM_WS_XML, loaded or not.
$rbmFilesOut = @()
$rbmDefs = @{}   # "kind|id" -> list of RBM file paths that define it
foreach ($module in $moduleList) {
    if (-not $module.rbm -or -not (Test-Path -LiteralPath $module.sub)) { continue }
    foreach ($f in (Get-XmlFiles $module 'Items')) {
        $rel = $module.repoDir + '/' + $f.name
        $tf = Read-TextFile $f.file
        $doc = New-Object System.Xml.XmlDocument
        $doc.LoadXml($tf.text)
        $tags = Get-RbmTags $doc
        $loads = ($f.campaign -and (Test-RbmFileLoads $tags))
        $ids = 0
        foreach ($k in @('Item', 'CraftedItem')) {
            $s = Get-TopLevelSpans $tf.text $k
            foreach ($sid in $s.Keys) {
                $key = $k + '|' + $sid
                if (-not $rbmDefs.ContainsKey($key)) { $rbmDefs[$key] = New-Object System.Collections.Generic.List[string] }
                if (-not $rbmDefs[$key].Contains($rel)) { $rbmDefs[$key].Add($rel) }
                $ids++
            }
        }
        if (-not $tags.Contains('RBM_XML_TAG')) { Add-Warning "${rel}: no RBM_XML_TAG comment, so the game merges it like a plain file (the editor assumes replacement)" }
        $rbmFilesOut += [ordered]@{ path = $rel; module = $module.id; bom = $tf.bom; eol = $tf.eol; tags = @($tags.Keys); loadsInCampaign = $loads
            gameTypes = $f.types; naval = ($module.id -eq 'RBM_WS'); count = $ids; text = $tf.text }
    }
}

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

$items = New-Object System.Collections.Generic.List[object]
$listed = @{}
$rbmItemCount = 0
foreach ($e in ($itemEntries.Values | Sort-Object { $_.order })) {
    if ($byItemId[$e.id] -ne $e) { continue }
    $node = $e.el; $kind = $e.kind; $id = $e.id
    $where = $e.sources -join ' > '
    $culture = Get-Attr $node 'culture'
    if ($null -ne $culture) { $culture = $culture -replace '^Culture\.', '' } else { $culture = '' }
    $name = Remove-LocKey (Get-Attr $node 'name')
    if ($name -eq '') { $name = $id }
    $type = ''
    if ($kind -eq 'CraftedItem') {
        $tpl = Get-Attr $node 'crafting_template'
        if ($null -ne $tpl -and $templatesOut.Contains($tpl)) { $type = $templatesOut[$tpl].itemType }
    }
    else {
        $t = Get-Attr $node 'Type'
        if ($null -ne $t) {
            # ItemObject.Deserialize: Enum.Parse(ItemTypeEnum, ignoreCase: true), then a weapon's class overrides it.
            foreach ($canon in $enums.ItemTypeEnum) { if ($canon -ieq $t) { $type = $canon; break } }
            $w0 = $node.SelectSingleNode('ItemComponent/Weapon')
            if ($null -ne $w0) {
                $wc = Get-Attr $w0 'weapon_class'
                if ($null -ne $wc -and $ClassType.ContainsKey($wc)) { $type = $ClassType[$wc] }
            }
        }
    }
    $txt = Get-EntryText $e
    $rec = [ordered]@{ id = $id; kind = $kind; type = $type; name = $name; culture = $culture; module = $e.module; file = $e.file }
    if ($e.sources.Count -gt 1) { $rec.sources = @($e.sources) }
    if (-not $e.campaign) { $rec.nc = $true }
    if ($e.alsoIn.Count -gt 0) { $rec.alsoIn = @($e.alsoIn) }
    $rec.raw = $txt.text
    $rec.rawIndent = $txt.indent
    if ($txt.merged) { $rec.rawMerged = $true }
    # The definition before RBM: the last non-RBM entry in the replacement chain.
    $b = $e.before
    while ($null -ne $b -and $b.rbm) { $b = $b.before }
    if ($e.rbm -and $null -ne $b) {
        $nt = Get-EntryText $b
        $rec.nativeRaw = $nt.text
        $rec.nativeIndent = $nt.indent
        $rec.nativeFrom = ($b.sources -join ' > ')
        if ($nt.merged) { $rec.nativeMerged = $true }
    }
    $origin = $e.sources[0].Split('/')[0]
    if ($origin -eq 'NavalDLC' -or $origin -eq 'RBM_WS') { $rec.naval = $true }
    $key = $kind + '|' + $id
    if ($rbmDefs.ContainsKey($key)) { $rec.rbm = @($rbmDefs[$key]); $rbmItemCount++ }
    if ($e.rbm -and $kind -eq 'Item' -and $null -eq (Get-Attr $node 'Type')) {
        Add-Warning "$id ($where): RBM Item without a Type attribute (MergeTwoXmlsPatch reads it with betterArrowVisuals on, the default, and throws)"
    }
    $items.Add($rec)
    $listed[$key] = $true
}
foreach ($key in ($rbmDefs.Keys | Sort-Object)) {
    if (-not $listed.ContainsKey($key)) {
        $id = $key.Split('|')[1]
        Add-Warning "$key is only defined in $($rbmDefs[$key] -join ', '), which does not load in campaigns: not listed"
    }
}

# ---------------------------------------------------------------- XSD vocabulary

Write-Host 'Reading XmlSchemas/Items.xsd...'
$vocab = [ordered]@{}
$xsdPath = Join-Path $GameRoot 'XmlSchemas\Items.xsd'
if (Test-Path -LiteralPath $xsdPath) {
    $xd = New-Object System.Xml.XmlDocument
    $xd.Load($xsdPath)
    $ns = New-Object System.Xml.XmlNamespaceManager($xd.NameTable)
    $ns.AddNamespace('xs', $XsNs)
    $named = @{}
    foreach ($st in $xd.SelectNodes('/xs:schema/xs:simpleType', $ns)) {
        $r = $st.SelectSingleNode('xs:restriction', $ns)
        if ($null -ne $r) { $named[$st.GetAttribute('name')] = $r.GetAttribute('base') }
    }
    foreach ($el in $xd.SelectNodes('//xs:element[@name]', $ns)) {
        $ename = $el.GetAttribute('name')
        if (@('Item', 'CraftedItem', 'Armor', 'Weapon', 'Horse', 'Trade', 'Banner', 'Flags', 'WeaponFlags', 'Piece') -notcontains $ename) { continue }
        if (-not $vocab.Contains($ename)) { $vocab[$ename] = [ordered]@{} }
        foreach ($a in $el.SelectNodes('xs:complexType/xs:attribute', $ns)) {
            $an = $a.GetAttribute('name')
            if ($an -eq '' -or $vocab[$ename].Contains($an)) { continue }
            $info = [ordered]@{ type = $a.GetAttribute('type'); use = $a.GetAttribute('use') }
            if ($info.type -eq '') {
                $r = $a.SelectSingleNode('xs:simpleType/xs:restriction', $ns)
                if ($null -ne $r) {
                    $info.type = $r.GetAttribute('base')
                    $pat = $r.SelectSingleNode('xs:pattern', $ns)
                    if ($null -ne $pat) { $info.pattern = $pat.GetAttribute('value') }
                    $ev = @()
                    foreach ($en in $r.SelectNodes('xs:enumeration', $ns)) { $ev += $en.GetAttribute('value') }
                    if ($ev.Count -gt 0) { $info.values = $ev }
                    $min = $r.SelectSingleNode('xs:minInclusive', $ns); if ($null -ne $min) { $info.min = $min.GetAttribute('value') }
                    $max = $r.SelectSingleNode('xs:maxInclusive', $ns); if ($null -ne $max) { $info.max = $max.GetAttribute('value') }
                }
            }
            elseif ($named.ContainsKey($info.type)) { $info.named = $info.type; $info.type = $named[$info.type] }
            if ($info.type -eq '') { $info.type = 'xs:string' }
            $vocab[$ename][$an] = $info
        }
    }
}
else { Add-Warning "Items.xsd not found: $xsdPath (no attribute vocabulary; every attribute is reported unknown)" }

# ---------------------------------------------------------------- lookups

Write-Host 'Reading cultures, modifier groups, categories, holsters, monsters, usages...'
$culturesOut = [ordered]@{}
foreach ($e in (Collect-Merged 'SPCultures' @('Culture') $false).Values) {
    $cname = Remove-LocKey (Get-Attr $e.el 'name'); if ($cname -eq '') { $cname = $e.id }
    $culturesOut[$e.id] = [ordered]@{ name = $cname; campaign = [bool]$e.campaign }
}
foreach ($e in (Collect-Merged 'BasicCultures' @('Culture') $false).Values) {
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

# Item categories are created in code: DefaultItemCategories, War Sails' NavalItemCategories, RBMCampaign's
# TradeGoodCategories (campaign on).
$categories = [ordered]@{}
foreach ($pair in @(@(($core + 'DefaultItemCategories.cs'), 'DefaultItemCategories'), @('NavalDLC\NavalDLC\NavalItemCategories.cs', 'NavalItemCategories'))) {
    $p = Join-Path $Decompiled $pair[0]
    if (-not (Test-Path -LiteralPath $p)) { if ($pair[1] -eq 'DefaultItemCategories' -or $navalInstalled) { Add-Warning "item categories source not found: $p" }; continue }
    foreach ($m in [regex]::Matches([System.IO.File]::ReadAllText($p), '\bCreate\("([^"]+)"\)')) { $categories[$m.Groups[1].Value] = $pair[1] }
}
$tgc = Join-Path $RepoRoot 'RBMCampaign\Economy\TradeGoodCategories.cs'
if (Test-Path -LiteralPath $tgc) {
    $bm = [regex]::Match([System.IO.File]::ReadAllText($tgc), 'string\[\]\s+Ids\s*=\s*\{([^}]*)\}')
    if ($bm.Success) { foreach ($m in [regex]::Matches($bm.Groups[1].Value, '"([^"]+)"')) { $categories[$m.Groups[1].Value] = 'RBMCampaign TradeGoodCategories' } }
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

$monsters = [ordered]@{}
foreach ($module in $moduleList) {
    if ($module.rbm -or -not (Test-Path -LiteralPath $module.sub)) { continue }
    foreach ($f in (Get-XmlFiles $module 'Monsters')) {
        $doc = New-Object System.Xml.XmlDocument; $doc.Load($f.file)
        foreach ($mn in $doc.SelectNodes('//Monster')) {
            $mid = Get-Attr $mn 'id'
            if ($null -eq $mid) { continue }
            $hp = Get-Number (Get-Attr $mn 'hit_points')
            $monsters[$mid] = $(if ($null -ne $hp) { [int]$hp } else { $null })
        }
    }
}

$usages = New-Object System.Collections.Generic.SortedSet[string]
foreach ($mod in @('Native', 'SandBoxCore', 'NavalDLC')) {
    $d = Join-Path $ModulesRoot ($mod + '\ModuleData')
    if (-not (Test-Path -LiteralPath $d)) { continue }
    foreach ($uf in (Get-ChildItem -LiteralPath $d -Filter 'item_usage_sets*.xml')) {
        foreach ($m in [regex]::Matches([System.IO.File]::ReadAllText($uf.FullName), '<item_usage_set\s+id\s*=\s*"([^"]+)"')) { [void]$usages.Add($m.Groups[1].Value) }
    }
}
if ($usages.Count -eq 0) { Add-Warning 'no item usage sets found (Native/ModuleData/item_usage_sets.xml)' }

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
    assumptions = 'RBM combat and campaign on (RBM_COMBAT_ONLY item files skipped, RBM_ECONOMY_COMBAT ones loaded); betterArrowVisuals not applied (it only copies mesh to flying_mesh at load). Campaign game type. Price multipliers: RBM config defaults (armor 1, weapon 1, horse 0.2).'
    items = $items
    rbmFiles = $rbmFilesOut
    vocab = $vocab
    enums = $enums
    templates = $templatesOut
    pieces = $piecesOut
    cultures = $culturesOut
    modifierGroups = @($modGroups)
    itemCategories = $categories
    holsters = $holsters
    monsters = $monsters
    itemUsages = @($usages)
    files = $fileLog
    warnings = $script:Warnings
}

Write-Host 'Writing...'
$sb = New-Object System.Text.StringBuilder
[void]$sb.Append("// Generated by Build-ItemData.ps1 - do not edit, do not commit (contains TaleWorlds text).`n")
[void]$sb.Append('window.ITEM_EDITOR_DATA = ')
Write-JsonValue $sb $data
[void]$sb.Append(";`n")
[System.IO.File]::WriteAllText($OutFile, $sb.ToString(), (New-Object System.Text.UTF8Encoding($false)))

$campaignItems = 0
$crafted = 0
foreach ($it in $items) { if (-not $it.Contains('nc')) { $campaignItems++ }; if ($it.kind -eq 'CraftedItem') { $crafted++ } }
Write-Host ''
Write-Host ("Items:    {0} ({1} loaded in campaigns, {2} crafted, {3} defined in an RBM item file)" -f $items.Count, $campaignItems, $crafted, $rbmItemCount)
Write-Host ("RBM item files: {0}" -f $rbmFilesOut.Count)
Write-Host ("Lookups:  {0} cultures, {1} crafting pieces, {2} templates, {3} modifier groups, {4} item categories, {5} holsters, {6} monsters, {7} item usages" -f `
    $culturesOut.Count, $piecesOut.Count, $templatesOut.Count, $modGroups.Count, $categories.Count, $holsters.Count, $monsters.Count, $usages.Count)
Write-Host ("Game:     {0}{1}" -f $gameVersion, $(if ($navalInstalled) { ' + War Sails' } else { '' }))
Write-Host ("Warnings: {0}" -f $script:Warnings.Count)
Write-Host ("Wrote {0} ({1:N0} KB) in {2:N1} s" -f $OutFile, ((Get-Item -LiteralPath $OutFile).Length / 1024), $Clock.Elapsed.TotalSeconds)
