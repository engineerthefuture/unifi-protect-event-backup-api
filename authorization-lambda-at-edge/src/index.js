// Lambda@Edge Function for authorizing requests to CloudFront using Cognito JWTs.
// Source: https://github.com/aws-samples/authorization-lambda-at-edge
// Updated: 8-27-2025

'use strict';

// Import required libraries for JWT validation
var jwt = require('jsonwebtoken'); // For decoding and verifying JWTs
var jwkToPem = require('jwk-to-pem'); // For converting JWK to PEM format

// These values are replaced at deploy time with your Cognito User Pool ID and JWKS
var USERPOOLID = '##USERPOOLID##'; // Cognito User Pool ID
var JWKS = '##JWKS##'; // Cognito User Pool's JSON Web Key Set (public keys)

// Validate JWKS
if (!JWKS) throw new Error('JWKS is not set');
let keys;
try {
    keys = JSON.parse(JWKS).keys;
} catch (e) {
    throw new Error('Invalid JWKS: ' + JWKS);
}

// AWS region where the Cognito User Pool resides
var region = 'us-east-1';
// Expected JWT issuer string for this User Pool
var iss = 'https://cognito-idp.' + region + '.amazonaws.com/' + USERPOOLID;

// Convert JWKS to PEMs for signature verification
var pems = {};
for (var i = 0; i < keys.length; i++) {
    // Convert each JWK to PEM and store by key ID
    var key_id = keys[i].kid;
    var modulus = keys[i].n;
    var exponent = keys[i].e;
    var key_type = keys[i].kty;
    var jwk = { kty: key_type, n: modulus, e: exponent };
    var pem = jwkToPem(jwk);
    pems[key_id] = pem;
}

// Standard 401 Unauthorized response for CloudFront
const response401 = {
    status: '401',
    statusDescription: 'Unauthorized'
};

// Cognito Hosted UI domain and client ID (replace with your values or set as env variables)
const cognitoDomain = '##COGNITO_DOMAIN##';
const clientId = '##COGNITO_CLIENT_ID##';
const redirectUri = encodeURIComponent('https://' + '##CLOUDFRONT_DOMAIN##' + '/');
const loginUrl = `${cognitoDomain}/login?client_id=${clientId}&response_type=token&scope=openid&redirect_uri=${redirectUri}`;

const responseRedirect = {
    status: '302',
    statusDescription: 'Found',
    headers: {
        location: [{ key: 'Location', value: loginUrl }],
        'cache-control': [{ key: 'Cache-Control', value: 'no-cache, no-store, must-revalidate' }],
        'content-type': [{ key: 'Content-Type', value: 'text/html; charset=utf-8' }]
    },
    body: ''
};

console.log('Lambda@Edge Auth Function starting');
console.log('Cognito login URL:', loginUrl);

/**
 * Lambda@Edge handler for CloudFront ViewerRequest event.
 * Validates JWT in Authorization header using Cognito public keys.
 * If valid, strips Authorization header and allows request to proceed to S3 origin.
 * If invalid or missing, returns 401 Unauthorized.
 */
exports.handler = async(event, context) => {
    console.log('Received event:', JSON.stringify(event));
    const cfrequest = event.Records[0].cf.request;
    const headers = cfrequest.headers;
    console.log('Request headers:', JSON.stringify(headers));

    // 1. Try to get Authorization from header or cookie
    let authHeader = headers.authorization && headers.authorization[0] && headers.authorization[0].value;
    if (!authHeader && headers.cookie) {
        // Parse cookies for Authorization
        const cookies = headers.cookie.map(c => c.value).join(';');
        const match = cookies.match(/Authorization=([^;]+)/);
        if (match) {
            authHeader = decodeURIComponent(match[1]);
            // Inject into headers for downstream logic
            headers.authorization = [{ key: 'Authorization', value: authHeader }];
            console.log('Authorization token found in cookie.');
        }
    }

    // 2. Require Authorization header
    if (!headers.authorization) {
        console.log("No Authorization header present, redirecting to Cognito login");
        return responseRedirect;
    }

    // 3. Extract JWT from Authorization header (strip 'Bearer ')
    var jwtToken = headers.authorization[0].value.slice(7);
    console.log('Extracted JWT token');

    // 4. Decode JWT (without verifying signature yet)
    var decodedJwt = jwt.decode(jwtToken, { complete: true });
    if (!decodedJwt) {
        console.log("Not a valid JWT token");
        return response401;
    }

    // 5. Check issuer matches expected Cognito User Pool
    if (decodedJwt.payload.iss != iss) {
        console.log("Invalid issuer");
        return response401;
    }

    // 6. Only allow 'access' tokens (not id or refresh tokens)
    if (decodedJwt.payload.token_use != 'access') {
        console.log("Not an access token");
        return response401;
    }

    // 7. Get PEM for key ID in JWT header
    var kid = decodedJwt.header.kid;
    var pem = pems[kid];
    if (!pem) {
        console.log('Invalid access token: unknown key ID');
        return response401;
    }

    // 8. Verify JWT signature and claims (promisified)
    try {
        await new Promise((resolve, reject) => {
            jwt.verify(jwtToken, pem, { issuer: iss }, function(err, payload) {
                if (err) reject(err);
                else resolve(payload);
            });
        });
        // Valid token: remove Authorization header and allow request
        console.log('Successful verification');
        delete cfrequest.headers.authorization;
        return cfrequest;
    } catch (err) {
        console.log('Token failed verification:', err);
        return response401;
    }
};