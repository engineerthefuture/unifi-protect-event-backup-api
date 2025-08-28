/************************
 * Lambda@Edge Function 
 * index.js
 *
 * Lambda@Edge function for authorizing requests to CloudFront using Cognito JWTs.
 * Source: https://github.com/aws-samples/authorization-lambda-at-edge
 ***********************/

pems = {};
var keys = JSON.parse(JWKS).keys;
for (var i = 0; i < keys.length; i++) {
    //Convert each key to PEM
    var key_id = keys[i].kid;
    var modulus = keys[i].n;
    var exponent = keys[i].e;
    var key_type = keys[i].kty;
    var jwk = { kty: key_type, n: modulus, e: exponent };
    var pem = jwkToPem(jwk);
    pems[key_id] = pem;
}

'use strict';

// Import required libraries for JWT validation
var jwt = require('jsonwebtoken'); // For decoding and verifying JWTs
var jwkToPem = require('jwk-to-pem'); // For converting JWK to PEM format

// These values are replaced at deploy time with your Cognito User Pool ID and JWKS
var USERPOOLID = '##USERPOOLID##'; // Cognito User Pool ID
var JWKS = '##JWKS##'; // Cognito User Pool's JSON Web Key Set (public keys)

// AWS region where the Cognito User Pool resides
var region = 'us-east-1';
// Expected JWT issuer string for this User Pool
var iss = 'https://cognito-idp.' + region + '.amazonaws.com/' + USERPOOLID;

// Convert JWKS to PEMs for signature verification
var pems = {};
var keys = JSON.parse(JWKS).keys;
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

/**
 * Lambda@Edge handler for CloudFront ViewerRequest event.
 * Validates JWT in Authorization header using Cognito public keys.
 * If valid, strips Authorization header and allows request to proceed to S3 origin.
 * If invalid or missing, returns 401 Unauthorized.
 */
exports.handler = (event, context, callback) => {
    const cfrequest = event.Records[0].cf.request;
    const headers = cfrequest.headers;

    // 1. Require Authorization header
    if (!headers.authorization) {
        console.log("No Authorization header present");
        callback(null, response401);
        return false;
    }

    // 2. Extract JWT from Authorization header (strip 'Bearer ')
    var jwtToken = headers.authorization[0].value.slice(7);
    console.log('Extracted JWT token');

    // 3. Decode JWT (without verifying signature yet)
    var decodedJwt = jwt.decode(jwtToken, { complete: true });
    if (!decodedJwt) {
        console.log("Not a valid JWT token");
        callback(null, response401);
        return false;
    }

    // 4. Check issuer matches expected Cognito User Pool
    if (decodedJwt.payload.iss != iss) {
        console.log("Invalid issuer");
        callback(null, response401);
        return false;
    }

    // 5. Only allow 'access' tokens (not id or refresh tokens)
    if (decodedJwt.payload.token_use != 'access') {
        console.log("Not an access token");
        callback(null, response401);
        return false;
    }

    // 6. Get PEM for key ID in JWT header
    var kid = decodedJwt.header.kid;
    var pem = pems[kid];
    if (!pem) {
        console.log('Invalid access token: unknown key ID');
        callback(null, response401);
        return false;
    }

    // 7. Verify JWT signature and claims
    jwt.verify(jwtToken, pem, { issuer: iss }, function(err, payload) {
        if (err) {
            console.log('Token failed verification:', err);
            callback(null, response401);
            return false;
        } else {
            // Valid token: remove Authorization header and allow request
            console.log('Successful verification');
            delete cfrequest.headers.authorization;
            callback(null, cfrequest);
            return true;
        }
    });
};