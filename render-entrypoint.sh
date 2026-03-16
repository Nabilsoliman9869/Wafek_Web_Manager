#!/bin/sh
# إنشاء appsettings.custom.json من المتغيرات البيئية عند الرفع على Render
if [ -n "$DB_SERVER" ]; then
  cat > appsettings.custom.json << EOF
{
  "DbServer": "$(echo "$DB_SERVER" | sed 's/"/\\"/g')",
  "DbName": "$(echo "${DB_NAME:-}" | sed 's/"/\\"/g')",
  "DbUser": "$(echo "${DB_USER:-}" | sed 's/"/\\"/g')",
  "DbPassword": "$(echo "${DB_PASSWORD:-}" | sed 's/"/\\"/g')",
  "SmtpServer": "$(echo "${SMTP_SERVER:-smtp.gmail.com}" | sed 's/"/\\"/g')",
  "SmtpPort": ${SMTP_PORT:-587},
  "SenderEmail": "$(echo "${SENDER_EMAIL:-}" | sed 's/"/\\"/g')",
  "SenderPassword": "$(echo "${SENDER_PASSWORD:-}" | sed 's/"/\\"/g')",
  "EnableSsl": true,
  "ApproveBaseUrl": "$(echo "${APPROVE_BASE_URL:-}" | sed 's/"/\\"/g')",
  "ApproveUrlOverride": null,
  "ImapEnabled": ${IMAP_ENABLED:-true},
  "ImapServer": "$(echo "${IMAP_SERVER:-imap.gmail.com}" | sed 's/"/\\"/g')",
  "ImapPort": ${IMAP_PORT:-993}
}
EOF
  echo "Created appsettings.custom.json from environment variables."
fi
exec dotnet Wafek_Web_Manager.dll
