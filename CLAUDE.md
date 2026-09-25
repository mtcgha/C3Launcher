# C3Launcher

A Windows desktop app that runs Claude Code sessions inside locked-down Docker
containers — one container per session — and manages their lifetime. The point
is that Claude gets a real terminal and a real project directory, but no reach
into the rest of the machine.

## Projects

| Project | Output | Role |
| --- | --- | --- |
| `C3Launcher.Core` | library | All the logic: compose planning, Docker, mounting, auth, the container build context |
| `C3Launcher.Gui` | WinExe | Avalonia 12 desktop app, MVVM via CommunityToolkit source generators. The primary surface |
| `C3Launcher.Cli` | Exe | A single-session launcher over the same Core. Useful for testing Core without the UI |

`net10.0`, nullable enabled, implicit usings. `Docker.DotNet.Enhanced` talks to
the daemon; there is no shelling out to `docker` except to start the session
terminal.

Windows-only by design: `wt.exe`, `%USERPROFILE%`, `LocalApplicationData`, and
Docker Desktop are all assumed.

## How a session launches

1. `SessionLauncher.PrepareAsync` resolves the project path.
2. `MountIgnoreScanner` walks the project against `.mountignore` patterns and
   returns the set of paths to shadow.
3. `SessionWorkspace.Create` makes a per-session folder under
   `%LOCALAPPDATA%\C3Launcher\sessions\<sessionId>` holding an empty stub file,
   an empty stub directory, and the generated `compose.yml`.
4. `ComposePlanner.Build` turns that into a `ComposeSpec`; `ComposeWriter`
   serializes it to YAML.
5. `TerminalLauncher.Launch` starts `docker compose run --rm` in a new Windows
   Terminal tab, falling back to a bare console window if `wt.exe` is absent.

`docker compose run --rm` — not `up` — so the container is disposable and the
terminal owns its lifetime.

## The container

Built from `C3Launcher.Core/Container/` by `ImageBuildService` through the
Docker API, not the `docker` CLI. The build context is assembled from an
explicit file list in `ImageBuildService.BuildContextFiles`; **a `COPY` in the
Dockerfile of a file missing from that list fails the build.** Keep the two in
sync.

Base is `node:22-slim` with git, ripgrep, jq, Python + `uv`, and build-essential.
Claude Code is installed in the last layer so bumping `CLAUDE_VERSION`
invalidates only that layer.

Runtime posture, set in `ComposeSpec` defaults: read-only root filesystem,
`cap_drop: ALL`, `no-new-privileges`, non-root `node` user, `/tmp` on a
`noexec` tmpfs.

`ComposePlanner` sets `TZ` per session from the host's time zone, converted to
an IANA name. Debian runs on UTC, so without it a session's timestamps disagree
with the clock the user is reading them against. `tzdata` is already in the base
image, so the variable is all that is needed.

Two files land on the read-only root filesystem, where nothing at runtime can
rewrite them:

- `/etc/claude-code/CLAUDE.md` (from `container-CLAUDE.md`) — managed policy
  loaded into every session in every project. This is how a session knows what
  the image does and does not contain.
- `/etc/claude-code/managed-settings.json` — the highest-precedence settings
  tier.

## Authentication

**The host's `~/.claude` is never mounted into a container.** Containers have
their own Claude login, held in a Docker named volume (`ClaudeHomeVolume`)
mounted at `/home/node` and shared by every session.

This matters and is easy to break by "helpfully" mounting host credentials:

- A token refresh rotates the refresh token and invalidates the previous one.
  Two copies of one credential cannot both stay valid, so a container
  refreshing against a copy of the host's credential silently kills the host's
  login.
- One volume for all sessions, not one per session, for the same reason.
  Sharing it makes concurrent sessions behave like several terminals on one
  machine, which is the ordinary supported case.
- Nothing on the host reads the volume, so a container writing to it can only
  affect other containers. That is what makes read-write access to it safe,
  where read-write access to the host directory would not be.
- Host settings, hooks, history and plugins stay on the host and are not
  visible from inside a session.

A fresh volume starts logged out. The first session runs `/login` and pastes
back the browser code (the container cannot reach Claude Code's local callback
server); every later session inherits it from the volume.

The volume is declared `external: true` in the compose file, so `compose down
-v` cannot delete it — and equally, compose will not create it. Something has
to, before every launch rather than once at startup: anything that removes the
volume while the app is open (`docker volume prune`, Docker Desktop) otherwise
turns the next launch into "external volume not found". `ClaudeHomeVolume
.EnsureAsync` is a list plus at most one create, so both the GUI's launch
command and the CLI call it on the way to starting a session.

Nothing reads the credential, and nothing reports which account is signed in.
There is no auth preflight and no launch gate: a session that is not signed in
prompts for `/login` itself, which is the only place that state is worth
showing. Reading it from the host would mean a container per read, since the
volume lives inside the WSL2 VM with no path Windows can open.

Docker creates a named volume referenced by a bind that does not exist yet, so
any code that mounts the volume creates it as a side effect — unlabelled, and
`EnsureAsync` then treats it as already present and never labels it. Anything
that mounts the volume without intending to create it has to check
`ExistsAsync` first.

Because the shared volume is writable by every session, `managed-settings.json`
sets `allowManagedHooksOnly` and `allowManagedMcpServersOnly`. Hooks and
permission rules *merge* across settings scopes rather than being overridden,
so pinning values is not enough on its own — those two managed-only keys are
what stop a settings file in the volume from carrying hooks or MCP servers
(both of which run commands) from one session into the next.

## The project mount

Each project is bind-mounted read-write at `/workspace/<slug>-<hash>`, which is
also the session's working directory (`ComposePlanner.TargetFor`). Edits inside
the container are edits to the user's actual files.

The path is per project rather than a bare `/workspace` because Claude Code
keys session transcripts, auto memory and trust decisions by the project's
path, and auto memory loads at session start. A shared path files every project
under one key, so one project's notes load into a session on another.

The slug comes from the project name and the hash from the full path, so two
projects sharing a folder name stay apart and renaming a launcher entry does
not orphan its history. Changing `TargetFor` changes where every project's
history lives, so treat it as a stable key.

## Mount shadowing

Anything matching `.mountignore` is then hidden by mounting an empty stub over
it — a more specific mount target added after the project's own wins. Secrets
and config files stay on the host; inside the container they read as
zero-length.
`container-CLAUDE.md` tells sessions that an unexpectedly empty file is
deliberate and must not be reconstructed.

The default patterns live at `C3Launcher.Core/Container/.mountignore` and ship
next to the executable rather than being baked into the image.

**Working on this repo inside a session:** `*.json` is a default pattern, so
this project's own JSON — including `managed-settings.json` — is shadowed to an
empty stub. Shadowing is decided by a scan at launch, so a JSON file created
mid-session stays real until the next launch, which makes the effect look
intermittent. Read those files from the host, or launch with a project-specific
`.mountignore` via `LaunchOptions.MountIgnorePath`. Patterns have no negation
syntax.

## Lifetime and cleanup

Compose derives container names from the compose file's directory, so labels
are the only stable handle. `SessionLabels` stamps every container with
`com.c3launcher.session` plus the project path and name; `SessionService`,
`DockerEventMonitor` and the sweeps all filter on it.

Three things leak if unmanaged, and each has a sweep that runs at startup and
never throws:

- **Session folders** — `SessionWorkspace.Sweep`, keyed on live session ids.
- **Compose networks** — `SessionNetworks.SweepAsync`. Each leftover holds a
  block of the daemon's address pool, and exhausting it breaks new launches
  with "could not find an available, non-overlapping IPv4 address pool". A
  one-minute grace period protects networks created before their container
  exists.
- **Containers** — `--rm` handles the normal path.

`wt.exe` exits as soon as it hands the request to Windows Terminal, so process
exit says nothing about session lifetime. The container `die` event from
`DockerEventMonitor` is the real signal, and what `LaunchedSession.Dispose`
should wait for.

That event is not guaranteed to arrive. Restarting the daemon — a Docker Desktop
update, a crash — kills every container while the event stream is down, and
Docker does not replay what was missed once `DockerEventMonitor` re-subscribes.
So the strip is also re-listed on a timer (`MainWindowViewModel.PollAsync`), and
`Drop` is what takes a row off it: it removes the session, disposes its
workspace, and recomputes the per-project running dot from `Sessions` rather
than from Docker, so a row removed by hand leaves the same UI as one that
exited. `SessionService.StopAsync` returns `StopOutcome`, whose `AlreadyGone`
distinguishes a 404 from the daemon — nothing to stop, drop the row — from a
real failure worth putting in front of the user.

## Errors and toasts

Every user-facing error goes to a toast, and nowhere else. `ToastHost` owns the
collection and the dismiss command; each window that can fail exposes one as
`Toasts` and renders it with the shared `ToastOverlay`, so there is a single
place to learn to look. `Show` expires itself after a few seconds; `ShowError`
has no timer and carries a close button, because a failure is usually something
the user has to act on and a message that fades takes the only account of it.
Errors dedupe on their text, and over the cap the oldest *transient* toast is
dropped first so an unread error is not evicted by chatter.

`ToastHost` deliberately is not a `ViewModelBase`: `ViewLocator` resolves a view
for anything that is one, and a host is a piece of a screen rather than a screen.

What is left in the other slots is state, not notification: `StatusText` narrates
a launch in progress and is cleared in a `finally`, `MountSummary` says
`scan failed` with the reason in the toast, and the update pill says
`update check failed` or `Docker unavailable` with the reason in the toast. The
pill has three forms — green tick, accent arrow for an available version, and a
red warning when `UpdateFailed` — so a failure never reads as success in the one
slot that is a tick by default.

## Conventions

File-scoped namespaces, `sealed` by default, records for data, collection
expressions, and `Try*(out …)` for expected failure. Errors that a user should
see come back as a result record with a message (`LaunchOutcome`,
`BuildOutcome`, `StopOutcome`, `ClaudeAccountResult`) rather than as exceptions;
cleanup paths swallow exceptions on purpose.

GUI view models use `[ObservableProperty] public partial` and `[RelayCommand]`.
Core has no UI dependency and no Avalonia reference — keep it that way.
