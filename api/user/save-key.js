const db = require('../db');
const jwt = require('jsonwebtoken');

const JWT_SECRET = process.env.JWT_SECRET || 'shadow_ai_default_secret_123';

module.exports = async (req, res) => {
  res.setHeader('Access-Control-Allow-Origin', '*');
  res.setHeader('Access-Control-Allow-Methods', 'POST, OPTIONS');
  res.setHeader('Access-Control-Allow-Headers', 'Content-Type, Authorization');

  if (req.method === 'OPTIONS') {
    return res.status(200).end();
  }

  if (req.method !== 'POST') {
    return res.status(405).json({ error: 'Method not allowed' });
  }

  const authHeader = req.headers.authorization;
  if (!authHeader || !authHeader.startsWith('Bearer ')) {
    return res.status(401).json({ error: 'Authorization token required' });
  }

  const token = authHeader.split(' ')[1];

  let body = req.body || {};
  if (typeof body === 'string') {
    try { body = JSON.parse(body); } catch (e) {}
  }
  const { groq_key, gemini_key } = body;
  if (!groq_key && !gemini_key) {
    return res.status(400).json({ error: 'groq_key or gemini_key is required.' });
  }

  try {
    const decoded = jwt.verify(token, JWT_SECRET);
    
    await db.query('ALTER TABLE users ADD COLUMN IF NOT EXISTS user_groq_key VARCHAR(255)');
    await db.query('ALTER TABLE users ADD COLUMN IF NOT EXISTS user_gemini_key VARCHAR(255)');

    const trimmedGroq = groq_key ? groq_key.trim() : null;
    const trimmedGemini = gemini_key ? gemini_key.trim() : null;

    await db.query(
      `UPDATE users 
       SET user_groq_key = COALESCE($1, user_groq_key), 
           user_gemini_key = COALESCE($2, user_gemini_key) 
       WHERE id = $3`,
      [trimmedGroq, trimmedGemini, decoded.id]
    );

    if (trimmedGroq) {
      await db.query(`
        CREATE TABLE IF NOT EXISTS user_api_keys (
          id SERIAL PRIMARY KEY,
          user_id INT NOT NULL,
          key_prefix VARCHAR(8) NOT NULL,
          key_hash VARCHAR(64) NOT NULL UNIQUE,
          api_key VARCHAR(255),
          name VARCHAR(255),
          created_at TIMESTAMP DEFAULT CURRENT_TIMESTAMP,
          last_used_at TIMESTAMP
        )
      `);
      await db.query('CREATE INDEX IF NOT EXISTS idx_api_keys_hash ON user_api_keys(key_hash)');

      const crypto = require('crypto');
      const keyHash = crypto.createHash('sha256').update(trimmedGroq).digest('hex');
      const prefix = trimmedGroq.substring(0, Math.min(trimmedGroq.length, 8));

      await db.query('DELETE FROM user_api_keys WHERE user_id = $1', [decoded.id]);
      await db.query(
        `INSERT INTO user_api_keys (user_id, key_prefix, key_hash, api_key, name)
         VALUES ($1, $2, $3, $4, $5)`,
        [decoded.id, prefix, keyHash, trimmedGroq, 'Groq API Key']
      );
    }

    return res.status(200).json({ success: true });
  } catch (error) {
    console.error('Save key error:', error);
    return res.status(500).json({ error: 'Internal server error: ' + error.message });
  }
};
