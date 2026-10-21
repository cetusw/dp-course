#!/usr/bin/env bash
set -euo pipefail

source "$(dirname -- "${BASH_SOURCE[0]}")/stop.sh"

for command in dotnet nginx curl; do
    if ! command -v "$command" > /dev/null; then
        echo "Не найдена необходимая команда: $command" >&2
        exit 1
    fi
done

for pid_file in "$runtime/nginx.pid" "$runtime/valuator-5001.pid" "$runtime/valuator-5002.pid"; do
    if [[ -s "$pid_file" ]]; then
        echo "Найден $pid_file. Сначала выполните scripts/stop.sh." >&2
        exit 1
    fi
done

for port in 5001 5002 8080; do
    if (exec 3<>/dev/tcp/127.0.0.1/"$port") 2>/dev/null; then
        echo "Порт $port уже занят." >&2
        exit 1
    fi
done

mkdir -p -- "$runtime"
dotnet build "$root/Valuator/Valuator.csproj" -c Release

started_ports=()
nginx_attempted=false

cleanup() {
    local status=$? port
    trap - EXIT
    if ((status != 0)); then
        echo "Запуск не удался. Проверьте логи в $runtime." >&2
        if "$nginx_attempted"; then
            stop_nginx || true
        fi
        for port in "${started_ports[@]}"; do
            stop_valuator "$port" || true
        done
    fi
    exit "$status"
}

wait_for_http() {
    local port=$1 pid=$2 attempt
    for ((attempt = 0; attempt < 30; attempt++)); do
        if ! is_valuator "$pid" "$port"; then
            echo "Valuator на порту $port завершился. См. $runtime/valuator-$port.log." >&2
            return 1
        fi
        if curl --noproxy '*' --fail --silent --max-time 1 "http://127.0.0.1:$port/" > /dev/null; then
            return 0
        fi
        sleep 0.2
    done
    echo "Valuator на порту $port не готов за отведённое время." >&2
    return 1
}

trap cleanup EXIT
trap 'exit 130' INT
trap 'exit 143' TERM
cd -- "$root/Valuator"

for port in 5001 5002; do
    dotnet "$application" --urls "http://127.0.0.1:$port" > "$runtime/valuator-$port.log" 2>&1 &
    pid=$!
    started_ports+=("$port")
    printf '%s\n' "$pid" > "$runtime/valuator-$port.pid"
    sleep 0.1
    wait_for_http "$port" "$pid"
done

nginx_attempted=true
nginx -p "$runtime/" -c "$config" -t
nginx -p "$runtime/" -c "$config"
curl --noproxy '*' --fail --silent --show-error --max-time 5 http://127.0.0.1:8080/ > /dev/null
trap - EXIT INT TERM
echo "Valuator запущен: http://localhost:8080"
