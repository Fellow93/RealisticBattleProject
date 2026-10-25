<#
.SYNOPSIS
    RBM battle log checker: reads one battle's AI behavior log (logs/ai/ai_<stamp>.log) and terrain log
    (logs/terrain/terrain_<stamp>.log) and flags the symptoms of dumb battle AI, PASS / WARN / FAIL per check.

.DESCRIPTION
    Windows PowerShell 5.1 compatible. Streams both logs once, keeps only the per-second FORM samples, the
    CHANGE lines and the terrain lines it needs, then runs the checks (see README.md next to this script).

.PARAMETER Stamp
    The battle's log stamp, e.g. 2026-10-22_14-54-29. Default: the newest battle (newest ai_*.log).
.PARAMETER LogsDir
    The RBM logs folder (holding ai\ and terrain\). Default: <My Documents>\Mount and Blade II Bannerlord\Configs\RBM\logs
.PARAMETER Out
    Write the report to this file (UTF-8) instead of printing it.
.PARAMETER Last
    Check the newest N battles (ignored when -Stamp is given). Default 1.
.PARAMETER MaxEvidence
    Evidence lines printed per check (the rest are counted). Default 8.
.PARAMETER Threshold
    Hashtable overriding any threshold below, e.g. -Threshold @{ StallMinShrink = 0.05; IdleSeconds = 180 }

.EXAMPLE
    powershell -NoProfile -ExecutionPolicy Bypass -File tools\BattleLogCheck\Check-BattleLogs.ps1
.EXAMPLE
    powershell -NoProfile -ExecutionPolicy Bypass -File tools\BattleLogCheck\Check-BattleLogs.ps1 -Stamp 2026-10-22_14-54-29 -Out C:\temp\check.txt
#>
[CmdletBinding()]
param(
    [string]$Stamp,
    [string]$LogsDir,
    [string]$Out,
    [int]$Last = 1,
    [int]$MaxEvidence = 8,
    [hashtable]$Threshold
)

Set-StrictMode -Off
$ErrorActionPreference = 'Stop'

# =====================================================================================================
# THRESHOLDS (first guesses; tune here or per run with -Threshold @{ Name = value })
# =====================================================================================================
$Cfg = @{
    # 1. Attacker progress
    ContactDist        = 40     # m: attacker main infantry distToEnemy <= this = contact (the approach ends)
    StallWindow        = 60     # s: sliding window length
    StallMinShrink     = 0.10   # the main-to-main gap must shrink by at least this share over every window before contact
    StallMinGap        = 60     # m: windows starting closer than this are skipped (already at fighting range)
    OwnAdvanceShare    = 0.10   # attacker's own advance >= this share of the gap = it marched but the defender withdrew (WARN, not FAIL)
    StartGrace         = 5      # s after the battle start before stalls / flips are counted (deployment-end order burst)
    # 2. Backward drift
    DriftMinSeconds    = 10     # s a drift must last
    DriftMinMetres     = 10     # m it must cover
    DriftMinSpeed      = 0.3    # m/s per step to count as moving away
    DriftMinOrderDist  = 5      # m: away-from-order only when the order point is at least this far
    # 3. Flicker
    FlickerJump        = 10     # m: an order-point jump between two samples
    FlickerWindow      = 20     # s
    FlickerWarnReversals = 3    # this many reversals inside FlickerWindow = WARN (foot formations only)
    FlickerReversals   = 4      # more reversals than this inside FlickerWindow = FAIL
    FlipWindow         = 60     # s
    FlipsWarn          = 6      # behavior / order kind / arrangement changes per FlipWindow above this = WARN
    FlipsFail          = 12     # ... above this = FAIL
    # 4. Charges
    ChargeFarDist      = 150    # m: a charge / engage with the formation's distToEnemy above this is premature
    RecallLoopGap      = 30     # s: two charges of one formation closer than this with a recall between = a loop
    RecallLoopMax      = 2      # more loops than this = FAIL (1..this = WARN)
    ChargeLoopCount    = 3      # this many local charges of one formation at the same enemy formation ...
    ChargeLoopWindow   = 120    # s ... within this = WARN (a bait loop: charge, recall, walk back, charge again)
    ChargeStartDist    = 50     # m: USER RULE 2026-10-22 an AI foot formation's real charge (AI log CHANGE ORDER -> Charge / ChargeToTarget) starts within this of its target ...
    ChargeStartSlack   = 30     # m ... plus this slack (distToEnemy is centre to centre, sampled once a second): a start from farther = WARN
    # 5. Churn
    ChurnWindow        = 60     # s
    ChurnMaxPerWindow  = 6      # take+release events of one kind per formation per window above this = WARN (= 1 pair / 20 s)
    AttackSideFlipsMax = 2      # FLANKPLAN attackSide changes per side above this = WARN
    FollowPlanMax      = 3      # FLANKUSE followPlan re-applies per team above this = WARN
    # 6. Cohesion
    CohesionDefender   = 60     # m edge gap from the main infantry (defender foot)
    CohesionAttacker   = 90     # m (attacker foot)
    CohesionReserve    = 100    # m (RBMBehaviorReservePlug, a reserve by design)
    CohesionSeconds    = 20     # s beyond the limit before it is reported
    MeleeDist          = 30     # m: distToEnemy <= this = in melee (cohesion not judged)
    # 7. Idle / stand-off
    IdleDist           = 200    # m: main-to-main gap above this
    IdleSeconds        = 120    # s both mains static
    IdleSpan           = 10     # s: static = moved less than IdleMoveMetres over this span
    IdleMoveMetres     = 5
    # 10. Reactions
    ReactWindowR1      = 8      # s: an R1 detect must get assign / line / covered within this
    ReactWindowR2      = 5      # s: ... an R2 detect
    ReactEventMin      = 10     # s: only events that lasted at least this long are judged
    ReactMissFail      = 3      # more unanswered events than this = FAIL (1..this = WARN)
    ReactPingPong      = 4      # one responder assigned more than this many times per ReactPingWindow = WARN
    ReactPingWindow    = 60     # s
    ReactLeashShare    = 0.5    # leash + chaseTimeout above this share of all task ends = WARN
    ReactLeashMinEnds  = 4      # ... judged from this many ends
    ReactTaskMax       = 95     # s: an assign with no end although the mission went on this long after it = WARN
    BalancePingPong    = 2      # one team's balance re-picks (REPICK reason=balance switched) more than this per BalancePingWindow = WARN
    BalancePingWindow  = 120    # s
    HelpLossWindow     = 15     # s (10b, FORM lines): the victim's loss window and the helpers' closing window
    HelpLossShare      = 0.10   # the victim (in melee) lost at least this share of its men in the window
    HelpFreeDist       = 60     # m: a helper's distToEnemy above this = free
    HelpReach          = 250    # m: helpers farther than this from the victim are not counted
    HelpCloseMetres    = 20     # m: a helper that closed this much on the victim answered
    # 11. Facing
    FacingOffMax       = 60     # deg: a holding main line facing more than this off the enemy's concentration ...
    FacingSeconds      = 30     # s ... for longer than this outside melee = WARN
    FacingRange        = 400    # m: enemy formations within this of the line make the concentration (men x class weight)
    FacingMinDist      = 40     # m: a concentration nearer than this is not judged
    # 12. Support fire
    SupportWindow      = 60     # s
    SupportMovesMax    = 6      # order-point moves (> FlickerJump m between FORM samples) of a ranged formation in support mode per SupportWindow above this = WARN
    # 15. Approach
    ApproachNoGainSec  = 30     # s: a route taken and dropped for noGain within this = WARN
    ApproachStillSec   = 10     # s: the main standing still this long while following a route = WARN
    ApproachStillMetres = 3     # m: still = moved less than this
    # 16. Charge flicker
    ChargeFlipWindow   = 30     # s
    ChargeFlipMax      = 3      # ChargeGuard advance <-> go switches of one formation above this per ChargeFlipWindow = WARN
    # misc
    PairToleranceSec   = 5      # s: ai_ and terrain_ stamps this close belong to one battle
    MinBattleSeconds   = 20     # s: shorter battles get a note, most checks have nothing to judge
}
if ($Threshold) { foreach ($k in $Threshold.Keys) { $Cfg[$k] = $Threshold[$k] } }

# =====================================================================================================
# helpers
# =====================================================================================================
class P {
    static [System.Globalization.CultureInfo] $C = [System.Globalization.CultureInfo]::InvariantCulture

    static [double] D([string]$s) {
        $v = 0.0
        if ([double]::TryParse($s, [System.Globalization.NumberStyles]::Float, [P]::C, [ref]$v)) { return $v }
        return [double]::NaN
    }
    # value after the first '=' (whole field when none)
    static [string] V([string]$f) {
        $i = $f.IndexOf('=')
        if ($i -lt 0) { return $f }
        return $f.Substring($i + 1)
    }
    # first field starting with key= (from index 2), its value; $null when none
    static [string] F([string[]]$p, [string]$key) {
        $k = $key + '='
        for ($i = 2; $i -lt $p.Length; $i++) { if ($p[$i].StartsWith($k)) { return $p[$i].Substring($k.Length) } }
        return $null
    }
    static [double] T([string]$f) { return [P]::D($f.Substring(2)) }
    static [string] N([double]$x, [int]$dec) {
        if ([double]::IsNaN($x)) { return '-' }
        return ([math]::Round($x, $dec)).ToString([P]::C)
    }
    # index of the sample nearest t in a sorted array
    static [int] Nearest([double[]]$ts, [double]$t) {
        if ($ts.Length -eq 0) { return -1 }
        $lo = 0; $hi = $ts.Length - 1
        while ($lo -lt $hi) {
            $mid = [int][math]::Floor(($lo + $hi) / 2)
            if ($ts[$mid] -lt $t) { $lo = $mid + 1 } else { $hi = $mid }
        }
        if ($lo -gt 0 -and ([math]::Abs($ts[$lo - 1] - $t) -le [math]::Abs($ts[$lo] - $t))) { return $lo - 1 }
        return $lo
    }
}

class FS {
    [double]$t; [string]$tk; [string]$team; [string]$form; [string]$cls; [int]$men; [bool]$ai
    [string]$beh; [string]$ord; [string]$state; [string]$arr
    [double]$w; [double]$d; [double]$ax; [double]$ay; [double]$ox; [double]$oy; [double]$dOrd; [double]$dEn; [double]$dx; [double]$dy
}

function N1([double]$x) { return [P]::N($x, 1) }
function N0([double]$x) { return [P]::N($x, 0) }

$script:Clock = [System.Diagnostics.Stopwatch]::StartNew()
function Phase([string]$name) { Write-Verbose ('{0,7} ms  {1}' -f $script:Clock.ElapsedMilliseconds, $name) }

$script:Report = New-Object 'System.Collections.Generic.List[string]'
function Say([string]$s) { $script:Report.Add($s) }

# results: Id, Name, Status, Why, Evidence
$script:Results = New-Object 'System.Collections.Generic.List[object]'
function Add-Result([string]$id, [string]$name, [string]$status, [string]$why, $evidence) {
    $ev = New-Object 'System.Collections.Generic.List[string]'
    if ($evidence) { foreach ($e in $evidence) { $ev.Add([string]$e) } }
    $script:Results.Add([pscustomobject]@{ Id = $id; Name = $name; Status = $status; Why = $why; Evidence = $ev })
    Phase ('check ' + $id + ' done')
}
function Worst([string[]]$st) {
    if ($st -contains 'FAIL') { return 'FAIL' }
    if ($st -contains 'WARN') { return 'WARN' }
    if ($st -contains 'PASS') { return 'PASS' }
    return 'INFO'
}

$FormName = @{ '0' = 'Infantry'; '1' = 'Ranged'; '2' = 'Cavalry'; '3' = 'HorseArcher'; '4' = 'Skirmisher'; '5' = 'HeavyInfantry'; '6' = 'LightCavalry'; '7' = 'HeavyCavalry'; '8' = 'General'; '9' = 'Bodyguard' }

function Get-StampTime([string]$s) {
    $dt = [datetime]::MinValue
    if ([datetime]::TryParseExact($s, 'yyyy-MM-dd_HH-mm-ss', [P]::C, [System.Globalization.DateTimeStyles]::None, [ref]$dt)) { return $dt }
    return $null
}

# =====================================================================================================
# one battle
# =====================================================================================================
function Invoke-Check([string]$aiPath, [string]$terrainPath, [string]$stamp) {
    $script:Results.Clear()

    # ------------------------------------------------------------------ parse the AI log
    $forms = @{}            # "team|form" -> List[FS]
    $teamSnaps = @{}        # team -> List[List[FS]] (one per snapshot, in order)
    $teamLastTk = @{}
    $sideOf = @{}           # team -> Attacker / Defender
    $playerTeam = @{}
    $culture = @{}
    $commander = @{}
    $tactics = @{}          # team -> List of [t, name]
    $behChanges = @{}       # "team|form" -> List of [t, old, new]
    $ordChanges = @{}
    $haChanges = @{}
    $menStart = @{}
    $missionEnd = [double]::NaN
    $lastT = 0.0
    $firstMove = [double]::NaN
    $aiErrors = New-Object 'System.Collections.Generic.List[string]'
    $decisions = @{}        # "team|form" -> @{ n = DECISION lines; writers = @{ writer = records } } (DecisionTrace)
    $flipLines = New-Object 'System.Collections.Generic.List[object]'

    foreach ($line in [System.IO.File]::ReadLines($aiPath)) {
        if ($line.Length -eq 0) { continue }
        if ($line[0] -eq '#') {
            if ($line.StartsWith('# mission ended at t=')) { $missionEnd = [P]::D($line.Substring(21).Trim()) }
            elseif ($line -match '^# FAILED' -or ($line -match 'Exception' -and $line -notmatch '^#\s{2,}')) { $aiErrors.Add($line) }
            continue
        }
        $p = $line.Split("`t")
        if ($p.Length -lt 3) { continue }
        $kind = $p[1]
        if ($kind -eq 'FORM') {
            if ($p.Length -lt 26) { continue }
            $s = New-Object FS
            $s.t = [P]::T($p[0]); $s.tk = $p[0]; $s.team = $p[2]; $s.form = $p[3]; $s.cls = $p[4]
            $s.men = [int][P]::D($p[5]); $s.ai = ($p[6] -eq 'aiControlled=1')
            $s.beh = $p[9]; $s.ord = $p[11]
            $s.state = ''; $s.arr = ''; $s.w = [double]::NaN; $s.d = [double]::NaN
            $s.ax = [double]::NaN; $s.ay = [double]::NaN; $s.ox = [double]::NaN; $s.oy = [double]::NaN
            $s.dOrd = [double]::NaN; $s.dEn = [double]::NaN; $s.dx = [double]::NaN; $s.dy = [double]::NaN
            for ($i = 12; $i -lt $p.Length; $i++) {
                $f = $p[$i]
                $eq = $f.IndexOf('=')
                if ($eq -lt 0) { continue }
                $k = $f.Substring(0, $eq); $v = $f.Substring($eq + 1)
                switch ($k) {
                    'state' { $s.state = $v }
                    'arr' { $s.arr = $v }
                    'w' { $s.w = [P]::D($v) }
                    'd' { $s.d = [P]::D($v) }
                    'avg' { $xy = $v.Split(','); if ($xy.Length -eq 2) { $s.ax = [P]::D($xy[0]); $s.ay = [P]::D($xy[1]) } }
                    'order' { $xy = $v.Split(','); if ($xy.Length -eq 2) { $s.ox = [P]::D($xy[0]); $s.oy = [P]::D($xy[1]) } }
                    'distToOrder' { $s.dOrd = [P]::D($v) }
                    'distToEnemy' { $s.dEn = [P]::D($v) }
                    'dir' { $xy = $v.Split(','); if ($xy.Length -eq 2) { $s.dx = [P]::D($xy[0]); $s.dy = [P]::D($xy[1]) } }
                }
            }
            $key = $s.team + '|' + $s.form
            if (-not $forms.ContainsKey($key)) { $forms[$key] = New-Object 'System.Collections.Generic.List[object]' }
            $forms[$key].Add($s)
            if (-not $teamSnaps.ContainsKey($s.team)) { $teamSnaps[$s.team] = New-Object 'System.Collections.Generic.List[object]'; $teamLastTk[$s.team] = '' }
            if ($teamLastTk[$s.team] -ne $s.tk) {
                $teamSnaps[$s.team].Add((New-Object 'System.Collections.Generic.List[object]'))
                $teamLastTk[$s.team] = $s.tk
            }
            $snaps = $teamSnaps[$s.team]
            $snaps[$snaps.Count - 1].Add($s)
            $lastT = $s.t
        }
        elseif ($kind -eq 'TEAM') {
            $team = $p[2]
            if (-not $sideOf.ContainsKey($team)) {
                $sideOf[$team] = $p[3]
                $playerTeam[$team] = ($p[4] -eq 'playerTeam=1')
            }
            $comp = [P]::F($p, 'comp')
            if ($comp -and $comp -match 'men=(\d+)/(\d+)') { if ([int]$Matches[1] -gt 0 -and -not $menStart.ContainsKey($team)) { $menStart[$team] = $Matches[1] } }
        }
        elseif ($kind -eq 'SELECT') {
            $culture[$p[2]] = [P]::V($p[3]); $commander[$p[2]] = [P]::V($p[4])
        }
        elseif ($kind -eq 'DECISION') {
            if ($p.Length -lt 5) { continue }
            $key = $p[2] + '|' + $p[3]
            if (-not $decisions.ContainsKey($key)) { $decisions[$key] = @{ n = 0; flips = 0; writers = @{} } }
            $dc = $decisions[$key]; $dc['n'] = $dc['n'] + 1
            $chain = [P]::F($p, 'chain')
            if ($chain) {
                foreach ($e in $chain.Split(@(' | '), [System.StringSplitOptions]::None)) {
                    # field:writer text @t  (writer = up to the first space after the field's colon)
                    $c = $e.IndexOf(':'); if ($c -lt 0) { continue }
                    $sp = $e.IndexOf(' ', $c + 1); if ($sp -lt 0) { $sp = $e.Length }
                    $w = $e.Substring(0, $c) + ':' + $e.Substring($c + 1, $sp - $c - 1)
                    $wt = $dc['writers']
                    if (-not $wt.ContainsKey($w)) { $wt[$w] = 0 }
                    $wt[$w] = $wt[$w] + 1
                }
            }
        }
        elseif ($kind -eq 'FLIP') {
            if ($p.Length -lt 5) { continue }
            $key = $p[2] + '|' + $p[3]
            if (-not $decisions.ContainsKey($key)) { $decisions[$key] = @{ n = 0; flips = 0; writers = @{} } }
            $decisions[$key]['flips'] = $decisions[$key]['flips'] + 1
            $flipLines.Add([pscustomobject]@{ t = [P]::T($p[0]); team = $p[2]; form = $p[3]; cls = $p[4]; field = [P]::F($p, 'field'); count = [P]::F($p, 'count'); writers = [P]::F($p, 'writers'); seq = [P]::F($p, 'seq') })
        }
        elseif ($kind -eq 'CHANGE') {
            $t = [P]::T($p[0])
            $sub = $p[2]
            if ($sub -eq 'TACTIC') {
                $team = $p[3]
                $nm = $p[4]; $ix = $nm.IndexOf(' -> '); if ($ix -ge 0) { $nm = $nm.Substring($ix + 4) }
                if (-not $tactics.ContainsKey($team)) { $tactics[$team] = New-Object 'System.Collections.Generic.List[object]' }
                $tactics[$team].Add(@($t, $nm))
            }
            elseif ($sub -eq 'BEHAVIOR' -or $sub -eq 'ORDER') {
                if ($p.Length -lt 6) { continue }
                $key = $p[3] + '|' + $p[4]
                $on = $p[5].Split(@(' -> '), [System.StringSplitOptions]::None)
                $old = $on[0]; $new = ''; if ($on.Length -gt 1) { $new = $on[1] }
                $tbl = $behChanges; if ($sub -eq 'ORDER') { $tbl = $ordChanges }
                if (-not $tbl.ContainsKey($key)) { $tbl[$key] = New-Object 'System.Collections.Generic.List[object]' }
                $tbl[$key].Add(@($t, $old, $new))
                if ($sub -eq 'ORDER' -and [double]::IsNaN($firstMove) -and $old -eq 'Stop') { $firstMove = $t }
            }
            elseif ($sub -eq 'HA_ORBIT') {
                $key = $p[3].Replace('/', '|')
                if (-not $haChanges.ContainsKey($key)) { $haChanges[$key] = New-Object 'System.Collections.Generic.List[object]' }
                $haChanges[$key].Add(@($t, '', $p[4]))
            }
        }
    }
    if ([double]::IsNaN($missionEnd)) { $missionEnd = $lastT }

    Phase 'ai log parsed'
    # ------------------------------------------------------------------ parse the terrain log
    $tr = @{
        Present = $false; Scene = ''; GatherT = [double]::NaN; PublishT = [double]::NaN; Skipped = $null
        Features = $null; BridgeCells = $null; ChokeFeatures = 0; WaterCross = 0; FordOnPath = 0
        SameGround = ''; ChokeHold = ''; ChokeBypass = ''; BattlePos = ''; BPOdds = $null; BPWinner = ''; ChokeOdds = $null; Errors = (New-Object 'System.Collections.Generic.List[string]')
        Behaviors = (New-Object 'System.Collections.Generic.List[object]')
        FlankReplan = 0; FlankStale = 0; SpotsReplan = 0; CavReplan = 0; ReplanStop = ''; BPRepick = 0; BPMoved = 0
        Repick = (New-Object 'System.Collections.Generic.List[string]'); FollowPlan = @{}; AttackSides = @{}
        TacticSummary = (New-Object 'System.Collections.Generic.List[string]'); FlankCap = $false; Hedgehog = (New-Object 'System.Collections.Generic.List[string]')
        Reacts = (New-Object 'System.Collections.Generic.List[object]'); BalanceRepicks = (New-Object 'System.Collections.Generic.List[object]')
        Shadow = @{}
        Approach = (New-Object 'System.Collections.Generic.List[object]'); ApproachUses = (New-Object 'System.Collections.Generic.List[object]')
    }
    if ($terrainPath -and (Test-Path -LiteralPath $terrainPath)) {
        $tr.Present = $true
        foreach ($line in [System.IO.File]::ReadLines($terrainPath)) {
            if ($line.Length -eq 0) { continue }
            if ($line[0] -eq '#') {
                if ($tr.Scene -eq '' -and $line -match 'scene=(\S+)') { $tr.Scene = $Matches[1] }
                if ($line -match '^# gathered at t=([0-9.]+)') { $tr.GatherT = [P]::D($Matches[1]) }
                elseif ($line -match '^# terrain tactic factors published at t=([0-9.]+)') { $tr.PublishT = [P]::D($Matches[1]) }
                elseif ($line -match '^# FAILED') { $tr.Errors.Add($line) }
                continue
            }
            $p = $line.Split("`t")
            if ($p.Length -lt 3) { continue }
            $kind = $p[1]
            # generic error= / failed= fields on any data line
            for ($i = 2; $i -lt $p.Length; $i++) {
                if ($p[$i] -match '^(error|failed|rayFailed)=' -or $p[$i] -match '\sfailed=') { $tr.Errors.Add($p[0] + ' ' + $kind + ' ' + $p[$i]) }
            }
            if ($line -match 'Exception') { $tr.Errors.Add($line) }
            # FormationGeometry shadow field "key=Xm(old Ym)": per site + key, new - old
            if ($line.Contains('(old ')) {
                $site = $kind
                if ($kind -eq 'BEHAVIOR' -and $p.Length -ge 6) { $site = 'BEHAVIOR ' + $p[4] + ' ' + ($p[5] -split '\s+')[0] }
                elseif ($kind -eq 'REACT' -and $p.Length -ge 5) { $site = 'REACT ' + $p[3] + ' ' + $p[4] }
                foreach ($m in [regex]::Matches($line, '(\w+)=(-?\d+)m\(old (-?\d+)m\)')) {
                    $sk = $site + ' ' + $m.Groups[1].Value
                    $d = [double]$m.Groups[2].Value - [double]$m.Groups[3].Value
                    if (-not $tr.Shadow.ContainsKey($sk)) { $tr.Shadow[$sk] = @{ n = 0; sum = 0.0; min = [double]::MaxValue; max = [double]::MinValue } }
                    $sh = $tr.Shadow[$sk]
                    $sh['n'] = $sh['n'] + 1; $sh['sum'] = $sh['sum'] + $d
                    if ($d -lt $sh['min']) { $sh['min'] = $d }
                    if ($d -gt $sh['max']) { $sh['max'] = $d }
                }
            }
            switch ($kind) {
                'TERRAIN' {
                    if ($p[2] -like 'skipped*') { $tr.Skipped = $p[2] }
                    $sc = [P]::F($p, 'scene'); if ($sc) { $tr.Scene = $sc }
                }
                'TERRAIN_SPACE' { $tr.SameGround = [P]::F($p, 'sameGround') }
                'TERRAIN_WATER' { $tr.BridgeCells = [P]::F($p, 'bridgeCells') }
                'FEATURES' { $tr.Features = ($p[2..($p.Length - 1)] -join ' ') }
                'FEATURE' {
                    $k2 = [P]::F($p, 'kind')
                    if ($k2 -eq 'choke') { $tr.ChokeFeatures++ }
                    elseif ($k2 -eq 'water' -and [P]::F($p, 'crosses') -eq '1') { $tr.WaterCross++ }
                    elseif ($k2 -eq 'ford' -and [P]::F($p, 'onPath') -eq '1') { $tr.FordOnPath++ }
                }
                'CHOKEHOLD' {
                    if ($p[2] -eq 'bypassed') { $tr.ChokeBypass = $p[0] + ' ' + $p[3] + ' ' + [P]::F($p, 'why') }
                    else {
                        $tr.ChokeHold = [P]::F($p, 'plan')
                        if ($line -match 'outnumbered=(\S+) k=(\S+)') { $tr.ChokeOdds = 'r=' + $Matches[1] + ' k=' + $Matches[2] }
                    }
                }
                'BATTLEPOS' {
                    if ($p[2] -eq 'repick') {
                        $tr.BPRepick++
                        if ($line -match 'published=1\(move') { $tr.BPMoved++ }
                    }
                    else {
                        $tr.BattlePos = [P]::F($p, 'plan')
                        $tr.BPOdds = [P]::F($p, 'outnumbered')
                        $bw = [P]::F($p, 'winner'); if ($bw) { $tr.BPWinner = ($bw -split ' ')[0] }
                    }
                }
                'FLANKPLAN' {
                    $side = $null
                    if ($p[2] -eq 'replan') {
                        $tr.FlankReplan++; $side = $p[3]
                        if ($line -match 'n=10/10') { $tr.FlankCap = $true }
                    }
                    elseif ($p[2] -eq 'stale') { $tr.FlankStale++; $side = $p[3] }
                    else { $side = $p[2] }
                    $as = [P]::F($p, 'attackSide')
                    if ($side -and $as) {
                        $asv = $as; $paren = $asv.IndexOf('('); if ($paren -ge 0) { $asv = $asv.Substring(0, $paren) }
                        if (-not $tr.AttackSides.ContainsKey($side)) { $tr.AttackSides[$side] = New-Object 'System.Collections.Generic.List[object]' }
                        $tr.AttackSides[$side].Add(@([P]::T($p[0]), $asv))
                    }
                }
                'SPOTS' {
                    if ($p[2] -eq 'replan') { $tr.SpotsReplan++ } elseif ($p[2] -eq 'replanStop') { $tr.ReplanStop = $p[0] + ' ' + $p[3] }
                }
                'CAVALRY' { if ($p[2] -eq 'replan') { $tr.CavReplan++ } }
                'REPICK' {
                    $tr.Repick.Add($p[0] + ' ' + $p[2] + ' ' + $p[3] + ' ' + ($p[5..($p.Length - 1)] -join ' '))
                    $rsn = [P]::F($p, 'reason')
                    if ($rsn -and $rsn.StartsWith('balance')) { $tr.BalanceRepicks.Add([pscustomobject]@{ t = [P]::T($p[0]); team = $p[2]; switched = ([P]::F($p, 'switched') -eq '1'); reason = $rsn; tactics = $p[3] }) }
                }
                'FLANKUSE' {
                    if ($line -match 'followPlan') {
                        $tm = $p[2]
                        if (-not $tr.FollowPlan.ContainsKey($tm)) { $tr.FollowPlan[$tm] = 0 }
                        $tr.FollowPlan[$tm]++
                    }
                }
                'APPROACH' {
                    if ($p[2] -eq 'use') {
                        if ($p.Length -lt 7) { break }
                        $fi = [P]::V($p[4]); $fname = $fi; if ($FormName.ContainsKey($fi)) { $fname = $FormName[$fi] }
                        $tr.ApproachUses.Add([pscustomobject]@{ t = [P]::T($p[0]); team = $p[3]; fi = $fi; form = $fname; what = $p[5]; why = [P]::F($p, 'why') })
                    }
                    else {
                        $tr.Approach.Add([pscustomobject]@{ t = [P]::T($p[0]); kind = $p[2]; chosen = [P]::F($p, 'chosen'); gain = [P]::F($p, 'gain'); why = [P]::F($p, 'why'); reason = [P]::F($p, 'reason'); text = ($p[2..([math]::Min(4, $p.Length - 1))] -join ' ') })
                    }
                }
                'TACTICSUMMARY' { $tr.TacticSummary.Add(($p[2..($p.Length - 1)] -join ' ')) }
                'HEDGEHOG' { $tr.Hedgehog.Add($p[2] + ' ' + [P]::F($p, 'verdict')) }
                'REACT' {
                    if ($p.Length -lt 6) { break }
                    $tr.Reacts.Add([pscustomobject]@{ t = [P]::T($p[0]); team = $p[2]; kind = $p[3]; what = $p[4]; id = [P]::F($p, 'id'); victim = [P]::F($p, 'victim'); responder = [P]::F($p, 'responder'); mode = [P]::F($p, 'mode'); why = [P]::F($p, 'why') })
                }
                'BEHAVIOR' {
                    if ($p.Length -lt 6) { break }
                    $fi = [P]::V($p[3])
                    $why = ''; $point = ''
                    for ($i = 6; $i -lt $p.Length; $i++) {
                        if ($p[$i].StartsWith('why=')) { $why = $p[$i].Substring(4) }
                        elseif ($p[$i].StartsWith('point=') -or $p[$i].StartsWith('spot=')) { $point = [P]::V($p[$i]) }
                    }
                    $fname = $fi; if ($FormName.ContainsKey($fi)) { $fname = $FormName[$fi] }
                    $tr.Behaviors.Add([pscustomobject]@{ t = [P]::T($p[0]); team = $p[2]; fi = $fi; form = $fname; beh = $p[4]; what = $p[5]; point = $point; why = $why })
                }
            }
        }
    }

    Phase 'terrain log parsed'
    # ------------------------------------------------------------------ derived series
    $start = $tr.GatherT
    if ([double]::IsNaN($start)) { $start = $firstMove }
    if ([double]::IsNaN($start)) { $start = 0.0 }
    $end = $missionEnd
    $duration = $end - $start

    # per formation: sorted times
    $formTs = @{}
    foreach ($k in $forms.Keys) { $arr = New-Object double[] $forms[$k].Count; for ($i = 0; $i -lt $arr.Length; $i++) { $arr[$i] = $forms[$k][$i].t }; $formTs[$k] = $arr }

    # main infantry per team (largest Infantry-class formation per snapshot) and per side
    $teamMain = @{}; $teamMainTs = @{}
    $sideMainByTk = @{}
    foreach ($team in $teamSnaps.Keys) {
        $lst = New-Object 'System.Collections.Generic.List[object]'
        foreach ($snap in $teamSnaps[$team]) {
            $best = $null
            foreach ($s in $snap) { if ($s.cls -eq 'Infantry' -and $s.men -gt 0 -and ($best -eq $null -or $s.men -gt $best.men)) { $best = $s } }
            if ($best -eq $null) { foreach ($s in $snap) { if ($s.men -gt 0 -and ($best -eq $null -or $s.men -gt $best.men)) { $best = $s } } }
            if ($best -ne $null -and -not [double]::IsNaN($best.ax)) {
                $lst.Add($best)
                $side = $sideOf[$team]
                if (-not $sideMainByTk.ContainsKey($side)) { $sideMainByTk[$side] = @{} }
                $cur = $sideMainByTk[$side][$best.tk]
                if ($cur -eq $null -or $best.men -gt $cur.men) { $sideMainByTk[$side][$best.tk] = $best }
            }
        }
        $teamMain[$team] = $lst
        $a = New-Object double[] $lst.Count; for ($i = 0; $i -lt $a.Length; $i++) { $a[$i] = $lst[$i].t }; $teamMainTs[$team] = $a
    }
    $sideMain = @{}; $sideMainTs = @{}
    foreach ($side in $sideMainByTk.Keys) {
        $lst = @($sideMainByTk[$side].Values | Sort-Object t)
        $sideMain[$side] = $lst
        $a = New-Object double[] $lst.Count; for ($i = 0; $i -lt $a.Length; $i++) { $a[$i] = $lst[$i].t }; $sideMainTs[$side] = $a
    }
    $enemySide = @{ 'Attacker' = 'Defender'; 'Defender' = 'Attacker' }
    function EnemyMainAt([string]$side, [double]$t) {
        $es = $enemySide[$side]
        if (-not $es -or -not $sideMain.ContainsKey($es)) { return $null }
        $ix = [P]::Nearest($sideMainTs[$es], $t)
        if ($ix -lt 0) { return $null }
        $s = $sideMain[$es][$ix]
        if ([math]::Abs($s.t - $t) -gt 3) { return $null }
        return $s
    }
    function TacticAt([string]$team, [double]$t) {
        if (-not $tactics.ContainsKey($team)) { return '-' }
        $nm = '-'
        foreach ($e in $tactics[$team]) { if ($e[0] -le $t) { $nm = $e[1] } else { break } }
        return $nm
    }
    function Dist([double]$x1, [double]$y1, [double]$x2, [double]$y2) { return [math]::Sqrt(($x1 - $x2) * ($x1 - $x2) + ($y1 - $y2) * ($y1 - $y2)) }
    function FormLabel($s) { return $s.team + '/' + $s.form }

    Phase 'series built'
    # ------------------------------------------------------------------ header
    Say ('=' * 100)
    Say ('BATTLE ' + $stamp)
    Say ('  ai log      : ' + $aiPath)
    if ($tr.Present) { Say ('  terrain log : ' + $terrainPath) } else { Say '  terrain log : (none found - terrain checks skipped)' }
    $sceneTxt = $tr.Scene; if (-not $sceneTxt) { $sceneTxt = '?' }
    Say ('  scene ' + $sceneTxt)
    Say ('  battle start t=' + (N1 $start) + ' (deployment end), end t=' + (N1 $end) + ', fought ' + (N0 $duration) + ' s')
    foreach ($team in ($sideOf.Keys | Sort-Object)) {
        $cul = '?'; if ($culture.ContainsKey($team)) { $cul = $culture[$team] }
        $men = '?'; if ($menStart.ContainsKey($team)) { $men = $menStart[$team] }
        $pl = ''; if ($playerTeam[$team]) { $pl = ' [player team]' }
        $tac = '-'
        if ($tactics.ContainsKey($team)) { $tac = (($tactics[$team] | ForEach-Object { $_[1] + '@' + (N0 $_[0]) }) -join ' > ') }
        Say ('  ' + $team.PadRight(12) + ' ' + $sideOf[$team].PadRight(9) + ' culture=' + $cul + ' men=' + $men + $pl)
        Say ('      tactics: ' + $tac)
        $nAi = 0; $nAll = 0
        if ($teamMain.ContainsKey($team)) { foreach ($s in $teamMain[$team]) { if ($s.t -ge $start) { $nAll++; if ($s.ai) { $nAi++ } } } }
        if ($nAll -gt 0 -and $nAi -lt $nAll) { Say ('      main infantry AI-controlled in ' + $nAi + '/' + $nAll + ' samples after the start (player-controlled samples are not judged)') }
    }
    foreach ($ts in $tr.TacticSummary) { Say ('      ' + $ts) }
    if ($duration -lt $Cfg.MinBattleSeconds) { Say ('  NOTE: battle fought only ' + (N0 $duration) + ' s - most checks have nothing to judge') }
    Say ''

    # =================================================================================================
    # 1. Attacker progress
    # =================================================================================================
    $ev = New-Object 'System.Collections.Generic.List[string]'; $st = @()
    $judged = 0
    foreach ($team in $teamMain.Keys) {
        if ($sideOf[$team] -ne 'Attacker') { continue }
        $ser = @($teamMain[$team] | Where-Object { $_.t -ge $start + $Cfg.StartGrace })
        if ($ser.Count -lt 5) { continue }
        $aiShare = @($ser | Where-Object { $_.ai }).Count / [double]$ser.Count
        if ($aiShare -lt 0.5) { $ev.Add($team + ': main infantry player-controlled, not judged'); $st += 'INFO'; continue }
        $judged++
        # contact
        $contactT = [double]::NaN
        foreach ($s in $ser) { if ($s.dEn -le $Cfg.ContactDist) { $contactT = $s.t; break } }
        $n = $ser.Count
        $ts = New-Object double[] $n; $gap = New-Object double[] $n; $ex = New-Object double[] $n; $ey = New-Object double[] $n
        for ($i = 0; $i -lt $n; $i++) {
            $s = $ser[$i]; $ts[$i] = $s.t
            $e = EnemyMainAt 'Attacker' $s.t
            if ($e -eq $null) { $gap[$i] = [double]::NaN; $ex[$i] = [double]::NaN; $ey[$i] = [double]::NaN }
            else { $gap[$i] = Dist $s.ax $s.ay $e.ax $e.ay; $ex[$i] = $e.ax; $ey[$i] = $e.ay }
        }
        $limit = $n; if (-not [double]::IsNaN($contactT)) { for ($i = 0; $i -lt $n; $i++) { if ($ts[$i] -ge $contactT) { $limit = $i; break } } }
        # sliding windows (two pointers)
        $episodes = New-Object 'System.Collections.Generic.List[object]'
        $j = 0
        for ($i = 0; $i -lt $limit; $i++) {
            if ($j -lt $i) { $j = $i }
            while ($j -lt $limit -and $ts[$j] -lt $ts[$i] + $Cfg.StallWindow) { $j++ }
            if ($j -ge $limit) { break }
            if ([double]::IsNaN($gap[$i]) -or [double]::IsNaN($gap[$j]) -or $gap[$i] -lt $Cfg.StallMinGap) { continue }
            $shrink = ($gap[$i] - $gap[$j]) / $gap[$i]
            if ($shrink -lt $Cfg.StallMinShrink) {
                if ($episodes.Count -gt 0 -and $episodes[$episodes.Count - 1].j -ge $i) { $episodes[$episodes.Count - 1].j = $j }
                else { $episodes.Add([pscustomobject]@{ i = $i; j = $j }) }
            }
        }
        $closeS = ''
        if (-not [double]::IsNaN($contactT)) { $closeS = 'contact at t=' + (N0 $contactT) } else { $closeS = 'no contact (main infantry never within ' + $Cfg.ContactDist + ' m)' }
        if ($episodes.Count -eq 0) {
            $g0 = $gap[0]; $ev.Add($team + ': kept closing, gap ' + (N0 $g0) + ' m at t=' + (N0 $ts[0]) + ', ' + $closeS); $st += 'PASS'; continue
        }
        foreach ($epi in $episodes) {
            $a = $ser[$epi.i]; $b = $ser[$epi.j]
            $ux = $ex[$epi.i] - $a.ax; $uy = $ey[$epi.i] - $a.ay; $ul = [math]::Sqrt($ux * $ux + $uy * $uy); if ($ul -lt 0.001) { $ul = 1 }
            $adv = (($b.ax - $a.ax) * $ux + ($b.ay - $a.ay) * $uy) / $ul
            $behs = New-Object 'System.Collections.Generic.List[string]'; $ords = New-Object 'System.Collections.Generic.List[string]'; $tacs = New-Object 'System.Collections.Generic.List[string]'
            for ($k = $epi.i; $k -le $epi.j; $k++) {
                if (-not $behs.Contains($ser[$k].beh)) { $behs.Add($ser[$k].beh) }
                if (-not $ords.Contains($ser[$k].ord)) { $ords.Add($ser[$k].ord) }
                $tc = TacticAt $team $ser[$k].t; if (-not $tacs.Contains($tc)) { $tacs.Add($tc) }
            }
            $sev = 'FAIL'; $note = 'attacker did not advance'
            if ($adv -ge $Cfg.OwnAdvanceShare * $gap[$epi.i]) { $sev = 'WARN'; $note = 'attacker advanced but the gap held (defender withdrew?)' }
            $st += $sev
            $ev.Add($sev + ' ' + $team + '/' + $a.form + ' t=' + (N0 $a.t) + '..' + (N0 $b.t) + ' (' + (N0 ($b.t - $a.t)) + ' s): gap ' + (N0 $gap[$epi.i]) + ' -> ' + (N0 $gap[$epi.j]) + ' m, own advance ' + (N0 $adv) + ' m; ' + $note + '; behavior ' + ($behs -join '/') + ', order ' + ($ords -join '/') + ', tactic ' + ($tacs -join '/'))
        }
        $ev.Add($team + ': ' + $closeS)
    }
    if ($judged -eq 0 -and $st.Count -eq 0) { $st += 'INFO'; $ev.Add('no AI-controlled attacker main infantry after the start') }
    Add-Result '1' 'Attacker progress' (Worst $st) ('the main-to-main infantry gap must shrink >= ' + ($Cfg.StallMinShrink * 100) + '% over every ' + $Cfg.StallWindow + ' s before contact (FAIL = the attacker itself stood / walked back; WARN = it advanced but the gap held)') $ev

    # =================================================================================================
    # 2. Backward drift
    # =================================================================================================
    $ev = New-Object 'System.Collections.Generic.List[string]'; $st = @('PASS')
    foreach ($key in $forms.Keys) {
        $ser = $forms[$key]
        if ($ser.Count -lt 3) { continue }
        $team = $ser[0].team; $side = $sideOf[$team]
        $runs = @{ enemy = $null; order = $null }
        $miss = @{ enemy = 0; order = 0 }
        $found = New-Object 'System.Collections.Generic.List[object]'
        for ($i = 1; $i -lt $ser.Count; $i++) {
            $a = $ser[$i - 1]; $b = $ser[$i]
            $okStep = ($a.t -ge $start + $Cfg.StartGrace) -and $a.ai -and $b.ai -and ($a.cls -eq 'Infantry' -or $a.cls -eq 'Ranged') -and $a.men -ge 10 -and $a.ord -eq 'Move' -and ($b.t - $a.t) -le 3 -and ($b.t - $a.t) -gt 0
            $dt = $b.t - $a.t
            $vx = $b.ax - $a.ax; $vy = $b.ay - $a.ay
            $cond = @{ enemy = $false; order = $false }
            $amt = @{ enemy = 0.0; order = 0.0 }
            if ($okStep) {
                if ($side -eq 'Attacker' -and $a.beh -ne 'BehaviorRetreat') {
                    $e = EnemyMainAt $side $b.t
                    if ($e -ne $null) {
                        $da = Dist $a.ax $a.ay $e.ax $e.ay; $db = Dist $b.ax $b.ay $e.ax $e.ay
                        # ranged walking to an order point placed behind (an archer spot) is not judged here
                        $toOrd = ($vx * ($a.ox - $a.ax) + $vy * ($a.oy - $a.ay))
                        if (($db - $da) -gt $Cfg.DriftMinSpeed * $dt -and ($a.cls -eq 'Infantry' -or $toOrd -le 0)) { $cond.enemy = $true; $amt.enemy = $db - $da }
                    }
                }
                $tox = $a.ox - $a.ax; $toy = $a.oy - $a.ay; $tol = [math]::Sqrt($tox * $tox + $toy * $toy)
                if ($tol -ge $Cfg.DriftMinOrderDist) {
                    $along = ($vx * $tox + $vy * $toy) / $tol
                    if ($along -lt - $Cfg.DriftMinSpeed * $dt) { $cond.order = $true; $amt.order = - $along }
                }
            }
            foreach ($kind in @('enemy', 'order')) {
                if ($cond[$kind]) {
                    if ($runs[$kind] -eq $null) { $runs[$kind] = [pscustomobject]@{ t0 = $a.t; t1 = $b.t; m = 0.0; beh = $a.beh; a = $a; b = $b } }
                    $runs[$kind].t1 = $b.t; $runs[$kind].m += $amt[$kind]; $runs[$kind].b = $b; $miss[$kind] = 0
                }
                elseif ($runs[$kind] -ne $null) {
                    $miss[$kind]++
                    if ($miss[$kind] -gt 1 -or -not $okStep -or $i -eq $ser.Count - 1) {
                        $r = $runs[$kind]
                        if (($r.t1 - $r.t0) -ge $Cfg.DriftMinSeconds -and $r.m -ge $Cfg.DriftMinMetres) { $found.Add(@($kind, $r)) }
                        $runs[$kind] = $null; $miss[$kind] = 0
                    }
                }
            }
        }
        foreach ($kind in @('enemy', 'order')) {
            $r = $runs[$kind]
            if ($r -ne $null -and ($r.t1 - $r.t0) -ge $Cfg.DriftMinSeconds -and $r.m -ge $Cfg.DriftMinMetres) { $found.Add(@($kind, $r)) }
        }
        foreach ($f in $found) {
            $kind = $f[0]; $r = $f[1]; $a = $r.a
            $sev = 'WARN'
            if ($kind -eq 'enemy' -and $a.cls -eq 'Infantry') { $sev = 'FAIL' }
            $st += $sev
            $what = 'walked AWAY FROM THE ENEMY'; if ($kind -eq 'order') { $what = 'walked away from its own order point' }
            $ev.Add($sev + ' ' + $key.Replace('|', '/') + ' (' + $a.cls + ', ' + $a.men + ' men) t=' + (N0 $r.t0) + '..' + (N0 $r.t1) + ': ' + $what + ' ' + (N0 $r.m) + ' m under Move; behavior ' + $r.beh + ', tactic ' + (TacticAt $a.team $r.t0) + ', avg ' + (N0 $a.ax) + ',' + (N0 $a.ay) + ' -> ' + (N0 $r.b.ax) + ',' + (N0 $r.b.ay) + ', order ' + (N0 $a.ox) + ',' + (N0 $a.oy))
        }
    }
    Add-Result '2' 'Backward drift' (Worst $st) ('an AI foot formation under a Move order moving away from the enemy (attackers; infantry = FAIL) or from its own order point (WARN) for >= ' + $Cfg.DriftMinSeconds + ' s and >= ' + $Cfg.DriftMinMetres + ' m') $ev

    # =================================================================================================
    # 3. Flicker: order point reversals + behavior / order / arrangement flips
    # =================================================================================================
    $ev = New-Object 'System.Collections.Generic.List[string]'; $st = @('PASS')
    foreach ($key in $forms.Keys) {
        $ser = $forms[$key]
        $rev = New-Object 'System.Collections.Generic.List[double]'
        $prevJx = 0.0; $prevJy = 0.0; $hasPrev = $false; $jumps = 0
        for ($i = 1; $i -lt $ser.Count; $i++) {
            $a = $ser[$i - 1]; $b = $ser[$i]
            # foot only: a mounted formation's order point orbits / skirmishes by design (HA mode flicker is counted below)
            if ($b.t -lt $start + $Cfg.StartGrace -or -not $b.ai -or $a.ord -ne 'Move' -or $b.ord -ne 'Move' -or ($b.cls -ne 'Infantry' -and $b.cls -ne 'Ranged')) { $hasPrev = $false; continue }
            $jx = $b.ox - $a.ox; $jy = $b.oy - $a.oy; $jl = [math]::Sqrt($jx * $jx + $jy * $jy)
            if ($jl -lt $Cfg.FlickerJump) { continue }
            if ($a.beh -ne $b.beh) { continue }   # a behavior switch moves the order point by design (flips are counted below)
            $jumps++
            if ($hasPrev) {
                $pl = [math]::Sqrt($prevJx * $prevJx + $prevJy * $prevJy)
                if (($jx * $prevJx + $jy * $prevJy) -lt -0.5 * $jl * $pl) { $rev.Add($b.t) }
            }
            $prevJx = $jx; $prevJy = $jy; $hasPrev = $true
        }
        if ($rev.Count -ge $Cfg.FlickerWarnReversals) {
            $best = 0; $bestT = 0.0; $j = 0
            for ($i = 0; $i -lt $rev.Count; $i++) {
                while ($j -lt $i -and $rev[$i] - $rev[$j] -gt $Cfg.FlickerWindow) { $j++ }
                if (($i - $j + 1) -gt $best) { $best = $i - $j + 1; $bestT = $rev[$j] }
            }
            $sev = 'INFO'
            if ($best -gt $Cfg.FlickerReversals) { $sev = 'FAIL' } elseif ($best -ge $Cfg.FlickerWarnReversals) { $sev = 'WARN' }
            if ($sev -ne 'INFO') {
                $st += $sev
                $s0 = $ser[0]
                $ev.Add($sev + ' ' + $key.Replace('|', '/') + ': order point reversed ' + $best + 'x within ' + $Cfg.FlickerWindow + ' s from t=' + (N0 $bestT) + ' (' + $rev.Count + ' reversals, ' + $jumps + ' jumps > ' + $Cfg.FlickerJump + ' m in the battle); behavior then ' + ($ser[[P]::Nearest($formTs[$key], $bestT)].beh) + ', tactic ' + (TacticAt $s0.team $bestT))
            }
        }
    }
    # flips
    $flipSets = @{}
    foreach ($key in $behChanges.Keys) { if (-not $flipSets.ContainsKey($key)) { $flipSets[$key] = @{} }; $flipSets[$key]['behavior'] = $behChanges[$key] }
    foreach ($key in $ordChanges.Keys) { if (-not $flipSets.ContainsKey($key)) { $flipSets[$key] = @{} }; $flipSets[$key]['order'] = $ordChanges[$key] }
    foreach ($key in $haChanges.Keys) { if (-not $flipSets.ContainsKey($key)) { $flipSets[$key] = @{} }; $flipSets[$key]['haOrbit'] = $haChanges[$key] }
    foreach ($key in $forms.Keys) {
        $lst = New-Object 'System.Collections.Generic.List[object]'; $prev = $null
        foreach ($s in $forms[$key]) { if ($prev -ne $null -and $s.arr -ne $prev.arr -and $s.ai) { $lst.Add(@($s.t, $prev.arr, $s.arr)) }; $prev = $s }
        if ($lst.Count -gt 0) { if (-not $flipSets.ContainsKey($key)) { $flipSets[$key] = @{} }; $flipSets[$key]['arrangement'] = $lst }
    }
    foreach ($key in $flipSets.Keys) {
        # AI-controlled at all?
        if ($forms.ContainsKey($key)) { if (@($forms[$key] | Where-Object { $_.ai }).Count -eq 0) { continue } }
        foreach ($cat in $flipSets[$key].Keys) {
            $evs = @($flipSets[$key][$cat] | Where-Object { $_[0] -ge $start + $Cfg.StartGrace })
            if ($evs.Count -le $Cfg.FlipsWarn) { continue }
            $best = 0; $bi = 0; $j = 0
            for ($i = 0; $i -lt $evs.Count; $i++) {
                while ($j -lt $i -and $evs[$i][0] - $evs[$j][0] -gt $Cfg.FlipWindow) { $j++ }
                if (($i - $j + 1) -gt $best) { $best = $i - $j + 1; $bi = $j }
            }
            if ($best -le $Cfg.FlipsWarn) { continue }
            $sev = 'WARN'; if ($best -gt $Cfg.FlipsFail) { $sev = 'FAIL' }
            $st += $sev
            $sample = @(); for ($k = $bi; $k -lt [math]::Min($bi + 4, $evs.Count); $k++) { $sample += ((N1 $evs[$k][0]) + ' ' + $evs[$k][1] + '->' + $evs[$k][2]) }
            $ev.Add($sev + ' ' + $key.Replace('|', '/') + ': ' + $cat + ' changed ' + $best + 'x in ' + $Cfg.FlipWindow + ' s from t=' + (N0 $evs[$bi][0]) + ' (' + $evs.Count + ' in the battle): ' + ($sample -join '; '))
        }
    }
    Add-Result '3' 'Flicker' (Worst $st) ('foot order point reversing (> ' + $Cfg.FlickerJump + ' m jumps back and forth, >= ' + $Cfg.FlickerWarnReversals + ' in ' + $Cfg.FlickerWindow + ' s = WARN, > ' + $Cfg.FlickerReversals + ' = FAIL), or behavior / order kind / arrangement / HA orbit mode changing > ' + $Cfg.FlipsWarn + ' (WARN) / > ' + $Cfg.FlipsFail + ' (FAIL) times per ' + $Cfg.FlipWindow + ' s') $ev

    # =================================================================================================
    # 4. Charges
    # =================================================================================================
    $ev = New-Object 'System.Collections.Generic.List[string]'; $st = @('PASS')
    $charges = New-Object 'System.Collections.Generic.List[object]'
    foreach ($b in $tr.Behaviors) {
        $w = $b.what
        $isCharge = ($w -match '->Charge$') -or ($w -eq 'Hold->Engage') -or ($w -eq 'Holding->Committed')
        $isRecall = ($w -eq 'Engage->Hold')
        if (-not $isCharge -and -not $isRecall) { continue }
        $why = $b.why -replace '^(lineCharged|squareCharged)\s+', ''
        $reason = ($why -split '\s+')[0]
        $charges.Add([pscustomobject]@{ t = $b.t; team = $b.team; form = $b.form; beh = $b.beh; what = $w; reason = $reason; why = $b.why; recall = $isRecall })
    }
    # native AI-log charges of a holding defender line (behavior -> BehaviorCharge while far)
    foreach ($key in $behChanges.Keys) {
        foreach ($c in $behChanges[$key]) {
            if ($c[2] -ne 'BehaviorCharge' -or $c[0] -lt $start + $Cfg.StartGrace) { continue }
            if ($c[1] -notmatch 'Hold|Defend|Stop|ShieldWall|Hedgehog|ReservePlug') { continue }
            $kk = $key.Split('|')
            $charges.Add([pscustomobject]@{ t = $c[0]; team = $kk[0]; form = $kk[1]; beh = $c[1]; what = 'native ' + $c[1] + '->BehaviorCharge'; reason = 'native'; why = '(AI log CHANGE BEHAVIOR)'; recall = $false })
        }
    }
    $nCharge = 0
    foreach ($c in ($charges | Sort-Object t)) {
        $key = $c.team + '|' + $c.form
        $dEn = [double]::NaN; $men = ''
        if ($forms.ContainsKey($key)) { $ix = [P]::Nearest($formTs[$key], $c.t); if ($ix -ge 0) { $s = $forms[$key][$ix]; if ([math]::Abs($s.t - $c.t) -le 3) { $dEn = $s.dEn; $men = $s.men } } }
        if ($c.recall) { continue }
        $nCharge++
        $sev = 'INFO'; $note = ''
        if (-not [double]::IsNaN($dEn) -and $dEn -gt $Cfg.ChargeFarDist) {
            if ($c.reason -in @('outshot', 'advantage', 'behind')) { $sev = 'FAIL'; $note = ' <- ' + $c.reason + ' charge from ' + (N0 $dEn) + ' m' }
            else { $sev = 'WARN'; $note = ' <- engaged with the nearest enemy ' + (N0 $dEn) + ' m away' }
        }
        if ($sev -eq 'INFO' -and $c.why -match 'f\d+:(Ranged|HorseArcher)\b' -and $c.reason -in @('enemyNear', 'infantry', 'selfThreat', 'battleJoined')) { $sev = 'WARN'; $note = ' <- triggered by enemy missile troops (bait?)' }
        if ($sev -ne 'INFO') { $st += $sev }
        $ev.Add($sev + ' t=' + (N1 $c.t) + ' ' + $c.team + '/' + $c.form + ' ' + $c.beh + ' ' + $c.what + ' reason=' + $c.reason + ' distToEnemy=' + (N0 $dEn) + ' men=' + $men + $note + '  [' + $c.why + ']')
    }
    # recall loops
    $byForm = @{}
    foreach ($c in ($charges | Sort-Object t)) { $k = $c.team + '|' + $c.form; if (-not $byForm.ContainsKey($k)) { $byForm[$k] = New-Object 'System.Collections.Generic.List[object]' }; $byForm[$k].Add($c) }
    foreach ($k in $byForm.Keys) {
        $lst = $byForm[$k]; $loops = 0; $lastCharge = $null; $recalled = $false; $first = $null
        foreach ($c in $lst) {
            if ($c.recall) { if ($lastCharge -ne $null) { $recalled = $true }; continue }
            if ($lastCharge -ne $null -and $recalled -and ($c.t - $lastCharge.t) -lt $Cfg.RecallLoopGap) { $loops++; if ($first -eq $null) { $first = $lastCharge.t } }
            $lastCharge = $c; $recalled = $false
        }
        if ($loops -gt 0) {
            $sev = 'WARN'; if ($loops -gt $Cfg.RecallLoopMax) { $sev = 'FAIL' }
            $st += $sev
            $ev.Add($sev + ' ' + $k.Replace('|', '/') + ': charge -> recall -> charge loop ' + $loops + 'x (charges < ' + $Cfg.RecallLoopGap + ' s apart) from t=' + (N0 $first))
        }
    }
    # charge loops at one enemy formation (a late-charge line's ->Charge lines name their trigger formation)
    $byTarget = @{}
    foreach ($c in ($charges | Sort-Object t)) {
        if ($c.recall -or $c.what -notmatch '->Charge$' -or $c.reason -notin @('behind', 'infantry')) { continue }
        if ($c.why -notmatch '(f\d+:[A-Za-z]+)') { continue }
        $k = $c.team + '|' + $c.form + '|' + $Matches[1]
        if (-not $byTarget.ContainsKey($k)) { $byTarget[$k] = New-Object 'System.Collections.Generic.List[double]' }
        $byTarget[$k].Add($c.t)
    }
    foreach ($k in $byTarget.Keys) {
        $ts = $byTarget[$k]; $best = 0; $bt = 0.0; $j = 0
        for ($i = 0; $i -lt $ts.Count; $i++) { while ($j -lt $i -and $ts[$i] - $ts[$j] -gt $Cfg.ChargeLoopWindow) { $j++ }; if (($i - $j + 1) -gt $best) { $best = $i - $j + 1; $bt = $ts[$j] } }
        if ($best -ge $Cfg.ChargeLoopCount) {
            $kk = $k.Split('|')
            $st += 'WARN'
            $ev.Add('WARN ' + $kk[0] + '/' + $kk[1] + ': ' + $best + ' local charges at ' + $kk[2] + ' within ' + $Cfg.ChargeLoopWindow + ' s from t=' + (N0 $bt) + ' (' + $ts.Count + ' in the battle; bait loop?)')
        }
    }
    # charge STARTS from far (USER RULE 2026-10-22: the real charge only within ChargeStartDist m; ChargeGuard walks it there)
    $farStarts = 0; $starts = 0
    foreach ($key in $ordChanges.Keys) {
        if (-not $forms.ContainsKey($key)) { continue }
        foreach ($c in $ordChanges[$key]) {
            if ($c[0] -lt $start + $Cfg.StartGrace) { continue }
            if ($c[2] -notin @('Charge', 'ChargeToTarget') -or $c[1] -in @('Charge', 'ChargeToTarget')) { continue }
            # the last FORM sample at or before the change (after it the men already run)
            $ix = [P]::Nearest($formTs[$key], $c[0])
            if ($ix -lt 0) { continue }
            if ($forms[$key][$ix].t -gt $c[0] -and $ix -gt 0) { $ix-- }
            $s = $forms[$key][$ix]
            if ([math]::Abs($s.t - $c[0]) -gt 3 -or -not $s.ai -or $s.cls -in @('Cavalry', 'HorseArcher', 'LightCavalry', 'HeavyCavalry')) { continue }
            $starts++
            if ([double]::IsNaN($s.dEn) -or $s.dEn -le $Cfg.ChargeStartDist + $Cfg.ChargeStartSlack) { continue }
            $farStarts++
            $st += 'WARN'
            $ev.Add('WARN t=' + (N1 $c[0]) + ' ' + $key.Replace('|', '/') + ' ' + $c[1] + '->' + $c[2] + ' started with distToEnemy=' + (N0 $s.dEn) + ' m (> ' + $Cfg.ChargeStartDist + ' + ' + $Cfg.ChargeStartSlack + ') behavior ' + $s.beh + ' men=' + $s.men + ' <- charge start from far')
        }
    }
    $nAdv = 0; $nGo = 0; $nDrop = 0
    foreach ($b in $tr.Behaviors) {
        if ($b.beh -ne 'ChargeGuard') { continue }
        if ($b.what -eq 'charge advance') { $nAdv++ } elseif ($b.what -eq 'charge go') { $nGo++ } elseif ($b.what -eq 'charge drop') { $nDrop++ }
    }
    $startsTxt = 'charge starts of AI foot formations: ' + $starts + ', from > ' + ($Cfg.ChargeStartDist + $Cfg.ChargeStartSlack) + ' m: ' + $farStarts + '; ChargeGuard advance=' + $nAdv + ' go=' + $nGo + ' drop=' + $nDrop
    if ($farStarts -gt 0) { $ev.Insert(0, 'WARN ' + $startsTxt) } else { $ev.Add($startsTxt) }
    if ($nCharge -eq 0) { $ev.Add('no charge / engage transitions logged') }
    Add-Result '4' 'Charges' (Worst $st) ('every late-charge / engage transition with its reason; outshot / advantage / behind charges from > ' + $Cfg.ChargeFarDist + ' m = FAIL, other engages from that far or set off by enemy missile troops = WARN, charge-recall loops, >= ' + $Cfg.ChargeLoopCount + ' local charges at one enemy formation within ' + $Cfg.ChargeLoopWindow + ' s = WARN, an AI foot formation whose Charge / ChargeToTarget order STARTED with distToEnemy > ' + $Cfg.ChargeStartDist + ' + ' + $Cfg.ChargeStartSlack + ' m = WARN (USER RULE: the real charge only within 50 m)') $ev

    # =================================================================================================
    # 5. Plan churn
    # =================================================================================================
    $ev = New-Object 'System.Collections.Generic.List[string]'; $st = @('PASS')
    if (-not $tr.Present) { $st = @('INFO'); $ev.Add('no terrain log') }
    else {
        $flankCapTxt = ''; if ($tr.FlankCap) { $flankCapTxt = ' (cap 10 reached)' }
        $ev.Add('plans: FLANKPLAN replan=' + $tr.FlankReplan + $flankCapTxt + ' stale=' + $tr.FlankStale + ', SPOTS replan=' + $tr.SpotsReplan + ', CAVALRY replan=' + $tr.CavReplan + ', BATTLEPOS repick=' + $tr.BPRepick + ' (moved ' + $tr.BPMoved + '), replanStop ' + $tr.ReplanStop)
        foreach ($r in $tr.Repick) { $ev.Add('REPICK ' + $r) }
        foreach ($tm in $tr.FollowPlan.Keys) {
            $n = $tr.FollowPlan[$tm]
            $sev = 'INFO'; if ($n -gt $Cfg.FollowPlanMax) { $sev = 'WARN'; $st += 'WARN' }
            $ev.Add($sev + ' ' + $tm + ': tactic re-applied ' + $n + 'x for a newer flank plan (FLANKUSE followPlan)')
        }
        foreach ($side in $tr.AttackSides.Keys) {
            $lst = $tr.AttackSides[$side]; $flips = 0; $seq = @()
            for ($i = 0; $i -lt $lst.Count; $i++) {
                if ($i -eq 0 -or $lst[$i][1] -ne $lst[$i - 1][1]) { $seq += ($lst[$i][1] + '@' + (N0 $lst[$i][0])) }
                if ($i -gt 0 -and $lst[$i][1] -ne $lst[$i - 1][1]) { $flips++ }
            }
            if ($flips -gt $Cfg.AttackSideFlipsMax) { $st += 'WARN'; $ev.Add('WARN ' + $side + ': flank plan attackSide changed ' + $flips + 'x: ' + ($seq -join ' > ')) }
            elseif ($flips -gt 0) { $ev.Add($side + ': attackSide ' + ($seq -join ' > ')) }
        }
        # per formation take/release churn
        $cats = @{}
        foreach ($b in $tr.Behaviors) {
            $w = $b.what; $cat = $null
            if ($w -eq 'cohesion') { if ($b.why -match '^(pull|unpull)') { $cat = 'cohesion' } }
            elseif ($w -match '^cover (take|release|change)') { $cat = 'cover' }
            elseif ($w -eq 'take' -or $w -eq 'release') { $cat = 'spot' }
            elseif ($w -match '^(take|release)Lane$') { $cat = 'lane' }
            elseif ($w -match '^reform (take|change)$') { $cat = 'reform' }
            elseif ($w -eq 'shift' -or $w -eq 'unshift') { $cat = 'shift' }
            elseif ($w -eq 'Holding->Moving' -or $w -eq 'Moving->Holding') { $cat = 'holdMove' }
            elseif ($b.beh -eq 'Hedgehog' -and $w -match '^(square|circle|line)$') { $cat = 'hedgehogShape' }
            elseif ($w -eq 'shieldWall' -or $w -eq 'line') { $cat = 'shieldWall' }
            if (-not $cat) { continue }
            $k = $b.team + '/' + $b.form + '|' + $cat
            if (-not $cats.ContainsKey($k)) { $cats[$k] = New-Object 'System.Collections.Generic.List[double]' }
            $cats[$k].Add($b.t)
        }
        $totals = @{}
        foreach ($k in ($cats.Keys | Sort-Object)) {
            $ts = $cats[$k]; $cat = $k.Split('|')[1]
            if (-not $totals.ContainsKey($cat)) { $totals[$cat] = 0 }; $totals[$cat] += $ts.Count
            $best = 0; $bt = 0.0; $j = 0
            for ($i = 0; $i -lt $ts.Count; $i++) { while ($j -lt $i -and $ts[$i] - $ts[$j] -gt $Cfg.ChurnWindow) { $j++ }; if (($i - $j + 1) -gt $best) { $best = $i - $j + 1; $bt = $ts[$j] } }
            if ($best -gt $Cfg.ChurnMaxPerWindow) { $st += 'WARN'; $ev.Add('WARN ' + $k.Split('|')[0] + ': ' + $cat + ' take/release ' + $best + 'x in ' + $Cfg.ChurnWindow + ' s from t=' + (N0 $bt) + ' (' + $ts.Count + ' in the battle)') }
        }
        if ($totals.Count -gt 0) { $ev.Add('per-kind totals: ' + (($totals.Keys | Sort-Object | ForEach-Object { $_ + '=' + $totals[$_] }) -join ' ')) }
    }
    Add-Result '5' 'Plan churn' (Worst $st) ('re-plans / re-picks / take-release counts; WARN when one formation takes and lets go of one kind of place > ' + $Cfg.ChurnMaxPerWindow + 'x per ' + $Cfg.ChurnWindow + ' s, the flank attack side flips > ' + $Cfg.AttackSideFlipsMax + 'x, or a tactic is re-applied for new flank plans > ' + $Cfg.FollowPlanMax + 'x') $ev

    # =================================================================================================
    # 6. Cohesion
    # =================================================================================================
    $ev = New-Object 'System.Collections.Generic.List[string]'; $st = @('PASS')
    # REACT task spans (assign .. end, else the mission end) per "team|formation name": a formation on a reaction task
    # left its place on purpose (check 10 judges it).
    $reactSpans = @{}
    foreach ($a in $tr.Reacts) {
        if ($a.what -ne 'assign' -or -not $a.responder -or $a.responder -notmatch '^f(\d+):') { continue }
        $fi = $Matches[1]; $fname = $fi; if ($FormName.ContainsKey($fi)) { $fname = $FormName[$fi] }
        $t1 = $end
        foreach ($b in $tr.Reacts) { if ($b.what -eq 'end' -and $b.team -eq $a.team -and $b.id -eq $a.id -and $b.responder -eq $a.responder -and $b.t -ge $a.t) { $t1 = $b.t; break } }
        $k = $a.team + '|' + $fname
        if (-not $reactSpans.ContainsKey($k)) { $reactSpans[$k] = New-Object 'System.Collections.Generic.List[object]' }
        $reactSpans[$k].Add(@($a.t, $t1))
    }
    function InReact([string]$team, [string]$form, [double]$t) {
        $k = $team + '|' + $form
        if (-not $reactSpans.ContainsKey($k)) { return $false }
        foreach ($sp in $reactSpans[$k]) { if ($t -ge $sp[0] -and $t -le $sp[1] + 2) { return $true } }
        return $false
    }
    foreach ($team in $teamSnaps.Keys) {
        $side = $sideOf[$team]
        $limit = $Cfg.CohesionDefender; if ($side -eq 'Attacker') { $limit = $Cfg.CohesionAttacker }
        $runs = @{}; $found = New-Object 'System.Collections.Generic.List[object]'
        foreach ($snap in $teamSnaps[$team]) {
            if ($snap.Count -eq 0 -or $snap[0].t -lt $start + $Cfg.StartGrace) { continue }
            # The main = the team's largest Infantry formation, AI- or PLAYER-led (FormationCohesion.TeamMain since
            # 2026-10-22: AI archers keep to the player's line too).
            $main = $null
            foreach ($s in $snap) { if ($s.cls -eq 'Infantry' -and ($main -eq $null -or $s.men -gt $main.men)) { $main = $s } }
            if ($main -eq $null -or [double]::IsNaN($main.ax)) { continue }
            $e = EnemyMainAt $side $main.t
            $fx = 1.0; $fy = 0.0
            if ($e -ne $null) { $fx = $e.ax - $main.ax; $fy = $e.ay - $main.ay; $fl = [math]::Sqrt($fx * $fx + $fy * $fy); if ($fl -gt 0.01) { $fx /= $fl; $fy /= $fl } else { $fx = 1.0; $fy = 0.0 } }
            $seen = @{}
            foreach ($s in $snap) {
                if ($s -eq $main -or -not $s.ai -or $s.men -lt 10 -or ($s.cls -ne 'Infantry' -and $s.cls -ne 'Ranged')) { continue }
                $k = $s.form; $seen[$k] = $true
                $exempt = ($s.ord -match 'Charge|Retreat') -or ($main.ord -match 'Charge|Retreat') -or ($s.dEn -le $Cfg.MeleeDist) -or ($main.dEn -le $Cfg.MeleeDist) -or ($s.beh -eq 'RBMBehaviorHedgehog') -or ($s.beh -eq 'RBMBehaviorReact') -or (InReact $team $s.form $s.t)
                $lim = $limit; if ($s.beh -eq 'RBMBehaviorReservePlug') { $lim = $Cfg.CohesionReserve }
                $dx = $s.ax - $main.ax; $dy = $s.ay - $main.ay
                $along = [math]::Abs($dx * $fx + $dy * $fy) - ($s.d + $main.d) / 2
                $across = [math]::Abs(- $dx * $fy + $dy * $fx) - ($s.w + $main.w) / 2
                if ($along -lt 0) { $along = 0 }; if ($across -lt 0) { $across = 0 }
                $g = [math]::Sqrt($along * $along + $across * $across)
                if (-not $exempt -and $g -gt $lim) {
                    $mainDesc = $main.beh; if (-not $main.ai) { $mainDesc = 'player-led ' + $main.form }
                    if (-not $runs.ContainsKey($k) -or $runs[$k] -eq $null) { $runs[$k] = [pscustomobject]@{ t0 = $s.t; t1 = $s.t; max = $g; beh = $s.beh; cls = $s.cls; lim = $lim; mainBeh = $mainDesc } }
                    $runs[$k].t1 = $s.t; if ($g -gt $runs[$k].max) { $runs[$k].max = $g }
                }
                elseif ($runs.ContainsKey($k) -and $runs[$k] -ne $null) {
                    $r = $runs[$k]; if (($r.t1 - $r.t0) -ge $Cfg.CohesionSeconds) { $found.Add(@($k, $r)) }; $runs[$k] = $null
                }
            }
            foreach ($k in @($runs.Keys)) { if (-not $seen.ContainsKey($k) -and $runs[$k] -ne $null) { $r = $runs[$k]; if (($r.t1 - $r.t0) -ge $Cfg.CohesionSeconds) { $found.Add(@($k, $r)) }; $runs[$k] = $null } }
        }
        foreach ($k in @($runs.Keys)) { $r = $runs[$k]; if ($r -ne $null -and ($r.t1 - $r.t0) -ge $Cfg.CohesionSeconds) { $found.Add(@($k, $r)) } }
        foreach ($f in $found) {
            $r = $f[1]
            $st += 'WARN'
            $ev.Add('WARN ' + $team + '/' + $f[0] + ' (' + $r.cls + ', ' + $r.beh + ') t=' + (N0 $r.t0) + '..' + (N0 $r.t1) + ': up to ' + (N0 $r.max) + ' m edge gap from the main infantry (limit ' + $r.lim + ' m, main ' + $r.mainBeh + ')')
        }
    }
    Add-Result '6' 'Cohesion' (Worst $st) ('AI foot formations must stay within ' + $Cfg.CohesionDefender + ' m (defender) / ' + $Cfg.CohesionAttacker + ' m (attacker) / ' + $Cfg.CohesionReserve + ' m (reserve plug) edge gap of their main infantry (largest Infantry formation, AI- or player-led) outside melee and outside a REACT task; WARN after' + $Cfg.CohesionSeconds + ' s beyond (rectangles from FORM w/d, facing = toward the enemy main)') $ev

    # =================================================================================================
    # 7. Idle army / stand-off
    # =================================================================================================
    $ev = New-Object 'System.Collections.Generic.List[string]'; $st = @('PASS')
    if ($sideMain.ContainsKey('Attacker') -and $sideMain.ContainsKey('Defender')) {
        function StaticAt([string]$side, [double]$t) {
            $ts = $sideMainTs[$side]; $i = [P]::Nearest($ts, $t); $j = [P]::Nearest($ts, $t - $Cfg.IdleSpan)
            if ($i -lt 0 -or $j -lt 0 -or $i -eq $j) { return $false }
            $a = $sideMain[$side][$j]; $b = $sideMain[$side][$i]
            return ((Dist $a.ax $a.ay $b.ax $b.ay) -lt $Cfg.IdleMoveMetres)
        }
        $run = $null; $runA = $null; $standoffs = New-Object 'System.Collections.Generic.List[object]'; $atkIdle = New-Object 'System.Collections.Generic.List[object]'
        foreach ($a in $sideMain['Attacker']) {
            if ($a.t -lt $start + $Cfg.IdleSpan) { continue }
            $d = EnemyMainAt 'Attacker' $a.t
            if ($d -eq $null) { continue }
            $gap = Dist $a.ax $a.ay $d.ax $d.ay
            $sa = StaticAt 'Attacker' $a.t; $sd = StaticAt 'Defender' $a.t
            if ($gap -gt $Cfg.IdleDist -and $sa -and $sd) { if ($run -eq $null) { $run = [pscustomobject]@{ t0 = $a.t; t1 = $a.t; gap = $gap; ab = $a.beh; db = $d.beh } }; $run.t1 = $a.t }
            elseif ($run -ne $null) { if (($run.t1 - $run.t0) -ge $Cfg.IdleSeconds) { $standoffs.Add($run) }; $run = $null }
            if ($gap -gt $Cfg.IdleDist -and $sa -and $a.ai) { if ($runA -eq $null) { $runA = [pscustomobject]@{ t0 = $a.t; t1 = $a.t; gap = $gap; ab = $a.beh } }; $runA.t1 = $a.t }
            elseif ($runA -ne $null) { if (($runA.t1 - $runA.t0) -ge $Cfg.IdleSeconds) { $atkIdle.Add($runA) }; $runA = $null }
        }
        if ($run -ne $null -and ($run.t1 - $run.t0) -ge $Cfg.IdleSeconds) { $standoffs.Add($run) }
        if ($runA -ne $null -and ($runA.t1 - $runA.t0) -ge $Cfg.IdleSeconds) { $atkIdle.Add($runA) }
        foreach ($r in $standoffs) { $st += 'FAIL'; $ev.Add('FAIL stand-off t=' + (N0 $r.t0) + '..' + (N0 $r.t1) + ' (' + (N0 ($r.t1 - $r.t0)) + ' s): both main infantries static ' + (N0 $r.gap) + ' m apart; attacker ' + $r.ab + ', defender ' + $r.db) }
        foreach ($r in $atkIdle) { $st += 'WARN'; $ev.Add('WARN attacker main infantry static t=' + (N0 $r.t0) + '..' + (N0 $r.t1) + ' (' + (N0 ($r.t1 - $r.t0)) + ' s) ' + (N0 $r.gap) + ' m from the enemy; behavior ' + $r.ab) }
    }
    else { $st = @('INFO'); $ev.Add('one side has no main infantry samples') }
    Add-Result '7' 'Idle army' (Worst $st) ('both main infantries static (< ' + $Cfg.IdleMoveMetres + ' m per ' + $Cfg.IdleSpan + ' s) > ' + $Cfg.IdleDist + ' m apart for > ' + $Cfg.IdleSeconds + ' s = stand-off FAIL; the AI attacker alone static that long = WARN') $ev

    # =================================================================================================
    # 8. Terrain read sanity
    # =================================================================================================
    $ev = New-Object 'System.Collections.Generic.List[string]'; $st = @('PASS')
    if (-not $tr.Present) { $st = @('WARN'); $ev.Add('no terrain log for this battle (battleTerrainLoggingEnabled off?)') }
    else {
        if ([double]::IsNaN($tr.PublishT)) { $st += 'FAIL'; $ev.Add('FAIL terrain tactic factors never published (no "# terrain tactic factors published" line)') }
        if ($tr.Skipped) { $st += 'WARN'; $ev.Add('WARN TERRAIN ' + $tr.Skipped) }
        $ev.Add('sameGround=' + $tr.SameGround + ', published t=' + (N1 $tr.PublishT) + ' (gather t=' + (N1 $tr.GatherT) + ')')
        if ($tr.Features) { $ev.Add('FEATURES ' + $tr.Features) } else { $st += 'WARN'; $ev.Add('WARN no FEATURES line') }
        $ev.Add('choke features=' + $tr.ChokeFeatures + ', water crossing the axis=' + $tr.WaterCross + ', fords on the path=' + $tr.FordOnPath + ', bridge cells=' + $(if ($null -ne $tr.BridgeCells) { $tr.BridgeCells } else { '-' }))
        if ($null -ne $tr.BridgeCells) {
            if ($tr.WaterCross -gt 0 -and $tr.SameGround -eq '1' -and $tr.BridgeCells -eq '0' -and $tr.FordOnPath -eq 0) { $st += 'WARN'; $ev.Add('WARN water crosses the axis and the armies share ground, but no bridge cell and no ford on the path: the grid may not see the crossing') }
        }
        else { $ev.Add('no TERRAIN_WATER line (older build?)') }
        if ($tr.SameGround -eq '0') { $st += 'WARN'; $ev.Add('WARN sameGround=0: the armies stand on separate walkable grounds') }
        $ev.Add('CHOKEHOLD plan=' + $tr.ChokeHold + ', BATTLEPOS plan=' + $tr.BattlePos)
        if ($tr.BPOdds -or $tr.ChokeOdds) { $ev.Add('INFO defender outnumbered ' + $(if ($tr.BPOdds) { $tr.BPOdds } else { '-' }) + '; position winner=' + $(if ($tr.BPWinner) { $tr.BPWinner } else { '-' }) + '; choke ' + $(if ($tr.ChokeOdds) { $tr.ChokeOdds } else { '-' })) }
        if ($tr.ChokeBypass) { $ev.Add('CHOKEHOLD bypassed t=' + $tr.ChokeBypass) }
        foreach ($h in $tr.Hedgehog) { $ev.Add('HEDGEHOG ' + $h) }
    }
    Add-Result '8' 'Terrain read' (Worst $st) 'the terrain snapshot was read and published; feature / choke counts and map hint for a sanity look' $ev

    # =================================================================================================
    # 9. Errors
    # =================================================================================================
    $ev = New-Object 'System.Collections.Generic.List[string]'; $st = @('PASS')
    foreach ($e in $aiErrors) { $st += 'FAIL'; $ev.Add('FAIL ai: ' + $e) }
    foreach ($e in $tr.Errors) {
        $sev = 'WARN'; if ($e -match '^# FAILED|Exception') { $sev = 'FAIL' }
        $st += $sev; $ev.Add($sev + ' terrain: ' + $e)
    }
    if ($ev.Count -eq 0) { $ev.Add('no failed= / error= / exception text') }
    Add-Result '9' 'Errors' (Worst $st) 'failed= / error= fields, "# FAILED" lines and exception text in either log' $ev

    # =================================================================================================
    # 10. Reactions (a: the monitor's REACT lines; b: FORM-line fallback, works with the monitor off)
    # =================================================================================================
    $ev = New-Object 'System.Collections.Generic.List[string]'; $st = @('PASS')
    $reacts = $tr.Reacts
    if ($tr.Present -and $reacts.Count -gt 0) {
        $counts = @{}
        foreach ($r in $reacts) { $kk = $r.kind + ' ' + $r.what; if (-not $counts.ContainsKey($kk)) { $counts[$kk] = 0 }; $counts[$kk]++ }
        $ev.Add('REACT lines: ' + (($counts.Keys | Sort-Object | ForEach-Object { $_ + '=' + $counts[$_] }) -join ', '))
        $byId = @{}
        foreach ($r in $reacts) { if (-not $r.id) { continue }; $k = $r.team + '#' + $r.id; if (-not $byId.ContainsKey($k)) { $byId[$k] = New-Object 'System.Collections.Generic.List[object]' }; $byId[$k].Add($r) }
        # (a1) an R1 / R2 event nobody answered in time. When every candidate was refused for what it IS (the victim, the
        # main infantry, ranged, horse archers, the player's, too few men, the general, no AI ...), nobody could have
        # answered: counted as INFO (an army of one line and its archers), not a miss. Calmer cavalry (USER RULE 2026-10-22):
        # weaker riders refused against enemy riders (weakVsMounted) and the defender's mounted-task cooldown are by rule.
        # The Embolon wedge is never a responder (wedge, USER RULE 2026-10-23).
        $structural = @('victim', 'player', 'fewMen', 'general', 'mainInfantry', 'horseArchers', 'ranged', 'class', 'notCavalry', 'noAI', 'noBehavior',
            'weakVsMounted', 'mountedCooldown', 'wedge')
        $misses = 0; $noPossible = 0
        foreach ($k in $byId.Keys) {
            $lst = $byId[$k]
            $det = $null; $clr = $null
            foreach ($r in $lst) { if ($r.what -eq 'detect' -and -not $det) { $det = $r }; if ($r.what -eq 'clear' -and -not $clr) { $clr = $r } }
            if (-not $det -or ($det.kind -ne 'R1' -and $det.kind -ne 'R2')) { continue }
            $tEnd = $end; if ($clr) { $tEnd = $clr.t }
            if (($tEnd - $det.t) -lt $Cfg.ReactEventMin) { continue }
            $win = $Cfg.ReactWindowR1; if ($det.kind -eq 'R2') { $win = $Cfg.ReactWindowR2 }
            $answered = $false; $none = $null
            foreach ($r in $lst) {
                if ($r.t -gt $det.t + $win) { continue }
                if ($r.what -eq 'assign' -or $r.what -eq 'covered' -or ($r.what -eq 'line' -and $r.why -notlike 'late*')) { $answered = $true }
                if ($r.what -eq 'none' -and -not $none) { $none = $r }
            }
            if (-not $answered) {
                $nw = ''; if ($none) { $nw = ' [' + $none.why + ']' }
                # noResponder fA:Class=why fB:Class=why ...: all structural = nobody could have answered
                $allStructural = $false
                if ($none -and $none.why -match '^noResponder\s+(.+)$') {
                    $allStructural = $true
                    foreach ($tok in ($Matches[1].Trim() -split '\s+')) {
                        $eq = $tok.IndexOf('='); if ($eq -lt 0) { continue }
                        $rw = $tok.Substring($eq + 1); $paren = $rw.IndexOf('('); if ($paren -ge 0) { $rw = $rw.Substring(0, $paren) }
                        if ($structural -notcontains $rw) { $allStructural = $false; break }
                    }
                }
                if ($allStructural) { $noPossible++; continue }
                $misses++
                $ev.Add('WARN ' + $det.team + ' ' + $det.kind + ' id=' + $det.id + ' t=' + (N1 $det.t) + ': own formation ' + $det.victim + ' attacked, nobody responded within ' + $win + ' s (event lasted ' + (N0 ($tEnd - $det.t)) + ' s)' + $nw)
            }
        }
        if ($noPossible -gt 0) { $ev.Add('INFO ' + $noPossible + ' R1 / R2 event(s) with no possible responder (every formation was the victim / main infantry / ranged / player / too small): not judged') }
        # USER RULE 2026-10-23: riders straight in front of the army are not answered unless they charge right in ("R1|R2 skip ... frontalCavalry")
        $frontal = @($reacts | Where-Object { $_.what -eq 'skip' -and $_.why -like 'frontalCavalry*' })
        if ($frontal.Count -gt 0) {
            $fr1 = @($frontal | Where-Object { $_.kind -eq 'R1' }).Count; $fr2 = @($frontal | Where-Object { $_.kind -eq 'R2' }).Count
            $ev.Add('INFO ' + $frontal.Count + ' skip(s) for frontal enemy cavalry (R1=' + $fr1 + ' R2=' + $fr2 + '; in front of the army, not charging in): not answered by rule')
        }
        if ($misses -gt $Cfg.ReactMissFail) { $st += 'FAIL' } elseif ($misses -gt 0) { $st += 'WARN' }
        # (a2) a task that never ended
        $assigns = @($reacts | Where-Object { $_.what -eq 'assign' })
        $ends = @($reacts | Where-Object { $_.what -eq 'end' })
        foreach ($a in $assigns) {
            $hasEnd = $false
            foreach ($e2 in $ends) { if ($e2.team -eq $a.team -and $e2.id -eq $a.id -and $e2.responder -eq $a.responder -and $e2.t -ge $a.t) { $hasEnd = $true; break } }
            if (-not $hasEnd -and ($end - $a.t) -gt $Cfg.ReactTaskMax) { $st += 'WARN'; $ev.Add('WARN ' + $a.team + ' ' + $a.kind + ' id=' + $a.id + ' t=' + (N1 $a.t) + ': ' + $a.mode + ' task of ' + $a.responder + ' never ended (no REACT end line)') }
        }
        # (a3) ping-pong: one responder assigned again and again
        $byResp = @{}
        foreach ($a in $assigns) { $k = $a.team + ' ' + $a.responder; if (-not $byResp.ContainsKey($k)) { $byResp[$k] = New-Object 'System.Collections.Generic.List[double]' }; $byResp[$k].Add($a.t) }
        foreach ($k in $byResp.Keys) {
            $ts = $byResp[$k]; $best = 0; $bt = 0.0; $j = 0
            for ($i = 0; $i -lt $ts.Count; $i++) { while ($j -lt $i -and $ts[$i] - $ts[$j] -gt $Cfg.ReactPingWindow) { $j++ }; if (($i - $j + 1) -gt $best) { $best = $i - $j + 1; $bt = $ts[$j] } }
            if ($best -gt $Cfg.ReactPingPong) { $st += 'WARN'; $ev.Add('WARN ' + $k + ': assigned ' + $best + 'x in ' + $Cfg.ReactPingWindow + ' s from t=' + (N0 $bt) + ' (ping-pong)') }
        }
        # (a4) leashed / chase-timed-out tasks
        if ($ends.Count -gt 0) {
            $why = @{}
            foreach ($e2 in $ends) { $w = ($e2.why -split '\s+')[0]; if (-not $why.ContainsKey($w)) { $why[$w] = 0 }; $why[$w]++ }
            $ev.Add('task ends: ' + (($why.Keys | Sort-Object | ForEach-Object { $_ + '=' + $why[$_] }) -join ' '))
            $le = 0; foreach ($w in @('leash', 'chaseTimeout')) { if ($why.ContainsKey($w)) { $le += $why[$w] } }
            if ($ends.Count -ge $Cfg.ReactLeashMinEnds -and ($le / [double]$ends.Count) -gt $Cfg.ReactLeashShare) { $st += 'WARN'; $ev.Add('WARN ' + $le + ' of ' + $ends.Count + ' tasks ended by leash / chaseTimeout (the monitor sends formations it cannot use)') }
        }
        # (a5) R3 hunts: how they went (counted with the other tasks above)
        $hunts = @($assigns | Where-Object { $_.mode -eq 'Hunt' })
        if ($hunts.Count -gt 0) {
            $hEnds = @($ends | Where-Object { $_.mode -eq 'Hunt' })
            $hw = @{}
            foreach ($e2 in $hEnds) { $w = ($e2.why -split '\s+')[0]; if (-not $hw.ContainsKey($w)) { $hw[$w] = 0 }; $hw[$w]++ }
            $ev.Add('hunts (R3): ' + $hunts.Count + ' assigned, ends: ' + (($hw.Keys | Sort-Object | ForEach-Object { $_ + '=' + $hw[$_] }) -join ' '))
        }
        # (a6) Deter screens (R7 shadow: riders parked round our flank / rear; R3 on riding horse archers, also a hunt
        # turned "Hunt->Deter haRunning") and the task hand-overs / held screens when an event closed (gap fixes 2026-10-22)
        $shadowDet = @($reacts | Where-Object { $_.kind -eq 'R7' -and $_.what -eq 'detect' })
        $deters = @($assigns | Where-Object { $_.mode -eq 'Deter' })
        $redirects = @($reacts | Where-Object { $_.what -eq 'phase' -and $_.why -like 'Hunt->Deter*' })
        if ($shadowDet.Count -gt 0 -or $deters.Count -gt 0 -or $redirects.Count -gt 0) {
            $dEnds = @($ends | Where-Object { $_.mode -eq 'Deter' })
            $dw = @{}
            foreach ($e2 in $dEnds) { $w = ($e2.why -split '\s+')[0]; if (-not $dw.ContainsKey($w)) { $dw[$w] = 0 }; $dw[$w]++ }
            $strikes = @($reacts | Where-Object { $_.what -eq 'phase' -and $_.mode -eq 'Deter' -and $_.why -like 'Approach->Strike*' }).Count
            $releases = @($reacts | Where-Object { $_.what -eq 'phase' -and $_.mode -eq 'Deter' -and $_.why -like '*release*' }).Count
            $ev.Add('deter: R7 shadows detected=' + $shadowDet.Count + ', Deter tasks assigned=' + $deters.Count + ' (+' + $redirects.Count + ' hunts turned Deter), strikes=' + $strikes + ' releases=' + $releases + ', ends: ' + (($dw.Keys | Sort-Object | ForEach-Object { $_ + '=' + $dw[$_] }) -join ' '))
        }
        $handoffs = @($reacts | Where-Object { $_.what -eq 'clear' -and $_.why -like '*task=handoff*' }).Count
        $held = @($reacts | Where-Object { $_.what -eq 'clear' -and $_.why -like '*task=screenHeld*' }).Count
        $sides = @($reacts | Where-Object { $_.what -eq 'phase' -and $_.why -like 'side *' }).Count
        if ($handoffs -gt 0 -or $held -gt 0 -or $sides -gt 0) { $ev.Add('event closed with a live task: handoff=' + $handoffs + ' screenHeld=' + $held + '; stage side re-picks=' + $sides) }
    }
    elseif ($tr.Present) { $ev.Add('no REACT lines (aiReactionsEnabled off, an older build, or nothing to react to): 10b only') }
    else { $ev.Add('no terrain log: 10b only') }

    # (b) FORM lines: an AI formation in melee losing men while a free non-ranged AI formation of its team stood near and did not come
    $snapByTk = @{}
    foreach ($team in $teamSnaps.Keys) { foreach ($snap in $teamSnaps[$team]) { if ($snap.Count -gt 0) { $snapByTk[$team + '|' + $snap[0].tk] = $snap } } }
    $nb = 0
    foreach ($key in $forms.Keys) {
        $ser = $forms[$key]
        if ($ser.Count -lt 3) { continue }
        $team = $ser[0].team
        $ts = $formTs[$key]
        $i = 0
        while ($i -lt $ser.Count) {
            $s = $ser[$i]
            if ($s.t -lt $start + $Cfg.StartGrace -or -not $s.ai -or $s.men -lt 10 -or [double]::IsNaN($s.dEn) -or $s.dEn -gt $Cfg.MeleeDist -or [double]::IsNaN($s.ax)) { $i++; continue }
            # the main infantry's clash and the lines that fight in place by rule are not "attacked" (R1's exclusions)
            if ($s.beh -eq 'RBMBehaviorHedgehog' -or $s.beh -eq 'RBMBehaviorHoldChoke') { $i++; continue }
            $snap = $snapByTk[$team + '|' + $s.tk]
            if (-not $snap) { $i++; continue }
            $main = $null
            foreach ($o in $snap) { if ($o.cls -eq 'Infantry' -and $o.ai -and ($main -eq $null -or $o.men -gt $main.men)) { $main = $o } }
            if ($main -ne $null -and $main.form -eq $s.form) { $i++; continue }
            $j = [P]::Nearest($ts, $s.t + $Cfg.HelpLossWindow)
            $s2 = $ser[$j]
            if ($j -le $i -or [math]::Abs($s2.t - ($s.t + $Cfg.HelpLossWindow)) -gt 3 -or ($s.men - $s2.men) -lt [math]::Max(1, $Cfg.HelpLossShare * $s.men)) { $i++; continue }
            # free helpers then: AI, >= 10 men, not ranged / horse archers, not fighting, within reach, not on a by-design post
            $helpers = New-Object 'System.Collections.Generic.List[object]'
            foreach ($h in $snap) {
                if ($h.form -eq $s.form -or -not $h.ai -or $h.men -lt 10 -or $h.cls -eq 'Ranged' -or $h.cls -eq 'HorseArcher' -or $h.form -eq 'General' -or $h.form -eq 'Bodyguard') { continue }
                if ($main -ne $null -and $h.form -eq $main.form) { continue }
                if ($h.beh -match 'Hedgehog|HoldChoke|ReservePlug|Retreat') { continue }
                if ([double]::IsNaN($h.dEn) -or $h.dEn -le $Cfg.HelpFreeDist -or [double]::IsNaN($h.ax)) { continue }
                if ((Dist $h.ax $h.ay $s.ax $s.ay) -gt $Cfg.HelpReach) { continue }
                $helpers.Add($h)
            }
            if ($helpers.Count -eq 0) { $i++; continue }
            $closed = $false
            foreach ($h in $helpers) {
                $hk = $team + '|' + $h.form
                if (-not $formTs.ContainsKey($hk)) { continue }
                $hj = [P]::Nearest($formTs[$hk], $s.t + $Cfg.HelpLossWindow)
                $h2 = $forms[$hk][$hj]
                if ([math]::Abs($h2.t - ($s.t + $Cfg.HelpLossWindow)) -gt 3) { continue }
                $d0 = Dist $h.ax $h.ay $s.ax $s.ay; $d1 = Dist $h2.ax $h2.ay $s2.ax $s2.ay
                if (($d0 - $d1) -ge $Cfg.HelpCloseMetres -or $h2.beh -eq 'RBMBehaviorReact') { $closed = $true; break }
            }
            if (-not $closed) {
                $nb++
                $st += 'WARN'
                $hl = ($helpers | ForEach-Object { $_.form + '(' + $_.cls + ' ' + $_.men + ' men, ' + (N0 (Dist $_.ax $_.ay $s.ax $s.ay)) + ' m, ' + $_.beh + ')' }) -join ', '
                $ev.Add('WARN ' + $team + '/' + $s.form + ' (' + $s.cls + ') t=' + (N0 $s.t) + '..' + (N0 $s2.t) + ': in melee, ' + $s.men + ' -> ' + $s2.men + ' men, behavior ' + $s.beh + '; free and near, did not come: ' + $hl)
            }
            $i = $j
        }
    }
    if ($nb -eq 0) { $ev.Add('10b: no formation lost >= ' + ($Cfg.HelpLossShare * 100) + '% of its men in melee within ' + $Cfg.HelpLossWindow + ' s with a free formation near that stayed away') }

    # (c) R6 balance re-picks (REPICK reason=balance ...): every evaluation listed, a team switching back and forth = WARN
    if ($tr.BalanceRepicks.Count -gt 0) {
        foreach ($b in $tr.BalanceRepicks) { $ev.Add('10c: t=' + (N0 $b.t) + ' ' + $b.team + ' ' + $b.tactics + ' ' + $b.reason) }
        $byTeam = @{}
        foreach ($b in $tr.BalanceRepicks) { if (-not $b.switched) { continue }; if (-not $byTeam.ContainsKey($b.team)) { $byTeam[$b.team] = New-Object 'System.Collections.Generic.List[double]' }; $byTeam[$b.team].Add($b.t) }
        foreach ($k in $byTeam.Keys) {
            $ts = $byTeam[$k]; $best = 0; $bt = 0.0; $j = 0
            for ($i = 0; $i -lt $ts.Count; $i++) { while ($j -lt $i -and $ts[$i] - $ts[$j] -gt $Cfg.BalancePingWindow) { $j++ }; if (($i - $j + 1) -gt $best) { $best = $i - $j + 1; $bt = $ts[$j] } }
            if ($best -gt $Cfg.BalancePingPong) { $st += 'WARN'; $ev.Add('WARN ' + $k + ': ' + $best + ' balance re-pick switches in ' + $Cfg.BalancePingWindow + ' s from t=' + (N0 $bt) + ' (ping-pong)') }
        }
    }
    Add-Result '10' 'Reactions' (Worst $st) ('a) REACT: R1 / R2 events (>= ' + $Cfg.ReactEventMin + ' s) with no assign / line / covered within ' + $Cfg.ReactWindowR1 + ' / ' + $Cfg.ReactWindowR2 + ' s (> ' + $Cfg.ReactMissFail + ' = FAIL), tasks (Hunt included) never ended, one responder assigned > ' + $Cfg.ReactPingPong + 'x per ' + $Cfg.ReactPingWindow + ' s, > ' + ($Cfg.ReactLeashShare * 100) + '% leash / chaseTimeout ends; b) FORM: an AI formation in melee (not the main infantry, hedgehog, choke line) losing >= ' + ($Cfg.HelpLossShare * 100) + '% of its men in ' + $Cfg.HelpLossWindow + ' s while a free non-ranged AI formation within ' + $Cfg.HelpReach + ' m (distToEnemy > ' + $Cfg.HelpFreeDist + ' m) did not close ' + $Cfg.HelpCloseMetres + ' m on it; c) REPICK balance: > ' + $Cfg.BalancePingPong + ' switches per team in ' + $Cfg.BalancePingWindow + ' s') $ev

    # =================================================================================================
    # 11. Facing: a holding main line facing away from the enemy's main concentration (FORM dir=)
    # =================================================================================================
    $ev = New-Object 'System.Collections.Generic.List[string]'; $st = @('PASS')
    $hasDir = $false
    foreach ($key in $forms.Keys) { foreach ($s in $forms[$key]) { if (-not [double]::IsNaN($s.dx)) { $hasDir = $true }; break }; if ($hasDir) { break } }
    if (-not $hasDir) { $st = @('INFO'); $ev.Add('no dir= field on the FORM lines (older build): not judged') }
    else {
        $classW = @{ 'Infantry' = 1.0; 'HeavyInfantry' = 1.0; 'Skirmisher' = 1.0; 'Ranged' = 0.4; 'Cavalry' = 0.7; 'LightCavalry' = 0.7; 'HeavyCavalry' = 0.7; 'HorseArcher' = 0.2 }
        $byTk = @{}
        foreach ($team in $teamSnaps.Keys) { foreach ($snap in $teamSnaps[$team]) { if ($snap.Count -gt 0) { $byTk[$team + '|' + $snap[0].tk] = $snap } } }
        foreach ($team in $teamSnaps.Keys) {
            $side = $sideOf[$team]
            $enemyTeams = @($teamSnaps.Keys | Where-Object { $sideOf[$_] -and $sideOf[$_] -ne $side })
            $run = $null; $found = New-Object 'System.Collections.Generic.List[object]'
            foreach ($snap in $teamSnaps[$team]) {
                if ($snap.Count -eq 0 -or $snap[0].t -lt $start + $Cfg.StartGrace) { continue }
                $main = $null
                foreach ($s in $snap) { if (($s.cls -eq 'Infantry' -or $s.cls -eq 'HeavyInfantry') -and $s.ai -and ($main -eq $null -or $s.men -gt $main.men)) { $main = $s } }
                $judged = $false; $off = 0.0; $cx = 0.0; $cy = 0.0
                if ($main -ne $null -and $main.men -ge 20 -and -not [double]::IsNaN($main.dx) -and -not [double]::IsNaN($main.ax) -and $main.beh -match 'HoldPosition|HoldChoke|Defend|HoldHighGround' -and $main.ord -notmatch 'Charge|Retreat' -and -not ($main.dEn -le $Cfg.MeleeDist)) {
                    $sw = 0.0; $sx = 0.0; $sy = 0.0
                    foreach ($et in $enemyTeams) {
                        $es = $byTk[$et + '|' + $main.tk]
                        if (-not $es) { continue }
                        foreach ($e in $es) {
                            if ($e.men -lt [math]::Max(10, 0.2 * $main.men) -or [double]::IsNaN($e.ax) -or $e.ord -eq 'Retreat') { continue }
                            if ((Dist $e.ax $e.ay $main.ax $main.ay) -gt $Cfg.FacingRange) { continue }
                            $cw = 1.0; if ($classW.ContainsKey($e.cls)) { $cw = $classW[$e.cls] }
                            $w = $e.men * $cw; $sw += $w; $sx += $e.ax * $w; $sy += $e.ay * $w
                        }
                    }
                    if ($sw -gt 0) {
                        $cx = $sx / $sw; $cy = $sy / $sw
                        $tx = $cx - $main.ax; $ty = $cy - $main.ay; $tl = [math]::Sqrt($tx * $tx + $ty * $ty)
                        $dl = [math]::Sqrt($main.dx * $main.dx + $main.dy * $main.dy)
                        if ($tl -gt $Cfg.FacingMinDist -and $dl -gt 0.01) {
                            $cos = ($tx * $main.dx + $ty * $main.dy) / ($tl * $dl); if ($cos -gt 1) { $cos = 1 }; if ($cos -lt -1) { $cos = -1 }
                            $off = [math]::Acos($cos) * 180 / [math]::PI
                            $judged = $true
                        }
                    }
                }
                if ($judged -and $off -gt $Cfg.FacingOffMax) {
                    if ($run -eq $null) { $run = [pscustomobject]@{ t0 = $main.t; t1 = $main.t; max = $off; beh = $main.beh; form = $main.form; cx = $cx; cy = $cy } }
                    $run.t1 = $main.t; if ($off -gt $run.max) { $run.max = $off }
                }
                elseif ($run -ne $null) { if (($run.t1 - $run.t0) -gt $Cfg.FacingSeconds) { $found.Add($run) }; $run = $null }
            }
            if ($run -ne $null -and ($run.t1 - $run.t0) -gt $Cfg.FacingSeconds) { $found.Add($run) }
            foreach ($r in $found) {
                $st += 'WARN'
                $ev.Add('WARN ' + $team + '/' + $r.form + ' t=' + (N0 $r.t0) + '..' + (N0 $r.t1) + ' (' + (N0 ($r.t1 - $r.t0)) + ' s): holding line (' + $r.beh + ') faced up to ' + (N0 $r.max) + ' deg off the enemy concentration (~' + (N0 $r.cx) + ',' + (N0 $r.cy) + ') outside melee')
            }
        }
    }
    Add-Result '11' 'Facing' (Worst $st) ('a holding AI main infantry (HoldPosition / HoldChoke / Defend / HoldHighGround, not charging, outside melee) facing more than ' + $Cfg.FacingOffMax + ' deg off the enemy concentration (enemy formations within ' + $Cfg.FacingRange + ' m, men x class weight) for > ' + $Cfg.FacingSeconds + ' s = WARN') $ev

    # =================================================================================================
    # 12. Support fire: ranged formations once the frontlines connect (terrain BEHAVIOR "support ..." lines, SupportFire)
    # =================================================================================================
    $ev = New-Object 'System.Collections.Generic.List[string]'; $st = @('PASS')
    $supCounts = @{}; $supSpans = @{}; $supOpen = @{}
    foreach ($b in ($tr.Behaviors | Sort-Object t)) {
        if ($b.what -notmatch '^support (\w+)') { continue }
        $kind = $Matches[1]
        $key = $b.team + '|' + $b.form
        if (-not $supCounts.ContainsKey($key)) { $supCounts[$key] = @{}; $supSpans[$key] = New-Object 'System.Collections.Generic.List[object]' }
        if (-not $supCounts[$key].ContainsKey($kind)) { $supCounts[$key][$kind] = 0 }
        $supCounts[$key][$kind]++
        if ($kind -eq 'lost') {
            if ($supOpen.ContainsKey($key)) { $supSpans[$key].Add(@($supOpen[$key], $b.t)); $supOpen.Remove($key) }
        }
        elseif ($kind -ne 'none' -and -not $supOpen.ContainsKey($key)) { $supOpen[$key] = $b.t }
    }
    foreach ($key in @($supOpen.Keys)) { $supSpans[$key].Add(@($supOpen[$key], $missionEnd)) }
    if ($supCounts.Count -eq 0) { $st = @('INFO'); $ev.Add('no support lines (no connected frontline with AI foot ranged, terrain log off, or an older build)') }
    foreach ($key in $supCounts.Keys) {
        $parts = @(); foreach ($k in ($supCounts[$key].Keys | Sort-Object)) { $parts += ($k + '=' + $supCounts[$key][$k]) }
        $spanSum = 0.0; foreach ($sp in $supSpans[$key]) { $spanSum += ($sp[1] - $sp[0]) }
        $ev.Add('INFO ' + $key.Replace('|', '/') + ': ' + ($parts -join ' ') + ' (in support ' + (N0 $spanSum) + ' s)')
        if (-not $forms.ContainsKey($key)) { continue }
        $moves = New-Object 'System.Collections.Generic.List[double]'
        $ser = $forms[$key]
        for ($i = 1; $i -lt $ser.Count; $i++) {
            $a = $ser[$i - 1]; $s = $ser[$i]
            if (-not $s.ai -or [double]::IsNaN($s.ox) -or [double]::IsNaN($a.ox)) { continue }
            $inSpan = $false; foreach ($sp in $supSpans[$key]) { if ($a.t -ge $sp[0] -and $s.t -le $sp[1]) { $inSpan = $true; break } }
            if (-not $inSpan) { continue }
            if ((Dist $a.ox $a.oy $s.ox $s.oy) -gt $Cfg.FlickerJump) { $moves.Add($s.t) }
        }
        $best = 0; $bestT = 0.0; $j = 0
        for ($i = 0; $i -lt $moves.Count; $i++) {
            while ($j -lt $i -and $moves[$i] - $moves[$j] -gt $Cfg.SupportWindow) { $j++ }
            if (($i - $j + 1) -gt $best) { $best = $i - $j + 1; $bestT = $moves[$j] }
        }
        if ($best -gt $Cfg.SupportMovesMax) {
            $st += 'WARN'
            $ev.Add('WARN ' + $key.Replace('|', '/') + ': in support mode the order point moved ' + $best + 'x (> ' + $Cfg.FlickerJump + ' m) within ' + $Cfg.SupportWindow + ' s from t=' + (N0 $bestT) + ' (' + $moves.Count + ' moves in support)')
        }
    }
    Add-Result '12' 'Support fire' (Worst $st) ('ranged formations once the frontlines connect: support line counts per formation (INFO); a formation in support mode moving its order point (> ' + $Cfg.FlickerJump + ' m between FORM samples) > ' + $Cfg.SupportMovesMax + ' times per ' + $Cfg.SupportWindow + ' s = WARN') $ev

    # =================================================================================================
    # 13. Decision trace: who changed each formation (AI log DECISION / FLIP lines, DecisionTrace)
    # =================================================================================================
    $ev = New-Object 'System.Collections.Generic.List[string]'; $st = @('PASS')
    if ($decisions.Count -eq 0) { $st = @('INFO'); $ev.Add('no DECISION / FLIP lines (older build, AI behavior log opened before DecisionTrace, or nothing changed)') }
    foreach ($f in $flipLines) {
        $st += 'WARN'
        $ev.Add('WARN ' + $f.team + '/' + $f.form + ' (' + $f.cls + ') t=' + (N0 $f.t) + ': FLIP ' + $f.field + ' ' + $f.count + ' writers=' + $f.writers + ' seq=' + $f.seq)
    }
    foreach ($key in ($decisions.Keys | Sort-Object { -$decisions[$_]['n'] })) {
        $dc = $decisions[$key]
        $wt = $dc['writers']
        $top = @($wt.Keys | Sort-Object { -$wt[$_] } | Select-Object -First 4 | ForEach-Object { $_ + ' x' + $wt[$_] })
        $ev.Add('INFO ' + $key.Replace('|', '/') + ': DECISION=' + $dc['n'] + ' FLIP=' + $dc['flips'] + ' top writers: ' + ($top -join ', '))
    }
    Add-Result '13' 'Decision trace' (Worst $st) ('DecisionTrace: DECISION lines and the most frequent field:writer per formation (INFO); every FLIP line (a formation whose Move point / order kind / arrangement / behavior keeps flipping, with the rules that fight) = WARN') $ev

    # =================================================================================================
    # 14. Gap shadow: FormationGeometry's footprint gaps vs the measure each site used before ("gap=Xm(old Ym)")
    # =================================================================================================
    $ev = New-Object 'System.Collections.Generic.List[string]'
    if ($tr.Shadow.Count -eq 0) { $ev.Add('no "(old Ym)" fields (terrain log off, an older build, or no migrated gap logged)') }
    foreach ($sk in ($tr.Shadow.Keys | Sort-Object { -$tr.Shadow[$_]['n'] })) {
        $sh = $tr.Shadow[$sk]
        $ev.Add('INFO ' + $sk + ': n=' + $sh['n'] + ' new-old mean=' + (N1 ($sh['sum'] / $sh['n'])) + ' min=' + (N0 $sh['min']) + ' max=' + (N0 $sh['max']) + ' m')
    }
    Add-Result '14' 'Gap shadow' 'INFO' ('per logging site and field: how far the true footprint gap (FormationGeometry) moved from the old measure (new - old, m; negative = now closer); judge each threshold from these before retuning') $ev

    # =================================================================================================
    # 15. Approach: the attacker's terrain route (terrain APPROACH lines, TerrainApproach)
    # =================================================================================================
    $ev = New-Object 'System.Collections.Generic.List[string]'; $st = @('PASS')
    if ($tr.Approach.Count -eq 0 -and $tr.ApproachUses.Count -eq 0) { $st = @('INFO'); $ev.Add('no APPROACH lines (terrain log off, not a field battle, or an older build)') }
    foreach ($a in $tr.Approach) {
        if ($a.kind -eq 'replanStop') { $ev.Add('INFO t=' + (N0 $a.t) + ' ' + $a.text); continue }
        $ev.Add('INFO t=' + (N0 $a.t) + ' ' + $a.kind + ': chosen=' + $a.chosen + ' gain=' + $a.gain + ' why=' + $a.why + $(if ($a.reason) { ' reason=' + $a.reason } else { '' }))
    }
    $drops = @{}
    $follow = @{}   # "team|form" -> t of the last take
    $spans = New-Object 'System.Collections.Generic.List[object]'
    foreach ($u in ($tr.ApproachUses | Sort-Object t)) {
        $key = $u.team + '|' + $u.form
        if ($u.what -eq 'take' -or $u.what -eq 'adopt') {
            if ($u.what -eq 'take' -or -not $follow.ContainsKey($key)) { $follow[$key] = $u.t }
            continue
        }
        if ($u.what -ne 'drop') { continue }
        $reason = ($u.why -split '\s+')[0]
        if (-not $drops.ContainsKey($reason)) { $drops[$reason] = 0 }
        $drops[$reason]++
        if (-not $follow.ContainsKey($key)) { continue }
        $t0 = $follow[$key]
        if ($reason -eq 'noGain' -and ($u.t - $t0) -le $Cfg.ApproachNoGainSec) {
            $st += 'WARN'; $ev.Add('WARN ' + $key.Replace('|', '/') + ': route taken at t=' + (N0 $t0) + ' and dropped for noGain ' + (N0 ($u.t - $t0)) + ' s later (' + $u.why + ')')
        }
        if ($reason -eq 'stalled') {
            $st += 'WARN'; $ev.Add('WARN ' + $key.Replace('|', '/') + ': t=' + (N0 $u.t) + ' drop ' + $u.why)
        }
        $spans.Add(@($key, $t0, $u.t))
        $follow.Remove($key)
    }
    foreach ($key in @($follow.Keys)) {
        $ev.Add('INFO ' + $key.Replace('|', '/') + ': still following at mission end (taken t=' + (N0 $follow[$key]) + ')')
        $spans.Add(@($key, $follow[$key], $missionEnd))
    }
    # FORM samples while following: standing still
    foreach ($sp in $spans) {
        $key = $sp[0]; $t0 = $sp[1]; $t1 = $sp[2]
        if (-not $forms.ContainsKey($key)) { continue }
        $anchor = $null
        foreach ($s in $forms[$key]) {
            if ($s.t -lt $t0 -or $s.t -gt $t1 -or [double]::IsNaN($s.ax)) { continue }
            if ($null -eq $anchor -or (Dist $anchor.ax $anchor.ay $s.ax $s.ay) -ge $Cfg.ApproachStillMetres) { $anchor = $s; continue }
            if ($s.t - $anchor.t -gt $Cfg.ApproachStillSec) {
                $st += 'WARN'; $ev.Add('WARN ' + $key.Replace('|', '/') + ': stood still ' + (N0 ($s.t - $anchor.t)) + ' s from t=' + (N0 $anchor.t) + ' while following the route (taken t=' + (N0 $t0) + ')')
                break
            }
        }
    }
    if ($drops.Count -gt 0) {
        $parts = @(); foreach ($k in ($drops.Keys | Sort-Object)) { $parts += ($k + '=' + $drops[$k]) }
        $ev.Add('INFO drops: ' + ($parts -join ' '))
    }
    Add-Result '15' 'Approach' (Worst $st) ('the attacker main infantry''s terrain approach route: choice per plan and drop reasons (INFO); a route dropped for noGain within ' + $Cfg.ApproachNoGainSec + ' s of being taken, a stalled drop, or the main standing still (< ' + $Cfg.ApproachStillMetres + ' m) > ' + $Cfg.ApproachStillSec + ' s while following = WARN') $ev

    # =================================================================================================
    # 16. Charge flicker: one formation's ChargeGuard advance <-> go switches (terrain BEHAVIOR ChargeGuard lines)
    # =================================================================================================
    $ev = New-Object 'System.Collections.Generic.List[string]'; $st = @('PASS')
    $flips = @{}
    foreach ($b in ($tr.Behaviors | Sort-Object t)) {
        if ($b.beh -ne 'ChargeGuard' -or ($b.what -ne 'charge advance' -and $b.what -ne 'charge go')) { continue }
        $key = $b.team + '|' + $b.fi
        if (-not $flips.ContainsKey($key)) { $flips[$key] = New-Object 'System.Collections.Generic.List[object]' }
        $flips[$key].Add($b)
    }
    foreach ($key in $flips.Keys) {
        $list = $flips[$key]; $times = New-Object 'System.Collections.Generic.List[double]'
        for ($i = 1; $i -lt $list.Count; $i++) { if ($list[$i].what -ne $list[$i - 1].what) { $times.Add($list[$i].t) } }
        $j = 0; $worstN = 0; $worstT = 0
        for ($i = 0; $i -lt $times.Count; $i++) {
            while ($times[$i] - $times[$j] -gt $Cfg.ChargeFlipWindow) { $j++ }
            if ($i - $j + 1 -gt $worstN) { $worstN = $i - $j + 1; $worstT = $times[$j] }
        }
        if ($worstN -gt $Cfg.ChargeFlipMax) { $st += 'WARN'; $ev.Add('WARN ' + $key.Replace('|', ' f') + ': ChargeGuard advance <-> go ' + $worstN + 'x within ' + $Cfg.ChargeFlipWindow + ' s from t=' + (N0 $worstT)) }
    }
    if ($flips.Count -eq 0) { $ev.Add('INFO no ChargeGuard advance / go lines (terrain log off, or no far charges)') }
    Add-Result '16' 'Charge flicker' (Worst $st) ('one formation''s ChargeGuard advance <-> go switching > ' + $Cfg.ChargeFlipMax + 'x in ' + $Cfg.ChargeFlipWindow + ' s = WARN (a formation that keeps releasing and losing its charge)') $ev

    # ------------------------------------------------------------------ print
    foreach ($r in $script:Results) {
        Say ('[' + $r.Status + '] ' + $r.Id + '. ' + $r.Name + ' -- ' + $r.Why)
        $shown = 0
        # FAIL first, then WARN, then the rest
        $ordered = @($r.Evidence | Where-Object { $_ -like 'FAIL*' }) + @($r.Evidence | Where-Object { $_ -like 'WARN*' }) + @($r.Evidence | Where-Object { $_ -notlike 'FAIL*' -and $_ -notlike 'WARN*' })
        foreach ($e in $ordered) {
            if ($shown -ge $MaxEvidence) { break }
            Say ('    ' + $e); $shown++
        }
        if ($ordered.Count -gt $shown) { Say ('    ... and ' + ($ordered.Count - $shown) + ' more') }
    }
    $nF = @($script:Results | Where-Object { $_.Status -eq 'FAIL' }).Count
    $nW = @($script:Results | Where-Object { $_.Status -eq 'WARN' }).Count
    $nP = @($script:Results | Where-Object { $_.Status -eq 'PASS' }).Count
    $nI = @($script:Results | Where-Object { $_.Status -eq 'INFO' }).Count
    Say ''
    Say ('SUMMARY ' + $stamp + ': FAIL ' + $nF + '  WARN ' + $nW + '  PASS ' + $nP + '  INFO ' + $nI)
    $top = New-Object 'System.Collections.Generic.List[string]'
    foreach ($sev in @('FAIL', 'WARN')) {
        foreach ($r in $script:Results) {
            foreach ($e in $r.Evidence) { if ($e -like ($sev + '*') -and $top.Count -lt 6) { $top.Add('  ' + $r.Id + '. ' + $r.Name + ': ' + $e) } }
        }
    }
    foreach ($x in $top) { Say $x }
    Say ''
    $worst = 0
    if ($nW -gt 0) { $worst = 1 }
    if ($nF -gt 0) { $worst = 2 }
    return $worst
}

# =====================================================================================================
# main: find the battle(s)
# =====================================================================================================
if (-not $LogsDir) {
    $docs = [Environment]::GetFolderPath('MyDocuments')
    $LogsDir = Join-Path $docs 'Mount and Blade II Bannerlord\Configs\RBM\logs'
}
$aiDir = Join-Path $LogsDir 'ai'
$trDir = Join-Path $LogsDir 'terrain'
if (-not (Test-Path -LiteralPath $aiDir)) { throw "No AI log folder: $aiDir (pass -LogsDir)" }

$aiFiles = @(Get-ChildItem -LiteralPath $aiDir -Filter 'ai_*.log' | Sort-Object Name -Descending)
$trFiles = @(); if (Test-Path -LiteralPath $trDir) { $trFiles = @(Get-ChildItem -LiteralPath $trDir -Filter 'terrain_*.log') }

$targets = @()
if ($Stamp) {
    $f = $aiFiles | Where-Object { $_.Name -eq ('ai_' + $Stamp + '.log') } | Select-Object -First 1
    if (-not $f) { throw "No ai_$Stamp.log in $aiDir" }
    $targets = @($f)
}
else { $targets = @($aiFiles | Select-Object -First ([math]::Max(1, $Last))) }
if ($targets.Count -eq 0) { throw "No ai_*.log in $aiDir" }

$exit = 0
foreach ($f in $targets) {
    $st = $f.BaseName.Substring(3)
    $stT = Get-StampTime $st
    $trPath = $null
    $exact = Join-Path $trDir ('terrain_' + $st + '.log')
    if (Test-Path -LiteralPath $exact) { $trPath = $exact }
    elseif ($stT) {
        $bestD = [double]::MaxValue
        foreach ($tf in $trFiles) {
            $tt = Get-StampTime ($tf.BaseName.Substring(8))
            if ($tt) { $dd = [math]::Abs(($tt - $stT).TotalSeconds); if ($dd -le $Cfg.PairToleranceSec -and $dd -lt $bestD) { $bestD = $dd; $trPath = $tf.FullName } }
        }
    }
    $r = @(Invoke-Check $f.FullName $trPath $st)
    $r = [int]$r[$r.Count - 1]
    if ($r -gt $exit) { $exit = $r }
}

if ($Out) {
    [System.IO.File]::WriteAllLines($Out, $script:Report, (New-Object System.Text.UTF8Encoding($false)))
    Write-Output ('Report written to ' + $Out)
}
else { foreach ($l in $script:Report) { Write-Output $l } }
exit $exit
