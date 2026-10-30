# ADOFAI IPC SDK

These DLLs are built from [ADOFAI IPC](https://github.com/KGH1113/adofai-ipc)
commit `1c284100a34a3840910dd818480044ea0dfb1392`, product version 2.1.0.
The accompanying MIT licenses are in `LICENSE` and `LICENSE_MANAGED_WEBSOCKET.txt`.
The loader and contracts DLLs
belong next to OrbitRender.dll; the runtime and manifest belong in `ipc/`.

Build the three projects from that revision with the .NET SDK, setting
`AdofaiManaged` to the game's managed assemblies directory. Use Release output
and copy the runtime manifest from `AdofaiIpc/src/AdofaiIpc.Runtime`.
