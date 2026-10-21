#!/usr/bin/env bash
set -euo pipefail

root=$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")/.." && pwd)
runtime="$root/.runtime"
config="$root/nginx/conf/nginx.conf"
application="$root/Valuator/bin/Release/net8.0/Valuator.dll"
rank_application="$root/RankCalculator/bin/Release/net8.0/RankCalculator.dll"

is_valuator() {
    local pid=$1 port=$2
    local -a arguments=()
    [[ "$pid" =~ ^[1-9][0-9]*$ && -r "/proc/$pid/cmdline" ]] || return 1
    mapfile -d '' -t arguments < "/proc/$pid/cmdline"
    [[ ${#arguments[@]} -eq 4 && "${arguments[1]}" == "$application" &&
        "${arguments[2]}" == --urls && "${arguments[3]}" == "http://127.0.0.1:$port" ]]
}

is_rank_calculator() {
    local pid=$1
    local -a arguments=()
    [[ "$pid" =~ ^[1-9][0-9]*$ && -r "/proc/$pid/cmdline" ]] || return 1
    mapfile -d '' -t arguments < "/proc/$pid/cmdline"
    [[ ${#arguments[@]} -eq 2 && "${arguments[1]}" == "$rank_application" ]]
}

is_nginx() {
    local pid=$1 title
    [[ "$pid" =~ ^[1-9][0-9]*$ && -r "/proc/$pid/cmdline" ]] || return 1
    IFS= read -r -d '' title < "/proc/$pid/cmdline" || return 1
    [[ "$title" == "nginx: master process nginx -p $runtime/ -c $config" ]]
}

wait_for_exit() {
    local kind=$1 pid=$2 port=${3:-} attempt
    for ((attempt = 0; attempt < 150; attempt++)); do
        if ! "is_$kind" "$pid" "$port"; then
            return 0
        fi
        sleep 0.2
    done
    echo "Процесс $pid не завершился за 30 секунд; PID-файл сохранён." >&2
    return 1
}

stop_valuator() {
    local port=$1 pid
    local pid_file="$runtime/valuator-$port.pid"
    [[ -f "$pid_file" ]] || return 0
    pid=$(cat -- "$pid_file")
    if is_valuator "$pid" "$port"; then
        kill -TERM "$pid" || return 1
        wait_for_exit valuator "$pid" "$port" || return 1
    fi
    rm -f -- "$pid_file"
}

stop_rank_calculator() {
    local instance=$1 pid
    local pid_file="$runtime/rank-calculator-$instance.pid"
    [[ -f "$pid_file" ]] || return 0
    pid=$(cat -- "$pid_file")
    if is_rank_calculator "$pid"; then
        kill -TERM "$pid" || return 1
        wait_for_exit rank_calculator "$pid" || return 1
    fi
    rm -f -- "$pid_file"
}

stop_nginx() {
    local pid
    [[ -f "$runtime/nginx.pid" ]] || return 0
    pid=$(cat -- "$runtime/nginx.pid")
    if is_nginx "$pid"; then
        nginx -p "$runtime/" -c "$config" -s quit || return 1
        wait_for_exit nginx "$pid" || return 1
    fi
    rm -f -- "$runtime/nginx.pid"
}

stop_all() {
    local status=0
    stop_nginx || status=1
    stop_valuator 5001 || status=1
    stop_valuator 5002 || status=1
    stop_rank_calculator 1 || status=1
    stop_rank_calculator 2 || status=1
    return "$status"
}

if [[ "${BASH_SOURCE[0]}" == "$0" ]]; then
    stop_all
fi
