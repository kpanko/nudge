package nudge.spike.alarms

import android.content.BroadcastReceiver
import android.content.Context
import android.content.Intent
import android.util.Log

/**
 * Receives the alarm itself and the notification's Complete, Snooze
 * and swipe intents. Not exported: only this app's own pending
 * intents reach it.
 */
class AlarmReceiver : BroadcastReceiver() {
    override fun onReceive(context: Context, intent: Intent) {
        val action = intent.action ?: return
        val id = intent.getIntExtra(Alarms.EXTRA_ID, -1)
        try {
            if (action == Alarms.ACTION_FIRE) {
                Alarms.fire(context, id, "alarm")
            } else {
                Alarms.onAction(context, action, id)
            }
        } catch (e: Exception) {
            SpikeLog.write("error", id, "$action: ${e.stackTraceToString()}")
        }
    }
}

/**
 * Re-registers the alarms after a reboot, which clears them all.
 * After an update of the app it does the same; stock Android keeps
 * the alarms then, so this only matters where a manufacturer does
 * not, and it is harmless otherwise. QUICKBOOT_POWERON is what some
 * manufacturers send instead of BOOT_COMPLETED after a fast reboot.
 */
class BootReceiver : BroadcastReceiver() {
    override fun onReceive(context: Context, intent: Intent) {
        val action = intent.action
        if (action == null || action !in RESTORE_ACTIONS) {
            // The receiver is exported, so any app can send it
            // anything; only the system's boot and update broadcasts
            // restore the alarms.
            Log.w(TAG, "BootReceiver ignored action $action")
            return
        }

        try {
            val name = action.substring(action.lastIndexOf('.') + 1)
            SpikeLog.write("boot-broadcast", null, name)
            Alarms.restoreAll(context, name)
        } catch (e: Exception) {
            SpikeLog.write(
                "error",
                null,
                "$action: ${e.stackTraceToString()}",
            )
        }
    }

    private companion object {
        const val TAG = "AlarmSpike"

        val RESTORE_ACTIONS = setOf(
            Intent.ACTION_BOOT_COMPLETED,
            Intent.ACTION_MY_PACKAGE_REPLACED,
            "android.intent.action.QUICKBOOT_POWERON",
        )
    }
}
