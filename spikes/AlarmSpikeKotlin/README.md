# Alarm spike, Kotlin (milestone M2)

A throwaway native Kotlin app, Android only, that answers one
question before the real Android app (M5) is built: **does a Kotlin
app deliver exact alarms reliably on the owner's phone?** In
particular, when the app's process is not running and Android has to
start it cold just to deliver the alarm broadcast.

Kotlin was chosen for the Android app on 2026-11-15 (see
DECISIONS.md); this spike replaces the earlier .NET MAUI one and
checks the alarm path on the real phone before the app is built on
it. Fill in the results table at the end.

It is a standalone Gradle project, not part of `Nudge.sln`; the
server's build, tests and CI never see it. It uses only the Android
platform APIs: no AndroidX, no Compose, no other libraries. It has no
network access (no `INTERNET` permission) and contains no personal
data: every alarm is an invented test string.

## What it does

- One page with buttons that schedule a test alarm in 1, 5, 15 or 60
  minutes, or 3 hours (for the Doze step), with
  `AlarmManager.setAlarmClock`. The page shows the
  Android version, manufacturer and model, whether exact alarms are
  allowed, whether notifications are enabled, whether the app is
  exempt from battery optimisation (it never asks to be), its standby
  bucket, the next alarm clock the system knows, the pending alarms,
  and the event log, newest first.
- When an alarm fires, a receiver posts a high-importance
  notification with three actions:
  - **Complete** dismisses it.
  - **Snooze** dismisses it and fires again 2 minutes later.
  - **Schedule** opens the app, then dismisses it (the real app would
    ask for a new time there).
- **Swiping the notification away** is detected and logged, and the
  alarm fires again 30 seconds later: a swipe is not an answer.
  ("Clear all" counts as a swipe.)
- After a **reboot**, or an update of the app, a boot receiver
  registers every pending alarm again. An alarm whose time passed
  while the phone was off fires at once and is logged as LATE. A
  notification that was up and unanswered is put back.
- Pending alarms (in the app's private storage) and the event log
  survive process death and reboots.

The receivers use only Android APIs and these files; nothing depends
on the page being alive.

### The log

One line per event, with local time, the offset and milliseconds
(the long line is wrapped here; in the file it is one line):

```
2026-11-15 14:03:00.004 +00:00 | process-start | - | pid 4242, 61 ms
    after the process was forked
2026-11-15 14:03:00.031 +00:00 | fired | #3 | due 2026-11-15
    14:03:00.000 +00:00, delay +31 ms, via alarm (test, 5 min);
    doze: yes, light doze: no, screen on: no, battery saver: no
2026-11-15 14:03:00.052 +00:00 | shown | #3 | posted; notifications
    enabled: yes, channel importance: High
```

The events:

- `process-start`: a new process started (a cold start if an alarm
  follows within a second).
- `scheduled`: handed to `setAlarmClock`, with the intended time.
- `fired`: the alarm arrived; `delay` is against the intended time;
  `LATE` if over 10 s; then whether the phone was in Doze, its screen
  on, battery saver on.
- `shown`: the notification was posted.
- `complete`, `snooze`, `schedule`: a notification action.
- `swiped`: the notification was swiped away; a `scheduled`
  (re-notify) follows.
- `tapped`: the notification's body was tapped.
- `boot-broadcast`, `restore`: the boot receiver ran
  (`BOOT_COMPLETED`, `MY_PACKAGE_REPLACED`).
- `re-registered`, `re-shown`: what the restore did with each alarm.
- `note`: your own note from the page's "Add note" box.
- `schedule-failed`, `error`: something went wrong; the details say
  what.

To get the log off the phone: **Copy log** (clipboard) or **Share
log** (email, Drive, ...; it is shared as text) on the page, or with
adb:

```bash
adb pull /sdcard/Android/data/nudge.spike.alarms/files/events.log
```

`adb logcat -s AlarmSpike` shows the same lines live.

Use **Add note** before each step below ("step 3: Doze"), so the log
reads on its own afterwards.

## Build

Needs the Android SDK (platform 35, build-tools 35) and a JDK 17, for
example as Visual Studio or Android Studio installs them. Gradle comes
with the project's wrapper (Gradle 8.11.1, checked against its
published SHA-256); the Android Gradle Plugin 8.7.3 and Kotlin 2.0.21
are fetched from Google's Maven repository and Maven Central on the
first build.

Tell the build where the SDK is, either with `ANDROID_HOME` or with a
`local.properties` file in this folder (it is ignored by git):

```properties
sdk.dir=C\:/Program Files (x86)/Android/android-sdk
```

Then, from this folder, in Git Bash:

```bash
cd spikes/AlarmSpikeKotlin
export JAVA_HOME="/c/Program Files (x86)/Android/openjdk/jdk-17.0.12"
./gradlew assembleRelease
```

(`gradlew.bat assembleRelease` does the same from cmd or PowerShell,
with `JAVA_HOME` set there.) Kotlin warnings are errors.

The APK is

```
spikes/AlarmSpikeKotlin/app/build/outputs/apk/release/app-release.apk
```

It is not minified and is signed with the local debug key, which is
enough to sideload it. It has no native code, so it runs on any phone
and on the emulator.

## Install

With adb (USB debugging on):

```bash
adb install -r app/build/outputs/apk/release/app-release.apk
```

Without adb: copy the APK to the phone (USB, Drive, email), open it
in the Files app and allow "Install unknown apps" for that app when
asked. Play Protect may warn about an unknown developer; choose to
install anyway.

If the MAUI spike is still installed, uninstall it first (`adb
uninstall nudge.spike.alarms`): both use the same application id but
are signed with different keys, so one cannot update the other.

On first start, allow notifications when asked (Android 13 and
later). Do **not** exempt the app from battery optimisation: the
point is to see whether `setAlarmClock` gets through without it.

## Checklist for the phone

Do every step with the phone on battery, not charging, unless the
step says otherwise. Before each step, add a note. In every step,
**pass** means: the `fired` line has a delay under 10 s (except where
LATE is expected), a `shown` line follows, and the notification is
on screen with Complete, Schedule and Snooze. Write the delay in the
results table.

The adb commands are optional; each step also says how to do it by
hand.

0. **Setup.** Install, open the app, allow notifications. Check on
   the page: exact alarms allowed: yes; notifications enabled: yes;
   exempt from battery optimisation: no. Note the Android version and
   model for the table.

1. **Baseline, app open.** Schedule "In 1 min" and leave the app on
   screen. Pass as above.

2. **Baseline, app swiped from recents.** Schedule "In 1 min", open
   recents and swipe the app away, turn the screen off. Pass as
   above, and a `process-start` line comes shortly before `fired`
   (the process was started cold for the alarm). With adb, instead
   of swiping: `adb shell am kill nudge.spike.alarms` with the app in
   the background.

3. **Doze.** Android does not enter deep Doze while an alarm clock
   (from any app, the Clock app included) is due within the next
   hour, so a short alarm never meets Doze. To deliver an alarm clock
   the phone leaves Doze for a moment, so the `fired` line usually
   says `doze: no` even when the phone was dozing until then; the
   proof that it dozed is the adb check below, or, by hand, hours of
   stillness before the alarm.

   By hand: in the evening, schedule "In 3 h", turn the screen off
   and leave the phone still, on battery, until it fires. Pass as
   above.

   With adb, much faster: force Doze first, then schedule. Forcing
   is refused ("Unable to go deep idle") while an alarm clock is due
   within the hour, and scheduling one while forced ends deep Doze at
   once, for the same reason. So shorten that hour for the test (the
   first line stops Google Play services from putting it back), and
   restore it afterwards:

   ```bash
   adb shell device_config set_sync_disabled_for_tests until_reboot
   adb shell device_config put device_idle min_time_to_alarm 60000
   # schedule "In 5 min" on the page, leave the app, then:
   adb shell input keyevent KEYCODE_SLEEP      # screen off
   adb shell dumpsys battery unplug
   adb shell dumpsys deviceidle force-idle     # "Now forced in ..."
   adb shell dumpsys deviceidle get deep       # IDLE or IDLE_MAINTENANCE
   # ... wait for the alarm; check `get deep` once or twice meanwhile ...
   adb shell dumpsys deviceidle unforce
   adb shell dumpsys battery reset
   adb shell device_config delete device_idle min_time_to_alarm
   adb shell device_config set_sync_disabled_for_tests none
   ```

   Pass as above, with `get deep` showing IDLE or IDLE_MAINTENANCE
   until the alarm. (`setAlarmClock` is allowed to wake the phone out
   of Doze; that is what is being tested.)

4. **Airplane mode.** Turn airplane mode on (and Wi-Fi off if it
   stays on), schedule "In 5 min", leave the app, screen off. Pass as
   above. With adb: `adb shell cmd connectivity airplane-mode enable`
   and `... disable` afterwards. The app never uses the network, so
   this shows that nothing in the path needs it.

5. **Reboot, alarm still in the future.** Schedule "In 15 min",
   restart the phone, unlock it once it is up. Pass: the log shows
   `boot-broadcast` (BOOT_COMPLETED), `restore` and `re-registered`
   for the alarm, then `fired` on time as above. The page's "System's
   next alarm clock" shows the alarm again after the reboot.

6. **Reboot, alarm due while the phone is off.** Schedule "In 1
   min", turn the phone off at once, wait two minutes, turn it on and
   unlock it. Pass: right after `boot-broadcast` and `restore`, a
   `fired` line marked LATE with "via BOOT_COMPLETED", and the
   notification appears.

7. **Notification actions.** With a notification up (from any step):
   - Complete: the notification goes; log `complete`; the alarm is no
     longer under Pending.
   - Snooze: the notification goes; log `snooze` and `scheduled`;
     about 2 minutes later `fired` and the notification again.
   - Schedule: the app opens; log `schedule`; the notification goes.
   Pass: each does that. Try Complete and Snooze once with the app
   swiped from recents first, so the action itself starts the
   process.

8. **Swipe-away.** With a notification up, swipe it away. Pass: log
   `swiped` and `scheduled` (re-notify); about 30 s later `fired` and
   the notification is back. Then clear it with Complete.

9. **Optional: a day of use.** Schedule "In 60 min" a few times over
   a normal day, phone in a pocket. Pass: every one fires with a
   delay under 10 s.

When done, share the log, or pull it with adb, and keep it with the
results (not in the repository if it shows anything personal, such
as notes).

## Known Android behaviours that are not failures

- **Force stop** (Settings > Apps > Force stop, or `adb shell am
  force-stop`) cancels all of an app's alarms, and Android delivers
  nothing to it, boot broadcasts included, until it is opened again.
  This is by design. Swiping from recents is not a force stop on
  stock Android. After a force stop, open the app and press
  "Re-register all".
- **Boot broadcasts arrive after the first unlock.** The app is not
  "direct boot aware", so until the phone is unlocked after a
  restart it gets nothing, and an alarm due in that time fires (LATE)
  at the unlock. The real app could fire before unlock with
  direct-boot storage; that is not part of this spike.
- **Some manufacturers block apps from starting after boot or in the
  background** unless allowed: Xiaomi (Autostart), Huawei (App
  launch), Oppo, Vivo and OnePlus (Auto-launch, Allow background
  activity), Samsung (sleeping apps, Never sleeping apps). If step 5,
  6 or 2 fails on such a phone, look for that setting, note it, allow
  it and repeat. Whether the real app can live with that setting is
  worth noting in the results. https://dontkillmyapp.com lists them
  per manufacturer.
- **Do Not Disturb** may hide the notification without blocking the
  alarm: the log still shows `fired` and `shown`. Test with Do Not
  Disturb off.
- **Notifications turned off** for the app or its "Test alarms"
  channel: `fired` appears but nothing is visible; the `shown` line
  says "notifications enabled: no".
- The alarm-clock icon in the status bar, and the alarm showing up as
  the "next alarm" in the clock or on the lock screen, are expected:
  that is what `setAlarmClock` does.
- With several of the app's notifications up, Android groups them
  into one; expand the group, then the notification, to see its
  actions. Clear each step's notification before the next step.
- After step 3 with adb, the phone stays in forced Doze until
  `deviceidle unforce`.
- Installing a new APK over the old one logs `MY_PACKAGE_REPLACED`
  and a restore; that is the update path, not a reboot.

## Checked on an emulator only (2026-11-15)

On the Pixel 7, API 35 (Android 15), x86_64 emulator, with the
release APK. The emulator was heavily loaded (load average 30 to 50
inside it) and its timings say little about a phone.

| Step | Delay | Notes |
| --- | --- | --- |
| 1 App open | +402 ms | |
| 2 After `am kill`, screen off | +2173 ms | cold start; `process-start` 298 ms after the fork |
| 3 Forced deep Doze | +64 ms | see below |
| 4 Airplane mode, after `am kill` | +283 ms | cold start; `process-start` 48 ms after the fork |
| 5 Reboot, alarm in the future | +339 ms | re-registered at BOOT_COMPLETED; cold start |
| 6 Reboot, alarm due while off | +113855 ms LATE | fired at BOOT_COMPLETED, as expected |
| 7 Complete, Snooze, Schedule | | each worked; Snooze and Complete also with the process killed first |
| 8 "Clear all" | +22 ms | logged as `swiped`; fired again 30 s later |

- With the process alive, delays were 22 ms to 4.6 s; the longer
  ones came with the emulator at its busiest. A cold start took 48
  to 850 ms from the fork to the `process-start` line, and the
  receiver ran 60 to 570 ms after that.
- Doze: `force-idle` held the emulator in IDLE and IDLE_MAINTENANCE
  for four minutes; about a minute before the alarm, with
  `min_time_to_alarm` at 60 s, it went to INACTIVE by itself, so the
  alarm arrived with `doze: no`. That is the mechanism described in
  step 3; the alarm still came on time.
- After a reboot, BOOT_COMPLETED reached the app about 1.5 minutes
  after the system said it had booted (a busy emulator; a phone is
  usually quicker), and the overdue alarm fired then.
- An APK update ran the restore (`MY_PACKAGE_REPLACED`) and put back
  the two unanswered notifications (`re-shown`).
- Copy log, Share log (text to the share sheet) and `adb pull` of the
  log worked.
- Swiping the notification with `adb shell input swipe` did not
  dismiss it on the emulator, so only "Clear all" was checked as a
  swipe; a finger swipe on the phone is step 8.
- Lines the app writes while shared storage is still being mounted,
  right after the emulator's first boot, were lost (the first
  `process-start`); later lines, including those of the boot
  receiver after a reboot, were kept.
- The emulator's system process restarted twice on its own (a Wi-Fi
  crash in the emulator image); one of those restarts dropped a
  pending alarm, as a reboot would, before the boot receiver had run
  again. It was given 3 GB of memory and run again, and the results
  above come from that run.

The emulator is no evidence for the owner's phone: a manufacturer's
own battery rules do not exist on it.

## Results

Phone: ______________________ Android version: ______ Date: ________

| Step | What | Delay (ms) | Pass? | Notes |
| --- | --- | --- | --- | --- |
| 0 | Setup: exact alarms yes, notifications yes, battery exempt no | | | |
| 1 | App open | | | |
| 2 | App swiped from recents (cold start) | | | |
| 3 | Doze | | | |
| 4 | Airplane mode | | | |
| 5 | Reboot, alarm in the future | | | |
| 6 | Reboot, alarm due while off (LATE expected) | | | |
| 7 | Complete, Snooze, Schedule | | | |
| 8 | Swipe-away detected and re-notified | | | |
| 9 | Optional: a day of use | | | |

Manufacturer settings that had to be changed: ______________________

Does a Kotlin app deliver exact alarms reliably on this phone:
__________

Record the outcome in STATUS.md and tick the M2 criteria and the
"Sideload and run the checklist on the phone" task in MILESTONES.md.
