# RBMAI.Tests

Executable specifications for RBMAI battle-AI rules that run without the game. Today: the army
composition rules (`ArmyComposition.Derive` / `ComputeRamps` / `LeadsVerdict` / `Tokens` / `IsDryAmmo`
/ `Ramp`: a normal mixed army reads every ramp 0, token forces and their hysteresis, the main-body
verdict), the sampled formation motion (`EnemyMotion.TryStepVelocity`) and a charging foot block's
cavalry target hysteresis and whether it can reach riders (`ChargeTargetChoice.KeepsCurrentMounted` / `Catchable`, `ChargeTargetChoiceSpecs.cs`),
and the size cap on a scattered formation's box (`FormationGeometry.CappedOffset`, `FormationGeometrySpecs.cs`). A test fake must not
mention game types (e.g. `Vec2`) in its signatures or static fields: NUnit loads every test type
before `GameAssemblies` can resolve the game's assemblies. The pure helpers are
`internal`; RBMAI's `AssemblyInfo.cs` grants this project access with `InternalsVisibleTo`.

Only rules that take plain values can be tested here. Anything that reads `Mission`, `Formation` or
the query system needs the running game. To add a spec for such a rule, first move its math into a
pure function that takes plain values.

The project is not in `RealisticBattle.sln`, so the mod's command-line build (no `/restore`) is
unchanged. The game's assemblies are not copied into the test output. `GameAssemblies.cs` loads them
from the install the build already points at.

Build (restores NUnit from nuget.org the first time; also builds RBMAI):

```
"C:\Program Files\Microsoft Visual Studio\18\Community\MSBuild\Current\Bin\MSBuild.exe" RBMAI.Tests\RBMAI.Tests.csproj /restore /p:Configuration=Debug
```

Run:

```
"C:\Program Files\Microsoft Visual Studio\18\Community\Common7\IDE\Extensions\TestPlatform\vstest.console.exe" RBMAI.Tests\bin\Debug\net472\RBMAI.Tests.dll /Platform:x64
```
