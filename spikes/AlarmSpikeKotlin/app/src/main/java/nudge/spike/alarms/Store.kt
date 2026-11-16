package nudge.spike.alarms

import android.util.Log
import java.io.File
import java.time.Instant
import java.time.ZoneId
import java.time.format.DateTimeFormatter
import java.util.Locale

enum class AlarmState {
    /** Registered with AlarmManager, waiting to fire. */
    Pending,

    /** Fired; its notification is up and not answered. */
    Showing,
}

/** One test alarm, as kept on the device. */
data class SpikeAlarm(
    val id: Int,
    val dueUtcMs: Long,
    val state: AlarmState,
    val reason: String,
)

/**
 * Times as the log writes them: local time with the offset and
 * milliseconds, so a line is unambiguous on its own.
 */
object Clock {
    private val format = DateTimeFormatter.ofPattern(
        "yyyy-MM-dd HH:mm:ss.SSS xxx",
        Locale.ROOT,
    )

    val nowMs: Long
        get() = System.currentTimeMillis()

    fun format(utcMs: Long): String =
        Instant.ofEpochMilli(utcMs)
            .atZone(ZoneId.systemDefault())
            .format(format)
}

/**
 * The pending alarms, in a small tab-separated file in the app's
 * private storage. Every receiver reads it afresh, so nothing
 * depends on a page or service being alive.
 */
object SpikeStore {
    private val gate = Any()

    private val folder: File
        get() = SpikeApplication.context.filesDir

    private val alarmsFile: File
        get() = File(folder, "alarms.tsv")

    private val nextIdFile: File
        get() = File(folder, "next-id.txt")

    fun all(): List<SpikeAlarm> = synchronized(gate) { read() }

    fun get(id: Int): SpikeAlarm? =
        synchronized(gate) { read().firstOrNull { it.id == id } }

    fun put(alarm: SpikeAlarm) {
        synchronized(gate) {
            write(read().filter { it.id != alarm.id } + alarm)
        }
    }

    fun remove(id: Int) {
        synchronized(gate) {
            write(read().filter { it.id != id })
        }
    }

    fun nextId(): Int = synchronized(gate) {
        val next = if (nextIdFile.exists()) {
            nextIdFile.readText().trim().toInt()
        } else {
            1
        }
        writeAtomically(nextIdFile, (next + 1).toString())
        next
    }

    private fun read(): List<SpikeAlarm> {
        if (!alarmsFile.exists()) {
            return emptyList()
        }

        return alarmsFile.readLines()
            .map { it.split('\t') }
            .filter { it.size == 4 }
            .map {
                SpikeAlarm(
                    it[0].toInt(),
                    it[1].toLong(),
                    AlarmState.valueOf(it[2]),
                    it[3],
                )
            }
            .sortedBy { it.dueUtcMs }
    }

    private fun write(alarms: List<SpikeAlarm>) {
        val text = StringBuilder()
        for (a in alarms) {
            text.append(a.id).append('\t')
            text.append(a.dueUtcMs).append('\t')
            text.append(a.state.name).append('\t')
            text.append(a.reason.replace('\t', ' ')).append('\n')
        }

        writeAtomically(alarmsFile, text.toString())
    }

    private fun writeAtomically(file: File, text: String) {
        val temp = File(file.path + ".tmp")
        temp.writeText(text)
        if (!temp.renameTo(file)) {
            throw IllegalStateException("could not replace ${file.name}")
        }
    }
}

/**
 * The event log: the evidence for the checklist. One line per event,
 * appended and never rewritten. It lives in the app's folder on
 * shared storage so that `adb pull` can read it from a release
 * build.
 */
object SpikeLog {
    private const val TAG = "AlarmSpike"

    private val gate = Any()

    val file: File
        get() {
            val context = SpikeApplication.context
            val folder = context.getExternalFilesDir(null)
                ?: context.filesDir
            return File(folder, "events.log")
        }

    /** Appends one line: time | event | alarm | details. */
    fun write(event: String, alarmId: Int?, details: String) {
        val id = if (alarmId == null) "-" else "#$alarmId"
        val oneLine = details.replace("\r\n", " ")
            .replace('\n', ' ')
            .replace('\r', ' ')
        val line = "${Clock.format(Clock.nowMs)} | $event | $id | $oneLine"
        synchronized(gate) {
            file.appendText(line + "\n")
        }

        Log.i(TAG, line)
    }

    fun readAll(): String = synchronized(gate) {
        if (file.exists()) file.readText() else ""
    }
}
