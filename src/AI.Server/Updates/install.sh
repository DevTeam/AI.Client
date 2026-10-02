#!/bin/sh
# Runs outside the application directory and survives the parent process.
set -u
pid=$1; product=$2; version=$3; package=$4; digest=$5; result=$6; executable=$7; install_dir=$8; companion=$9
shift 9
companion_digest=$1
shift
log_dir=$(dirname "$result")
exec >>"$log_dir/installer.log" 2>&1
success=false
attempt=0
while kill -0 "$pid" 2>/dev/null; do
    attempt=$((attempt + 1))
    if [ "$attempt" -ge 60 ]; then
        printf '{"Success":false,"Version":"%s"}' "$version" > "$result.tmp"
        mv "$result.tmp" "$result"
        exit 1
    fi
    sleep 1
done

verify() {
    if [ "$(uname -s)" = Darwin ]; then actual=$(shasum -a 256 "$1" | cut -d ' ' -f 1)
    else actual=$(sha256sum "$1" | cut -d ' ' -f 1); fi
    [ "$actual" = "$2" ]
}
# Single-quote paths when generating the privileged command; no untrusted shell interpolation.
quote() { printf "'%s'" "$(printf '%s' "$1" | sed "s/'/'\\\\''/g")"; }
if verify "$package" "$digest"; then
    if [ "$(uname -s)" = Darwin ]; then
        choices="$log_dir/choices.xml"
        selected=0
        [ -d '/Library/Application Support/AI Client/McpCsharp' ] && selected=1
        printf '<?xml version="1.0"?><plist version="1.0"><array><dict><key>choiceIdentifier</key><string>csharp</string><key>choiceAttribute</key><string>selected</string><key>attributeSetting</key><integer>%s</integer></dict></array></plist>' "$selected" > "$choices"
        command="/usr/sbin/installer -pkg $(quote "$package") -target / -applyChoiceChangesXML $(quote "$choices")"
        # Pass the command as argv, rather than embedding it in AppleScript source.
        if /usr/bin/osascript - "$command" <<'APPLESCRIPT'
on run argv
    do shell script (item 1 of argv) with administrator privileges
end run
APPLESCRIPT
        then success=true; fi
    else
        command="dpkg -i $(quote "$package")"
        if [ -n "$companion" ]; then
            if verify "$companion" "$companion_digest"; then command="$command $(quote "$companion")"
            else command=false; fi
        fi
        if command -v pkexec >/dev/null 2>&1; then
            if pkexec /bin/sh -c "$command"; then success=true; fi
        elif command -v zenity >/dev/null 2>&1; then
            # sudo prompts through the desktop, never through an invisible terminal.
            askpass="$log_dir/askpass.sh"
            printf '#!/bin/sh\nexec zenity --password --title="AI Client update"\n' > "$askpass"
            chmod 700 "$askpass"
            if SUDO_ASKPASS="$askpass" sudo -A /bin/sh -c "$command"; then success=true; fi
        else
            echo 'Install policykit (pkexec) or zenity to authorize package updates.'
        fi
    fi
fi
printf '{"Success":%s,"Version":"%s"}' "$success" "$version" > "$result.tmp"
mv "$result.tmp" "$result"
if [ "$product" = Host ] && [ "$#" -eq 1 ] && [ "$1" = --public-web ]; then
    if [ "$(uname -s)" = Darwin ]; then
        launchctl kickstart "gui/$(id -u)/org.devteam.ai-client-host" || true
    else
        systemctl --user start ai-client-host.service || true
    fi
else
    "$executable" "$@" </dev/null >/dev/null 2>&1 &
fi
if [ "$product" = Host ] && [ "$(uname -s)" = Darwin ]; then
    launchctl remove "org.devteam.ai-client-update.$pid" || true
fi
