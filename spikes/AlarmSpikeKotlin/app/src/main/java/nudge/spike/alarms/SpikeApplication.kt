package nudge.spike.alarms

import android.app.Application
import android.content.Context
import android.os.Process
import android.os.SystemClock

class SpikeApplication : Application() {
    /**
     * Runs in every new process, including one Android starts only to
     * deliver an alarm. The log line shows when that happened and how
     * long the start-up took before the receiver could run.
     */
    override fun onCreate() {
        super.onCreate()
        context = applicationContext
        val sinceStart = SystemClock.elapsedRealtime() -
            Process.getStartElapsedRealtime()
        SpikeLog.write(
            "process-start",
            null,
            "pid ${Process.myPid()}, $sinceStart ms after the process "
                + "was forked",
        )
    }

    companion object {
        /** The application context, set before any receiver runs. */
        lateinit var context: Context
            private set
    }
}
