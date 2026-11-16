package nudge.spike.alarms

import android.app.AlarmManager
import android.app.Notification
import android.app.NotificationChannel
import android.app.NotificationManager
import android.app.PendingIntent
import android.content.Context
import android.content.Intent
import android.graphics.drawable.Icon
import android.os.Build
import android.os.PowerManager

/**
 * Everything an alarm goes through: scheduling with
 * `AlarmManager.setAlarmClock`, firing, the notification and its
 * actions, swipes and the restore after a reboot. Only Android APIs
 * and the files in [SpikeStore] are used, so the receivers work in a
 * process that has just been started for them.
 */
object Alarms {
    const val ACTION_FIRE = "nudge.spike.alarms.FIRE"
    const val ACTION_COMPLETE = "nudge.spike.alarms.COMPLETE"
    const val ACTION_SNOOZE = "nudge.spike.alarms.SNOOZE"
    const val ACTION_SWIPED = "nudge.spike.alarms.SWIPED"
    const val ACTION_SCHEDULE = "nudge.spike.alarms.SCHEDULE"
    const val ACTION_OPEN = "nudge.spike.alarms.OPEN"
    const val EXTRA_ID = "alarm_id"

    private const val CHANNEL_ID = "spike_alarms"

    const val SNOOZE_MINUTES = 2L

    const val RENOTIFY_SECONDS = 30L

    /** A fire later than this is marked LATE in the log. */
    const val LATE_AFTER_MS = 10_000L

    fun scheduleNew(context: Context, minutesFromNow: Long) {
        val alarm = SpikeAlarm(
            SpikeStore.nextId(),
            Clock.nowMs + minutesFromNow * 60_000L,
            AlarmState.Pending,
            "test, $minutesFromNow min",
        )
        SpikeStore.put(alarm)
        register(context, alarm, "scheduled")
    }

    /** Hands the alarm to AlarmManager and logs it. */
    private fun register(context: Context, alarm: SpikeAlarm, event: String) {
        val manager = alarmManagerOf(context)
        if (Build.VERSION.SDK_INT >= Build.VERSION_CODES.S
            && !manager.canScheduleExactAlarms()
        ) {
            SpikeLog.write(
                "schedule-failed",
                alarm.id,
                "exact alarms are not allowed for this app",
            )
            return
        }

        try {
            val info = AlarmManager.AlarmClockInfo(
                alarm.dueUtcMs,
                openAppIntent(context, ACTION_OPEN, 0),
            )
            manager.setAlarmClock(info, fireIntent(context, alarm.id))
            SpikeLog.write(
                event,
                alarm.id,
                "due ${Clock.format(alarm.dueUtcMs)} (${alarm.reason})",
            )
        } catch (e: SecurityException) {
            SpikeLog.write("schedule-failed", alarm.id, e.message ?: "")
        }
    }

    /** The alarm went off (or was overdue at boot). */
    fun fire(context: Context, id: Int, via: String) {
        val alarm = SpikeStore.get(id)
        if (alarm == null) {
            SpikeLog.write("fired-unknown", id, "via $via; not stored")
            return
        }

        val delay = Clock.nowMs - alarm.dueUtcMs

        // A pending alarm that is not due yet: a stale broadcast, for
        // example one already on its way when Snooze moved the alarm.
        // Not shown; registered again so it cannot be left without an
        // alarm. No tolerance: AlarmManager's RTC alarms and
        // Clock.nowMs read the same wall clock, and AlarmManager never
        // delivers before the trigger time, so a real fire is never
        // early. (A fire for an alarm already Showing, a duplicate
        // broadcast, is shown again below: the notification is
        // replaced, not doubled.)
        if (alarm.state == AlarmState.Pending && delay < 0) {
            SpikeLog.write(
                "fired-early",
                id,
                "via $via, due ${Clock.format(alarm.dueUtcMs)}, "
                    + "${-delay} ms early (${alarm.reason}); not shown",
            )
            register(context, alarm, "re-registered")
            return
        }

        val late = if (delay > LATE_AFTER_MS) " LATE" else ""
        val signed = if (delay >= 0) "+$delay" else "$delay"
        // Nothing on the way from here to the notification may throw
        // because of the log line, so the device state is read safely.
        val state = try {
            deviceState(context)
        } catch (e: Exception) {
            "device state unknown: $e"
        }
        SpikeLog.write(
            "fired",
            id,
            "due ${Clock.format(alarm.dueUtcMs)}, delay $signed ms$late, "
                + "via $via (${alarm.reason}); $state",
        )

        val showing = alarm.copy(state = AlarmState.Showing)
        SpikeStore.put(showing)
        show(context, showing, "shown")
    }

    private fun show(context: Context, alarm: SpikeAlarm, event: String) {
        val notifications = notificationManagerOf(context)
        ensureChannel(notifications)

        val dueTime = Clock.format(alarm.dueUtcMs).substring(11, 19)
        val notification = Notification.Builder(context, CHANNEL_ID)
            .setSmallIcon(android.R.drawable.ic_lock_idle_alarm)
            .setContentTitle("Spike test alarm #${alarm.id}")
            .setContentText("Invented test reminder, due $dueTime")
            .setCategory(Notification.CATEGORY_REMINDER)
            .setWhen(alarm.dueUtcMs)
            .setShowWhen(true)
            .setAutoCancel(false)
            .setContentIntent(openAppIntent(context, ACTION_OPEN, alarm.id))
            .setDeleteIntent(actionIntent(context, ACTION_SWIPED, alarm.id))
            .addAction(
                button(
                    "Complete",
                    actionIntent(context, ACTION_COMPLETE, alarm.id),
                ),
            )
            .addAction(
                button(
                    "Schedule",
                    openAppIntent(context, ACTION_SCHEDULE, alarm.id),
                ),
            )
            .addAction(
                button(
                    "Snooze",
                    actionIntent(context, ACTION_SNOOZE, alarm.id),
                ),
            )
            .build()
        notifications.notify(alarm.id, notification)

        val channel = notifications.getNotificationChannel(CHANNEL_ID)
        SpikeLog.write(
            event,
            alarm.id,
            "posted; notifications enabled: "
                + yesNo(notifications.areNotificationsEnabled())
                + ", channel importance: "
                + importanceName(channel?.importance),
        )
    }

    /** Complete, Snooze, swipe, or Schedule (from the app). */
    fun onAction(context: Context, action: String, id: Int) {
        val alarm = SpikeStore.get(id)
        val name = action.substring(action.lastIndexOf('.') + 1)
            .lowercase()
        if (alarm == null || alarm.state != AlarmState.Showing) {
            SpikeLog.write(
                "$name-ignored",
                id,
                if (alarm == null) "not stored" else "not showing",
            )
            return
        }

        val notifications = notificationManagerOf(context)
        when (action) {
            ACTION_COMPLETE -> {
                notifications.cancel(id)
                SpikeStore.remove(id)
                SpikeLog.write("complete", id, "dismissed and removed")
            }

            ACTION_SCHEDULE -> {
                notifications.cancel(id)
                SpikeStore.remove(id)
                SpikeLog.write(
                    "schedule",
                    id,
                    "app opened; dismissed and removed (the real app "
                        + "would ask for a new time here)",
                )
            }

            ACTION_SNOOZE -> {
                notifications.cancel(id)
                SpikeLog.write(
                    "snooze",
                    id,
                    "dismissed; again in $SNOOZE_MINUTES min",
                )
                requeue(context, alarm, SNOOZE_MINUTES * 60_000L, "snoozed")
            }

            ACTION_SWIPED -> {
                SpikeLog.write(
                    "swiped",
                    id,
                    "a swipe is not an answer; again in "
                        + "$RENOTIFY_SECONDS s",
                )
                requeue(
                    context,
                    alarm,
                    RENOTIFY_SECONDS * 1_000L,
                    "re-notify",
                )
            }
        }
    }

    private fun requeue(
        context: Context,
        alarm: SpikeAlarm,
        afterMs: Long,
        reason: String,
    ) {
        val again = alarm.copy(
            dueUtcMs = Clock.nowMs + afterMs,
            state = AlarmState.Pending,
            reason = reason,
        )
        SpikeStore.put(again)
        register(context, again, "scheduled")
    }

    /**
     * After a reboot or an update: registers every pending alarm
     * again, fires at once those whose time has passed, and puts back
     * the notifications that were up and unanswered.
     */
    fun restoreAll(context: Context, why: String) {
        val alarms = SpikeStore.all()
        SpikeLog.write("restore", null, "$why; ${alarms.size} stored")
        for (alarm in alarms) {
            when {
                alarm.state == AlarmState.Showing ->
                    show(context, alarm, "re-shown")

                alarm.dueUtcMs <= Clock.nowMs ->
                    fire(context, alarm.id, why)

                else -> register(context, alarm, "re-registered")
            }
        }
    }

    fun cancelAll(context: Context) {
        val manager = alarmManagerOf(context)
        val notifications = notificationManagerOf(context)
        for (alarm in SpikeStore.all()) {
            manager.cancel(fireIntent(context, alarm.id))
            notifications.cancel(alarm.id)
            SpikeStore.remove(alarm.id)
            SpikeLog.write("cancelled", alarm.id, "by the Cancel button")
        }
    }

    fun canScheduleExact(context: Context): Boolean =
        Build.VERSION.SDK_INT < Build.VERSION_CODES.S
            || alarmManagerOf(context).canScheduleExactAlarms()

    fun notificationsEnabled(context: Context): Boolean {
        val notifications = notificationManagerOf(context)
        val channel = notifications.getNotificationChannel(CHANNEL_ID)
        return notifications.areNotificationsEnabled()
            && channel?.importance != NotificationManager.IMPORTANCE_NONE
    }

    /** The next alarm clock the system knows, from any app. */
    fun nextSystemAlarm(context: Context): String {
        val next = alarmManagerOf(context).nextAlarmClock ?: return "none"
        val creator = next.showIntent?.creatorPackage
        val owner = when (creator) {
            context.packageName -> "this app"
            null -> "unknown app"
            else -> creator
        }
        return "${Clock.format(next.triggerTime)} ($owner)"
    }

    fun yesNo(value: Boolean): String = if (value) "yes" else "no"

    /** The importance as the MAUI spike's log wrote it, e.g. High. */
    private fun importanceName(importance: Int?): String =
        when (importance) {
            null -> ""
            NotificationManager.IMPORTANCE_NONE -> "None"
            NotificationManager.IMPORTANCE_MIN -> "Min"
            NotificationManager.IMPORTANCE_LOW -> "Low"
            NotificationManager.IMPORTANCE_DEFAULT -> "Default"
            NotificationManager.IMPORTANCE_HIGH -> "High"
            NotificationManager.IMPORTANCE_MAX -> "Max"
            NotificationManager.IMPORTANCE_UNSPECIFIED -> "Unspecified"
            else -> importance.toString()
        }

    private fun ensureChannel(notifications: NotificationManager) {
        if (notifications.getNotificationChannel(CHANNEL_ID) != null) {
            return
        }

        val channel = NotificationChannel(
            CHANNEL_ID,
            "Test alarms",
            NotificationManager.IMPORTANCE_HIGH,
        )
        channel.description = "Alarms scheduled from the spike's page"
        notifications.createNotificationChannel(channel)
    }

    private fun button(
        title: String,
        intent: PendingIntent,
    ): Notification.Action =
        Notification.Action.Builder(null as Icon?, title, intent).build()

    private fun fireIntent(context: Context, id: Int): PendingIntent =
        actionIntent(context, ACTION_FIRE, id)

    private fun actionIntent(
        context: Context,
        action: String,
        id: Int,
    ): PendingIntent {
        val intent = Intent(context, AlarmReceiver::class.java)
            .setAction(action)
            .putExtra(EXTRA_ID, id)
        return PendingIntent.getBroadcast(
            context,
            id,
            intent,
            PendingIntent.FLAG_UPDATE_CURRENT or PendingIntent.FLAG_IMMUTABLE,
        )
    }

    private fun openAppIntent(
        context: Context,
        action: String,
        id: Int,
    ): PendingIntent {
        val intent = Intent(context, MainActivity::class.java)
            .setAction(action)
            .putExtra(EXTRA_ID, id)
            .addFlags(
                Intent.FLAG_ACTIVITY_SINGLE_TOP
                    or Intent.FLAG_ACTIVITY_CLEAR_TOP,
            )
        return PendingIntent.getActivity(
            context,
            id,
            intent,
            PendingIntent.FLAG_UPDATE_CURRENT or PendingIntent.FLAG_IMMUTABLE,
        )
    }

    private fun alarmManagerOf(context: Context): AlarmManager =
        context.getSystemService(AlarmManager::class.java)

    private fun notificationManagerOf(context: Context): NotificationManager =
        context.getSystemService(NotificationManager::class.java)

    fun ignoringBatteryOptimisations(context: Context): Boolean =
        powerManagerOf(context)
            .isIgnoringBatteryOptimizations(context.packageName)

    /**
     * What the phone was doing when the alarm arrived, so the log
     * shows whether it really was in Doze, on screen or saving power.
     */
    private fun deviceState(context: Context): String {
        val power = powerManagerOf(context)
        val light = if (Build.VERSION.SDK_INT >= Build.VERSION_CODES.TIRAMISU) {
            yesNo(power.isDeviceLightIdleMode)
        } else {
            "n/a"
        }
        return "doze: ${yesNo(power.isDeviceIdleMode)}, " +
            "light doze: $light, " +
            "screen on: ${yesNo(power.isInteractive)}, " +
            "battery saver: ${yesNo(power.isPowerSaveMode)}"
    }

    private fun powerManagerOf(context: Context): PowerManager =
        context.getSystemService(PowerManager::class.java)
}
