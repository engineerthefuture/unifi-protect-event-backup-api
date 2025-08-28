/************************
 * Unifi Webhook Event Receiver UI
 * main.js
 *
 * Centralized dashboard logic for Unifi Protect event backup UI.
 * Handles fetching, rendering, and UI interactions for event data.
 *
 * Author: Brent Foster
 * Created: 08-28-2025
 ***********************/

// Injected at deploy time by workflow:
window.__CONFIG__ = {
    API_URL: '%%API_URL%%', // PATCHED BY WORKFLOW
    API_KEY: '%%API_KEY%%' // PATCHED BY WORKFLOW
};

// Fetch summary data from the API
async function fetchSummary() {
    const { API_URL, API_KEY } = window.__CONFIG__;
    const res = await fetch(`${API_URL}`, {
        headers: {
            'X-API-Key': API_KEY
        }
    });
    if (!res.ok) throw new Error('Failed to fetch summary');
    return res.json();
}

// Generate a badge for the event type
function triggerBadge(key) {
    if (key === "person") return `<span class="badge person">Person</span>`;
    if (key === "vehicle") return `<span class="badge vehicle">Vehicle</span>`;
    if (key === "line_crossed") return `<span class="badge line_crossed">Line Crossed</span>`;
    return `<span class="badge other">${key || "Other"}</span>`;
}

// Render the dashboard with the fetched data
function renderDashboard(data) {
    document.getElementById("summaryTile").innerText = `Total Events (24h): ${data.totalCount}`;
    const container = document.getElementById('dashboard');
    container.innerHTML = '';
    (data.cameras || []).forEach(camera => {
        const camDiv = document.createElement('div');
        camDiv.className = 'camera-card';
        const header = document.createElement('div');
        header.className = 'camera-header';
        header.innerHTML = `
            <span>${camera.cameraName}</span>
            <span>${camera.count24h || 0} events</span>
        `;
        camDiv.appendChild(header);
        (camera.events || []).forEach(event => {
            const evDiv = document.createElement('div');
            evDiv.className = 'event-card';
            const trigger = (event.eventData && event.eventData.triggers && event.eventData.triggers[0]) || {};
            evDiv.innerHTML = `
                <p class="event-title">${event.eventData?.name || ''}</p>
                <p class="event-meta">
                    ${triggerBadge(trigger.key)} 
                    Device: ${trigger.deviceName || 'N/A'}  
                    Date: ${trigger.date ? new Date(trigger.date).toLocaleString() : ''}
                </p>
                <div class="thumbnail">
                    <img src="https://via.placeholder.com/640x360?text=Thumbnail" alt="thumbnail">
                    <div class="play-overlay">▶</div>
                    <video src="${event.videoUrl}" controls></video>
                </div>
                <p class="file-name">${event.originalFileName || ''}</p>
            `;
            // click handler for thumbnail -> video swap
            const thumbDiv = evDiv.querySelector('.thumbnail');
            const videoEl = evDiv.querySelector('video');
            thumbDiv.addEventListener('click', () => {
                thumbDiv.querySelector('img').style.display = 'none';
                thumbDiv.querySelector('.play-overlay').style.display = 'none';
                videoEl.classList.add('show');
                videoEl.play();
            });
            camDiv.appendChild(evDiv);
        });
        container.appendChild(camDiv);
    });
}

// Fetch summary data from the API
fetchSummary()
    .then(renderDashboard)
    .catch(err => {
        document.getElementById("summaryTile").innerText = 'Failed to load summary';
        document.getElementById('dashboard').innerHTML = `<div style="color:#f55">${err.message}</div>`;
    });