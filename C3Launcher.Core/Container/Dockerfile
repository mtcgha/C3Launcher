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

# node:22-slim already ships a non-root user called "node" (UID/GID 1000)
RUN mkdir -p /workspace /mnt/claude-dir && chown node:node /workspace /mnt/claude-dir

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

USER node
WORKDIR /workspace

ENV COLORTERM=truecolor
ENV DISABLE_AUTOUPDATER=1

# tmpfs covers /home/node so Claude Code can write trust decisions and token
# refreshes freely — nothing persists back to the host
# Run via sh rather than exec form: COPY from a Windows host lands the script
# non-executable, and this avoids needing BuildKit's COPY --chmod to fix it.
ENTRYPOINT ["/bin/sh", "/entrypoint.sh"]
