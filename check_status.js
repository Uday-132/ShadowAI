const https = require('https');

function loginAndCheckStatus() {
    // 1. First, login to get a token
    const payload = JSON.stringify({
        email: 'uday@gmail.com',
        password: 'password' // wait, what is the password? In our db rows: "uday@gmail.com" password hash exists.
        // Let's create a fresh user or use a dummy request
    });

    // Actually, we don't even need to login if we can check the OPTIONS headers, or just make a request to status.js with a dummy token!
    // If the token is invalid (dummy), the try-catch block inside api/session/status.js catches it:
    // } catch (error) {
    //   console.error('Session status error:', error);
    //   return res.status(401).json({ error: 'Invalid token: ' + error.message });
    // }
    // Wait! In the new code, the error catch returns res.status(401).json({ error: 'Invalid token: ' + error.message });
    // In the old code, does it do the same?
    // Let's check!
}

// Alternatively, let's login with one of the test accounts.
// Let's register a new user 'test_temp@gmail.com' and check status!
function postRequest(path, data) {
    return new Promise((resolve, reject) => {
        const options = {
            hostname: 'shadow-ai-iota.vercel.app',
            path: path,
            method: 'POST',
            headers: {
                'Content-Type': 'application/json',
                'Content-Length': data.length
            }
        };

        const req = https.request(options, (res) => {
            let body = '';
            res.on('data', chunk => body += chunk);
            res.on('end', () => resolve({ statusCode: res.statusCode, body: JSON.parse(body) }));
        });

        req.on('error', reject);
        req.write(data);
        req.end();
    });
}

function getRequest(path, token) {
    return new Promise((resolve, reject) => {
        const options = {
            hostname: 'shadow-ai-iota.vercel.app',
            path: path,
            method: 'GET',
            headers: {
                'Authorization': `Bearer ${token}`
            }
        };

        const req = https.request(options, (res) => {
            let body = '';
            res.on('data', chunk => body += chunk);
            res.on('end', () => resolve({ statusCode: res.statusCode, body: JSON.parse(body) }));
        });

        req.on('error', reject);
        req.end();
    });
}

async function main() {
    try {
        console.log("Registering temporary user to test APIs...");
        const signupRes = await postRequest('/api/auth/signup', JSON.stringify({
            email: 'test_verifier_' + Date.now() + '@gmail.com',
            password: 'password123'
        }));

        console.log("Signup Response:", signupRes);
        if (signupRes.statusCode === 201 && signupRes.body.token) {
            const token = signupRes.body.token;
            console.log("\nCalling session status with new token...");
            const statusRes = await getRequest('/api/session/status', token);
            console.log("Status Response:", statusRes);
        }
    } catch (e) {
        console.error("Test failed:", e);
    }
}

main();
