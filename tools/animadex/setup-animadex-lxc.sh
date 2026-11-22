#!/usr/bin/env bash
# =============================================================================
#  AnimaDex in einem Proxmox-LXC-Container einrichten (Debian 12/13)
#
#  Installiert AnimaDex (https://github.com/zetaneko/AnimaDex) als Dienst,
#  erreichbar im Heimnetz unter http://<container-ip>:<port>, und importiert den
#  Katalog von animadex.net (nur Vorschaubilder, ein paar GB).
#
#  Aufruf im Container als root:
#     bash setup-animadex-lxc.sh                 # fragt nach dem Export-Token
#     bash setup-animadex-lxc.sh --skip-import   # nur installieren, Import später
#     bash setup-animadex-lxc.sh --port 5000 --save-token
#
#  Optionen:
#     --port <n>       Port im Netz (Standard 5000)
#     --dir <pfad>     Installationsordner (Standard /srv/animadex)
#     --skip-import    Katalog nicht importieren (später: animadex-update)
#     --save-token     Token in /etc/animadex/token ablegen (nur root lesbar),
#                      damit animadex-update ohne Nachfrage läuft
#     --branch <name>  Git-Branch (Standard: Standard-Branch des Repos)
#
#  Kann gefahrlos erneut ausgeführt werden: vorhandene Installation wird
#  aktualisiert, config.toml und Daten bleiben erhalten.
#
#  Token: animadex.net -> Account -> "Offline dataset export" -> Generate token
# =============================================================================
set -euo pipefail

PORT=5000
APP_DIR=/srv/animadex
DO_IMPORT=1
SAVE_TOKEN=0
BRANCH=""
REPO_URL="https://github.com/zetaneko/AnimaDex.git"
SVC_USER=animadex
ETC_DIR=/etc/animadex

while [ $# -gt 0 ]; do
    case "$1" in
        --port)        PORT="$2"; shift 2 ;;
        --dir)         APP_DIR="$2"; shift 2 ;;
        --skip-import) DO_IMPORT=0; shift ;;
        --save-token)  SAVE_TOKEN=1; shift ;;
        --branch)      BRANCH="$2"; shift 2 ;;
        -h|--help)     sed -n '2,27p' "$0"; exit 0 ;;
        *) echo "Unbekannte Option: $1" >&2; exit 2 ;;
    esac
done

say()  { echo; echo "==> $*"; }
fail() { echo "FEHLER: $*" >&2; exit 1; }

[ "$(id -u)" -eq 0 ] || fail "Bitte als root ausführen."
command -v apt-get >/dev/null || fail "Dieses Skript ist für Debian/Ubuntu-Container gedacht."
case "$PORT" in ''|*[!0-9]*) fail "Ungültiger Port: $PORT" ;; esac

# Token früh abfragen, damit der Rest ohne Rückfrage durchläuft
TOKEN=""
if [ "$DO_IMPORT" = 1 ]; then
    if [ -s "$ETC_DIR/token" ]; then
        TOKEN="$(cat "$ETC_DIR/token")"
        echo "Export-Token aus $ETC_DIR/token wird verwendet."
    else
        echo
        echo "Export-Token von animadex.net (Account -> \"Offline dataset export\")."
        echo "Leer lassen, um den Import zu überspringen."
        read -rsp "Token: " TOKEN; echo
        [ -n "$TOKEN" ] || { DO_IMPORT=0; echo "Kein Token – Import wird übersprungen."; }
    fi
fi

# --- 1. Pakete -------------------------------------------------------------
say "Pakete installieren"
export DEBIAN_FRONTEND=noninteractive
export PIP_NO_CACHE_DIR=1
apt-get update -qq
apt-get install -y -qq git python3 python3-venv python3-pip sqlite3 ca-certificates curl >/dev/null
PYVER="$(python3 -c 'import sys; print(f"{sys.version_info.major}.{sys.version_info.minor}")')"
case "$PYVER" in 3.11|3.12|3.13|3.14) echo "Python $PYVER" ;; *) fail "Python 3.11+ nötig, gefunden $PYVER (Debian 12 oder neuer verwenden)." ;; esac

# --- 2. Benutzer und Code --------------------------------------------------
say "Dienstbenutzer und Code"
id "$SVC_USER" >/dev/null 2>&1 || useradd --system --home-dir "$APP_DIR" --shell /usr/sbin/nologin "$SVC_USER"
PARENT="$(dirname "$APP_DIR")"
mkdir -p "$PARENT"
if [ -d "$APP_DIR/.git" ]; then
    echo "Vorhandene Installation in $APP_DIR – aktualisiere."
    chown -R "$SVC_USER:$SVC_USER" "$APP_DIR"
    sudo_u() { runuser -u "$SVC_USER" -- "$@"; }
    sudo_u git -C "$APP_DIR" pull --ff-only
else
    [ -e "$APP_DIR" ] && [ -n "$(ls -A "$APP_DIR" 2>/dev/null)" ] && fail "$APP_DIR existiert und ist nicht leer."
    mkdir -p "$APP_DIR"
    chown "$SVC_USER:$SVC_USER" "$APP_DIR"
    if [ -n "$BRANCH" ]; then
        runuser -u "$SVC_USER" -- git clone --depth 1 --branch "$BRANCH" "$REPO_URL" "$APP_DIR"
    else
        runuser -u "$SVC_USER" -- git clone --depth 1 "$REPO_URL" "$APP_DIR"
    fi
fi
# Datenordner liegt laut Standard-config neben dem Code (../animadex-data)
DATA_DIR="$PARENT/animadex-data"
mkdir -p "$DATA_DIR"
chown "$SVC_USER:$SVC_USER" "$DATA_DIR"
run_app() { runuser -u "$SVC_USER" -- bash -c "cd '$APP_DIR' && $*"; }

# --- 3. Installation (venv, Abhängigkeiten, config.toml, DB, Beispiele) ----
say "AnimaDex installieren (install.sh)"
run_app "bash ./install.sh"   # ohne Ausführungsrecht im Repo nicht direkt startbar
run_app ". .venv/bin/activate && python -m pip install -q gunicorn"

# --- 4. Konfiguration ------------------------------------------------------
say "config.toml anpassen"
CFG="$APP_DIR/config.toml"
[ -f "$CFG" ] || fail "config.toml fehlt nach install.sh."
cp -n "$CFG" "$CFG.orig" || true
set_key() {   # set_key <abschnitt> <schlüssel> <wert-roh>
    python3 - "$CFG" "$1" "$2" "$3" <<'PY'
import re, sys
path, section, key, value = sys.argv[1:5]
lines = open(path, encoding="utf-8").read().splitlines(keepends=True)
cur, done = None, False
for i, l in enumerate(lines):
    m = re.match(r"\s*\[([^\]]+)\]", l)
    if m:
        cur = m.group(1).strip(); continue
    if cur == section and re.match(rf"\s*{re.escape(key)}\s*=", l):
        lines[i] = f"{key} = {value}\n"; done = True; break
if not done:
    raise SystemExit(f"Schlüssel [{section}].{key} nicht gefunden")
open(path, "w", encoding="utf-8").writelines(lines)
PY
}
# Server im Netz erreichbar machen (gunicorn bindet selbst, host/port für 'serve' und Links)
set_key server host '"0.0.0.0"'
set_key server port "$PORT"
# Geheimschlüssel nur setzen, wenn noch leer
if grep -Eq '^\s*secret_key\s*=\s*""' "$CFG"; then
    KEY="$(run_app ". .venv/bin/activate && python -m animadex genkey")"
    set_key server secret_key "\"$KEY\""
    echo "secret_key erzeugt."
fi
# Lastbremse aus: gedacht für öffentliche Seiten, unsere Apps laden viele Vorschaubilder auf einmal
set_key ratelimit enabled false
# Kontaktformular braucht im Heimnetz niemand
set_key features contact_enabled false
chown "$SVC_USER:$SVC_USER" "$CFG"
chmod 640 "$CFG"

# --- 5. Katalog importieren ------------------------------------------------
if [ "$DO_IMPORT" = 1 ]; then
    say "Katalog von animadex.net importieren (nur Vorschaubilder – kann eine Weile dauern)"
    # Token per Umgebungsvariable an das Import-Skript (nicht dauerhaft gespeichert, außer mit --save-token)
    runuser -u "$SVC_USER" -- env ANIMADEX_IMPORT_TOKEN="$TOKEN" \
        bash -c "cd '$APP_DIR' && . .venv/bin/activate && python scripts/import_from_site.py --token \"\$ANIMADEX_IMPORT_TOKEN\""
    if [ "$SAVE_TOKEN" = 1 ]; then
        mkdir -p "$ETC_DIR"; umask 077
        printf '%s' "$TOKEN" > "$ETC_DIR/token"; chmod 600 "$ETC_DIR/token"
        echo "Token gespeichert in $ETC_DIR/token (nur root)."
    fi
fi

# --- 6. Dienst -------------------------------------------------------------
say "systemd-Dienst einrichten"
cat > /etc/systemd/system/animadex.service <<EOF
[Unit]
Description=AnimaDex (Charakter- und Künstler-Katalog)
After=network-online.target
Wants=network-online.target

[Service]
User=$SVC_USER
Group=$SVC_USER
WorkingDirectory=$APP_DIR
Environment=ANIMADEX_CACHE_API_SECONDS=60
ExecStart=$APP_DIR/.venv/bin/gunicorn -w 2 --threads 4 -b 0.0.0.0:$PORT --timeout 120 --access-logfile - 'animadex.app:create_app()'
Restart=on-failure
RestartSec=5
NoNewPrivileges=true
PrivateTmp=true
ProtectSystem=full
ProtectHome=true

[Install]
WantedBy=multi-user.target
EOF
systemctl daemon-reload
systemctl enable animadex >/dev/null 2>&1
systemctl restart animadex

# --- 7. Update-Befehl ------------------------------------------------------
say "Befehl animadex-update anlegen"
cat > /usr/local/bin/animadex-update <<EOF
#!/usr/bin/env bash
# AnimaDex aktualisieren: Code (git pull), Abhängigkeiten, Katalog (nur Änderungen), Dienst neu starten.
set -euo pipefail
[ "\$(id -u)" -eq 0 ] || { echo "Bitte als root ausführen." >&2; exit 1; }
APP_DIR='$APP_DIR'; SVC_USER='$SVC_USER'
runuser -u "\$SVC_USER" -- git -C "\$APP_DIR" pull --ff-only
runuser -u "\$SVC_USER" -- bash -c "cd '\$APP_DIR' && . .venv/bin/activate && python -m pip install -q -r requirements.txt && python -m pip install -q -e . gunicorn && python -m animadex db-init"
if [ -s '$ETC_DIR/token' ]; then TOKEN="\$(cat '$ETC_DIR/token')"; else read -rsp "Export-Token (leer = Katalog nicht aktualisieren): " TOKEN; echo; fi
if [ -n "\$TOKEN" ]; then
    runuser -u "\$SVC_USER" -- env ANIMADEX_IMPORT_TOKEN="\$TOKEN" \\
        bash -c "cd '\$APP_DIR' && . .venv/bin/activate && python scripts/import_from_site.py --token \"\\\$ANIMADEX_IMPORT_TOKEN\""
fi
systemctl restart animadex
echo "AnimaDex aktualisiert."
EOF
chmod 755 /usr/local/bin/animadex-update

# --- 8. Prüfen ---------------------------------------------------------------
say "Prüfen"
ok=0
for _ in $(seq 1 20); do
    if curl -fsS "http://127.0.0.1:$PORT/api/characters/facets" -o /tmp/animadex-check.json 2>/dev/null; then ok=1; break; fi
    sleep 1
done
if [ "$ok" = 1 ]; then
    TOTAL="$(python3 -c 'import json; print(json.load(open("/tmp/animadex-check.json")).get("total", "?"))' 2>/dev/null || echo "?")"
    echo "Dienst läuft – $TOTAL Charaktere in der Datenbank."
else
    echo "Dienst antwortet nicht. Log ansehen mit:  journalctl -u animadex -n 50"
fi
rm -f /tmp/animadex-check.json

IP="$(hostname -I 2>/dev/null | awk '{print $1}')"
cat <<EOF

============================================================
  AnimaDex ist eingerichtet.

  Im Browser:     http://${IP:-<container-ip>}:$PORT
  API-Test:       http://${IP:-<container-ip>}:$PORT/api/characters/search?q=miku

  Dienst:         systemctl status animadex
  Log:            journalctl -u animadex -f
  Aktualisieren:  animadex-update   (Code + Katalog-Änderungen)
  Konfiguration:  $APP_DIR/config.toml  (Original: config.toml.orig)
  Daten:          $DATA_DIR
============================================================
EOF
