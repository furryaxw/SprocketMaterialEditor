# Sec's Material Editor

Sprocket MelonLoader mod for changing armour materials on saved vehicle blueprints.

## Features

- Press `F8` (or use the left-middle panel button) to open the editor.
- Refresh the most recently saved blueprint and select armour parts.
- Select a material discovered from the game's base and mod technology files.
- Apply changes to a copy, or apply them in place after creating a timestamped backup.
- Read the current vehicle-designer selection when editing a vehicle.

## Installation

1. Install a MelonLoader version compatible with the Sprocket build you use.
2. Put `SprocketMaterialEditor.dll` in the game's `Mods` directory.
3. Start Sprocket, save a vehicle, and press `F8` in the vehicle designer.

## Build

The project targets .NET 6 and references the local Sprocket MelonLoader assemblies.
From this repository directory:

```powershell
dotnet build .\SprocketMaterialEditor\SprocketMaterialEditor.csproj --configuration Release
```

The default build deploys the DLL to `G:\Sprocket\Mods`. To only produce an artifact:

```powershell
dotnet build .\SprocketMaterialEditor\SprocketMaterialEditor.csproj --configuration Release -p:SkipModDeploy=true -p:SprocketGameRoot="G:\Sprocket"
```

## Author

Original author: **Sectumsempra**

- QQ: `2447554453`
- Discord: `hpytoby`

This repository packages and publishes the author's provided source for users who need a GitHub-hosted release.

The published DLL is built from the checked-in source with the local Sprocket game assemblies used only as compile-time references.

## License

MIT. See [LICENSE](LICENSE).
