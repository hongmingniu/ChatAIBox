#!/bin/sh
set -eu
[ "$(id -u)" -eq 0 ] || { echo 'Run this script as root.' >&2; exit 1; }
command -v node >/dev/null || { echo 'Install Node.js 22+ first.' >&2; exit 1; }
node -e 'if(Number(process.versions.node.split(".")[0])<22)process.exit(1)'
base=$(CDPATH= cd -- "$(dirname -- "$0")" && pwd)
[ -f "$base/node_modules/ws/package.json" ] || { echo 'Run npm ci --omit=dev --ignore-scripts in this directory first.' >&2; exit 1; }
install -d -m 755 /opt/chataibox-relay
install -m 644 "$base/relay.cjs" "$base/package.json" /opt/chataibox-relay/
cp -R "$base/node_modules" /opt/chataibox-relay/
chmod -R a+rX /opt/chataibox-relay
if [ ! -f /etc/chataibox-relay.env ]; then
    node "$base/generate-key.cjs" /etc/chataibox-relay.env
fi
install -m 644 "$base/chataibox-relay.service" /etc/systemd/system/chataibox-relay.service
systemctl daemon-reload
systemctl enable chataibox-relay.service
systemctl restart chataibox-relay.service
echo 'Relay is bound to 127.0.0.1:39181. Configure your existing HTTPS reverse proxy; see README.md.'
