const { S3Client, GetObjectCommand, PutObjectCommand, ListObjectsV2Command } = require('@aws-sdk/client-s3');
const { SQSClient, GetQueueAttributesCommand } = require('@aws-sdk/client-sqs');
const s3 = new S3Client();
const sqs = new SQSClient();

// Use environment variable directly
const BUCKET_NAME = process.env.SUMMARY_BUCKET_NAME || '';
const ALARM_PROCESSING_DLQ_URL = process.env.AlarmProcessingDlqUrl || '';
const SUMMARY_EVENT_DLQ_URL = process.env.SummaryEventDlqUrl || '';

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

// Helper to convert UTC timestamp to Eastern Time date folder
function getUtcDateFolders(easternDateFolder) {
    // For a given Eastern date (e.g., "2025-09-09"), we need to check UTC dates
    // that could contain videos for that Eastern day
    
    const [year, month, day] = easternDateFolder.split('-').map(Number);
    
    // Eastern midnight corresponds to UTC 5:00 AM the same day
    // Eastern 11:59 PM corresponds to UTC 4:59 AM the next day
    // So for Eastern date 2025-09-09, we need to check:
    // - UTC 2025-09-09 (from 5:00 AM onwards)
    // - UTC 2025-09-10 (from 12:00 AM to 4:59 AM)
    
    const utcFolders = [];
    
    // Same day UTC folder 
    utcFolders.push(`${year}-${String(month).padStart(2, '0')}-${String(day).padStart(2, '0')}`);
    
    // Next day UTC folder
    const nextDay = new Date(year, month - 1, day + 1);
    const nextYear = nextDay.getFullYear();
    const nextMonth = String(nextDay.getMonth() + 1).padStart(2, '0');
    const nextDayStr = String(nextDay.getDate()).padStart(2, '0');
    utcFolders.push(`${nextYear}-${nextMonth}-${nextDayStr}`);
    
    return utcFolders;
}

// Helper to check if a UTC timestamp falls within an Eastern date
function isUtcTimestampInEasternDate(utcTimestamp, easternDateFolder) {
    const [year, month, day] = easternDateFolder.split('-').map(Number);
    
    // Eastern date boundaries in UTC
    const easternMidnightUtc = new Date(year, month - 1, day, 5, 0, 0); // 5 AM UTC = Midnight ET
    const easternEndOfDayUtc = new Date(year, month - 1, day + 1, 4, 59, 59); // 4:59 AM UTC next day = 11:59 PM ET
    
    const fileDate = new Date(utcTimestamp);
    const isInRange = fileDate >= easternMidnightUtc && fileDate <= easternEndOfDayUtc;
    
    return isInRange;
}

// Helper to find additional missing video events by checking UTC folders for JSON files without videos
async function findAdditionalMissingVideoEvents(easternDateFolder, existingMissingEvents) {
    const additionalMissingEvents = [];
    
    try {
        // Get all UTC folders that could contain files for this Eastern date
        const utcFolders = getUtcDateFolders(easternDateFolder);
        console.log(`[INFO] Checking UTC folders ${utcFolders.join(', ')} for JSON metadata files without videos for Eastern date ${easternDateFolder}`);
        
        // Track all events across all UTC folders
        const allEventFiles = {};
        
        // Check each UTC folder for JSON and video files
        for (const utcFolder of utcFolders) {
            console.log(`[INFO] Scanning UTC folder: ${utcFolder} for missing videos`);
            
            const listParams = {
                Bucket: BUCKET_NAME,
                Prefix: `${utcFolder}/`,
                MaxKeys: 1000
            };
            
            const response = await s3.send(new ListObjectsV2Command(listParams));
            const objects = response.Contents || [];
            
            console.log(`[INFO] Found ${objects.length} objects in UTC folder ${utcFolder}`);
            
            for (const obj of objects) {
                const key = obj.Key;
                const fileName = key.split('/').pop();
                
                // Skip summary files and other non-event files
                if (fileName.startsWith('summary_') || !fileName.includes('_')) {
                    continue;
                }
                
                // Extract timestamp and check if it belongs to our Eastern date
                const timestampMatch = fileName.match(/_(\d{13})\./);
                if (!timestampMatch) continue;
                
                const utcTimestamp = parseInt(timestampMatch[1]);
                
                // Only process files that belong to our target Eastern date
                if (!isUtcTimestampInEasternDate(utcTimestamp, easternDateFolder)) {
                    continue;
                }
                
                // Extract prefix key from filename (eventId_device_timestamp)
                const prefixMatch = fileName.match(/^(.+_\d+)\./);
                if (!prefixMatch) continue;
                
                const prefixKey = prefixMatch[1];
                
                if (!allEventFiles[prefixKey]) {
                    allEventFiles[prefixKey] = { 
                        json: false, 
                        video: false, 
                        jsonFile: null, 
                        videoFile: null,
                        utcFolder: utcFolder
                    };
                }
                
                if (fileName.endsWith('.json')) {
                    allEventFiles[prefixKey].json = true;
                    allEventFiles[prefixKey].jsonFile = obj;
                } else if (fileName.endsWith('.mp4') || fileName.endsWith('.mov')) {
                    allEventFiles[prefixKey].video = true;
                    allEventFiles[prefixKey].videoFile = obj;
                }
            }
        }
        
        console.log(`[INFO] Found ${Object.keys(allEventFiles).length} total events across UTC folders for Eastern date ${easternDateFolder}`);
        
        // Find JSON files that don't have corresponding video files
        for (const [prefixKey, files] of Object.entries(allEventFiles)) {
            if (files.json && !files.video && files.jsonFile) {
                // Check if this event is already in the existing missing events list
                const alreadyListed = existingMissingEvents.some(event => 
                    event.jsonFile === files.jsonFile.Key
                );
                
                if (!alreadyListed) {
                    console.log(`[INFO] Found JSON metadata without video: ${files.jsonFile.Key}`);
                    
                    // Extract event details from the prefix key
                    const parts = prefixKey.split('_');
                    if (parts.length >= 3) {
                        const eventId = parts[0];
                        const device = parts[1];
                        const timestamp = parts[2];
                        
                        additionalMissingEvents.push({
                            eventId: eventId,
                            device: device,
                            timestamp: parseInt(timestamp),
                            prefixKey: prefixKey,
                            jsonFile: files.jsonFile.Key,
                            lastModified: files.jsonFile.LastModified,
                            size: files.jsonFile.Size,
                            utcFolder: files.utcFolder,
                            note: 'JSON metadata exists but video file is missing'
                        });
                    }
                }
            }
        }
        
        console.log(`[INFO] Found ${additionalMissingEvents.length} additional JSON files without videos in Eastern date ${easternDateFolder}`);
        
    } catch (error) {
        console.error(`[ERROR] Failed to find additional missing video events for Eastern date ${easternDateFolder}:`, error);
    }
    
    return additionalMissingEvents;
}

// Helper to find events with JSON metadata but missing video files
async function findMissingVideoFiles(folder) {
    const missingVideoEvents = [];
    
    try {
        // List all objects in the date folder
        const listParams = {
            Bucket: BUCKET_NAME,
            Prefix: `${folder}/`,
            MaxKeys: 1000 // Adjust if needed
        };
        
        const response = await s3.send(new ListObjectsV2Command(listParams));
        const objects = response.Contents || [];
        
        // Group files by event ID
        const eventFiles = {};
        
        for (const obj of objects) {
            const key = obj.Key;
            const fileName = key.split('/').pop(); // Get just the filename
            
            // Skip summary files and other non-event files
            if (fileName.startsWith('summary_') || !fileName.includes('_')) {
                continue;
            }
            
            // Extract event ID from filename (assuming format: type_eventId_timestamp.extension)
            const parts = fileName.split('_');
            if (parts.length >= 3) {
                const eventId = parts[1];
                
                if (!eventFiles[eventId]) {
                    eventFiles[eventId] = { json: false, video: false, metadata: null };
                }
                
                if (fileName.endsWith('.json')) {
                    eventFiles[eventId].json = true;
                    eventFiles[eventId].metadata = obj;
                } else if (fileName.endsWith('.mp4') || fileName.endsWith('.mov')) {
                    eventFiles[eventId].video = true;
                }
            }
        }
        
        // Find events with JSON but no video
        for (const [eventId, files] of Object.entries(eventFiles)) {
            if (files.json && !files.video && files.metadata) {
                missingVideoEvents.push({
                    eventId: eventId,
                    jsonFile: files.metadata.Key,
                    lastModified: files.metadata.LastModified,
                    size: files.metadata.Size
                });
            }
        }
        
        console.log(`[INFO] Found ${missingVideoEvents.length} events with JSON metadata but missing video files in folder ${folder}`);
        
    } catch (error) {
        console.error(`[ERROR] Failed to check for missing video files in folder ${folder}:`, error);
    }
    
    return missingVideoEvents;
}

// Helper to get DLQ message counts
async function getDlqMessageCounts() {
    const dlqCounts = {};
    let totalDlqCount = 0;
    
    // Get Alarm Processing DLQ count
    if (ALARM_PROCESSING_DLQ_URL) {
        try {
            const count = await getDlqCountForQueue(ALARM_PROCESSING_DLQ_URL);
            dlqCounts['AlarmProcessingDLQ'] = count;
            totalDlqCount += count;
            console.log(`[INFO] Alarm Processing DLQ message count: ${count}`);
        } catch (error) {
            console.error(`[ERROR] Failed to get Alarm Processing DLQ count:`, error);
            dlqCounts['AlarmProcessingDLQ'] = 0;
        }
    } else {
        console.log(`[INFO] Alarm Processing DLQ URL not configured`);
        dlqCounts['AlarmProcessingDLQ'] = 0;
    }
    
    // Get Summary Event DLQ count
    if (SUMMARY_EVENT_DLQ_URL) {
        try {
            const count = await getDlqCountForQueue(SUMMARY_EVENT_DLQ_URL);
            dlqCounts['SummaryEventDLQ'] = count;
            totalDlqCount += count;
            console.log(`[INFO] Summary Event DLQ message count: ${count}`);
        } catch (error) {
            console.error(`[ERROR] Failed to get Summary Event DLQ count:`, error);
            dlqCounts['SummaryEventDLQ'] = 0;
        }
    } else {
        console.log(`[INFO] Summary Event DLQ URL not configured`);
        dlqCounts['SummaryEventDLQ'] = 0;
    }
    
    console.log(`[INFO] Total DLQ message count: ${totalDlqCount}`);
    
    return {
        dlqCounts,
        totalDlqCount
    };
}

// Helper to get message count for a specific DLQ
async function getDlqCountForQueue(queueUrl) {
    try {
        const command = new GetQueueAttributesCommand({
            QueueUrl: queueUrl,
            AttributeNames: ['ApproximateNumberOfMessages']
        });
        
        const response = await sqs.send(command);
        const count = parseInt(response.Attributes?.ApproximateNumberOfMessages || '0', 10);
        return count;
    } catch (error) {
        console.error(`[ERROR] Failed to get queue attributes for ${queueUrl}:`, error);
        throw error;
    }
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
        const key = `${folder}/summary_${year}-${month}-${day}.json`;
        
        // Initialize default summary structure
        let summaryData = {
            metadata: {
                date: `${year}-${month}-${day}`,
                dateFormatted: new Date(year, month - 1, day).toISOString().split('T')[0],
                lastUpdated: new Date().toISOString(),
                totalEvents: 0,
                missingVideoCount: 0,
                dlqMessageCount: 0
            },
            eventCounts: {},
            deviceCounts: {},
            hourlyCounts: {},
            events: [],
            missingVideoEvents: [],
            dlqCounts: {}
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
                totalEvents: 0,
                missingVideoCount: 0,
                dlqMessageCount: 0
            };
        }
        if (!summaryData.metadata.missingVideoCount) summaryData.metadata.missingVideoCount = 0;
        if (!summaryData.metadata.dlqMessageCount) summaryData.metadata.dlqMessageCount = 0;
        if (!summaryData.eventCounts) summaryData.eventCounts = {};
        if (!summaryData.deviceCounts) summaryData.deviceCounts = {};
        if (!summaryData.hourlyCounts) summaryData.hourlyCounts = {};
        if (!summaryData.events) summaryData.events = [];
        if (!summaryData.missingVideoEvents) summaryData.missingVideoEvents = [];
        if (!summaryData.dlqCounts) summaryData.dlqCounts = {};

        // Add the new event to the summary
        summaryData.events.push(summaryEvent);
        console.log(`[INFO] Added event to summary:`, {
            EventId: summaryEvent.EventId,
            Device: summaryEvent.DeviceName || summaryEvent.Device,
            EventType: summaryEvent.EventType || summaryEvent.Type,
            AlarmName: summaryEvent.AlarmName,
            EventPath: summaryEvent.EventPath,
            EventLocalLink: summaryEvent.EventLocalLink,
            hasMetadata: !!summaryEvent.Metadata,
            metadataKeys: summaryEvent.Metadata ? Object.keys(summaryEvent.Metadata) : [],
            originalFileName: summaryEvent.Metadata?.originalFileName
        });

        // Extract event details
        const eventType = summaryEvent.EventType || summaryEvent.Type || 'Unknown';
        const deviceName = summaryEvent.DeviceName || summaryEvent.Device || 'Unknown';
        const eventHour = new Date(summaryEvent.Timestamp).getHours();
        const alarmName = summaryEvent.AlarmName || '';

        // Update event type counters
        summaryData.eventCounts[eventType] = (summaryData.eventCounts[eventType] || 0) + 1;

        // Update object and activity counters based on alarm name
        if (alarmName.includes('Object')) {
            summaryData.eventCounts['Object'] = (summaryData.eventCounts['Object'] || 0) + 1;
            console.log(`[INFO] Object detection event detected in alarm: ${alarmName}`);
        }
        if (alarmName.includes('Activity')) {
            summaryData.eventCounts['Activity'] = (summaryData.eventCounts['Activity'] || 0) + 1;
            console.log(`[INFO] Activity detection event detected in alarm: ${alarmName}`);
        }

        // Update device counters
        summaryData.deviceCounts[deviceName] = (summaryData.deviceCounts[deviceName] || 0) + 1;

        // Update hourly counters
        summaryData.hourlyCounts[eventHour] = (summaryData.hourlyCounts[eventHour] || 0) + 1;

        // Check for missing video files in the date folder
        const missingVideoEvents = await findMissingVideoFiles(folder);
        
        // Check for additional missing video events from UTC folders
        const additionalMissingEvents = await findAdditionalMissingVideoEvents(folder, missingVideoEvents);
        
        // Combine both missing video events lists
        summaryData.missingVideoEvents = [...missingVideoEvents, ...additionalMissingEvents];

        // Check DLQ message counts
        const { dlqCounts, totalDlqCount } = await getDlqMessageCounts();
        summaryData.dlqCounts = dlqCounts;

        // Update metadata
        summaryData.metadata.totalEvents = summaryData.events.length;
        summaryData.metadata.missingVideoCount = summaryData.missingVideoEvents.length;
        summaryData.metadata.dlqMessageCount = totalDlqCount;
        summaryData.metadata.lastUpdated = new Date().toISOString();

        console.log(`[INFO] Updated counters:`, {
            eventType,
            eventTypeCount: summaryData.eventCounts[eventType],
            deviceName,
            deviceCount: summaryData.deviceCounts[deviceName],
            alarmName,
            objectCount: summaryData.eventCounts['Object'] || 0,
            activityCount: summaryData.eventCounts['Activity'] || 0,
            totalEvents: summaryData.metadata.totalEvents,
            missingVideoCount: summaryData.metadata.missingVideoCount,
            dlqMessageCount: summaryData.metadata.dlqMessageCount
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