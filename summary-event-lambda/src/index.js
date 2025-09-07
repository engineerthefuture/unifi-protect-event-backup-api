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
        } catch (err) {
            console.error('Invalid event JSON:', err);
            continue;
        }
        const { year, month, day, folder } = getEasternDateString(summaryEvent.Timestamp);
        const key = `${folder}/summary_${year}_${month}_${day}.json`;
        let summaryData = { events: [], counters: {} };
        try {
            const s3Obj = await s3.send(new GetObjectCommand({ Bucket: BUCKET_NAME, Key: key }));
            const bodyContents = await streamToString(s3Obj.Body);
            summaryData = JSON.parse(bodyContents);
        } catch (err) {
            if (err.name !== 'NoSuchKey' && err.Code !== 'NoSuchKey') {
                console.error('Error reading summary file:', err);
                continue;
            }
        }
        // Add the new event to the summary
        summaryData.events.push(summaryEvent);

        // Increment counters for event attributes
        // Example: count by event type and device name
        const type = summaryEvent.Type || 'Unknown';
        const device = summaryEvent.DeviceName || 'Unknown';
        if (!summaryData.counters.type) summaryData.counters.type = {};
        if (!summaryData.counters.device) summaryData.counters.device = {};
        summaryData.counters.type[type] = (summaryData.counters.type[type] || 0) + 1;
        summaryData.counters.device[device] = (summaryData.counters.device[device] || 0) + 1;

        // Save back to S3
        await s3.send(new PutObjectCommand({
            Bucket: BUCKET_NAME,
            Key: key,
            Body: JSON.stringify(summaryData, null, 2),
            ContentType: 'application/json'
        }));
        // Helper to convert stream to string (for AWS SDK v3 GetObjectCommand)
        function streamToString(stream) {
            return new Promise((resolve, reject) => {
                const chunks = [];
                stream.on('data', (chunk) => chunks.push(chunk));
                stream.on('error', reject);
                stream.on('end', () => resolve(Buffer.concat(chunks).toString('utf-8')));
            });
        }
        console.log(`Updated summary file: ${key}`);
    }
    return { statusCode: 200 };
};