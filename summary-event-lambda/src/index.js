const { S3Client, GetObjectCommand, PutObjectCommand } = require('@aws-sdk/client-s3');
const s3 = new S3Client();

// Use environment variable directly
const BUCKET_NAME = process.env.SUMMARY_BUCKET_NAME || '';

// Helper to get ET midnight for a given timestamp
function getEasternDateString(timestamp) {
    const date = new Date(timestamp);
    // Convert to UTC-5 (ET, ignoring DST for simplicity)
    const utc = date.getTime() + (date.getTimezoneOffset() * 60000);
    // ET offset: -5 hours in ms
    const et = new Date(utc - (5 * 60 * 60 * 1000));
    const year = et.getFullYear();
    const month = String(et.getMonth() + 1).padStart(2, '0');
    const day = String(et.getDate()).padStart(2, '0');
    return { year, month, day, folder: `${year}-${month}-${day}` };
}

// Lambda handler
exports.handler = async(event) => {
    for (const record of event.Records) {
        let summaryEvent;
        try {
            summaryEvent = JSON.parse(record.body);
            console.log(`[INFO] Processing event:`, {
                EventId: summaryEvent.EventId,
                Timestamp: summaryEvent.Timestamp,
                Device: summaryEvent.DeviceName || summaryEvent.Device,
                EventType: summaryEvent.EventType || summaryEvent.Type
            });
        } catch (err) {
            console.error('[ERROR] Invalid event JSON:', err, { recordBody: record.body });
            continue;
        }
        const { year, month, day, folder } = getEasternDateString(summaryEvent.Timestamp);
        const key = `${folder}/summary_${year}_${month}_${day}.json`;
        
        // Initialize default summary structure
        let summaryData = {
            metadata: {
                date: `${year}-${month}-${day}`,
                dateFormatted: new Date(year, month - 1, day).toISOString().split('T')[0],
                lastUpdated: new Date().toISOString(),
                totalEvents: 0
            },
            eventCounts: {},
            deviceCounts: {},
            hourlyCounts: {},
            events: []
        };
        
        let fileExisted = true;
        try {
            const s3Obj = await s3.send(new GetObjectCommand({ Bucket: BUCKET_NAME, Key: key }));
            const bodyContents = await streamToString(s3Obj.Body);
            summaryData = JSON.parse(bodyContents);
            console.log(`[INFO] Loaded existing summary file: ${key}`);
        } catch (err) {
            if (err.name === 'NoSuchKey' || err.Code === 'NoSuchKey') {
                fileExisted = false;
                console.log(`[INFO] No existing summary file found for ${key}, will create new.`);
            } else {
                console.error('[ERROR] Error reading summary file:', err, { key });
                continue;
            }
        }
        // Initialize summary structure if needed
        if (!summaryData.metadata) {
            summaryData.metadata = {
                date: `${year}-${month}-${day}`,
                dateFormatted: new Date(year, month - 1, day).toISOString().split('T')[0],
                lastUpdated: new Date().toISOString(),
                totalEvents: 0
            };
        }
        if (!summaryData.eventCounts) summaryData.eventCounts = {};
        if (!summaryData.deviceCounts) summaryData.deviceCounts = {};
        if (!summaryData.hourlyCounts) summaryData.hourlyCounts = {};
        if (!summaryData.events) summaryData.events = [];

        // Add the new event to the summary
        summaryData.events.push(summaryEvent);
        console.log(`[INFO] Added event to summary:`, {
            EventId: summaryEvent.EventId,
            Device: summaryEvent.DeviceName || summaryEvent.Device,
            EventType: summaryEvent.EventType || summaryEvent.Type
        });

        // Extract event details
        const eventType = summaryEvent.EventType || summaryEvent.Type || 'Unknown';
        const deviceName = summaryEvent.DeviceName || summaryEvent.Device || 'Unknown';
        const eventHour = new Date(summaryEvent.Timestamp).getHours();

        // Update event type counters
        summaryData.eventCounts[eventType] = (summaryData.eventCounts[eventType] || 0) + 1;

        // Update device counters
        summaryData.deviceCounts[deviceName] = (summaryData.deviceCounts[deviceName] || 0) + 1;

        // Update hourly counters
        summaryData.hourlyCounts[eventHour] = (summaryData.hourlyCounts[eventHour] || 0) + 1;

        // Update metadata
        summaryData.metadata.totalEvents = summaryData.events.length;
        summaryData.metadata.lastUpdated = new Date().toISOString();

        console.log(`[INFO] Updated counters:`, {
            eventType,
            eventTypeCount: summaryData.eventCounts[eventType],
            deviceName,
            deviceCount: summaryData.deviceCounts[deviceName],
            totalEvents: summaryData.metadata.totalEvents
        });

        // Save back to S3
        try {
            await s3.send(new PutObjectCommand({
                Bucket: BUCKET_NAME,
                Key: key,
                Body: JSON.stringify(summaryData, null, 2),
                ContentType: 'application/json'
            }));
            console.log(`[SUCCESS] Updated summary file: ${key}`);
        } catch (err) {
            console.error('[ERROR] Failed to write summary file to S3:', err, { key });
        }

        // Helper to convert stream to string (for AWS SDK v3 GetObjectCommand)
        function streamToString(stream) {
            return new Promise((resolve, reject) => {
                const chunks = [];
                stream.on('data', (chunk) => chunks.push(chunk));
                stream.on('error', reject);
                stream.on('end', () => resolve(Buffer.concat(chunks).toString('utf-8')));
            });
        }
    }
    return { statusCode: 200 };
};