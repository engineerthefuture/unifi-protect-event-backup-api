
const AWS = require('aws-sdk');
const s3 = new AWS.S3();

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
        let summaryData = { events: [] };
        try {
            const s3Obj = await s3.getObject({ Bucket: BUCKET_NAME, Key: key }).promise();
            summaryData = JSON.parse(s3Obj.Body.toString('utf-8'));
        } catch (err) {
            if (err.code !== 'NoSuchKey') {
                console.error('Error reading summary file:', err);
                continue;
            }
        }
        // Add the new event to the summary
        summaryData.events.push(summaryEvent);
        // Save back to S3
        await s3.putObject({
            Bucket: BUCKET_NAME,
            Key: key,
            Body: JSON.stringify(summaryData, null, 2),
            ContentType: 'application/json'
        }).promise();
        console.log(`Updated summary file: ${key}`);
    }
    return { statusCode: 200 };
};
