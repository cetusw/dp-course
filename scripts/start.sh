#!/usr/bin/env bash
set -euo pipefail

source "$(dirname -- "${BASH_SOURCE[0]}")/stop.sh"

for command in dotnet nginx curl grep; do
    if ! command -v "$command" > /dev/null; then
        echo "Не найдена необходимая команда: $command" >&2
        exit 1
    fi
done

for pid_file in "$runtime/nginx.pid" "$runtime/valuator-5001.pid" "$runtime/valuator-5002.pid" \
    "$runtime/rank-calculator-1.pid" "$runtime/rank-calculator-2.pid" \
    "$runtime/events-logger-1.pid" "$runtime/events-logger-2.pid"; do
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
dotnet build "$root/ds-2024.sln" -c Release
if ! (cd -- "$root/RankCalculator" && dotnet "$rank_application" --check-dependencies); then
    echo "Не удалось подключиться к Redis или RabbitMQ. Проверьте DB_MAIN, DB_RU, DB_EU, DB_ASIA и RabbitMQ." >&2
    exit 1
fi

started_ports=()
started_calculators=()
started_loggers=()
nginx_attempted=false

cleanup() {
    local status=$? port instance
    trap - EXIT
    if ((status != 0)); then
        echo "Запуск не удался. Проверьте логи в $runtime." >&2
        if "$nginx_attempted"; then
            stop_nginx || true
        fi
        for port in "${started_ports[@]}"; do
            stop_valuator "$port" || true
        done
        for instance in "${started_calculators[@]}"; do
            stop_rank_calculator "$instance" || true
        done
        for instance in "${started_loggers[@]}"; do
            stop_events_logger "$instance" || true
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

wait_for_rank_calculator() {
    local instance=$1 pid=$2 attempt
    for ((attempt = 0; attempt < 50; attempt++)); do
        if ! is_rank_calculator "$pid"; then
            echo "RankCalculator $instance завершился. См. $runtime/rank-calculator-$instance.log." >&2
            return 1
        fi
        if grep -q 'Потребитель очереди' "$runtime/rank-calculator-$instance.log"; then
            return 0
        fi
        sleep 0.2
    done
    echo "RankCalculator $instance не готов за отведённое время." >&2
    return 1
}

wait_for_events_logger() {
    local instance=$1 pid=$2 attempt
    for ((attempt = 0; attempt < 50; attempt++)); do
        if ! is_events_logger "$pid"; then
            echo "EventsLogger $instance завершился. См. $runtime/events-logger-$instance.log." >&2
            return 1
        fi
        if grep -q 'Подписчик событий готов' "$runtime/events-logger-$instance.log"; then
            return 0
        fi
        sleep 0.2
    done
    echo "EventsLogger $instance не готов за отведённое время." >&2
    return 1
}

trap cleanup EXIT
trap 'exit 130' INT
trap 'exit 143' TERM
cd -- "$root/EventsLogger"

for instance in 1 2; do
    dotnet "$events_application" > "$runtime/events-logger-$instance.log" 2>&1 &
    pid=$!
    started_loggers+=("$instance")
    printf '%s\n' "$pid" > "$runtime/events-logger-$instance.pid"
    sleep 0.1
    wait_for_events_logger "$instance" "$pid"
done

cd -- "$root/RankCalculator"

for instance in 1 2; do
    dotnet "$rank_application" > "$runtime/rank-calculator-$instance.log" 2>&1 &
    pid=$!
    started_calculators+=("$instance")
    printf '%s\n' "$pid" > "$runtime/rank-calculator-$instance.pid"
    sleep 0.1
    wait_for_rank_calculator "$instance" "$pid"
done

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
