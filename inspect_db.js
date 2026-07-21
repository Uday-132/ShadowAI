const { Client } = require('pg');

const connectionString = "postgresql://neondb_owner:npg_Rpbk3sgJwA6U@ep-dawn-truth-audpwajr-pooler.c-10.us-east-1.aws.neon.tech/neondb?sslmode=require&channel_binding=require";

async function main() {
    console.log("Connecting to Neon PostgreSQL...");
    const client = new Client({ connectionString });
    await client.connect();
    
    try {
        console.log("\n--- Checking users where user_groq_key is NOT NULL ---");
        const usersRes = await client.query("SELECT id, email, user_groq_key FROM users WHERE user_groq_key IS NOT NULL");
        console.log(usersRes.rows);

    } catch (err) {
        console.error("Database query failed:", err);
    } finally {
        await client.end();
        console.log("Disconnected.");
    }
}

main().catch(err => console.error("Error in main:", err));
