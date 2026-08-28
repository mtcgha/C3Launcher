#!/bin/sh
# Exit immediately on any error (-e) and treat unset variables as errors (-u).
set -eu

# Create the .claude config directory in the writable tmpfs home if it doesn't
# already exist. cp will fail without this when the tmpfs starts empty.
mkdir -p "$HOME/.claude"

# Copy the host's ~/.claude directory (mounted read-only at /mnt/claude-dir)
# into the writable tmpfs home so Claude Code can update it freely at runtime
# (trust decisions, token refreshes, etc.) without touching the host original.
cp -r /mnt/claude-dir/. "$HOME/.claude/"

# Copy the host's ~/.claude.json OAuth account file (mounted read-only at
# /mnt/claude-json) into the writable tmpfs home for the same reason.
cp /mnt/claude-json "$HOME/.claude.json"

# Create ~/.local/bin and symlink the global claude binary into it.
# Claude Code (installMethod=native) checks for its own binary at
# ~/.local/bin/claude and warns if it is absent. The npm global install
# lands in /usr/local/bin, but /home/node is a tmpfs at runtime so nothing
# from the image layer under $HOME survives — the symlink must be created here.
mkdir -p "$HOME/.local/bin"
ln -sf /usr/local/bin/claude "$HOME/.local/bin/claude"
export PATH="$HOME/.local/bin:$PATH"

# Replace this shell process with claude, forwarding any arguments passed to
# the entrypoint (e.g. --model, --debug). Using exec avoids a wrapper process
# and ensures signals (SIGINT, SIGTERM) reach claude directly.
exec claude "$@"
