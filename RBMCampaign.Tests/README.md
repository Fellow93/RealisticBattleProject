# RBMCampaign.Tests

Executable specifications for RBMCampaign formulas that run without the game: the market price
curve (`RBMMarketPrices.ScarcityFactor` / `SignalFactor`), the spoils share upgrading men carry
(`SpoilsPool.GetCarriedSpoils`), the wage table (`TierBasedWageModel.WageForTier`) and the
battle-loot rules: which kit a man notices, what he can carry, and how equal-rank stacks split a heap
(`SpoilsPool.GetNoticedPieces` / `GetCarryCapacity` / `SplitAmongPeers`), and the settlement patrol
rules: boost, template level, size, keep and raise reserves, the buy-a-man check, the garrison floor and
the 7-day pot average (`PatrolUpkeep`'s pure overloads of `Boost`, `TemplateLevel`, `MaxSize`,
`SizeAtPot`, `KeepFloor`, `HoldsKeepReserve`, `HoldsRaiseReserve`, `CanAffordMan`, `GarrisonSpare`,
`StepAveragePot`). The pure helpers are `internal`; RBMCampaign's `AssemblyInfo.cs` grants this
project access with `InternalsVisibleTo`. The project references `TaleWorlds.CampaignSystem` at compile
time only (not copied), because overloads of these helpers take a `Settlement`.
Settings and random rolls are passed in as parameters. The game-side caller reads `RBMConfig` and
`MBRandom` and passes their values in.

Test names describe behaviour in campaign terms. Only formulas that take plain numbers can be tested
here. Calling into `Campaign.Current`, settlements or parties needs the running game. To add a spec
for such a rule, first move its math into a pure function that takes plain values.

The project is not in `RealisticBattle.sln`, so the mod's command-line build (no `/restore`) is
unchanged. The game's assemblies are not copied into the test output. `GameAssemblies.cs` loads them
from the install that the build already points at.

Build (restores NUnit from nuget.org the first time; also builds RBMCampaign):

```
"C:\Program Files\Microsoft Visual Studio\18\Community\MSBuild\Current\Bin\MSBuild.exe" RBMCampaign.Tests\RBMCampaign.Tests.csproj /restore /p:Configuration=Debug
```

Run:

```
"C:\Program Files\Microsoft Visual Studio\18\Community\Common7\IDE\Extensions\TestPlatform\vstest.console.exe" RBMCampaign.Tests\bin\Debug\net472\RBMCampaign.Tests.dll /Platform:x64
```
