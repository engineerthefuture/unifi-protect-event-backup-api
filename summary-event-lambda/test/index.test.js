const { handler } = require('../src/index');
const { S3Client, GetObjectCommand, PutObjectCommand } = require('@aws-sdk/client-s3');

jest.mock('@aws-sdk/client-s3');

function mockS3GetObject(data) {
    S3Client.prototype.send = jest.fn(async(cmd) => {
        if (cmd instanceof GetObjectCommand) {
            return { Body: toStream(JSON.stringify(data)) };
        }
        if (cmd instanceof PutObjectCommand) {
            return {};
        }
        throw new Error('Unknown command');
    });
}

function toStream(str) {
    const { Readable } = require('stream');
    const s = new Readable();
    s.push(str);
    s.push(null);
    return s;
}

describe('summary-event-lambda', () => {
    beforeEach(() => {
        jest.clearAllMocks();
        process.env.SUMMARY_BUCKET_NAME = 'test-bucket';
    });

    it('should create a new summary file if none exists', async() => {
        S3Client.prototype.send = jest.fn(async(cmd) => {
            if (cmd instanceof GetObjectCommand) {
                const err = new Error('NoSuchKey');
                err.name = 'NoSuchKey';
                throw err;
            }
            if (cmd instanceof PutObjectCommand) {
                return {};
            }
        });
        const event = {
            Records: [{
                body: JSON.stringify({
                    EventId: 'evt1',
                    Timestamp: Date.now(),
                    DeviceName: 'DeviceA',
                    EventType: 'motion'
                })
            }]
        };
        const res = await handler(event);
        expect(res.statusCode).toBe(200);
        expect(S3Client.prototype.send).toHaveBeenCalledWith(expect.any(PutObjectCommand));
    });

    it('should update counters for event type and device', async() => {
        const summaryData = {
            events: [],
            counters: { type: { motion: 1 }, device: { DeviceA: 1 } }
        };
        mockS3GetObject(summaryData);
        const event = {
            Records: [{
                body: JSON.stringify({
                    EventId: 'evt2',
                    Timestamp: Date.now(),
                    DeviceName: 'DeviceA',
                    EventType: 'motion'
                })
            }]
        };
        const res = await handler(event);
        expect(res.statusCode).toBe(200);
        expect(S3Client.prototype.send).toHaveBeenCalledWith(expect.any(PutObjectCommand));
    });

    it('should handle invalid event JSON gracefully', async() => {
        const event = {
            Records: [
                { body: '{invalid json}' }
            ]
        };
        const res = await handler(event);
        expect(res.statusCode).toBe(200);
    });
});