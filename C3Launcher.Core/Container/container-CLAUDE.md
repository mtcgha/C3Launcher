# Container environment

This session is running inside a locked-down Docker container (Debian,
`node:22-slim` base). The notes below cover constraints that cannot be
discovered by inspecting the project.

## Available tooling

- **Node.js 22** and `npm`
- **Python 3** with `venv`, plus **`uv`** for package and environment management
- `git`, `curl`, `wget`, `jq`, `rg` (ripgrep)
- `less`, `ps`, `file`, `unzip`, `zip`, `xz`
- `gcc` / `g++` / `make` (build-essential), `pkg-config`, and Python headers,
  for native node modules and Python wheels without prebuilt binaries

## Not available

There is no .NET SDK, Mono, Java, Go, Rust, PHP, or Ruby toolchain in this
image. If a task requires one, say so directly rather than searching the
filesystem for it. .NET Framework specifically cannot run on Linux at all, so
Framework projects can be read and edited here but never built or run.

## Filesystem

- **`/workspace` is the only writable, persistent location.** It is a bind
  mount of the user's real project directory on the host — changes here are
  changes to their actual files.
- **The root filesystem is read-only.** `apt-get install`, `npm install -g`,
  and any other system-level install will fail. Do not attempt them; ask the
  user to add the package to the container image instead.
- **`/home/node` is a 512 MB tmpfs**, discarded when the session ends. Usable
  for caches, but nothing written there survives.
- **`/tmp` is 256 MB and mounted `noexec`.** Native code cannot be executed or
  loaded from it. Never create a Python venv in `/tmp`: pure-Python imports
  work, but any native wheel (numpy, pandas, cryptography) fails with a
  misleading error. Create venvs at `/workspace/.venv`.

## Python

Prefer `uv` over `pip`. Debian marks the system Python as externally managed
(PEP 668), so a bare `pip install` fails with
`error: externally-managed-environment`.

## Empty files are often intentional

Selected files are shadowed with empty stubs before the container starts, so
their contents never enter it at all. A file that is unexpectedly zero-length —
most often config and secret-bearing files such as `*.json`, `*.config`, or
`*.env` — is being withheld deliberately. It is not corrupt, truncated, or a
bug. Do not try to reconstruct it or overwrite it with generated content; ask
the user for whatever you need from it.

This can include files a build would normally require. Install and build steps
may fail for that reason, and that is expected rather than something to work
around.
