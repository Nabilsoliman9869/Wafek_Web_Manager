# Wafek Web Manager - Project Context & Development Log

## 📅 Session Summary: 2026-03-13 (Workflow & Email Engine)

### 1. Core Objectives
- **Modernize Workflow:** Replace the legacy desktop workflow trigger with a modern Web/SQL-based engine.
- **Actionable Emails:** Enable "Reply-to-Approve" functionality (`*#Approve`, `*#Reject`).
- **Integration:** Ensure seamless integration with existing Wafek ERP tables (`TBL010`, `TBL013`, `TBL016`).

### 2. Key Architectural Decisions
- **Source of Truth:** 
  - `TBL010` (Bonds Transaction) is the trigger source.
  - `TBL009` (Bond Types) is reference only.
- **Email Delivery:**
  - **Decision:** Use C# `SmtpClient` with `MailKit` (or `System.Net.Mail`) directly from the Web Worker.
  - **Why:** `Database Mail` failed due to lack of `sysadmin` permissions for the connecting user (`LA7`).
  - **Settings:** `smtp.gmail.com:587`, TLS, App Password.
- **Email Reception:**
  - `InboundEmailCommandWorker` polls IMAP for replies.
  - Matches `In-Reply-To` header or Subject body for tracking IDs (`wafek-{logId}`).

### 3. Current Implementation Status
- **Database Schema:** Updated with `WF_Definitions`, `WF_Steps`, `WF_Logs`.
- **Stored Procedures:** `Approve_CreateFirstProcess` rewritten to handle both Legacy (GUID, int) and New calls.
- **Worker Service:** `WorkflowEngineWorker` is active and polling `WF_Logs`.
- **GitHub:** Synced with `https://github.com/Nabilsoliman9869/Wafek_Web_Manager`.

### 4. Known Issues & Fixes Applied
- **Issue:** Email Sending Failure.
  - **Fix:** Switched from Database Mail back to C# SMTP with `UseDefaultCredentials = false` and `DeliveryMethod = Network`.
- **Issue:** Permission Denied on `msdb`.
  - **Fix:** Abandoned `sp_send_dbmail` approach in favor of app-level SMTP.
- **Issue:** `In-Reply-To` Tracking.
  - **Action:** Need to ensure `Message-ID` is explicitly set when sending emails to allow threading.

### 5. Next Steps
- Verify email delivery in production environment (bypass firewalls if any).
- Test the full loop: Create Bond -> Email Sent -> User Replies `*#Approve` -> DB Updated.

---
*This file is automatically updated to maintain context across sessions.*
