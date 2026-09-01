#!/bin/sh
# Exit immediately on any error (-e) and treat unset variables as errors (-u).
set -eu

# $HOME is a Docker named volume shared by every session, so Claude Code's
# config directory, credentials and history are already there and already
# current. Nothing is copied in from the host: the host's ~/.claude is not
# mounted at all, and the container's login is its own.
#
# The first session on a fresh volume starts logged out. Run /login inside it
# once; the browser shows a code to paste back, because the container cannot
# reach Claude Code's local callback server. Every later session — and every
# container after this one — picks up that login from the volume.

# Create ~/.local/bin and symlink the global claude binary into it.
# Claude Code (installMethod=native) checks for its own binary at
# ~/.local/bin/claude and warns if it is absent. The npm global install lands
# in /usr/local/bin, so the symlink has to point there. -f keeps this
# idempotent across restarts now that $HOME persists.
mkdir -p "$HOME/.local/bin"
ln -sf /usr/local/bin/claude "$HOME/.local/bin/claude"
export PATH="$HOME/.local/bin:$PATH"

# Replace this shell process with claude, forwarding any arguments passed to
# the entrypoint (e.g. --model, --debug). Using exec avoids a wrapper process
# and ensures signals (SIGINT, SIGTERM) reach claude directly.
exec claude "$@"
