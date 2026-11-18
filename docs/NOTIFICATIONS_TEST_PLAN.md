# Stage 37 — notifications and reminders verification

## Notification API
1. An unauthenticated request to every notification endpoint returns 401.
2. A signed-in traveller can list only their own notifications.
3. A trip member can create a notification for a trip they belong to.
4. A non-member cannot create a notification for another trip.
5. Unsupported notification types and over-length titles/messages are rejected.
6. A future reminder is accepted and stored as a UTC timestamp.
7. A past scheduled timestamp is rejected.
8. A duplicate non-null deduplication key returns a conflict without creating a second notification.
9. An unread notification can be marked read; repeating the action is idempotent.
10. Mark-all-read affects only the signed-in user's due, non-dismissed notifications.
11. Dismissal removes the notification from the active inbox without changing another user's copy.
12. A future-dated reminder is not returned in the active inbox before its scheduled time.
13. The unread count excludes dismissed and not-yet-due notifications.
14. The list limit accepts 1–100 and rejects values outside that range.

## Workspace
15. The top-bar notification control shows the unread count.
16. Opening the inbox lists the signed-in user's due notifications.
17. Mark read updates the item and unread count.
18. Dismiss removes the item from the active inbox.
19. Mark all read clears the unread count.
20. A reminder can be created for the currently selected trip with title, message and a future local date/time.
21. The browser converts the chosen local date/time to UTC before submission.
22. Due reminders appear after the inbox refreshes while the workspace is open.
23. The inbox refreshes on visibility changes and on its periodic refresh interval.
24. Offline mode does not attempt to create or refresh server notifications.

## Database and migration
25. Migration 017 creates the notification table, trip/user foreign keys and indexes.
26. The unique deduplication-key index allows multiple NULL values but rejects duplicate non-NULL values.
27. The CI PostgreSQL job applies migration 017 after migration 016.

## Explicit limitations
- No background worker or server-side scheduler currently pushes due notifications to a closed browser.
- No browser push permission, email, SMS or mobile push delivery is implemented.
- Reminder preferences and per-user channel controls are not yet implemented.\n- A scheduled reminder is stored as UTC after the browser interprets the datetime-local value in the device's local timezone.
- Scheduled reminders use UTC for storage; the client currently interprets the datetime-local input in the device's local timezone.
