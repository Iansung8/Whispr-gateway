#!/usr/bin/env bash
# Streams the VRAM guard's behaviour: state changes, relevant gateway log lines (local time),
# and VRAM usage with the top GPU-memory processes on big changes or every 2 minutes.
LOG="$(cd "$(dirname "$0")/.." && pwd)/gateway.log"
TOP="$(cd "$(dirname "$0")" && pwd -W)/top-vram.ps1"
top() { powershell -NoProfile -ExecutionPolicy Bypass -File "$TOP" 2>/dev/null | tr -d '\r\n'; }
n=$(wc -l < "$LOG"); prev=""; lastUsed=0; lastBeat=$(date +%s); down=0
echo "[$(date +%H:%M:%S)] monitor started | top VRAM: $(top)"
while true; do
  s=$(curl -s -m 4 http://127.0.0.1:8790/control/status)
  if [ -z "$s" ]; then
    [ $down -eq 0 ] && echo "[$(date +%H:%M:%S)] GATEWAY NOT RESPONDING"
    down=1; sleep 5; continue
  fi
  [ $down -eq 1 ] && echo "[$(date +%H:%M:%S)] gateway responding again"
  down=0
  read -r state used total ours <<< "$(echo "$s" | python -c "import json,sys;s=json.load(sys.stdin);v=s.get('vram') or {};print('%s/%s/%s/backedOff=%s/hog=%s' % (s['backend'],s['activeDevice'],s.get('activeModel') or '-',s['backedOff'],((s.get('guardHog') or {}).get('name','-')+':'+str((s.get('guardHog') or {}).get('mib',''))).replace(' ','_')), v.get('usedMiB',0), v.get('totalMiB',0), s.get('ourVramMiB',0))")"
  now=$(date +%s)
  if [ "$state" != "$prev" ]; then
    echo "[$(date +%H:%M:%S)] STATE $state | VRAM ${used}/${total} MiB (ours ${ours})"
    prev=$state
  fi
  m=$(wc -l < "$LOG")
  if [ "$m" -gt "$n" ]; then
    tail -n +$((n + 1)) "$LOG" | head -n $((m - n)) | grep -E 'VRAM guard|ready on|stopping|preload|failed|transcription' \
      | python -c "import sys,datetime
for l in sys.stdin:
    try: t=datetime.datetime.fromisoformat(l[:24].replace('Z','+00:00')).astimezone().strftime('%H:%M:%S')
    except Exception: t='?'
    print('[%s] log: %s' % (t, l[25:].rstrip()[:160]))"
    n=$m
  fi
  d=$((used - lastUsed)); d=${d#-}
  if [ "$d" -ge 1024 ] || [ $((now - lastBeat)) -ge 120 ]; then
    echo "[$(date +%H:%M:%S)] VRAM ${used}/${total} MiB | top: $(top)"
    lastUsed=$used; lastBeat=$now
  fi
  sleep 5
done
