# RBMCampaign.Tests

Executable specifications for RBMCampaign formulas that run without the game: the market price
curve (`RBMMarketPrices.ScarcityFactor` / `SignalFactor`), the spoils share upgrading men carry
(`SpoilsPool.GetCarriedSpoils`) and the wage table (`TierBasedWageModel.WageForTier`).

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
