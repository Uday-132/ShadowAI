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
  const { utr } = req.body || {};

  if (!utr || !/^\d{12}$/.test(utr.trim())) {
    return res.status(400).json({ error: 'Invalid UPI Ref No. / UTR. Must be exactly 12 digits.' });
  }

  const cleanUtr = utr.trim();

  try {
    const decoded = jwt.verify(token, JWT_SECRET);

    await db.query('ALTER TABLE users ADD COLUMN IF NOT EXISTS payment_credit BOOLEAN DEFAULT FALSE');

    const utrCheck = await db.query('SELECT id FROM payments WHERE utr = $1', [cleanUtr]);
    if (utrCheck.rows.length > 0) {
      return res.status(400).json({ error: 'This UPI Ref No. / UTR has already been processed.' });
    }

    await db.query(
      'INSERT INTO payments (user_id, utr, amount, status) VALUES ($1, $2, $3, $4)',
      [decoded.id, cleanUtr, 50.00, 'completed']
    );

    await db.query('UPDATE users SET payment_credit = TRUE WHERE id = $1', [decoded.id]);

    return res.status(200).json({
      success: true,
      message: 'Payment verified successfully! You have received 1 session credit. Click "Start Session" to activate.'
    });
  } catch (error) {
    console.error('Payment verification error:', error);
    return res.status(500).json({ error: 'Internal server error: ' + error.message });
  }
};
