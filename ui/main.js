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

/************************
 * Cognito token check: redirect to login if missing or expired
 */
(function() {
    function getCookie(name) {
        const value = `; ${document.cookie}`;
        const parts = value.split(`; ${name}=`);
        if (parts.length === 2) return parts.pop().split(';').shift();
    }

    function isJwtExpired(token) {
        try {
            const payload = JSON.parse(atob(token.split('.')[1]));
            if (!payload.exp) return true;
            // exp is in seconds since epoch
            return (Date.now() / 1000) > payload.exp;
        } catch (e) {
            return true;
        }
    }
    var token = getCookie('CognitoAccessToken');
    var loginUrl = window.__CONFIG__.LOGIN_URL;
    if (!token || isJwtExpired(token)) {
        window.location.replace(loginUrl);
    }
})();

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
    // Show the time the API request was performed
    const now = new Date();
    const timeStr = now.toLocaleString(undefined, { hour12: false });
    document.getElementById("summaryTile").innerText = `Total Events (last 24h as of ${timeStr}): ${data.totalCount}`;
    // Show summary message if present
    const summaryMsgDiv = document.getElementById('summaryMessage');
    if (data.summaryMessage) {
        summaryMsgDiv.textContent = data.summaryMessage;
        summaryMsgDiv.style.display = '';
    } else {
        summaryMsgDiv.textContent = '';
        summaryMsgDiv.style.display = 'none';
    }
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
                <div class="event-meta">
                    <span class="event-trigger">${triggerBadge(trigger.key)}</span>
                    <span class="event-device">Device: ${trigger.deviceName || 'N/A'}</span>
                </div>
                <div class="event-date">
                    ${trigger.date ? new Date(trigger.date + (trigger.date.match(/Z|[+-]\d{2}:?\d{2}$/) ? '' : 'Z')).toLocaleString('en-US', { timeZone: 'America/New_York', timeZoneName: 'short' }) : ''}
                </div>
                <div class="thumbnail">
                    <img src="" alt="thumbnail" loading="lazy" style="background:#222;min-width:100px;min-height:56px;">
                    <div class="play-overlay">▶</div>
                </div>
                <p class="event-name">${event.eventData?.name || ''}</p>
                <p class="file-name">${event.originalFileName || ''}</p>
            `;
            // Generate thumbnail from first frame
            const thumbDiv = evDiv.querySelector('.thumbnail');
            const imgEl = thumbDiv.querySelector('img');
            (async() => {
                let video;
                try {
                    video = document.createElement('video');
                    video.muted = true;
                    video.playsInline = true;
                    video.crossOrigin = 'anonymous';
                    video.preload = 'auto';
                    video.style.display = 'none';
                    document.body.appendChild(video);
                    video.src = event.videoUrl;
                    video.load();
                    await new Promise((resolve, reject) => {
                        let resolved = false;
                        const done = () => {
                            if (!resolved) {
                                resolved = true;
                                resolve();
                            }
                        };
                        video.addEventListener('loadeddata', done, { once: true });
                        video.addEventListener('canplay', done, { once: true });
                        video.addEventListener('error', reject, { once: true });
                        setTimeout(done, 2500);
                    });
                    video.currentTime = 0;
                    await new Promise((resolve) => setTimeout(resolve, 100));
                    const canvas = document.createElement('canvas');
                    canvas.width = video.videoWidth;
                    canvas.height = video.videoHeight;
                    const ctx = canvas.getContext('2d');
                    ctx.drawImage(video, 0, 0, canvas.width, canvas.height);
                    imgEl.src = canvas.toDataURL('image/jpeg', 0.7);
                } catch (e) {
                    imgEl.src = 'data:image/svg+xml,%3Csvg width="640" height="360" xmlns="http://www.w3.org/2000/svg"%3E%3Crect width="100%25" height="100%25" fill="%23222"/%3E%3Ctext x="50%25" y="50%25" fill="%23fff" font-size="24" text-anchor="middle" alignment-baseline="middle" dy=".3em"%3ENo Thumbnail%3C/text%3E%3C/svg%3E';
                } finally {
                    if (video && video.parentNode) video.parentNode.removeChild(video);
                }
            })();

            // Lazy load video only on click
            thumbDiv.addEventListener('click', () => {
                imgEl.style.display = 'none';
                thumbDiv.querySelector('.play-overlay').style.display = 'none';
                if (!thumbDiv.querySelector('video')) {
                    const videoEl = document.createElement('video');
                    videoEl.src = event.videoUrl;
                    videoEl.controls = true;
                    videoEl.classList.add('show');
                    thumbDiv.appendChild(videoEl);
                    videoEl.play();
                } else {
                    const videoEl = thumbDiv.querySelector('video');
                    videoEl.classList.add('show');
                    videoEl.play();
                }
            });
            camDiv.appendChild(evDiv);
        });
        container.appendChild(camDiv);
    });
}

// Fetch summary data from the API
document.getElementById("summaryTile").innerText = 'Loading summary...';
document.getElementById('dashboard').innerHTML = `
    <div class="loading-indicator">
        Loading events...
        <div id="progressBarContainer" style="width:100%;background:#eee;border-radius:8px;margin-top:8px;">
            <div id="progressBar" style="width:0%;height:16px;background:#4a90e2;border-radius:8px;transition:width 0.2s;"></div>
        </div>
        <div id="progressPercent" style="font-size:12px;margin-top:4px;">0%</div>
    </div>
`;

// Update progress bar
let progress = 0;
let progressInterval = setInterval(() => {
    if (progress < 90) {
        progress += Math.random() * 5 + 2; // Increment by 2-7%
        if (progress > 90) progress = 90;
        document.getElementById('progressBar').style.width = progress + '%';
        document.getElementById('progressPercent').innerText = Math.floor(progress) + '%';
    }
}, 200);
fetchSummary()
    .then(data => {
        clearInterval(progressInterval);
        document.getElementById('progressBar').style.width = '100%';
        document.getElementById('progressPercent').innerText = '100%';
        setTimeout(() => renderDashboard(data), 300);
    })
    .catch(err => {
        clearInterval(progressInterval);
        document.getElementById("summaryTile").innerText = 'Failed to load summary';
        document.getElementById('progressBar').style.width = '100%';
        document.getElementById('progressPercent').innerText = 'Error';
        document.getElementById('dashboard').innerHTML = `<div style="color:#f55">${err.message}</div>`;
    });