<#
.SYNOPSIS
    Builds troop-perk-data.js for the troop-perk editor (index.html in this folder).

.DESCRIPTION
    Collects, without running the game:
      - every perk from the decompiled DefaultPerks.cs (and War Sails' NavalPerks.cs when present), with its
        roles, bonuses and the description text filled in the way PerkObject.Initialize does it
        (StringHelpers.GetEffectIncrementTypeBonusText);
      - every non-hero NPCCharacter from the XML of Native, SandBoxCore, SandBox, StoryMode, CustomBattle,
        NavalDLC (when installed) and RBM's own XML in this repo (RBMXML, plus RBM_WS_XML when NavalDLC is
        installed), found through each module's SubModule.xml; later modules override earlier ones by id;
      - the current RBMXML/rbm_troop_perks.xml, so the editor opens with the existing mapping;
      - the troop-perk audit, audit/*.json in this folder (files starting with '_' are skipped): each perk gets
        an `audit` object (verdict, confidence, summary, conditions, rbm, sites, group). The audit is maintained
        by hand (or by an agent); this script only merges it and warns about ids it cannot match, personal perks
        with no audit entry, and disagreements with TroopPerks.cs NoTroopEffectPerkIds.

    The output contains TaleWorlds text and is gitignored. Windows PowerShell 5.1 compatible.

.PARAMETER ModulesRoot
    The game's Modules folder. Defaults to the folder two levels above this one's parent (the repo sits in it).

.EXAMPLE
    powershell -ExecutionPolicy Bypass -File tools\TroopPerkEditor\Build-TroopPerkData.ps1
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
$OutFile = Join-Path $PSScriptRoot 'troop-perk-data.js'

$script:Warnings = New-Object System.Collections.Generic.List[string]
function Add-Warning([string]$message) {
    $script:Warnings.Add($message)
    Write-Warning $message
}

# ---------------------------------------------------------------- JSON writer
# Hand-rolled: PS 5.1's ConvertTo-Json is slow, wraps arrays in {value,Count} under some type data, and
# formats numbers with the current culture.

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

function Split-CsArgs([string]$text) {
    # Splits a C# argument list on top-level commas (outside string literals and brackets).
    $parts = New-Object System.Collections.Generic.List[string]
    $sb = New-Object System.Text.StringBuilder
    $depth = 0
    $inString = $false
    foreach ($ch in $text.ToCharArray()) {
        if ($inString) {
            [void]$sb.Append($ch)
            if ($ch -eq '"') { $inString = $false }
            continue
        }
        if ($ch -eq '"') { $inString = $true; [void]$sb.Append($ch); continue }
        if ($ch -eq '(' -or $ch -eq '[' -or $ch -eq '{') { $depth++ }
        elseif ($ch -eq ')' -or $ch -eq ']' -or $ch -eq '}') { $depth-- }
        if ($ch -eq ',' -and $depth -eq 0) {
            $parts.Add($sb.ToString().Trim())
            [void]$sb.Clear()
            continue
        }
        [void]$sb.Append($ch)
    }
    if ($sb.Length -gt 0) { $parts.Add($sb.ToString().Trim()) }
    return ,$parts
}

function Get-CsString([string]$arg) {
    $a = $arg.Trim()
    if ($a.Length -ge 2 -and $a.StartsWith('"') -and $a.EndsWith('"')) { return $a.Substring(1, $a.Length - 2) }
    return $null
}

function Get-EnumTail([string]$arg) {
    # "PartyRole.Personal" -> "Personal"; "TroopUsageFlags.A | TroopUsageFlags.B" -> "A|B"
    $names = @()
    foreach ($piece in ($arg -split '\|')) {
        $p = $piece.Trim()
        if ($p -eq '') { continue }
        $dot = $p.LastIndexOf('.')
        if ($dot -ge 0) { $p = $p.Substring($dot + 1) }
        $names += $p
    }
    return ($names -join '|')
}

function ConvertTo-Bonus([string]$arg, [string]$context) {
    $a = $arg.Trim() -replace '[fFdDmM]$', ''
    $result = 0.0
    if ([double]::TryParse($a, [System.Globalization.NumberStyles]::Float, $Inv, [ref]$result)) { return $result }
    Add-Warning "$context : could not parse bonus '$arg', using 0"
    return 0.0
}

# StringHelpers.GetEffectIncrementTypeBonusText: AddFactor -> x100, "{0:0.#}", "+" when the bonus is > 0.
function Get-BonusText([double]$bonus, [string]$incrementType) {
    $num = $bonus
    if ($incrementType -eq 'AddFactor') { $num = [double]([single]$bonus * [single]100) }
    $text = $num.ToString('0.#', $Inv)
    if ($bonus -gt 0) { return '+' + $text }
    return $text
}

function Format-PerkText([string]$description, [double]$bonus, [string]$incrementType, [string]$context) {
    $text = Remove-LocKey $description
    if ($text -eq '') { return '' }
    $text = $text.Replace('{VALUE}', (Get-BonusText $bonus $incrementType))
    $text = $text.Replace('{newline}', "`n")
    if ($text -match '\{[^}]+\}') { Add-Warning "$context : unresolved placeholder in '$text'" }
    return $text
}

# ---------------------------------------------------------------- skills

function Read-SkillClass([string]$path, [string]$className, $skillMap) {
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
        $skillMap[$className + '.' + $m.Groups[1].Value] = [ordered]@{ id = $id; name = $name }
    }
}

$skillMap = @{}
Read-SkillClass (Join-Path $Decompiled 'TaleWorlds.Core\TaleWorlds.Core\DefaultSkills.cs') 'DefaultSkills' $skillMap
$navalSkillsPath = Join-Path $Decompiled 'NavalDLC\NavalDLC.CharacterDevelopment\NavalSkills.cs'
if (Test-Path -LiteralPath $navalSkillsPath) { Read-SkillClass $navalSkillsPath 'NavalSkills' $skillMap }

# ---------------------------------------------------------------- loader's no-effect list (from TroopPerks.cs)
# NoTroopEffectPerkIds: { "PerkId", NoEffectReason.X } entries; the loader logs these perks as doing nothing for
# a troop. Kept here only to cross-check it against the audit (the audit verdict is what the editor shows).

$reasonToVerdict = @{ HeroOnly = 'hero-only'; CampaignOnly = 'campaign-only'; NoCheckFound = 'no-check-found'; RbmBypassed = 'rbm-bypassed' }
$noEffect = @{}
$troopPerksCs = Join-Path $RepoRoot 'RBMConfig\Shared\TroopPerks.cs'
if (Test-Path -LiteralPath $troopPerksCs) {
    $src = [System.IO.File]::ReadAllText($troopPerksCs)
    $m = [regex]::Match($src, '(?s)NoTroopEffectPerkIds\s*=\s*new Dictionary<string,\s*NoEffectReason>\([^)]*\)\s*\{(.*?)\n\s*\};')
    if ($m.Success) {
        foreach ($s in [regex]::Matches($m.Groups[1].Value, '\{\s*"(\w+)"\s*,\s*NoEffectReason\.(\w+)\s*\}')) {
            $reason = $s.Groups[2].Value
            if ($reasonToVerdict.ContainsKey($reason)) { $noEffect[$s.Groups[1].Value] = $reasonToVerdict[$reason] }
            else { Add-Warning "TroopPerks.cs: unknown NoEffectReason '$reason' for '$($s.Groups[1].Value)'" }
        }
        if ($noEffect.Count -eq 0) { Add-Warning "NoTroopEffectPerkIds in $troopPerksCs has no entries the script can read" }
    }
    else { Add-Warning "NoTroopEffectPerkIds not found in $troopPerksCs" }
}
else { Add-Warning "not found: $troopPerksCs" }

# ---------------------------------------------------------------- epic perk thresholds
# DefaultCharacterDevelopmentModel: Min/MaxSkillRequiredForEpicPerkBonus (epic perks, Athletics.MightyBlow HP).

$epic = [ordered]@{ min = 200; max = 250 }
$devModel = Join-Path $Decompiled 'TaleWorlds.CampaignSystem\TaleWorlds.CampaignSystem.GameComponents\DefaultCharacterDevelopmentModel.cs'
if (Test-Path -LiteralPath $devModel) {
    $src = [System.IO.File]::ReadAllText($devModel)
    $mMin = [regex]::Match($src, 'MinSkillRequiredForEpicPerkBonus\s*=>\s*(\d+)')
    $mMax = [regex]::Match($src, 'MaxSkillRequiredForEpicPerkBonus\s*=>\s*(\d+)')
    if ($mMin.Success) { $epic.min = [int]$mMin.Groups[1].Value } else { Add-Warning "MinSkillRequiredForEpicPerkBonus not found, using $($epic.min)" }
    if ($mMax.Success) { $epic.max = [int]$mMax.Groups[1].Value } else { Add-Warning "MaxSkillRequiredForEpicPerkBonus not found, using $($epic.max)" }
}
else { Add-Warning "not found: $devModel (epic perk thresholds default to 200/250)" }

# ---------------------------------------------------------------- perks

$perks = New-Object System.Collections.Specialized.OrderedDictionary
$perkSkillOrder = New-Object System.Collections.Generic.List[string]

function Read-PerkClass([string]$path, [string]$sourceName) {
    if (-not (Test-Path -LiteralPath $path)) { Add-Warning "perk file not found: $path"; return }
    $src = [System.IO.File]::ReadAllText($path)
    $tiers = @()
    $tm = [regex]::Match($src, 'TierSkillRequirements\s*=\s*new int\[\d*\]\s*\{([^}]*)\}')
    if ($tm.Success) { foreach ($n in ($tm.Groups[1].Value -split ',')) { $tiers += [int]$n.Trim() } }
    else { Add-Warning "$sourceName : TierSkillRequirements not found" }

    $fieldToId = @{}
    foreach ($m in [regex]::Matches($src, '(_\w+)\s*=\s*Create\("(\w+)"\)')) { $fieldToId[$m.Groups[1].Value] = $m.Groups[2].Value }

    $lines = [System.IO.File]::ReadAllLines($path)
    $count = 0
    foreach ($line in $lines) {
        $lm = [regex]::Match($line, '^\s*(_\w+)\.Initialize\((.*)\);\s*$')
        if (-not $lm.Success) { continue }
        $field = $lm.Groups[1].Value
        if (-not $fieldToId.ContainsKey($field)) { Add-Warning "$sourceName : Initialize on unknown field $field"; continue }
        $id = $fieldToId[$field]
        $ctx = "$sourceName.$id"
        $count++
        $perk = [ordered]@{
            id = $id; name = $id; skill = ''; skillName = ''; level = 0; alt = $null
            primary = $null; secondary = $null; personal = ''; loaderNoEffect = $null; audit = $null; source = $sourceName
        }
        if ($noEffect.ContainsKey($id)) { $perk.loaderNoEffect = $noEffect[$id] }
        try {
            $a = Split-CsArgs $lm.Groups[2].Value
            # Initialize(name, skill, requiredSkillValue, alternativePerk, primaryDescription, primaryRole,
            #   primaryBonus, incrementType, secondaryDescription = "", secondaryRole = None, secondaryBonus = 0,
            #   secondaryIncrementType = Invalid, primaryTroopUsageMask = Undefined, secondaryTroopUsageMask =
            #   Undefined, primaryEffectEnvironment = All, secondaryEffectEnvironment = All)
            if ($a.Count -lt 8) { throw "only $($a.Count) arguments" }
            $def = @('', '', '', '', '', '', '', '', '""', 'PartyRole.None', '0f', 'EffectIncrementType.Invalid',
                'TroopUsageFlags.Undefined', 'TroopUsageFlags.Undefined', 'PerkObject.EffectEnvironment.All', 'PerkObject.EffectEnvironment.All')
            $args16 = @()
            for ($i = 0; $i -lt 16; $i++) { if ($i -lt $a.Count) { $args16 += $a[$i] } else { $args16 += $def[$i] } }
            if ($a.Count -gt 16) { Add-Warning "$ctx : more than 16 arguments, extra ignored" }
            foreach ($arg in $args16) { if ($arg -match '^\w+\s*:') { Add-Warning "$ctx : named argument '$arg' not supported" } }

            $nameStr = Get-CsString $args16[0]
            if ($null -ne $nameStr) { $perk.name = Remove-LocKey $nameStr } else { Add-Warning "$ctx : name is not a literal" }

            $skillExpr = $args16[1].Trim()
            if ($skillMap.ContainsKey($skillExpr)) { $perk.skill = $skillMap[$skillExpr].id; $perk.skillName = $skillMap[$skillExpr].name }
            else { $perk.skill = (Get-EnumTail $skillExpr); $perk.skillName = $perk.skill; Add-Warning "$ctx : unknown skill '$skillExpr'" }

            $lvl = $args16[2].Trim()
            $gm = [regex]::Match($lvl, '^GetTierCost\((\d+)\)$')
            if ($gm.Success -and [int]$gm.Groups[1].Value -ge 1 -and [int]$gm.Groups[1].Value -le $tiers.Count) { $perk.level = $tiers[[int]$gm.Groups[1].Value - 1] }
            elseif ($lvl -match '^\d+$') { $perk.level = [int]$lvl }
            else { Add-Warning "$ctx : could not read required skill '$lvl'" }

            # Alternative pairing, applied in call order exactly as Initialize does (this.Alt = arg; if arg is
            # not null, arg.Alt = this). A perk initialized later overwrites its own Alt, so only the reciprocal
            # write into an already-initialized perk needs replaying.
            $altExpr = $args16[3].Trim()
            if ($altExpr -ne 'null') {
                if ($fieldToId.ContainsKey($altExpr)) {
                    $altId = $fieldToId[$altExpr]
                    $perk.alt = $altId
                    if ($perks.Contains($altId)) { $perks[$altId].alt = $id }
                }
                else { Add-Warning "$ctx : unknown alternative '$altExpr'" }
            }

            $pDesc = Get-CsString $args16[4]
            $pRole = Get-EnumTail $args16[5]
            $pBonus = ConvertTo-Bonus $args16[6] $ctx
            $pIncr = Get-EnumTail $args16[7]
            $sDesc = Get-CsString $args16[8]
            if ($null -eq $sDesc) { $sDesc = ''; Add-Warning "$ctx : secondary description is not a literal" }
            $sRole = Get-EnumTail $args16[9]
            $sBonus = ConvertTo-Bonus $args16[10] $ctx
            $sIncrArg = Get-EnumTail $args16[11]
            $pUsage = Get-EnumTail $args16[12]
            $sUsage = Get-EnumTail $args16[13]
            $pEnv = Get-EnumTail $args16[14]
            $sEnv = Get-EnumTail $args16[15]

            if ($null -eq $pDesc) { $pDesc = ''; Add-Warning "$ctx : primary description is not a literal" }
            $perk.primary = [ordered]@{
                role = $pRole; text = (Format-PerkText $pDesc $pBonus $pIncr $ctx); bonus = $pBonus
                increment = $pIncr; usage = $pUsage; env = $pEnv
            }
            if ($sDesc -ne '') {
                # The text uses the secondaryIncrementType argument as given (Invalid formats like Add); the
                # stored SecondaryIncrementType falls back to the primary one.
                $sIncrStored = $sIncrArg
                if ($sIncrStored -eq 'Invalid') { $sIncrStored = $pIncr }
                $perk.secondary = [ordered]@{
                    role = $sRole; text = (Format-PerkText $sDesc $sBonus $sIncrArg $ctx); bonus = $sBonus
                    increment = $sIncrStored; usage = $sUsage; env = $sEnv
                }
            }
            elseif ($sRole -ne 'None') {
                $perk.secondary = [ordered]@{ role = $sRole; text = ''; bonus = $sBonus; increment = $sIncrArg; usage = $sUsage; env = $sEnv }
            }
            $isP = ($pRole -eq 'Personal')
            $isS = ($null -ne $perk.secondary -and $perk.secondary.role -eq 'Personal')
            if ($isP -and $isS) { $perk.personal = 'both' }
            elseif ($isP) { $perk.personal = 'primary' }
            elseif ($isS) { $perk.personal = 'secondary' }
        }
        catch {
            Add-Warning "$ctx : could not parse Initialize call ($($_.Exception.Message)); kept with partial data"
        }
        if ($perks.Contains($id)) { Add-Warning "$ctx : initialized twice; the last call wins" ; $perks.Remove($id) }
        $perks[$id] = $perk
        if ($perk.skill -ne '' -and -not $perkSkillOrder.Contains($perk.skill)) { $perkSkillOrder.Add($perk.skill) }
    }
    $created = $fieldToId.Count
    if ($count -ne $created) { Add-Warning "$sourceName : $created perks created but $count Initialize calls parsed" }
    Write-Host ("  {0}: {1} perks" -f $sourceName, $count)
}

Write-Host 'Reading perks...'
Read-PerkClass (Join-Path $Decompiled 'TaleWorlds.CampaignSystem\TaleWorlds.CampaignSystem.CharacterDevelopment\DefaultPerks.cs') 'DefaultPerks'
$navalPerksPath = Join-Path $Decompiled 'NavalDLC\NavalDLC.CharacterDevelopment\NavalPerks.cs'
if (Test-Path -LiteralPath $navalPerksPath) { Read-PerkClass $navalPerksPath 'NavalPerks' }
else { Write-Host '  NavalPerks.cs not in decompiled/ (War Sails perks skipped)' }
foreach ($hid in $noEffect.Keys) { if (-not $perks.Contains($hid)) { Add-Warning "no-effect perk '$hid' (TroopPerks.cs NoTroopEffectPerkIds) not found among the perks" } }

# ---------------------------------------------------------------- audit (audit/*.json)
# Hand/agent-maintained verdicts on what each perk does for a regular troop under RBM (see audit/README.md).
# This script only merges them; it never decides a verdict. Files starting with '_' are notes, not data.

$validVerdicts = @('works', 'conditional', 'partial', 'hero-only', 'campaign-only', 'rbm-bypassed', 'no-personal-effect', 'no-check-found', 'unclear')
$noTroopEffectVerdicts = @('hero-only', 'campaign-only', 'no-check-found', 'rbm-bypassed')

function Get-JsonProp($obj, [string]$name) {
    if ($null -eq $obj) { return $null }
    $prop = $obj.PSObject.Properties[$name]
    if ($null -eq $prop) { return $null }
    return $prop.Value
}

function Get-JsonText($obj, [string]$name) {
    $v = Get-JsonProp $obj $name
    if ($null -eq $v) { return '' }
    return [string]$v
}

$auditGroups = New-Object System.Collections.Generic.List[object]
$auditDir = Join-Path $PSScriptRoot 'audit'
$audited = @{}
Write-Host 'Reading audit...'
if (Test-Path -LiteralPath $auditDir) {
    foreach ($file in (Get-ChildItem -LiteralPath $auditDir -Filter '*.json' | Sort-Object Name)) {
        if ($file.Name.StartsWith('_')) { continue }
        try {
            $doc = ([System.IO.File]::ReadAllText($file.FullName, [System.Text.Encoding]::UTF8)) | ConvertFrom-Json
        }
        catch {
            Add-Warning "audit/$($file.Name): not valid JSON ($($_.Exception.Message)); skipped"
            continue
        }
        $groupName = Get-JsonText $doc 'group'
        if ($groupName -eq '') { $groupName = $file.BaseName }
        $entries = @(Get-JsonProp $doc 'perks')
        $n = 0
        foreach ($e in $entries) {
            if ($null -eq $e) { continue }
            $auditId = Get-JsonText $e 'id'
            if ($auditId -eq '') { Add-Warning "audit/$($file.Name): an entry has no id; skipped"; continue }
            if (-not $perks.Contains($auditId)) { Add-Warning "audit/$($file.Name): perk id '$auditId' not found among the perks"; continue }
            if ($audited.ContainsKey($auditId)) { Add-Warning "audit/$($file.Name): '$auditId' already audited in $($audited[$auditId]); this entry wins" }
            $verdict = Get-JsonText $e 'verdict'
            if ($validVerdicts -notcontains $verdict) { Add-Warning "audit/$($file.Name): '$auditId' has unknown verdict '$verdict'" }
            $sites = New-Object System.Collections.Generic.List[object]
            foreach ($s in @(Get-JsonProp $e 'sites')) {
                if ($null -eq $s) { continue }
                $sites.Add([ordered]@{
                    method = (Get-JsonText $s 'method'); file = (Get-JsonText $s 'file')
                    reachesTroop = [bool](Get-JsonProp $s 'reachesTroop'); why = (Get-JsonText $s 'why')
                })
            }
            $perks[$auditId].audit = [ordered]@{
                verdict = $verdict; confidence = (Get-JsonText $e 'confidence'); summary = (Get-JsonText $e 'summary')
                conditions = (Get-JsonText $e 'conditions'); rbm = (Get-JsonText $e 'rbm'); sites = $sites; group = $groupName
            }
            $audited[$auditId] = $file.Name
            $n++
        }
        $auditGroups.Add([ordered]@{ group = $groupName; file = $file.Name; auditedAt = (Get-JsonText $doc 'auditedAt'); perks = $n })
        Write-Host ("  {0,-24} {1,3} perks ({2})" -f $file.Name, $n, (Get-JsonText $doc 'auditedAt'))
    }
}
else { Add-Warning "audit folder not found: $auditDir (no verdicts in the editor)" }

foreach ($p in $perks.Values) {
    if ($null -eq $p.audit) {
        if ($p.personal -ne '') { Add-Warning "$($p.id): has a personal effect but no audit entry" }
        continue
    }
    $v = $p.audit.verdict
    if ($v -eq 'no-personal-effect' -and $p.personal -ne '') { Add-Warning "$($p.id): audit says no-personal-effect, but the perk has a personal $($p.personal) half" }
    if ($v -ne 'no-personal-effect' -and $p.personal -eq '') { Add-Warning "$($p.id): audit verdict '$v', but the perk has no personal half" }
    $inLoader = ($null -ne $p.loaderNoEffect)
    $noEffectVerdict = ($noTroopEffectVerdicts -contains $v)
    if ($noEffectVerdict -and -not $inLoader) { Add-Warning "$($p.id): audit verdict '$v' but missing from TroopPerks.cs NoTroopEffectPerkIds" }
    elseif ($inLoader -and -not $noEffectVerdict) { Add-Warning "$($p.id): in TroopPerks.cs NoTroopEffectPerkIds ($($p.loaderNoEffect)) but the audit says '$v'" }
    elseif ($inLoader -and $p.loaderNoEffect -ne $v) { Add-Warning "$($p.id): TroopPerks.cs reason '$($p.loaderNoEffect)' differs from the audit verdict '$v'" }
}

# ---------------------------------------------------------------- modules and their XML

$navalInstalled = Test-Path -LiteralPath (Join-Path $ModulesRoot 'NavalDLC\SubModule.xml')
$moduleList = @(
    @{ id = 'Native'; sub = (Join-Path $ModulesRoot 'Native\SubModule.xml'); data = (Join-Path $ModulesRoot 'Native\ModuleData') },
    @{ id = 'SandBoxCore'; sub = (Join-Path $ModulesRoot 'SandBoxCore\SubModule.xml'); data = (Join-Path $ModulesRoot 'SandBoxCore\ModuleData') },
    @{ id = 'SandBox'; sub = (Join-Path $ModulesRoot 'SandBox\SubModule.xml'); data = (Join-Path $ModulesRoot 'SandBox\ModuleData') },
    @{ id = 'StoryMode'; sub = (Join-Path $ModulesRoot 'StoryMode\SubModule.xml'); data = (Join-Path $ModulesRoot 'StoryMode\ModuleData') },
    @{ id = 'CustomBattle'; sub = (Join-Path $ModulesRoot 'CustomBattle\SubModule.xml'); data = (Join-Path $ModulesRoot 'CustomBattle\ModuleData') }
)
if ($navalInstalled) {
    $moduleList += @{ id = 'NavalDLC'; sub = (Join-Path $ModulesRoot 'NavalDLC\SubModule.xml'); data = (Join-Path $ModulesRoot 'NavalDLC\ModuleData') }
}
# RBM's own XML straight from the repo (RBMXML is deployed as the RBM module's ModuleData).
$moduleList += @{ id = 'RBM'; sub = (Join-Path $RepoRoot 'RBMXML\SubModule.xml'); data = (Join-Path $RepoRoot 'RBMXML') }
if ($navalInstalled) {
    $moduleList += @{ id = 'RBM_WS'; sub = (Join-Path $RepoRoot 'RBM_WS_XML\SubModule.xml'); data = (Join-Path $RepoRoot 'RBM_WS_XML') }
}

function Get-XmlFiles($module, [string]$xmlId) {
    $result = @()
    if (-not (Test-Path -LiteralPath $module.sub)) { Add-Warning "$($module.id): SubModule.xml not found ($($module.sub))"; return $result }
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
        if (-not (Test-Path -LiteralPath $file)) {
            if (Test-Path -LiteralPath (Join-Path $module.data ($path + '.xslt'))) { Add-Warning "$($module.id): $xmlId '$path' is an XSLT transform, not read" }
            else { Add-Warning "$($module.id): $xmlId file not found: $file" }
            continue
        }
        $result += @{ file = $file; name = ($path + '.xml'); campaign = $campaign; types = ($types -join ',') }
    }
    return ,$result
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

Write-Host 'Reading skill sets and troops...'
$fileLog = New-Object System.Collections.Generic.List[object]
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

# ---------------------------------------------------------------- troops

function Get-Attr($node, [string]$name) {
    $a = $node.Attributes[$name]
    if ($null -eq $a) { return $null }
    return $a.Value
}

function Test-True($value) { return ($null -ne $value -and $value.Trim() -ieq 'true') }

$troops = New-Object System.Collections.Specialized.OrderedDictionary
$heroCount = 0
$mountedClasses = @('Cavalry', 'HorseArcher', 'LightCavalry', 'HeavyCavalry')
foreach ($module in $moduleList) {
    if (-not (Test-Path -LiteralPath $module.sub)) { continue }
    foreach ($f in (Get-XmlFiles $module 'NPCCharacters')) {
        $doc = New-Object System.Xml.XmlDocument
        $doc.Load($f.file)
        $inFile = 0
        foreach ($n in $doc.SelectNodes('//NPCCharacter')) {
            $id = Get-Attr $n 'id'
            if ([string]::IsNullOrEmpty($id)) { continue }
            if (Test-True (Get-Attr $n 'is_hero')) { $heroCount++; continue }
            if ($troops.Contains($id)) {
                $prev = $troops[$id]
                # A non-campaign file (e.g. custom battle only) never replaces a campaign definition here:
                # troop perks only load in a campaign.
                if ($prev.campaign -and -not $f.campaign) { $prev.alsoIn += ($module.id + '/' + $f.name); continue }
            }
            $inFile++
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

            $skills = [ordered]@{}
            $template = Get-Attr $n 'skill_template'
            if ($null -ne $template) {
                $tid = $template -replace '^SkillSet\.', ''
                $sets = $skillSetsAll
                if ($f.campaign) { $sets = $skillSetsCampaign }
                if ($sets.ContainsKey($tid)) { foreach ($k in $sets[$tid].Keys) { $skills[$k] = $sets[$tid][$k] } }
                else { Add-Warning "$id : skill template '$tid' not found" }
            }
            foreach ($sn in $n.SelectNodes('skills/skill|Skills/skill')) {
                if ($null -eq $sn.Attributes['id'] -or $null -eq $sn.Attributes['value']) { continue }
                $skills[$sn.Attributes['id'].Value] = [int]$sn.Attributes['value'].Value
            }

            $upgrades = @()
            foreach ($u in $n.SelectNodes('upgrade_targets/upgrade_target')) {
                $uid = Get-Attr $u 'id'
                if ($null -ne $uid) { $upgrades += ($uid -replace '^NPCCharacter\.', '') }
            }
            $horse = $false
            foreach ($e in $n.SelectNodes('.//equipment')) {
                if ((Get-Attr $e 'slot') -eq 'Horse' -and -not [string]::IsNullOrEmpty((Get-Attr $e 'id'))) { $horse = $true; break }
            }

            $name = Remove-LocKey (Get-Attr $n 'name')
            if ($name -eq '') { $name = $id }
            $sources = @()
            if ($troops.Contains($id)) { $sources = @($troops[$id].sources); $troops.Remove($id) }
            $sources += ($module.id + '/' + $f.name)
            $troops[$id] = [ordered]@{
                id = $id; name = $name; culture = $culture; level = $level; tier = $tier
                occupation = $occupation; group = $group
                mounted = ($mountedClasses -contains $group); horse = $horse
                upgrades = $upgrades; skills = $skills
                module = $module.id; file = $f.name; sources = $sources; alsoIn = @()
                campaign = [bool]$f.campaign
                template = (Test-True (Get-Attr $n 'is_template'))
                obsolete = (Test-True (Get-Attr $n 'is_obsolete'))
                hidden = (Test-True (Get-Attr $n 'is_hidden_encyclopedia'))
                basic = (Test-True (Get-Attr $n 'is_basic_troop'))
            }
        }
        $fileLog.Add([ordered]@{ module = $module.id; file = $f.name; campaign = [bool]$f.campaign; types = $f.types; troops = $inFile })
        Write-Host ("  {0,-12} {1,-40} {2,4} troop definitions{3}" -f $module.id, $f.name, $inFile, $(if ($f.campaign) { '' } else { '  (not loaded in campaigns)' }))
    }
}
# Remove helper-only fields that are always empty to keep the file small.
foreach ($t in $troops.Values) { if ($t.alsoIn.Count -eq 0) { $t.Remove('alsoIn') } }

# ---------------------------------------------------------------- current mapping

$perkFile = Join-Path $RepoRoot 'RBMXML\rbm_troop_perks.xml'
$current = [ordered]@{ header = ''; order = @(); map = [ordered]@{}; comments = [ordered]@{} }
if (Test-Path -LiteralPath $perkFile) {
    $doc = New-Object System.Xml.XmlDocument
    $doc.PreserveWhitespace = $true
    $doc.Load($perkFile)
    foreach ($child in $doc.ChildNodes) {
        if ($child.NodeType -eq [System.Xml.XmlNodeType]::Comment) { $current.header = $child.Value; break }
        if ($child.NodeType -eq [System.Xml.XmlNodeType]::Element) { break }
    }
    $root = $doc.DocumentElement
    $pendingComment = $null
    if ($null -ne $root) {
        foreach ($child in $root.ChildNodes) {
            if ($child.NodeType -eq [System.Xml.XmlNodeType]::Comment) { $pendingComment = $child.Value.Trim(); continue }
            if ($child.NodeType -ne [System.Xml.XmlNodeType]::Element -or $child.Name -ne 'Troop') { continue }
            $tid = Get-Attr $child 'id'
            if ([string]::IsNullOrEmpty($tid)) { $pendingComment = $null; continue }
            if (-not $current.map.Contains($tid)) { $current.map[$tid] = @(); $current.order += $tid }
            foreach ($p in $child.SelectNodes('Perk')) {
                $perkId = Get-Attr $p 'id'
                if (-not [string]::IsNullOrEmpty($perkId) -and -not ($current.map[$tid] -contains $perkId)) { $current.map[$tid] += $perkId }
            }
            if ($null -ne $pendingComment) { $current.comments[$tid] = $pendingComment; $pendingComment = $null }
            if (-not $troops.Contains($tid)) { Add-Warning "rbm_troop_perks.xml: unknown troop id '$tid'" }
            foreach ($perkId in $current.map[$tid]) { if (-not $perks.Contains($perkId)) { Add-Warning "rbm_troop_perks.xml: unknown perk id '$perkId' (troop '$tid')" } }
        }
    }
}
else { Add-Warning "not found: $perkFile" }

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
foreach ($s in $skillMap.Values) { $skillNames[$s.id] = $s.name }

$data = [ordered]@{
    generated = (Get-Date).ToString('yyyy-MM-dd HH:mm:ss', $Inv)
    gameVersion = $gameVersion
    navalDlc = $navalInstalled
    skills = $skillNames
    skillOrder = $perkSkillOrder
    epicPerkSkill = $epic
    auditGroups = $auditGroups
    perks = @($perks.Values)
    troops = @($troops.Values)
    files = $fileLog
    current = $current
    warnings = $script:Warnings
}

$sb = New-Object System.Text.StringBuilder
[void]$sb.Append("// Generated by Build-TroopPerkData.ps1 - do not edit, do not commit (contains TaleWorlds text).`n")
[void]$sb.Append('window.TROOP_PERK_DATA = ')
Write-JsonValue $sb $data
[void]$sb.Append(";`n")
[System.IO.File]::WriteAllText($OutFile, $sb.ToString(), (New-Object System.Text.UTF8Encoding($false)))

$personal = 0
$verdictCounts = [ordered]@{}
foreach ($v in $validVerdicts) { $verdictCounts[$v] = 0 }
foreach ($p in $perks.Values) {
    if ($p.personal -ne '') { $personal++ }
    if ($null -ne $p.audit -and $verdictCounts.Contains($p.audit.verdict)) { $verdictCounts[$p.audit.verdict]++ }
}
$campaignTroops = 0
foreach ($t in $troops.Values) { if ($t.campaign) { $campaignTroops++ } }
$verdictText = (@($verdictCounts.Keys | Where-Object { $verdictCounts[$_] -gt 0 } | ForEach-Object { '{0} {1}' -f $_, $verdictCounts[$_] }) -join ', ')
Write-Host ''
Write-Host ("Perks:   {0} ({1} with a personal effect, {2} in the loader's no-effect list)" -f $perks.Count, $personal, $noEffect.Count)
Write-Host ("Audit:   {0} perks audited: {1}" -f $audited.Count, $verdictText)
Write-Host ("Troops:  {0} non-hero ({1} loaded in campaigns; {2} hero entries skipped)" -f $troops.Count, $campaignTroops, $heroCount)
Write-Host ("Mapping: {0} troop(s) in rbm_troop_perks.xml" -f $current.map.Count)
Write-Host ("Game:    {0}{1}" -f $gameVersion, $(if ($navalInstalled) { ' + War Sails' } else { '' }))
Write-Host ("Warnings: {0}" -f $script:Warnings.Count)
Write-Host ("Wrote {0}" -f $OutFile)
