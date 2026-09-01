FROM node:22-slim

ENV DEBIAN_FRONTEND=noninteractive

# Install runtime deps and the tooling Claude reaches for most often:
#   git, ca-certificates  - already required for source control / TLS
#   curl, wget            - fetching files and probing endpoints
#   jq                    - JSON on the command line; used constantly
#   ripgrep               - fast search from Bash (Claude's Grep tool ships
#                           its own copy, but `rg` in a shell is separate)
#   less, procps, file    - paging, `ps`, type detection
#   unzip, zip, xz-utils  - archive handling
#   python3 + venv + dev  - Python work; -dev supplies the headers that
#                           build-essential needs to compile C extensions
#   build-essential,      - native builds: node-gyp modules and Python
#   pkg-config              wheels that have no prebuilt binary
RUN apt-get update && apt-get install -y --no-install-recommends \
      git \
      ca-certificates \
      curl \
      wget \
      jq \
      ripgrep \
      less \
      procps \
      file \
      unzip \
      zip \
      xz-utils \
      python3 \
      python3-venv \
      python3-dev \
      build-essential \
      pkg-config \
  && rm -rf /var/lib/apt/lists/*

# uv: single-binary Python package/venv manager. Installed instead of
# python3-pip because Debian marks its Python as externally managed
# (PEP 668), which makes a bare `pip install` fail. uv creates and manages
# venvs without tripping that.
RUN curl -LsSf https://astral.sh/uv/install.sh \
      | env UV_INSTALL_DIR=/usr/local/bin sh

# node:22-slim already ships a non-root user called "node" (UID/GID 1000).
# /home/node is the mount point for the shared Claude home volume; Docker copies
# this directory's ownership onto the volume the first time it is populated, so
# chowning it here is what lets the unprivileged "node" user write to it.
RUN mkdir -p /workspace && chown node:node /workspace /home/node

COPY entrypoint.sh /entrypoint.sh

# Claude Code is installed LAST so that bumping CLAUDE_VERSION invalidates
# only this layer. Everything above it stays in the layer cache, which keeps
# an update rebuild to a few seconds instead of re-running the whole apt
# install. Installed as root so it lands in PATH, then we drop to "node".
ARG CLAUDE_VERSION=latest
RUN npm install -g @anthropic-ai/claude-code@${CLAUDE_VERSION}

# Managed-policy CLAUDE.md: loaded into every session in every project, ahead
# of the user's ~/.claude/CLAUDE.md and any project CLAUDE.md, and merged with
# them rather than replacing them. It lives here rather than in /workspace
# because that path is a bind mount of the user's real project directory — a
# file written there would appear in their working tree. The read-only root
# filesystem keeps this copy from being modified at runtime.
COPY container-CLAUDE.md /etc/claude-code/CLAUDE.md

# Managed settings: the highest-precedence settings tier, and the only one the
# container cannot rewrite (read-only root filesystem, and not under $HOME).
# The Claude home volume is shared and writable by every session, so a settings
# file there could otherwise carry hooks or MCP servers — both of which run
# commands — from one session into the next. These two keys close that path:
#   allowManagedHooksOnly       - only hooks deployed here run; hooks written
#                                 into the volume are ignored rather than merged
#   allowManagedMcpServersOnly  - only the managed MCP allowlist applies, and
#                                 none is deployed, so no MCP server can start
# Hooks and permission rules merge across settings scopes rather than being
# overridden, so pinning values here is not enough on its own — these are the
# managed-only keys that switch that merge off.
COPY managed-settings.json /etc/claude-code/managed-settings.json

USER node
WORKDIR /workspace

ENV COLORTERM=truecolor
ENV DISABLE_AUTOUPDATER=1

# Fullscreen rendering instead of the default scrolling renderer, which leaves
# the session in the terminal's own scrollback. Set here rather than left to
# /tui fullscreen so it holds on a volume that has no saved preference yet.
ENV CLAUDE_CODE_NO_FLICKER=1

# /home/node is a Docker named volume at runtime, shared by every session: the
# login Claude Code writes there has to outlive the container, or each session
# starts logged out. It is Docker-managed storage, unreachable from the host
# user profile.
# Run via sh rather than exec form: COPY from a Windows host lands the script
# non-executable, and this avoids needing BuildKit's COPY --chmod to fix it.
ENTRYPOINT ["/bin/sh", "/entrypoint.sh"]
