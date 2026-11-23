#!/usr/bin/env bash
# Runs the queues one after the other, after the control queue (pid $1) ends.
cd /private/tmp/t0-mzhr
while kill -0 "$1" 2>/dev/null; do sleep 5; done
./queue2.sh > queue2.log 2>&1
./queue-n.sh > queue-n.log 2>&1
./queue-l.sh > queue-l.log 2>&1
