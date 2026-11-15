# Alarm spike (milestone M2)

A throwaway .NET MAUI app, Android only, that answers one question
before the real Android app (M5) is built: **does a MAUI app deliver
exact alarms reliably on the owner's phone?** In particular, when the
app's process is not running and Android has to start it cold, with
all of MAUI's start-up, just to deliver the alarm broadcast.

The outcome is the last acceptance criterion of M2: MAUI or Kotlin
for the Android app. Fill in the results table at the end.

It is not part of `Nudge.sln`; the server's build, tests and CI never
see it. It has no network access (no `INTERNET` permission) and
contains no personal data: every alarm is an invented test string.

## What it does

- One page with buttons that schedule a test alarm in 1, 5, 15 or 60
  minutes, or 3 hours (for the Doze step), with
  `AlarmManager.SetAlarmClock`. The page shows the
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
on the page or any MAUI service being alive.

### The log

One line per event, with local time, the offset and milliseconds:

```
2026-11-15 14:03:00.004 +00:00 | process-start | - | pid 4242, 412 ms after the process was forked
2026-11-15 14:03:00.031 +00:00 | fired | #3 | due 2026-11-15 14:03:00.000 +00:00, delay +31 ms, via alarm (test, 5 min); doze: yes, light doze: no, screen on: no, battery saver: no
2026-11-15 14:03:00.052 +00:00 | shown | #3 | posted; notifications enabled: yes, channel importance: High
```

| Event | Meaning |
| --- | --- |
| `process-start` | A new process started (a cold start if an alarm follows within a second) |
| `scheduled` | Handed to `SetAlarmClock`, with the intended time |
| `fired` | The alarm arrived; `delay` is against the intended time; `LATE` if over 10 s; then whether the phone was in Doze, its screen on, battery saver on |
| `shown` | The notification was posted |
| `complete`, `snooze`, `schedule` | A notification action |
| `swiped` | The notification was swiped away; a `scheduled` (re-notify) follows |
| `tapped` | The notification's body was tapped |
| `boot-broadcast`, `restore` | The boot receiver ran (`BOOT_COMPLETED`, `MY_PACKAGE_REPLACED`) |
| `re-registered`, `re-shown` | What the restore did with each alarm |
| `note` | Your own note from the page's "Add note" box |
| `schedule-failed`, `error` | Something went wrong; the details say what |

To get the log off the phone: **Copy log** (clipboard) or **Share
log** (email, Drive, ...) on the page, or with adb:

```bash
adb pull /sdcard/Android/data/nudge.spike.alarms/files/events.log
```

`adb logcat -s AlarmSpike` shows the same lines live.

Use **Add note** before each step below ("step 3: Doze"), so the log
reads on its own afterwards.

## Build

Needs the .NET 9 SDK with the MAUI Android workload, the Android SDK
(API 35) and a JDK 17, all as Visual Studio installs them. From this
folder (the `global.json` here selects the .NET 9 SDK):

```bash
cd spikes/AlarmSpike
dotnet build -c Release \
  -p:AndroidSdkDirectory="C:\Program Files (x86)\Android\android-sdk" \
  -p:JavaSdkDirectory="C:\Program Files (x86)\Android\openjdk\jdk-17.0.12"
```

The two `-p:` options only tell the build where Visual Studio put the
Android SDK and the JDK; leave them out if it finds them anyway.

The APK is

```
spikes/AlarmSpike/bin/Release/net9.0-android/nudge.spike.alarms-Signed.apk
```

It contains code for arm64 phones and the x64 emulator, and is signed
with the local debug key, which is enough to sideload it.

Why .NET 9 and not 10: the .NET 10 SDK on the build machine has the
.NET 10 MAUI and Android workload manifests but not their packs, so
`net10.0-android` does not build there without `dotnet workload
install maui-android`. To move the spike to .NET 10, run that, change
`TargetFramework` to `net10.0-android` and `global.json` to
`10.0.100`. Alarm delivery is Android's job either way; what MAUI
adds is the start-up work before the receiver runs, which is similar
in both versions.

## Install

With adb (USB debugging on):

```bash
adb install -r bin/Release/net9.0-android/nudge.spike.alarms-Signed.apk
```

Without adb: copy the APK to the phone (USB, Drive, email), open it
in the Files app and allow "Install unknown apps" for that app when
asked. Play Protect may warn about an unknown developer; choose to
install anyway.

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
  part of the decision. https://dontkillmyapp.com lists them per
  manufacturer.
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
Release APK and the app's process killed before each alarm: an alarm
fired with the screen off, in airplane mode, and in forced deep Doze
(with `min_time_to_alarm` shortened as in step 3), each 3.6 to 6 s
late, almost all of it MAUI's cold start on a heavily loaded
emulator; with the process alive, 12 to 14 ms. (The very first, a
minute after the emulator's first boot, was 35 s late: Android
started the process 7 s late and MAUI took 22 s to start.) After a reboot the
boot receiver re-registered a future alarm (it then fired), fired an
overdue one as LATE, and put back an unanswered notification. The
notification showed Complete, Schedule and Snooze, and each worked;
"Clear all" was detected as a swipe and the alarm came back 30 s
later. An APK update ran the restore (`MY_PACKAGE_REPLACED`).
`adb pull` of the log worked. The emulator is no evidence for the
owner's phone: timings there were distorted (load average up to 57),
and a manufacturer's own battery rules do not exist on it.

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

**Decision: MAUI or Kotlin for the Android app:** __________

Reason: ______________________________________________________

Record the decision in DECISIONS.md and tick the last M2 criterion in
MILESTONES.md.
