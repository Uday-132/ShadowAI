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

  const authHeader = req.headers.authorization;
  if (!authHeader || !authHeader.startsWith('Bearer ')) {
    return res.status(401).json({ error: 'Authorization token required' });
  }

  const token = authHeader.split(' ')[1];

  try {
    const decoded = jwt.verify(token, JWT_SECRET);
    
    await db.query('ALTER TABLE users ADD COLUMN IF NOT EXISTS payment_credit BOOLEAN DEFAULT FALSE');

    const userResult = await db.query(
      'SELECT id, payment_credit, paid_until, is_session_active FROM users WHERE id = $1',
      [decoded.id]
    );

    if (userResult.rows.length === 0) {
      return res.status(404).json({ error: 'User not found' });
    }

    const user = userResult.rows[0];
    const now = new Date();

    if (user.is_session_active && user.paid_until && new Date(user.paid_until) > now) {
      return res.status(200).json({
        message: 'Session is already active.',
        paid_until: user.paid_until
      });
    }

    if (!user.payment_credit) {
      return res.status(400).json({ error: 'No unused payment credits. Please make a payment of 50 INR first.' });
    }

    const paidUntil = new Date(now.getTime() + 3 * 60 * 60 * 1000);

    await db.query(
      `UPDATE users 
       SET payment_credit = FALSE, 
           is_session_active = TRUE, 
           session_started_at = $1, 
           paid_until = $2 
       WHERE id = $3`,
      [now, paidUntil, user.id]
    );

    return res.status(200).json({
      message: 'Session started successfully! You have 3 hours of access.',
      paid_until: paidUntil,
      session_started_at: now
    });
  } catch (error) {
    console.error('Session start error:', error);
    return res.status(500).json({ error: 'Internal server error: ' + error.message });
  }
};
