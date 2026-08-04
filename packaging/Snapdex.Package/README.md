# snapdex MSIX packaging

This folder contains the Desktop Bridge packaging project used to produce a
Windows 10/11 MSIX installer for `src/Snapdex.App`.

## Build on Windows

From the repository root:

```powershell
./scripts/windows/build-msix.ps1 -Configuration Release -Platform x64
```

The script writes artifacts under `artifacts/msix/` and prints the generated
`.msix` path.

## Install locally (developer mode)

```powershell
Add-AppxPackage -Path .\artifacts\msix\<package>.msix -AllowUnsigned
```

After install, **snapdex** appears in the Start menu.

## Notes

- The package is configured for sideload builds (`UapAppxPackageBuildMode=SideloadOnly`).
- Signing is disabled by default for local/dev packaging. For production
  distribution, configure code signing in the packaging build pipeline.
