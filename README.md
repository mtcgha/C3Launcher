# C3Launcher

A Windows desktop app that runs Claude Code sessions in locked-down Docker containers, one container per session.

I use it daily for running Claude Code on my own projects. A session has access to one project directory and nothing else on the machine.

It also keeps my projects in one place: a filterable list, a marker on the ones with a session running, and a strip showing each live session's uptime, CPU, and memory.

## Prerequisites

- Windows 10/11
- [Docker Desktop](https://www.docker.com/products/docker-desktop/) with the WSL2 backend, running
- [.NET 10 SDK](https://dotnet.microsoft.com/download)
- Windows Terminal (optional, falls back to a console window)

## Running

```powershell
git clone https://github.com/mtcgha/C3Launcher.git
cd C3Launcher
dotnet build
dotnet run --project C3Launcher.Gui
```

On first launch, hit **Rebuild** to build the container image. Add a project, select it, launch a session.

The first session starts logged out. Run `/login` and paste the code back from the browser, since the container can't reach Claude Code's local callback server. Later sessions pick the login up from the shared volume.

## Layout

| Project | Output | Role |
| --- | --- | --- |
| `C3Launcher.Core` | library | Compose planning, Docker, mounting, auth, container build context |
| `C3Launcher.Gui` | WinExe | Avalonia desktop app, the primary surface |
| `C3Launcher.Cli` | Exe | Single-session launcher over the same Core |

Windows-only by design: `wt.exe`, `%USERPROFILE%`, `LocalApplicationData` and Docker Desktop are all assumed. `CLAUDE.md` has the architecture notes.

## CLI

`C3Launcher.Cli` launches a single session without the UI:

```powershell
dotnet run --project C3Launcher.Cli -- [ProjectPath] [--model <name>] [--no-build] [--verbose] [-- ClaudeArgs...]
```

Defaults to the current directory. Everything after the second `--` is forwarded to the `claude` binary.

## Scope

Personal project, public so it can be cloned or forked.

## Isolation

- One disposable container per session (`docker compose run --rm`), opened in a Windows Terminal tab.
- The project is bind-mounted read-write, so edits inside the container are edits to the files on disk.
- Files matching `.mountignore` are shadowed with empty stubs. They stay on the host and read as zero-length inside. Defaults cover `*.json`, `*.env`, `*.config`, `*.sql`, `*.lic`, `*.keystore` and a few more.
- The host's `~/.claude` is never mounted. Containers log in separately and keep it in a Docker named volume shared between sessions, so a token refresh inside a container can't invalidate the host's login.
- Read-only root filesystem, `cap_drop: ALL`, `no-new-privileges`, non-root user, `/tmp` on a `noexec` tmpfs.
- Managed settings on the read-only root filesystem stop hooks and MCP servers leaking between sessions through the shared volume.

## Security

The container limits blast radius. It is not a hardened sandbox.

Covered:

- Mistakes. Deleting the wrong thing, editing files outside the project, reading SSH keys or browser profiles. A session sees one project directory.
- Host credentials. The host's `~/.claude` is never mounted, so a container can't read it or invalidate its login.

Not covered:

- Network egress. The container has unrestricted outbound access. Project contents can leave, and anything pulled from npm or PyPI runs with a full network.
- The project itself. It's mounted read-write, so anything in it can be changed or destroyed. Source control is the only undo.
- Persistence between sessions. `/home/node` is a shared writable volume and is also on `PATH`, so one session can leave a binary or dotfile behind for a later one to pick up. Managed settings block hooks and MCP servers, not this.
- Container escape. Containers share the host kernel, so a kernel or runtime bug gets out. Docker Desktop's WSL2 VM is a second layer, not a guarantee.

## License

MIT. See [LICENSE](LICENSE).
