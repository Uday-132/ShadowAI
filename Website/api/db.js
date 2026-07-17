const { Pool } = require('pg');

const connectionString = process.env.POSTGRES_URL || "postgresql://neondb_owner:npg_Rpbk3sgJwA6U@ep-dawn-truth-audpwajr-pooler.c-10.us-east-1.aws.neon.tech/neondb?sslmode=require&channel_binding=require";

const pool = new Pool({
  connectionString,
  ssl: {
    rejectUnauthorized: false
  }
});

module.exports = {
  query: (text, params) => pool.query(text, params),
  pool
};
