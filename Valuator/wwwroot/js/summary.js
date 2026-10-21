function displayRank(rankElement, statusElement, value) {
    if (typeof value !== "number" || !Number.isFinite(value)) {
        return;
    }
    rankElement.textContent = "Оценка содержания: " + value;
    rankElement.dataset.complete = "true";
    statusElement.textContent = "";
}

function displayConnectionError(rankElement, statusElement) {
    if (rankElement.dataset.complete !== "true") {
        statusElement.textContent = "Не удалось получить обновление. Обновите страницу вручную.";
    }
}

async function watchText(connection, rankElement, statusElement) {
    try {
        const rank = await connection.invoke("WatchText", rankElement.dataset.textId);
        displayRank(rankElement, statusElement, rank);
    } catch {
        displayConnectionError(rankElement, statusElement);
    }
}

async function startWatchingRank() {
    const rankElement = document.getElementById("rank");
    if (!rankElement) {
        return;
    }
    const statusElement = document.getElementById("rank-status");
    const connection = new signalR.HubConnectionBuilder()
        .withUrl("/hubs/rank", {
            transport: signalR.HttpTransportType.WebSockets,
            skipNegotiation: true
        })
        .withAutomaticReconnect()
        .build();
    connection.on("RankCalculated", value => displayRank(rankElement, statusElement, value));
    connection.onreconnected(() => watchText(connection, rankElement, statusElement));
    connection.onclose(() => displayConnectionError(rankElement, statusElement));
    try {
        await connection.start();
        await watchText(connection, rankElement, statusElement);
    } catch {
        displayConnectionError(rankElement, statusElement);
    }
}

startWatchingRank();
