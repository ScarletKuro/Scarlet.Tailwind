# Scarlet.Tailwind.Runtime.windows-x64

This package contains the official Tailwind CSS standalone CLI for Windows x64. It also serves Windows
ARM64 with the x64 executable that Windows on ARM runs under emulation, because Tailwind does not publish a
native Windows ARM64 executable.

Scarlet.Tailwind.MSBuild does not depend on this package directly. Reference the runtime package that
matches the build host, and Scarlet.Tailwind.MSBuild will discover it through the TailwindRuntimePack item
contributed by this package.

The package version corresponds to the Tailwind CSS version it contains.

Scarlet.Tailwind and Tailwind CSS are MIT licensed; see `LICENSE-3RD-PARTY.txt` in the package.
