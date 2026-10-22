#!/bin/sh
# Moves the local user's data from the top of an AI data directory into users/<local user id>/
# (ADR-014). Run it once, with the Host and the desktop app stopped:
#
#   ./move-data-to-user.sh "$HOME/.local/share/AI"
#
# Each entry moves on its own and an entry already in place is skipped, so an interrupted run can
# simply be repeated. Nothing is overwritten and no document is changed.
set -u

if [ "$#" -ne 1 ]; then
    echo "Usage: $0 <data directory>" >&2
    exit 2
fi

root=$1
if [ ! -d "$root" ]; then
    echo "The data directory '$root' does not exist." >&2
    exit 1
fi

target="$root/users/00000000000000000000000000000001"
mkdir -p "$target" || exit 1

failed=0
for entry in settings.json command-rules.json project-order.json credentials projects memory instructions \
    resources assets usage skills; do
    [ -e "$root/$entry" ] || continue
    if [ -e "$target/$entry" ]; then
        echo "Skipped $entry: '$target/$entry' already exists." >&2
        failed=1
        continue
    fi
    if mv "$root/$entry" "$target/$entry"; then
        echo "Moved $entry"
    else
        echo "Could not move $entry." >&2
        failed=1
    fi
done

if [ "$failed" -ne 0 ]; then
    echo "Some entries were not moved; see the messages above." >&2
    exit 1
fi
echo "Done: $target"
