# Scarlet.Tailwind.Runtime.darwin-arm64

This package contains the official Tailwind CSS standalone CLI for macOS ARM64 (Apple Silicon).

Scarlet.Tailwind.MSBuild does not depend on this package directly. Reference the runtime package that
matches the build host, and Scarlet.Tailwind.MSBuild will discover it through the TailwindRuntimePack item
contributed by this package.

The package version corresponds to the Tailwind CSS version it contains.

Scarlet.Tailwind and Tailwind CSS are MIT licensed; see `LICENSE-3RD-PARTY.txt` in the package.
