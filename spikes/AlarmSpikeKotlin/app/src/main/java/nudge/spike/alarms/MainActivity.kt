package nudge.spike.alarms

import android.Manifest
import android.app.Activity
import android.app.usage.UsageStatsManager
import android.content.ClipData
import android.content.ClipboardManager
import android.content.Intent
import android.content.pm.PackageManager
import android.graphics.Typeface
import android.net.Uri
import android.os.Build
import android.os.Bundle
import android.os.Handler
import android.os.Looper
import android.provider.Settings
import android.util.TypedValue
import android.view.View
import android.view.ViewGroup
import android.view.WindowInsets
import android.widget.Button
import android.widget.EditText
import android.widget.LinearLayout
import android.widget.ScrollView
import android.widget.TextView

/**
 * The spike's only page: buttons to schedule test alarms, what the
 * phone allows, the pending alarms and the event log. It only reads
 * and writes the files; the receivers never need it.
 */
class MainActivity : Activity() {
    private lateinit var device: TextView
    private lateinit var pending: TextView
    private lateinit var log: TextView
    private lateinit var note: EditText
    private lateinit var exactSettings: Button

    private val handler = Handler(Looper.getMainLooper())
    private var visible = false

    private val tick = object : Runnable {
        override fun run() {
            if (visible) {
                refresh()
                handler.postDelayed(this, REFRESH_MS)
            }
        }
    }

    override fun onCreate(savedInstanceState: Bundle?) {
        super.onCreate(savedInstanceState)
        setContentView(buildPage())
        if (savedInstanceState == null) {
            handleIntent(intent)
            askForNotifications()
        }
    }

    override fun onNewIntent(intent: Intent) {
        super.onNewIntent(intent)
        setIntent(intent)
        handleIntent(intent)
        refresh()
    }

    override fun onResume() {
        super.onResume()
        visible = true
        refresh()
        handler.removeCallbacks(tick)
        handler.postDelayed(tick, REFRESH_MS)
    }

    override fun onPause() {
        visible = false
        handler.removeCallbacks(tick)
        super.onPause()
    }

    /**
     * The notification's Schedule button and its body open the app;
     * both arrive here. Schedule is an activity intent because
     * Android 12 and later refuse to start an activity from a
     * receiver behind a notification.
     */
    private fun handleIntent(intent: Intent?) {
        val id = intent?.getIntExtra(Alarms.EXTRA_ID, -1) ?: -1
        when (intent?.action) {
            Alarms.ACTION_SCHEDULE ->
                Alarms.onAction(this, Alarms.ACTION_SCHEDULE, id)

            Alarms.ACTION_OPEN -> if (id > 0) {
                SpikeLog.write(
                    "tapped",
                    id,
                    "notification body; app opened",
                )
            }
        }
    }

    private fun askForNotifications() {
        if (Build.VERSION.SDK_INT < Build.VERSION_CODES.TIRAMISU) {
            return
        }

        val permission = Manifest.permission.POST_NOTIFICATIONS
        if (checkSelfPermission(permission)
            == PackageManager.PERMISSION_GRANTED
        ) {
            return
        }

        requestPermissions(arrayOf(permission), NOTIFICATIONS_REQUEST)
    }

    override fun onRequestPermissionsResult(
        requestCode: Int,
        permissions: Array<out String>,
        grantResults: IntArray,
    ) {
        super.onRequestPermissionsResult(
            requestCode,
            permissions,
            grantResults,
        )
        if (requestCode != NOTIFICATIONS_REQUEST) {
            return
        }

        val granted = grantResults.firstOrNull() ==
            PackageManager.PERMISSION_GRANTED
        SpikeLog.write(
            "permission",
            null,
            "notifications: " + if (granted) "Granted" else "Denied",
        )
        refresh()
    }

    private fun buildPage(): View {
        device = text()
        pending = text()
        log = text(mono = true)
        note = EditText(this).apply {
            hint = "Note, e.g. step 3"
            setSingleLine(true)
        }

        val schedule = FlowLayout(this, dp(8))
        // 3 h is for the Doze step: Android does not enter deep Doze
        // when an alarm clock is due within the next hour.
        for (minutes in longArrayOf(1, 5, 15, 60, 180)) {
            val label = if (minutes < 60) {
                "In $minutes min"
            } else {
                "In ${minutes / 60} h"
            }
            schedule.addView(
                act(label) { Alarms.scheduleNew(this, minutes) },
            )
        }

        exactSettings = act("Allow exact alarms") { openExactAlarmSettings() }

        val tools = FlowLayout(this, dp(8))
        tools.addView(act("Add note") { addNote() })
        tools.addView(act("Copy log") { copyLog() })
        tools.addView(act("Share log") { shareLog() })
        tools.addView(
            act("Re-register all") { Alarms.restoreAll(this, "button") },
        )
        tools.addView(act("Cancel all") { Alarms.cancelAll(this) })
        tools.addView(
            act("Notification settings") { openNotificationSettings() },
        )

        val column = LinearLayout(this).apply {
            orientation = LinearLayout.VERTICAL
            val pad = dp(16)
            setPadding(pad, pad, pad, pad)
        }
        for (view in listOf(
            heading("Nudge alarm spike"),
            device,
            exactSettings,
            heading("Schedule a test alarm"),
            schedule,
            heading("Pending"),
            pending,
            heading("Log (newest first)"),
            note,
            tools,
            log,
        )) {
            val params = LinearLayout.LayoutParams(
                if (view is Button) {
                    ViewGroup.LayoutParams.WRAP_CONTENT
                } else {
                    ViewGroup.LayoutParams.MATCH_PARENT
                },
                ViewGroup.LayoutParams.WRAP_CONTENT,
            )
            params.bottomMargin = dp(8)
            column.addView(view, params)
        }

        val scroll = ScrollView(this)
        scroll.addView(column)
        keepClearOfSystemBars(scroll)
        return scroll
    }

    /**
     * Apps that target API 35 are drawn edge to edge on Android 15;
     * pad the page so the status and navigation bars and the keyboard
     * do not cover it. Older versions lay the page out inside the bars
     * by themselves.
     */
    private fun keepClearOfSystemBars(view: View) {
        if (Build.VERSION.SDK_INT < Build.VERSION_CODES.R) {
            return
        }

        view.setOnApplyWindowInsetsListener { v, insets ->
            val bars = insets.getInsets(
                WindowInsets.Type.systemBars()
                    or WindowInsets.Type.displayCutout()
                    or WindowInsets.Type.ime(),
            )
            v.setPadding(bars.left, bars.top, bars.right, bars.bottom)
            insets
        }
    }

    private fun refresh() {
        val canExact = Alarms.canScheduleExact(this)
        device.text = listOf(
            "Android ${Build.VERSION.RELEASE} (API ${Build.VERSION.SDK_INT})",
            "${Build.MANUFACTURER} ${Build.MODEL}",
            "Exact alarms allowed: ${Alarms.yesNo(canExact)}",
            "Notifications enabled: "
                + Alarms.yesNo(Alarms.notificationsEnabled(this)),
            "Exempt from battery optimisation: "
                + Alarms.yesNo(Alarms.ignoringBatteryOptimisations(this)),
            "Standby bucket: ${standbyBucket()}",
            "System's next alarm clock: " + Alarms.nextSystemAlarm(this),
            "Log file: ${SpikeLog.path}",
        ).joinToString("\n")
        exactSettings.visibility = if (canExact) View.GONE else View.VISIBLE

        val alarms = SpikeStore.all()
        pending.text = if (alarms.isEmpty()) {
            "None"
        } else {
            alarms.joinToString("\n") {
                "#${it.id} ${it.state}, due ${Clock.format(it.dueUtcMs)} " +
                    "(${it.reason})"
            }
        }

        log.text = SpikeLog.readAll()
            .split('\n')
            .filter { it.isNotEmpty() }
            .asReversed()
            .take(SHOWN_LOG_LINES)
            .joinToString("\n")
    }

    /** The bucket's name as the MAUI spike showed it, e.g. Active. */
    private fun standbyBucket(): String {
        if (Build.VERSION.SDK_INT < Build.VERSION_CODES.P) {
            return "n/a"
        }

        val usage = getSystemService(UsageStatsManager::class.java)
        return when (val bucket = usage.appStandbyBucket) {
            UsageStatsManager.STANDBY_BUCKET_ACTIVE -> "Active"
            UsageStatsManager.STANDBY_BUCKET_WORKING_SET -> "WorkingSet"
            UsageStatsManager.STANDBY_BUCKET_FREQUENT -> "Frequent"
            UsageStatsManager.STANDBY_BUCKET_RARE -> "Rare"
            UsageStatsManager.STANDBY_BUCKET_RESTRICTED -> "Restricted"
            else -> bucket.toString()
        }
    }

    private fun addNote() {
        val text = note.text.toString().trim()
        if (text.isEmpty()) {
            return
        }

        SpikeLog.write("note", null, text)
        note.setText("")
    }

    /**
     * Copies the last [SHARED_LOG_LINES] lines: the clipboard goes
     * through a binder call, which fails on very large text.
     */
    private fun copyLog() {
        val clipboard = getSystemService(ClipboardManager::class.java)
        clipboard.setPrimaryClip(
            ClipData.newPlainText(
                "Alarm spike log",
                SpikeLog.lastLines(SHARED_LOG_LINES),
            ),
        )
        SpikeLog.write(
            "log-copied",
            null,
            "to the clipboard (last $SHARED_LOG_LINES lines at most)",
        )
    }

    /**
     * Shares the last [SHARED_LOG_LINES] lines as text: a file would
     * need a content provider, and AndroidX's FileProvider is not used
     * here; an intent's extras have a size limit too. `adb pull` gets
     * the whole log.
     */
    private fun shareLog() {
        val send = Intent(Intent.ACTION_SEND)
            .setType("text/plain")
            .putExtra(Intent.EXTRA_SUBJECT, "Alarm spike log")
            .putExtra(
                Intent.EXTRA_TEXT,
                SpikeLog.lastLines(SHARED_LOG_LINES),
            )
        startActivity(Intent.createChooser(send, "Alarm spike log"))
    }

    private fun openExactAlarmSettings() {
        if (Build.VERSION.SDK_INT < Build.VERSION_CODES.S) {
            return
        }

        startActivity(
            Intent(
                Settings.ACTION_REQUEST_SCHEDULE_EXACT_ALARM,
                Uri.parse("package:$packageName"),
            ),
        )
    }

    private fun openNotificationSettings() {
        startActivity(
            Intent(Settings.ACTION_APP_NOTIFICATION_SETTINGS)
                .putExtra(Settings.EXTRA_APP_PACKAGE, packageName),
        )
    }

    private fun act(label: String, action: () -> Unit): Button =
        Button(this).apply {
            text = label
            isAllCaps = false
            setOnClickListener {
                try {
                    action()
                } catch (e: Exception) {
                    SpikeLog.write(
                        "error",
                        null,
                        "$label: ${e.stackTraceToString()}",
                    )
                }

                refresh()
            }
        }

    private fun heading(label: String): TextView =
        TextView(this).apply {
            text = label
            setTextSize(TypedValue.COMPLEX_UNIT_SP, 20f)
            setTypeface(typeface, Typeface.BOLD)
        }

    private fun text(mono: Boolean = false): TextView =
        TextView(this).apply {
            setTextSize(TypedValue.COMPLEX_UNIT_SP, if (mono) 11f else 14f)
            if (mono) {
                typeface = Typeface.MONOSPACE
            }
            setTextIsSelectable(true)
        }

    private fun dp(value: Int): Int =
        TypedValue.applyDimension(
            TypedValue.COMPLEX_UNIT_DIP,
            value.toFloat(),
            resources.displayMetrics,
        ).toInt()

    private companion object {
        const val SHOWN_LOG_LINES = 300
        const val SHARED_LOG_LINES = 500
        const val REFRESH_MS = 2_000L
        const val NOTIFICATIONS_REQUEST = 1
    }
}
