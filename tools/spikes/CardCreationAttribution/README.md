# Card creation integration checks

Run `dotnet run --project tools/spikes/CardCreationAttribution` from the repository root with the .NET 9 SDK and local.props pointing to the game. References use installed game/Harmony DLLs and the production mod project; no NuGet packages and no game launch are required.

The harness patches real AbstractModel override methods through production discovery, including the installed Soulbound override (without executing gameplay). It tests async/nested/concurrent context lifetimes and production contributor extraction. The dispatch-order scenario simulates the ordering separately verified in the installed Hook source. It does not replace an in-game Soulbound/visual acceptance check.
