#!/bin/bash

# gemini-wrapper.sh
# Restricts commands that the GeminiWorker background service can execute
# inside the vault sandbox. Only explicitly allowlisted binaries are permitted.
# All arguments after the command are passed through as-is.

set -euo pipefail

COMMAND=$1
shift  # remaining args are passed to the command

ALLOWED_COMMANDS=("git" "gemini" "ls" "cat" "grep" "echo" "touch" "mkdir" "pwd")

is_allowed=false
for allowed in "${ALLOWED_COMMANDS[@]}"; do
    if [ "$COMMAND" = "$allowed" ]; then
        is_allowed=true
        break
    fi
done

if [ "$is_allowed" = true ]; then
    # Execute the allowed command, passing all remaining args correctly
    exec "$COMMAND" "$@"
else
    echo "ERROR: Command '$COMMAND' is not permitted in the vault sandbox." >&2
    exit 1
fi
