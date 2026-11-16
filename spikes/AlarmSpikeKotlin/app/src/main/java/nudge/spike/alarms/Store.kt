package nudge.spike.alarms

import android.os.Environment
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
 *
 * Writing never throws, so logging cannot crash a receiver. While
 * shared storage is not mounted (early after a boot) or a write to it
 * fails, lines go to `pending.log` in private storage, and the next
 * write that reaches shared storage moves them over first, in order.
 * Logcat gets every line either way.
 */
object SpikeLog {
    private const val TAG = "AlarmSpike"

    private val gate = Any()

    /** The shared-storage log, or null while it cannot be used. */
    private val sharedFile: File?
        get() {
            val folder = SpikeApplication.context.getExternalFilesDir(null)
            val mounted = folder != null &&
                Environment.getExternalStorageState(folder) ==
                Environment.MEDIA_MOUNTED
            return if (mounted) File(folder, "events.log") else null
        }

    private val pendingFile: File
        get() = File(SpikeApplication.context.filesDir, "pending.log")

    /** Where the log is meant to be, for the page. */
    val path: String
        get() = try {
            sharedFile?.absolutePath ?: "not mounted yet"
        } catch (e: Exception) {
            "unknown (${e.message})"
        }

    /** Appends one line: time | event | alarm | details. */
    fun write(event: String, alarmId: Int?, details: String) {
        val line = try {
            val id = if (alarmId == null) "-" else "#$alarmId"
            val oneLine = details.replace("\r\n", " ")
                .replace('\n', ' ')
                .replace('\r', ' ')
            "${Clock.format(Clock.nowMs)} | $event | $id | $oneLine"
        } catch (e: Exception) {
            "$event | $alarmId | $details"
        }

        try {
            synchronized(gate) { append(line) }
            Log.i(TAG, line)
        } catch (e: Exception) {
            Log.w(TAG, "$line (not saved: $e)")
        }
    }

    private fun append(line: String) {
        val shared = try {
            sharedFile
        } catch (e: Exception) {
            null
        }
        if (shared != null) {
            try {
                movePending(shared)
                shared.appendText(line + "\n")
                return
            } catch (e: Exception) {
                Log.w(TAG, "events.log not writable: $e")
            }
        }

        pendingFile.appendText(line + "\n")
    }

    /** Moves the lines kept while shared storage was not usable. */
    private fun movePending(shared: File) {
        val pending = pendingFile
        if (!pending.exists()) {
            return
        }

        shared.appendText(pending.readText())
        pending.delete()
    }

    /** The whole log, lines still waiting in pending.log included. */
    fun readAll(): String = try {
        synchronized(gate) {
            val shared = try {
                sharedFile
            } catch (e: Exception) {
                null
            }
            val pending = pendingFile
            (if (shared?.exists() == true) shared.readText() else "") +
                (if (pending.exists()) pending.readText() else "")
        }
    } catch (e: Exception) {
        "The log could not be read: $e\n"
    }

    /** The last [count] lines of the log, for Copy and Share. */
    fun lastLines(count: Int): String =
        readAll().split('\n')
            .filter { it.isNotEmpty() }
            .takeLast(count)
            .joinToString("\n", postfix = "\n")
}
