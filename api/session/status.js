const db = require('../db');
const jwt = require('jsonwebtoken');

const JWT_SECRET = process.env.JWT_SECRET || 'shadow_ai_default_secret_123';

module.exports = async (req, res) => {
  res.setHeader('Access-Control-Allow-Origin', '*');
  res.setHeader('Access-Control-Allow-Methods', 'GET, OPTIONS');
  res.setHeader('Access-Control-Allow-Headers', 'Content-Type, Authorization');

  if (req.method === 'OPTIONS') {
    return res.status(200).end();
  }

  const authHeader = req.headers.authorization;
  if (!authHeader || !authHeader.startsWith('Bearer ')) {
    return res.status(401).json({ error: 'Authorization token required' });
  }

  const token = authHeader.split(' ')[1];

  try {
    const decoded = jwt.verify(token, JWT_SECRET);
    await db.query('ALTER TABLE users ADD COLUMN IF NOT EXISTS payment_credit BOOLEAN DEFAULT FALSE');
    await db.query('ALTER TABLE users ADD COLUMN IF NOT EXISTS is_admin BOOLEAN DEFAULT FALSE');
    
    // Ensure the new user_api_keys table is initialized
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

    const userResult = await db.query(
      'SELECT email, trial_ends_at, paid_until, session_started_at, is_session_active, payment_credit, is_admin FROM users WHERE id = $1',
      [decoded.id]
    );

    if (userResult.rows.length === 0) {
      return res.status(404).json({ error: 'User not found' });
    }

    const user = userResult.rows[0];
    const now = new Date();

    const isAdmin = Boolean(user.is_admin || (user.email && (user.email.toLowerCase().includes('admin') || user.email.toLowerCase() === 'udayv@gmail.com')));
    
    const isTrialActive = isAdmin || (user.trial_ends_at && new Date(user.trial_ends_at) > now);
    let isPaidActive = isAdmin || (user.is_session_active && user.paid_until && new Date(user.paid_until) > now);

    const configResult = await db.query("SELECT value FROM app_config WHERE key = 'free_trial_groq_key'");
    const dbGroqKey = configResult.rows.length > 0 ? configResult.rows[0].value : "";

    // Fetch key from the user_api_keys table
    const keysResult = await db.query(
      'SELECT api_key FROM user_api_keys WHERE user_id = $1 ORDER BY id DESC LIMIT 1',
      [decoded.id]
    );
    const dbUserKey = keysResult.rows.length > 0 ? keysResult.rows[0].api_key : "";

    const unlimitedDate = new Date('2099-12-31T23:59:59Z');

    return res.status(200).json({
      email: user.email,
      is_admin: isAdmin,
      isTrialActive,
      isPaidActive,
      trial_ends_at: isAdmin ? unlimitedDate : user.trial_ends_at,
      paid_until: isAdmin ? unlimitedDate : user.paid_until,
      session_started_at: user.session_started_at,
      is_session_active: isAdmin ? true : user.is_session_active,
      payment_credit: isAdmin ? true : user.payment_credit,
      system_groq_key: dbGroqKey,
      user_groq_key: dbUserKey
    });
  } catch (error) {
    console.error('Session status error:', error);
    return res.status(401).json({ error: 'Invalid token: ' + error.message });
  }
};
