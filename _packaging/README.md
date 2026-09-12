# Luzr portable packaging support

This directory stores reusable packaging support outside the application source tree.

Build through:

```powershell
& .\scripts\build_portable_release.ps1 `
  -ProjectRoot 'D:\Tool\Luzr1 ok all - test connect' `
  -Output 'D:\Tool\Luzr App Release\LL-Portable-Ready-vNEXT'
```

The wrapper keeps application source in the project but forces Cargo/Rustup/MinGW
toolchain, Cargo target cache, and build staging to this `_packaging` directory. It refuses missing
external toolchain inputs and the release pipeline refuses to overwrite non-empty
output or staging directories.

Do not place private keys, real tokens, `.env` files, source maps, test fixtures,
debug traces, or antivirus-quarantined artifacts here.
