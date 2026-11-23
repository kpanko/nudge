# shellcheck shell=bash
#
# Helpers for the /verify skill. Source this file, then run launch,
# doctor, drive and cleanup in that SAME shell, so the app stays a
# direct background job of it. See SKILL.md.
#
# Works in Git Bash on Windows and in bash on Linux. Needs dotnet,
# curl, git and a Python 3 with sqlite3.

NV_SKILL_DIR=$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)
NV_HOME=${NUDGE_VERIFY_HOME:-$HOME/.nudge-verify}
NV_TIME_ZONE=Etc/UTC
NV_HTTP_TIMEOUT=30
NV_HEALTH_DEADLINE=60
NV_STOPPED=1
NV_HEADERS=()

case "$(uname -s)" in
    MINGW* | MSYS* | CYGWIN*) NV_OS=windows ;;
    *) NV_OS=linux ;;
esac

# A path as native programs (dotnet, Python) want it.
_nv_native() {
    if [ "$NV_OS" = windows ]; then
        cygpath -m "$1"
    else
        printf '%s\n' "$1"
    fi
}

_nv_die() {
    printf 'verify: REFUSED: %s\n' "$*" >&2
    return 1
}

_nv_repo() {
    local top
    top=$(git -C "$NV_SKILL_DIR" rev-parse --show-toplevel) || return 1
    if [ "$NV_OS" = windows ]; then
        cygpath -u "$top"
    else
        printf '%s\n' "$top"
    fi
}

# --- Python -----------------------------------------------------------

# Finds an interpreter by running it: a shim that exists but does not
# work is skipped. Keeps the real executable, not the shim.
_nv_find_python() {
    [ -n "${NV_PY:-}" ] && return 0
    local candidate out
    local check='import json,sqlite3,sys;print("nv-ok",sys.executable)'
    for candidate in python3 python py; do
        command -v "$candidate" > /dev/null 2>&1 || continue
        out=$(timeout 20 "$candidate" -I -c "$check" 2> /dev/null \
            | tr -d '\r') || continue
        case "$out" in
            "nv-ok "*)
                NV_PY=${out#nv-ok }
                [ "$NV_OS" = windows ] && NV_PY=$(cygpath -u "$NV_PY")
                return 0
                ;;
        esac
    done
    _nv_die "no working Python 3 with sqlite3; database state cannot" \
        "be verified, so the run stops here"
}

# Read-only by two locks: the file is opened mode=ro, and query_only
# is set. Rows are printed with | between columns.
_NV_SQL='
import pathlib, sqlite3, sys
uri = pathlib.Path(sys.argv[1]).resolve().as_uri() + "?mode=ro"
db = sqlite3.connect(uri, uri=True, timeout=5)
db.execute("PRAGMA query_only = ON")
for row in db.execute(sys.argv[2], sys.argv[3:]).fetchall():
    print("|".join("NULL" if v is None else str(v) for v in row))
'

# A failed read prints a marker that can never equal a real value. A
# JSON null prints null; a missing key is a failure, not a null.
_NV_GET='
import json, sys
def fail(why):
    print("<nv_get failed: %s>" % why)
    sys.exit(3)
try:
    value = json.load(open(sys.argv[1], encoding="utf-8"))
except Exception:
    fail("answer is not JSON")
for part in filter(None, sys.argv[2].split(".")):
    if isinstance(value, list):
        if not part.isdigit() or int(part) >= len(value):
            fail("no element " + part)
        value = value[int(part)]
    elif isinstance(value, dict):
        if part not in value:
            fail("no key " + part)
        value = value[part]
    else:
        fail("nothing at " + part)
print(value if isinstance(value, str) else json.dumps(value))
'

_NV_HAS_ID='
import json, sys
try:
    rows = json.load(open(sys.argv[1], encoding="utf-8"))
except Exception:
    rows = None
if not isinstance(rows, list):
    print("<nv_get failed: answer is not a JSON list>")
    sys.exit(3)
print("yes" if any(r.get("id") == sys.argv[2] for r in rows) else "no")
'

# --- Process and port -------------------------------------------------

# PIDs of processes whose command line names this run's Nudge.Web.dll.
# Returns non-zero if the list could not be read: "none found" must
# never be concluded from a failed lookup.
_nv_run_pids() {
    if [ "$NV_OS" = windows ]; then
        local out
        # Values go by environment: quotes do not survive the trip to
        # powershell.exe.
        out=$(NV_MATCH=$NV_DLL_NATIVE NV_IMAGE=dotnet.exe \
            powershell.exe -NoProfile -NonInteractive -Command '
            $ErrorActionPreference = [Management.Automation.ActionPreference]::Stop
            Get-CimInstance Win32_Process |
            Where-Object { $_.Name -eq $env:NV_IMAGE -and $_.CommandLine -and
                $_.CommandLine.IndexOf($env:NV_MATCH,
                    [StringComparison]::OrdinalIgnoreCase) -ge 0 } |
            ForEach-Object { $_.ProcessId }') || return 1
        printf '%s' "$out" | tr -d '\r'
        [ -z "$out" ] || printf '\n'
    else
        local dir
        local -a argv
        for dir in /proc/[0-9]*; do
            mapfile -d '' -t argv 2> /dev/null < "$dir/cmdline" || continue
            if [ "${argv[0]##*/}" = dotnet ] \
                && [ "${argv[1]:-}" = "$NV_DLL_NATIVE" ]; then
                printf '%s\n' "${dir#/proc/}"
            fi
        done
    fi
}

# The PID listening on a loopback port; empty if none is.
_nv_port_owner() {
    local port=$1
    if [ "$NV_OS" = windows ]; then
        netstat -ano -p TCP | tr -d '\r' | awk -v p=":$port" '
            $1 == "TCP" && $4 == "LISTENING" &&
            substr($2, length($2) - length(p) + 1) == p { print $5; exit }'
        return 0
    fi
    local hex inode dir
    hex=$(printf '%04X' "$port")
    inode=$(cat /proc/net/tcp /proc/net/tcp6 2> /dev/null | awk -v h=":$hex" '
        $4 == "0A" && substr($2, length($2) - 4) == h { print $10; exit }')
    [ -n "$inode" ] || return 0
    for dir in /proc/[0-9]*; do
        if ls -l "$dir/fd" 2> /dev/null \
            | grep -qF "socket:[$inode]"; then
            printf '%s\n' "${dir#/proc/}"
            return 0
        fi
    done
    printf 'unknown\n'
}

# --- Guards -----------------------------------------------------------

# The one check every start goes through: the connection string must
# be exactly this run's scratch database.
_nv_guard_connection() {
    local given=$1 expected
    expected="Data Source=$(_nv_native "$NV_SCRATCH")/nudge.db"
    if [ "$given" != "$expected" ]; then
        _nv_die "connection string is not this run's scratch database"
        return 1
    fi
    if _nv_is_under "$(_nv_lexical "$NV_SCRATCH")" /data; then
        _nv_die "scratch directory is under /data"
        return 1
    fi
}

# A path with repeated slashes collapsed, made absolute. Dot segments
# are refused: they could step out of whatever is checked.
_nv_lexical() {
    local path=$1
    [ -n "$path" ] || return 1
    if [ "$NV_OS" = windows ]; then
        case "$path" in
            [A-Za-z]:* | *\\*) path=$(cygpath -u -- "$path") || return 1 ;;
        esac
    fi
    case "$path" in
        /*) ;;
        *) path=$PWD/$path ;;
    esac
    while [[ $path == *//* ]]; do
        path=${path//\/\//\/}
    done
    [ "$path" = / ] || path=${path%/}
    case "$path/" in
        */../* | */./*) return 1 ;;
    esac
    printf '%s\n' "$path"
}

# The real location: links and junctions resolved on the nearest
# parent that exists, with what does not exist yet added back.
# Creates nothing.
_nv_canonical() {
    local parent rest=""
    parent=$(_nv_lexical "$1") || return 1
    while [ ! -e "$parent" ] && [ ! -L "$parent" ]; do
        rest=/${parent##*/}$rest
        parent=${parent%/*}
        [ -n "$parent" ] || parent=/
    done
    parent=$(cd -P -- "$parent" 2> /dev/null && pwd -P) || return 1
    if [ "$NV_OS" = windows ]; then
        parent=$(cygpath -u "$(cygpath -l -m "$parent")") || return 1
    fi
    [ "$parent" != / ] || parent=
    printf '%s%s\n' "$parent" "$rest"
}

_nv_fold() {
    if [ "$NV_OS" = windows ]; then
        printf '%s\n' "$1" | tr '[:upper:]' '[:lower:]'
    else
        printf '%s\n' "$1"
    fi
}

# Whether path $1 is $2 or inside it, by case on Windows.
_nv_is_under() {
    local path top
    path=$(_nv_fold "$1")
    top=$(_nv_fold "${2%/}")
    case "$path/" in
        "$top"/*) return 0 ;;
    esac
    return 1
}

# Refuses a path that is, or resolves into, the repository or /data.
# Creates nothing. Sets NV_CHECKED to the real path.
_nv_check_protected() {
    local lexical real repo common top
    NV_CHECKED=
    lexical=$(_nv_lexical "$1") \
        || { _nv_die "evidence path is not usable"; return 1; }
    real=$(_nv_canonical "$1") \
        || { _nv_die "evidence path cannot be resolved"; return 1; }
    repo=$(_nv_repo) || return 1
    repo=$(_nv_canonical "$repo") || return 1
    common=$(git -C "$NV_SKILL_DIR" rev-parse --path-format=absolute \
        --git-common-dir) || return 1
    common=$(_nv_canonical "${common%/.git}") || return 1
    for top in "$repo" "$common"; do
        if _nv_is_under "$real" "$top"; then
            _nv_die "evidence path is inside the repository"
            return 1
        fi
    done
    if _nv_is_under "$lexical" /data || _nv_is_under "$real" /data; then
        _nv_die "evidence path is under /data"
        return 1
    fi
    NV_CHECKED=$real
}

# Decides before anything is created. Sets NV_HOME to the real path.
_nv_guard_home() {
    _nv_check_protected "$NV_HOME" || return 1
    mkdir -p -- "$NV_CHECKED" || return 1
    NV_HOME=$NV_CHECKED
}

# What the build was made from: tracked and new files under src, by
# content, so an uncommitted edit counts.
_nv_fingerprint() {
    local repo list
    repo=$(_nv_repo) || return 1
    list=$(git -C "$repo" ls-files -cmo --exclude-standard -- \
        src Directory.Build.props | sort -u | while IFS= read -r f; do
        [ -f "$repo/$f" ] && printf '%s\n' "$f"
    done)
    paste <(printf '%s\n' "$list") \
        <(printf '%s\n' "$list" | git -C "$repo" hash-object --stdin-paths) \
        | sha256sum | cut -d' ' -f1
}

# --- Evidence ---------------------------------------------------------

_nv_next() {
    NV_STEP=$((NV_STEP + 1))
    printf -v NV_STEP_FILE '%s/%03d-%s.txt' "$NV_EVIDENCE" "$NV_STEP" "$1"
}

_nv_record() {
    printf '%s\t%s\t%s\t%s\n' "$1" "$2" "$3" "$4" \
        >> "$NV_EVIDENCE/results.tsv"
    printf '%-10s %s\n' "$1" "$2"
}

# Takes home folders out of everything in the evidence folder.
_nv_scrub() {
    local file
    find "$NV_EVIDENCE" -type f -print0 | while IFS= read -r -d '' file; do
        sed -i -E \
            -e 's#[A-Za-z]:[\\/]+Users[\\/]+[^\\/ "]+#<home>#g' \
            -e 's#/[a-z]/Users/[^/ "]+#<home>#g' \
            -e 's#/home/[^/ "]+#<home>#g' \
            -e 's#/root/#<home>/#g' \
            "$file"
    done
}

# Fails if anything shaped like an API token is in the evidence.
_nv_scan_evidence() {
    if grep -rqE 'nudge_[A-Za-z0-9_-]{43}' "$NV_EVIDENCE"; then
        printf 'FAIL       evidence contains a token\n' >&2
        return 1
    fi
}

# Whether a value is the mark of a failed read.
_nv_is_failed_read() {
    case "$1" in
        *"<nv_get failed"* | *"<nv_sql failed"* | *NV-CAPTURE-FAILED*)
            return 0 ;;
    esac
    return 1
}

# nv_expect LABEL EXPECTED ACTUAL. An empty expected value, or a failed
# read on either side, is a FAIL even if the two are equal.
nv_expect() {
    if [ -n "$2" ] && [ "$2" = "$3" ] && ! _nv_is_failed_read "$2"; then
        _nv_record PASS "$1" "$2" "$3"
    else
        _nv_record FAIL "$1" "$2" "$3"
        printf '           expected: %s\n           actual:   %s\n' \
            "$2" "$3"
    fi
}

# nv_expect_match LABEL REGEX ACTUAL
nv_expect_match() {
    if ! _nv_is_failed_read "$3" && [[ $3 =~ $2 ]]; then
        _nv_record PASS "$1" "$2" "$3"
    else
        _nv_record FAIL "$1" "$2" "$3"
        printf '           expected: /%s/\n           actual:   %s\n' \
            "$2" "$3"
    fi
}

# nv_capture VAR PATH: keeps a value from the last answer for later
# steps. Empty, null or unreadable is a FAIL, and VAR gets a marker
# that makes every later call or comparison using it fail too.
nv_capture() {
    local _nv_value
    _nv_value=$(nv_get "$2")
    case "$_nv_value" in
        "" | null | "[]" | "{}" | "<nv_get failed"*)
            _nv_record FAIL "capture $1 from $2" "a value" "$_nv_value"
            printf -v "$1" 'NV-CAPTURE-FAILED-%s' "$1"
            return 1
            ;;
    esac
    printf -v "$1" '%s' "$_nv_value"
}

# nv_unverified LABEL REASON: something this run did not exercise.
nv_unverified() {
    _nv_record UNVERIFIED "$1" "-" "$2"
}

# --- Launch -----------------------------------------------------------

# Records why the launch stopped, with any error output, redacted.
# nv_stop then cleans up and reports the failure.
_nv_refuse() {
    printf 'verify: REFUSED: %s\n' "$1" >&2
    _nv_record FAIL "launch: $1" ok refused
    if [ -n "${2-}" ] && [ -f "$2" ]; then
        sed -E 's/nudge_[A-Za-z0-9_-]{43}/<token>/g' "$2" \
            > "$NV_EVIDENCE/launch-error-${2##*/}.txt"
    fi
    return 1
}

# What the app runs with: env -i and this list, nothing inherited. A
# name bash cannot express, such as ConnectionStrings:Nudge, is still
# passed on by the shell, so unsetting names is not enough.
_nv_app_environment() {
    local home=$NV_SCRATCH/home tmp=$NV_SCRATCH/tmp name
    mkdir -p "$home" "$tmp" || return 1
    NV_APP_ENV=(
        "ConnectionStrings__Nudge=$NV_CONNECTION"
        ASPNETCORE_ENVIRONMENT=Production
        "Nudge__TimeZone=$NV_TIME_ZONE"
        DOTNET_NOLOGO=1
    )
    if [ "$NV_OS" = windows ]; then
        local native_home native_tmp windows
        native_home=$(_nv_native "$home")
        native_tmp=$(_nv_native "$tmp")
        windows=$(cygpath -w -W)
        NV_APP_ENV+=(
            "PATH=${NV_DOTNET%/*}:$(cygpath -u -S)"
            "SYSTEMROOT=$windows"
            "WINDIR=$windows"
            "SYSTEMDRIVE=${windows%%\\*}"
            "USERPROFILE=$native_home"
            "APPDATA=$native_home"
            "LOCALAPPDATA=$native_home"
            "TEMP=$native_tmp"
            "TMP=$native_tmp"
        )
    else
        NV_APP_ENV+=(
            "PATH=${NV_DOTNET%/*}:/usr/bin:/bin"
            "HOME=$home"
            "TMPDIR=$tmp"
        )
        # Machine setup the runtime itself may need on a minimal VM.
        # An if, not `[ ] &&`: an unset last name must not make the
        # function return 1 and refuse the launch.
        for name in DOTNET_ROOT DOTNET_SYSTEM_GLOBALIZATION_INVARIANT; do
            if [ -n "${!name:-}" ]; then
                NV_APP_ENV+=("$name=${!name}")
            fi
        done
    fi
}

# What the SDK runs with (publish, the browser test): the same idea,
# with the machine's real profile, since it needs the NuGet cache and
# the installed browser. No application setting is on this list.
_nv_tool_environment() {
    local allowed entry name
    allowed='^(PATH|HOME|USERPROFILE|HOMEDRIVE|HOMEPATH|APPDATA|LOCALAPPDATA'
    allowed+='|PROGRAMDATA|PROGRAMFILES|PROGRAMFILES\(X86\)|PROGRAMW6432'
    allowed+='|COMMONPROGRAMFILES|COMMONPROGRAMFILES\(X86\)|COMMONPROGRAMW6432'
    allowed+='|SYSTEMROOT|WINDIR|SYSTEMDRIVE|COMSPEC|PATHEXT|TEMP|TMP|TMPDIR'
    allowed+='|NUMBER_OF_PROCESSORS|PROCESSOR_ARCHITECTURE|OS|USERNAME|USER'
    allowed+='|LANG|LC_ALL|DOTNET_ROOT|DOTNET_CLI_HOME'
    allowed+='|DOTNET_SYSTEM_GLOBALIZATION_INVARIANT|NUGET_PACKAGES'
    allowed+='|PLAYWRIGHT_BROWSERS_PATH|SSL_CERT_FILE|SSL_CERT_DIR'
    allowed+='|XDG_CACHE_HOME)$'
    NV_TOOL_ENV=(DOTNET_NOLOGO=1)
    while IFS= read -r -d '' entry; do
        name=${entry%%=*}
        name=$(printf '%s' "$name" | tr '[:lower:]' '[:upper:]')
        if [[ $name =~ $allowed ]]; then
            NV_TOOL_ENV+=("$entry")
        fi
    done < <(env -0)
}

# Proves which database the app uses before any token is written: the
# server must have created the scratch file, and the command line,
# reading only, must name it.
_nv_prove_scratch() {
    local db=$NV_SCRATCH/nudge.db err=$NV_SCRATCH/database-check.err said want
    [ -f "$db" ] || {
        _nv_refuse "the server did not create the scratch database"
        return 1
    }
    nv_sql launch-application-id "PRAGMA application_id"
    [ "$NV_ROWS" = 1314210887 ] || {
        _nv_refuse "the scratch file is not a Nudge database"
        return 1
    }
    nv_cli token list > /dev/null 2> "$err" || {
        _nv_refuse "token list failed on the scratch database" "$err"
        return 1
    }
    said=$(tr -d '\r' < "$err" | sed -n 's/^Database: //p' | head -1)
    said=$(_nv_fold "${said//\\//}")
    want=$(_nv_fold "$(_nv_native "$db")")
    [ -n "$said" ] && [ "$said" = "$want" ] || {
        _nv_refuse "the app's command line uses another database" "$err"
        return 1
    }
}

nv_launch() {
    NV_STOPPED=1
    NV_RUN_RC=0
    trap _nv_on_exit EXIT
    if ! _nv_guard_home; then
        NV_RUN_RC=1
        NV_SHELL_FAILED=1
        return 1
    fi

    local repo stamp
    if ! repo=$(_nv_repo); then
        NV_RUN_RC=1
        NV_SHELL_FAILED=1
        _nv_die "not in a git repository"
        return 1
    fi
    stamp=$(date -u +%Y%m%dT%H%M%SZ)-$$
    # The whole run path, since runs/ under a safe home can be a link.
    if ! _nv_check_protected "$NV_HOME/runs/$stamp"; then
        NV_RUN_RC=1
        NV_SHELL_FAILED=1
        return 1
    fi
    NV_RUN=$NV_CHECKED
    NV_SCRATCH=$NV_RUN/scratch
    NV_EVIDENCE=$NV_RUN/evidence
    NV_APP=$NV_RUN/app
    NV_LOG=$NV_SCRATCH/app.log
    NV_STEP=0
    NV_PID=
    NV_WINPID=
    NV_PORT=
    if ! mkdir -p "$NV_SCRATCH" "$NV_EVIDENCE" "$NV_APP"; then
        NV_RUN_RC=1
        NV_SHELL_FAILED=1
        _nv_die "could not create the run folder"
        return 1
    fi
    chmod 700 "$NV_SCRATCH"
    : > "$NV_EVIDENCE/results.tsv"
    NV_DLL_NATIVE=$(_nv_native "$NV_APP")/Nudge.Web.dll
    NV_CONNECTION="Data Source=$(_nv_native "$NV_SCRATCH")/nudge.db"

    # From here on, nv_stop cleans up and reports whatever happens.
    NV_STOPPED=0
    NV_DOTNET=$(command -v dotnet) \
        || { _nv_refuse "dotnet is not installed"; return 1; }
    command -v curl > /dev/null 2>&1 \
        || { _nv_refuse "curl is not installed"; return 1; }
    _nv_find_python \
        || { _nv_refuse "no working Python 3 with sqlite3"; return 1; }
    _nv_guard_connection "$NV_CONNECTION" \
        || { _nv_refuse "connection string failed the guard"; return 1; }
    [ ! -e "$NV_SCRATCH/nudge.db" ] \
        || { _nv_refuse "the scratch database already exists"; return 1; }
    _nv_app_environment \
        || { _nv_refuse "could not prepare the app's environment"; return 1; }
    _nv_tool_environment

    printf 'verify: building into %s\n' "$NV_RUN"
    NV_FINGERPRINT=$(_nv_fingerprint)
    [ -n "$NV_FINGERPRINT" ] \
        || { _nv_refuse "could not fingerprint the source"; return 1; }
    if ! env -i "${NV_TOOL_ENV[@]}" "$NV_DOTNET" publish \
        "$(_nv_native "$repo")/src/Nudge.Web/Nudge.Web.csproj" \
        -c Release -o "$(_nv_native "$NV_APP")" --nologo -v q \
        > "$NV_EVIDENCE/publish.log" 2>&1; then
        _nv_refuse "build failed; see publish.log"
        return 1
    fi
    NV_DLL_SHA=$(sha256sum "$NV_APP/Nudge.Web.dll" | cut -d' ' -f1)
    {
        printf 'commit: %s\n' "$(git -C "$repo" rev-parse HEAD)"
        printf 'source fingerprint: %s\n' "$NV_FINGERPRINT"
        printf 'Nudge.Web.dll sha256: %s\n' "$NV_DLL_SHA"
        printf 'dotnet: %s\n' "$(dotnet --version)"
        printf 'os: %s\n' "$NV_OS"
        printf 'time zone: %s\n' "$NV_TIME_ZONE"
    } > "$NV_EVIDENCE/build.txt"

    # Port 0: the system picks a free port as Kestrel binds, so no
    # other process can take it between choosing and binding. A direct
    # background job of this shell, with only the listed environment.
    local back=$PWD
    cd "$NV_APP" || { _nv_refuse "cannot enter the build folder"; return 1; }
    env -i "${NV_APP_ENV[@]}" ASPNETCORE_URLS=http://127.0.0.1:0 \
        "$NV_DOTNET" "$NV_DLL_NATIVE" > "$NV_LOG" 2>&1 < /dev/null &
    NV_PID=$!
    cd "$back" || { _nv_refuse "cannot return to $back"; return 1; }

    [ "$NV_OS" = windows ] && NV_WINPID=$(cat "/proc/$NV_PID/winpid" 2> /dev/null)
    _nv_save_state

    local i
    for i in $(seq 1 120); do
        NV_PORT=$(tr -d '\r' < "$NV_LOG" | sed -n \
            's#.*Now listening on: http://127\.0\.0\.1:\([0-9]*\).*#\1#p' \
            | head -1)
        [ -n "$NV_PORT" ] && break
        if ! kill -0 "$NV_PID" 2> /dev/null; then
            _nv_refuse "the app exited during startup" "$NV_LOG"
            return 1
        fi
        sleep 0.5
    done
    if [ -z "$NV_PORT" ]; then
        _nv_refuse "the app did not start listening" "$NV_LOG"
        return 1
    fi
    NV_BASE=http://127.0.0.1:$NV_PORT
    _nv_save_state

    local healthy=0 deadline=$((SECONDS + NV_HEALTH_DEADLINE))
    while [ "$SECONDS" -lt "$deadline" ]; do
        if curl -fsS --connect-timeout 2 --max-time 5 -o /dev/null \
            "$NV_BASE/healthz" 2> /dev/null; then
            healthy=1
            break
        fi
        sleep 0.5
    done
    [ "$healthy" = 1 ] \
        || { _nv_refuse "the health endpoint never answered" "$NV_LOG"; return 1; }

    _nv_prove_scratch || return 1

    # The token lives only in this scratch database, which is what
    # lets the doctor prove the server is reading that database.
    local token_err=$NV_SCRATCH/token.err
    NV_TOKEN_NAME=verify-$stamp
    NV_TOKEN=$(nv_cli token create "$NV_TOKEN_NAME" 2> "$token_err" \
        | tr -d '\r')
    if [ -z "$NV_TOKEN" ]; then
        _nv_refuse "could not make a scratch token" "$token_err"
        return 1
    fi
    printf 'verify: running at %s (pid %s)\n' "$NV_BASE" "$NV_PID"
    printf 'verify: evidence in %s\n' "$NV_EVIDENCE"
}

_nv_save_state() {
    {
        printf 'NV_RUN=%q\nNV_SCRATCH=%q\n' "$NV_RUN" "$NV_SCRATCH"
        printf 'NV_EVIDENCE=%q\nNV_APP=%q\n' "$NV_EVIDENCE" "$NV_APP"
        printf 'NV_DLL_NATIVE=%q\n' "$NV_DLL_NATIVE"
        printf 'NV_PID=%q\nNV_WINPID=%q\n' "$NV_PID" "${NV_WINPID:-}"
        printf 'NV_PORT=%q\nNV_LOG=%q\n' "${NV_PORT:-}" "$NV_LOG"
        printf 'NV_STEP=0\n'
    } > "$NV_RUN/state.env"
}

# --- Doctor -----------------------------------------------------------

# Read-only. Any failure means the run proves nothing: stop.
nv_doctor() {
    local before=0 after pids owner want fingerprint sha
    before=$(grep -c '^FAIL' "$NV_EVIDENCE/results.tsv")

    pids=$(_nv_run_pids | tr '\n' ' ')
    pids=${pids% }
    want=${NV_WINPID:-$NV_PID}
    nv_expect "doctor: one process runs this run's Nudge.Web.dll" \
        "$want" "$pids"

    owner=$(_nv_port_owner "$NV_PORT")
    nv_expect "doctor: port $NV_PORT belongs to that process" "$want" "$owner"

    fingerprint=$(_nv_fingerprint)
    nv_expect "doctor: build matches the source now" \
        "$NV_FINGERPRINT" "$fingerprint"
    sha=$(sha256sum "$NV_APP/Nudge.Web.dll" | cut -d' ' -f1)
    nv_expect "doctor: running dll is the one built" "$NV_DLL_SHA" "$sha"

    if _nv_guard_connection "$NV_CONNECTION"; then
        nv_expect "doctor: connection string is the scratch database" \
            ok ok
    else
        nv_expect "doctor: connection string is the scratch database" \
            ok refused
    fi

    nv_sql doctor-application-id "PRAGMA application_id"
    nv_expect "doctor: scratch file is a Nudge database" \
        1314210887 "$NV_ROWS"
    nv_sql doctor-token-row \
        "SELECT count(*) FROM ApiTokens WHERE Name = ?" "$NV_TOKEN_NAME"
    nv_expect "doctor: scratch database holds this run's token" 1 "$NV_ROWS"

    nv_api doctor-health GET /healthz
    nv_expect "doctor: health endpoint answers" 200 "$NV_STATUS"
    NV_ANON=1 nv_api doctor-no-token GET /api/contexts
    nv_expect "doctor: API refuses a request with no token" 401 "$NV_STATUS"
    nv_api doctor-with-token GET /api/contexts
    nv_expect "doctor: server accepts the scratch-only token" \
        200 "$NV_STATUS"

    after=$(grep -c '^FAIL' "$NV_EVIDENCE/results.tsv")
    if [ "$after" != "$before" ]; then
        printf 'verify: DOCTOR FAILED: do not drive; fix or report\n' >&2
        return 1
    fi
    printf 'verify: doctor passed\n'
}

# --- Drive ------------------------------------------------------------

# nv_api LABEL METHOD PATH [JSON]
# Sets NV_STATUS, NV_LOCATION and NV_BODY (a file). The request and
# the answer are saved as evidence, without the Authorization header.
# NV_ANON=1 sends no token. NV_HEADERS=(-H "Name: value") adds headers
# to the next call only.
nv_api() {
    local label=$1 method=$2 path=${3#"$NV_BASE"} body=${4-}
    local head=$NV_SCRATCH/last.headers args rc
    NV_BODY=$NV_SCRATCH/last.body
    # Never left over from the call before: a failed call reads as
    # empty, not as the previous answer.
    : > "$NV_BODY"
    : > "$head"
    NV_LOCATION=
    if _nv_is_failed_read "$path$body"; then
        NV_STATUS=not-sent
        NV_HEADERS=()
        _nv_record FAIL "$label: not sent, an earlier capture failed" \
            sent not-sent
        return 1
    fi
    _nv_next "$label"
    # A deadline, and never --retry: a write must not be sent twice.
    args=(-sS --connect-timeout 5 --max-time "$NV_HTTP_TIMEOUT"
        -X "$method" -o "$(_nv_native "$NV_BODY")"
        -D "$(_nv_native "$head")" -w '%{http_code}')
    args+=("${NV_HEADERS[@]}")
    if [ -n "$body" ]; then
        printf '%s' "$body" > "$NV_SCRATCH/last.request"
        args+=(-H 'Content-Type: application/json'
            --data-binary "@$(_nv_native "$NV_SCRATCH/last.request")")
    fi
    if [ "${NV_ANON:-0}" = 1 ]; then
        NV_STATUS=$(curl "${args[@]}" "$NV_BASE$path")
        rc=$?
    else
        # By config on stdin: the token is never on a command line.
        NV_STATUS=$(printf 'header = "Authorization: Bearer %s"\n' \
            "$NV_TOKEN" | curl -K - "${args[@]}" "$NV_BASE$path"
            exit "${PIPESTATUS[1]}")
        rc=$?
    fi
    NV_LOCATION=$(tr -d '\r' < "$head" \
        | sed -n 's/^[Ll]ocation: //p' | head -1)
    {
        printf '> %s %s\n' "$method" "$path"
        [ "${NV_ANON:-0}" = 1 ] && printf '> (no token)\n'
        [ -n "$body" ] && printf '> %s\n' "$body"
        printf '< %s\n' "$NV_STATUS"
        [ -n "$NV_LOCATION" ] && printf '< Location: %s\n' "$NV_LOCATION"
        [ "$rc" = 0 ] \
            || printf '! curl exit %s: INCOMPLETE, partial answer below\n' "$rc"
        cat "$NV_BODY"
        printf '\n'
    } > "$NV_STEP_FILE"
    NV_HEADERS=()
    if [ "$rc" != 0 ]; then
        # The partial answer is in the evidence. Nothing may read it.
        NV_STATUS=curl-failed-$rc
        NV_LOCATION=
        : > "$NV_BODY"
        : > "$head"
        local why="curl exit $rc"
        [ "$rc" = 28 ] && why="timed out after ${NV_HTTP_TIMEOUT}s"
        _nv_record FAIL "$label: request did not complete" complete "$why"
        return 1
    fi
}

# nv_get a.b.0.c: one value from the last answer. Prints a JSON null
# as null. A missing path or a non-JSON answer prints
# "<nv_get failed: ...>" and returns non-zero.
nv_get() {
    local out rc
    out=$("$NV_PY" -I -c "$_NV_GET" "$(_nv_native "$NV_BODY")" "$1" 2>&1)
    rc=$?
    out=${out//$'\r'/}
    if [ "$rc" != 0 ] && ! _nv_is_failed_read "$out"; then
        out="<nv_get failed: reader error>"
    fi
    printf '%s\n' "$out"
    return "$rc"
}

# nv_has_id ID: yes or no, whether the last answer, a list, holds it.
nv_has_id() {
    local out rc
    if _nv_is_failed_read "$1" || [ -z "$1" ]; then
        printf '<nv_get failed: no id to look for>\n'
        return 3
    fi
    out=$("$NV_PY" -I -c "$_NV_HAS_ID" "$(_nv_native "$NV_BODY")" "$1" 2>&1)
    rc=$?
    out=${out//$'\r'/}
    if [ "$rc" != 0 ] && ! _nv_is_failed_read "$out"; then
        out="<nv_get failed: reader error>"
    fi
    printf '%s\n' "$out"
    return "$rc"
}

# nv_sql LABEL SQL [PARAMETER...]: read-only; sets NV_ROWS. A query
# that fails sets "<nv_sql failed>" and the error goes to evidence.
nv_sql() {
    local label=$1 sql=$2 out rc=0
    shift 2
    _nv_next "$label"
    if _nv_is_failed_read "$*"; then
        out="an earlier capture failed; query not run"
        rc=1
    else
        out=$("$NV_PY" -I -c "$_NV_SQL" \
            "$(_nv_native "$NV_SCRATCH/nudge.db")" "$sql" "$@" 2>&1) || rc=$?
        out=${out//$'\r'/}
    fi
    if [ "$rc" = 0 ]; then
        NV_ROWS=$out
    else
        NV_ROWS="<nv_sql failed>"
    fi
    {
        printf 'SQL: %s\n' "$sql"
        [ "$#" -gt 0 ] && printf 'Parameters: %s\n' "$*"
        printf '%s\n' "$out"
    } > "$NV_STEP_FILE"
    return "$rc"
}

# nv_cli ARGS...: the app's command line, on the scratch database.
nv_cli() {
    _nv_guard_connection "$NV_CONNECTION" || return 1
    local back=$PWD rc
    cd "$NV_APP" || return 1
    env -i "${NV_APP_ENV[@]}" "$NV_DOTNET" "$NV_DLL_NATIVE" "$@" < /dev/null
    rc=$?
    cd "$back" || return 1
    return "$rc"
}

# "ok", or why a dotnet test log does not prove a run: exactly one
# summary, with tests that ran and passed, none failed or skipped.
_nv_smoke_verdict() {
    local log=$1 rc=$2 lines failed passed skipped total
    if [ "$rc" != 0 ]; then
        printf 'dotnet test exited %s\n' "$rc"
        return
    fi
    lines=$(tr -d '\r' < "$log" | grep -E \
        'Failed: +[0-9]+, Passed: +[0-9]+, Skipped: +[0-9]+, Total: +[0-9]+')
    if [ -z "$lines" ] || [ "$(printf '%s\n' "$lines" | wc -l)" != 1 ]; then
        printf 'no single test summary in the log\n'
        return
    fi
    failed=$(sed -E 's/.*Failed: +([0-9]+).*/\1/' <<< "$lines")
    passed=$(sed -E 's/.*Passed: +([0-9]+).*/\1/' <<< "$lines")
    skipped=$(sed -E 's/.*Skipped: +([0-9]+).*/\1/' <<< "$lines")
    total=$(sed -E 's/.*Total: +([0-9]+).*/\1/' <<< "$lines")
    if [ "$failed" = 0 ] && [ "$skipped" = 0 ] && [ "$passed" -gt 0 ] \
        && [ "$total" -gt 0 ] && [ "$passed" = "$total" ]; then
        printf 'ok\n'
    else
        printf 'failed=%s passed=%s skipped=%s total=%s\n' \
            "$failed" "$passed" "$skipped" "$total"
    fi
}

# The signed-in web UI on the TEST HOST, with its fake Google. Recorded
# as TEST-HOST: never as proof of the real sign-in. Anything but a run
# with passing tests and none skipped is a FAIL.
nv_ui_smoke() {
    local repo rc verdict out=$NV_EVIDENCE/ui-smoke-test-host.log
    repo=$(_nv_repo) || return 1
    env -i "${NV_TOOL_ENV[@]}" NUDGE_PLAYWRIGHT=1 "$NV_DOTNET" test \
        "$(_nv_native "$repo")/tests/Nudge.Web.UiTests/Nudge.Web.UiTests.csproj" \
        --nologo > "$out" 2>&1
    rc=$?
    verdict=$(_nv_smoke_verdict "$out" "$rc")
    if [ "$verdict" = ok ]; then
        _nv_record TEST-HOST "signed-in UI smoke (fake Google)" \
            passed passed
    else
        _nv_record FAIL "signed-in UI smoke (fake Google)" passed "$verdict"
    fi
}

# --- Cleanup ----------------------------------------------------------

# Runs when the shell exits, however it exits: stops the app, and a
# run with a recorded FAIL can never leave the shell with status 0.
_nv_on_exit() {
    local rc=$? stopped
    trap - EXIT
    nv_stop
    stopped=$?
    [ "$rc" = 0 ] && rc=$stopped
    [ "$rc" = 0 ] && [ "${NV_SHELL_FAILED:-0}" = 1 ] && rc=1
    exit "$rc"
}

# Whether this shell's job is still running. An exited job that has
# not been reaped yet (a zombie) does not count.
_nv_job_alive() {
    local stat
    kill -0 "$1" 2> /dev/null || return 1
    if [ -r "/proc/$1/stat" ]; then
        stat=$(cat "/proc/$1/stat" 2> /dev/null) || return 1
        stat=${stat##*) }
        [ "${stat%% *}" != Z ] || return 1
    fi
    return 0
}

# Polls for up to $2 quarter-seconds; true once the job has exited.
_nv_job_gone() {
    local i
    for i in $(seq 1 "$2"); do
        _nv_job_alive "$1" || return 0
        sleep 0.25
    done
    ! _nv_job_alive "$1"
}

# Stops the job this shell started, by its own PID: this does not
# depend on the command-line lookup. Never waits on a live process.
_nv_stop_job() {
    local pid=$1 now
    kill "$pid" 2> /dev/null
    if ! _nv_job_gone "$pid" 20; then
        if [ "$NV_OS" = windows ] && [ -n "${NV_WINPID:-}" ]; then
            now=$(cat "/proc/$pid/winpid" 2> /dev/null)
            if [ "$now" = "$NV_WINPID" ]; then
                MSYS_NO_PATHCONV=1 taskkill /PID "$NV_WINPID" /F \
                    > /dev/null 2>&1
            fi
        fi
        kill -KILL "$pid" 2> /dev/null
        _nv_job_gone "$pid" 20 || return 1
    fi
    wait "$pid" 2> /dev/null
    return 0
}

# Returns non-zero if the run has any FAIL, also when called again.
nv_stop() {
    [ "${NV_STOPPED:-1}" = 1 ] && return "${NV_RUN_RC:-0}"
    NV_STOPPED=1

    local i left owner job_left=
    if [ -n "${NV_PID:-}" ]; then
        _nv_stop_job "$NV_PID" || job_left="job-$NV_PID"
    fi

    # Only PIDs whose command line names this run's dll are touched.
    left=$(_nv_run_pids) || left=lookup-failed
    if [ "$left" != lookup-failed ]; then
        for i in $left; do
            if [ "$NV_OS" = windows ]; then
                MSYS_NO_PATHCONV=1 taskkill /PID "$i" /F > /dev/null 2>&1
            else
                kill -KILL "$i" 2> /dev/null
            fi
        done
    fi
    for i in $(seq 1 40); do
        left=$(_nv_run_pids) || left=lookup-failed
        [ -z "$left" ] && break
        sleep 0.25
    done
    left=${left:-$job_left}

    [ -f "${NV_LOG:-}" ] && cp "$NV_LOG" "$NV_EVIDENCE/app.log"
    if [ -n "$left" ]; then
        _nv_record FAIL "cleanup: no process of this run remains" none "$left"
    else
        _nv_record PASS "cleanup: no process of this run remains" none none
    fi
    if [ -n "${NV_PORT:-}" ]; then
        owner=$(_nv_port_owner "$NV_PORT")
        if [ -n "$owner" ]; then
            _nv_record FAIL "cleanup: port $NV_PORT is free" free "$owner"
        else
            _nv_record PASS "cleanup: port $NV_PORT is free" free free
        fi
    fi

    NV_TOKEN=
    if [ -z "$left" ]; then
        for i in $(seq 1 20); do
            rm -rf "$NV_SCRATCH" "$NV_APP" 2> /dev/null
            [ ! -e "$NV_SCRATCH" ] && [ ! -e "$NV_APP" ] && break
            sleep 0.25
        done
        if [ -e "$NV_SCRATCH" ] || [ -e "$NV_APP" ]; then
            # state.env stays, so that nv_recover can finish the job.
            _nv_record FAIL "cleanup: scratch removed" removed "still there"
        else
            _nv_record PASS "cleanup: scratch removed" removed removed
            rm -f "$NV_RUN/state.env"
        fi
    fi

    _nv_scrub
    if ! _nv_scan_evidence; then
        _nv_record FAIL "cleanup: evidence holds no token" none found
    else
        _nv_record PASS "cleanup: evidence holds no token" none none
    fi
    nv_summary
    NV_RUN_RC=$?
    [ "$NV_RUN_RC" = 0 ] || NV_SHELL_FAILED=1
    return "$NV_RUN_RC"
}

nv_summary() {
    local file=$NV_EVIDENCE/results.tsv kind line=""
    for kind in PASS FAIL UNVERIFIED TEST-HOST; do
        line="$line$kind=$(grep -c "^$kind	" "$file") "
    done
    printf '%s\n' "$line" | tee "$NV_EVIDENCE/summary.txt"
    printf 'verify: evidence kept in %s\n' "$NV_EVIDENCE"
    ! grep -q '^FAIL' "$file"
}

# After a shell died mid-run: nv_recover RUN_DIRECTORY
nv_recover() {
    local state=$1/state.env
    [ -f "$state" ] || { _nv_die "no state file in $1"; return 1; }
    # shellcheck disable=SC1090
    . "$state"
    NV_STOPPED=0
    # Not a job of this shell: only the command-line match can stop it.
    NV_PID=
    nv_stop
}
