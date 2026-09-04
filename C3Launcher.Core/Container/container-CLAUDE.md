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

The user set this image up and already knows what is in it. Do not remind them
that you cannot build or run the project, and do not append it as a caveat when
handing back work — write the code and stop. Mention a missing toolchain only
when it actually changes what they should do next, and then once.

## Filesystem

- **The working directory is the user's project.** It is a bind mount of their
  real project directory on the host — changes here are changes to their actual
  files, and it is the only place project work belongs. It sits in a
  per-project directory under `/workspace`; use the working directory rather
  than assuming `/workspace` itself is the project.
- **The root filesystem is read-only.** `apt-get install`, `npm install -g`,
  and any other system-level install will fail. Do not attempt them; ask the
  user to add the package to the container image instead.
- **`/home/node` persists and is shared with the user's other Claude Code
  sessions.** It holds Claude Code's own configuration, credentials and
  history. Caches are fine there; project files are not, and anything written
  there is visible to their other sessions.
- **`/tmp` is 256 MB and mounted `noexec`.** Native code cannot be executed or
  loaded from it. Never create a Python venv in `/tmp`: pure-Python imports
  work, but any native wheel (numpy, pandas, cryptography) fails with a
  misleading error. Create venvs inside the project, such as `.venv` in the
  working directory. `/workspace` itself is on the read-only root filesystem —
  only the project directory under it is writable. The project is a bind mount
  of the user's real directory, so delete the venv when you are finished with
  it rather than leaving it in their working tree.

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

## Comments

The default is none. A comment is what you write when you could not make the
code say it — not a finishing step, and not evidence that you thought about the
line.

So the burden is on the comment. It has to carry something the file does not: a
constraint from outside it, the failure that motivated an odd-looking line, the
library or platform behaviour being worked around. Those are the narrow cases,
not a menu to match a comment against, and "it explains why" does not clear the
bar on its own — a reason the next reader would reach unaided is still noise,
and it has to be maintained alongside the code it duplicates. If you cannot say
what a comment tells someone who has already read the lines around it, delete
it. Match the density of the file you are editing; do not arrive and annotate
everything.

Write them in the present tense, describing how the code works now. Do not frame
a comment around what changed, what the code used to do, or what was removed:
the next reader has the current file in front of them, not your diff, and
"now returns X instead of Y" is stale the first time someone opens it fresh.
The same goes for commented-out code — delete it; version control already has
it. Reach for history only when it is the actual point, such as a workaround
pinned to a specific upstream bug, or a deliberate rejection of the obvious
approach that someone would otherwise undo.
