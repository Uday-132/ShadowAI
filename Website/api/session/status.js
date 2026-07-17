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

    const userResult = await db.query(
      'SELECT email, trial_ends_at, paid_until, session_started_at, is_session_active, payment_credit FROM users WHERE id = $1',
      [decoded.id]
    );

    if (userResult.rows.length === 0) {
      return res.status(404).json({ error: 'User not found' });
    }

    const user = userResult.rows[0];
    const now = new Date();
    
    const isTrialActive = user.trial_ends_at && new Date(user.trial_ends_at) > now;
    
    let isPaidActive = false;
    if (user.is_session_active && user.paid_until && new Date(user.paid_until) > now) {
      isPaidActive = true;
    }

    const configResult = await db.query("SELECT value FROM app_config WHERE key = 'free_trial_groq_key'");
    const dbGroqKey = configResult.rows.length > 0 ? configResult.rows[0].value : "";

    return res.status(200).json({
      email: user.email,
      isTrialActive,
      isPaidActive,
      trial_ends_at: user.trial_ends_at,
      paid_until: user.paid_until,
      session_started_at: user.session_started_at,
      is_session_active: user.is_session_active,
      payment_credit: user.payment_credit,
      system_groq_key: dbGroqKey
    });
  } catch (error) {
    console.error('Session status error:', error);
    return res.status(401).json({ error: 'Invalid token: ' + error.message });
  }
};
